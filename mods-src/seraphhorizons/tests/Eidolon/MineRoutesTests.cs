using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The crew's routes (<see cref="MineRoutePlanner"/>, <see cref="MineRoutes"/>; README "Eidolon",
/// working together, *Routes*): five to ten stations each, ways that cross no claim, slots apart, no dig under
/// a planned stand, spare work in a trench and in front of a tunnel, repair that keeps the routes, a member
/// gone, re-solving only for much better, the same routes wherever made, and in bounded time.</summary>
public class MineRoutesTests(ITestOutputHelper output)
{
    private const int L = MineCases.L;

    private static readonly string[] Geometries = ["G1", "G2", "G3", "G4", "G5"];

    // a case's live state and plan as a job has them at its start, and a solve's input over a copy
    private sealed record Start(MineCase Case, MineTestWorld World, MinePlan Plan, MineState Live)
    {
        public MineRouteInput Input(double now = 0, MineRoutes? current = null, IEnumerable<MineRouteMember>? members = null) =>
            new(Plan, 1, Live.Clone(), (members ?? Case.Starts.Select((s, i) => new MineRouteMember(i + 1, PlanRunner.Start(Live.Site, s), null, false, now))).ToList(),
                [], now, current, null);
    }

    private static Start Begin(MineCase c)
    {
        var world = c.World();
        var plan = c.Plan(world);
        var live = new MineState(world.Site(c.Area), c.Home);
        live.BuildNow(plan.Targets);
        live.Bands = plan.Bands(live.Remaining);
        return new Start(c, world, plan, live);
    }

    private static IEnumerable<MineStop> Stations(MineRoutes routes) =>
        routes.Routes.Values.SelectMany(r => r.Stops).Where(s => s.Kind == MineStopKind.Station);

    [Fact]
    public void Routes_hold_five_to_ten_stations_each()
    {
        var start = Begin(MineCases.WidePit(3));
        var routes = MineRoutePlanner.Solve(start.Input());
        output.WriteLine($"J {routes.Score.J:0.0}, {routes.Stats}");
        Assert.Equal(3, routes.Routes.Count);
        foreach (var r in routes.Routes.Values)
        {
            int n = r.Stops.Count(s => s.Kind == MineStopKind.Station);
            output.WriteLine($"{r.Member}: {string.Join(" ", r.Stops.Select(s => s.Kind == MineStopKind.Wait ? "w" : $"#{s.Station}"))}");
            Assert.InRange(n, MineRoutes.MinStops, MineRoutes.MaxStops);
        }
        // simulated again in time order: at each stop's start, every cell it digs may be dug (by its band, or ahead)
        var state = start.Live.Clone();
        var stops = Stations(routes).OrderBy(s => s.Start).ToList();
        foreach (var stop in stops)
        {
            state.Apply(stops.Where(o => o.Finish <= stop.Start + 1e-9 && o != stop).SelectMany(o => o.Cells).Where(state.Remaining.Contains).ToList());
            Assert.All(stop.Cells, c => Assert.True(state.Bands!.Eligible(c) || stop.Ahead && MineAhead.Eligible(state, c),
                $"#{stop.Station}'s {c} may not be dug at {stop.Start:0.0}"));
        }
    }

    // a way's box columns past its first two nodes
    private static HashSet<(int X, int Z)> Columns(IReadOnlyList<PathCell> leg)
    {
        var columns = new HashSet<(int X, int Z)>();
        foreach (var n in leg.Skip(2))
            for (int x = n.X - 1; x <= n.X; x++)
            for (int z = n.Z - 1; z <= n.Z; z++)
                columns.Add((x, z));
        return columns;
    }

    // A way may cross another's claim only once one of the two has waited MineRoutePlanner.PassAfter for the other
    // (the walker for the claim, or the claim's for the way: the game goes through then); none crosses one sooner.
    // Ways to a planned wait are not held to it: they lead out of the others' way.
    [Fact]
    public void A_member_s_way_crosses_another_s_claim_only_after_waiting_for_it()
    {
        static double Waited(MineRoute r, MineStop stop)
        {
            int k = r.Stops.ToList().IndexOf(stop);
            return stop.Depart - (k == 0 ? 0 : r.Stops[k - 1].Finish);
        }
        foreach (string g in Geometries)
        foreach (int crew in new[] { 2, 3 })
        {
            var start = Begin(MineCases.ByName(g, crew));
            var routes = MineRoutePlanner.Solve(start.Input());
            int legs = 0, crossing = 0;
            foreach (var r in routes.Routes.Values)
            foreach (var leg in r.Stops.Where(s => s.Kind == MineStopKind.Station && s.Leg.Count > 2))
            {
                legs++;
                var columns = Columns(leg.Leg);
                foreach (var o in routes.Routes.Values.Where(o => o.Member != r.Member))
                foreach (var stop in o.Stops.Where(s => s.Kind == MineStopKind.Station))
                {
                    if (!(leg.Depart < stop.Finish && stop.Depart < leg.Arrive))
                        continue;
                    var claim = MineDispatch.Claim(start.Plan.Stations[stop.Station] with { Cells = stop.Cells }, start.Plan.Tunnel).ToHashSet();
                    if (!claim.Overlaps(columns))
                        continue;
                    crossing++;
                    Assert.True(Waited(r, leg) >= MineRoutePlanner.PassAfter - 1e-6 || Waited(o, stop) >= MineRoutePlanner.PassAfter - 1e-6,
                        $"{g} x{crew}: {r.Member}'s way to {leg.At} ({leg.Depart:0.0}..{leg.Arrive:0.0}) crosses {o.Member}'s #{stop.Station} ({stop.Depart:0.0}..{stop.Finish:0.0}), neither waiting");
                }
            }
            output.WriteLine($"{g} x{crew}: {legs} ways, {crossing} across a claim after a wait");
        }
    }

    // No way walks into another's box at a stand in the covered part while that one works there (in a heading there is
    // no room to pass, the game's walker stopping short of it and parting from it): it waits for it to be done, or
    // goes round.
    [Fact]
    public void A_member_s_way_never_passes_by_another_s_stand_while_it_works_there()
    {
        foreach (string g in Geometries)
        foreach (int crew in new[] { 2, 3 })
        {
            var start = Begin(MineCases.ByName(g, crew));
            var routes = MineRoutePlanner.Solve(start.Input());
            int legs = 0;
            foreach (var r in routes.Routes.Values)
            foreach (var leg in r.Stops.Where(s => s.Kind == MineStopKind.Station && s.Leg.Count > 2))
            {
                legs++;
                var columns = Columns(leg.Leg);
                foreach (var o in routes.Routes.Values.Where(o => o.Member != r.Member))
                foreach (var stop in o.Stops.Where(s => s.Kind == MineStopKind.Station && s.Arrive < leg.Arrive && leg.Depart < s.Finish))
                {
                    bool by = false, covered = false;
                    for (int x = stop.At.X - 1; x <= stop.At.X; x++)
                    for (int z = stop.At.Z - 1; z <= stop.At.Z; z++)
                    {
                        by |= columns.Contains((x, z));
                        covered |= start.Plan.Tunnel?.Covered.Contains((x, z)) == true;
                    }
                    if (!covered)
                        continue;
                    Assert.False(by, $"{g} x{crew}: {r.Member}'s way to {leg.At} ({leg.Depart:0.0}..{leg.Arrive:0.0}) walks into {o.Member}'s box at {stop.At} (#{stop.Station}, {stop.Arrive:0.0}..{stop.Finish:0.0})");
                }
            }
            output.WriteLine($"{g} x{crew}: {legs} ways");
        }
    }

    [Fact]
    public void Narrow_way_slots_never_overlap()
    {
        foreach (string g in Geometries)
        foreach (int crew in new[] { 2, 3 })
        {
            var start = Begin(MineCases.ByName(g, crew));
            var routes = MineRoutePlanner.Solve(start.Input());
            Assert.False(routes.Slots.Overlaps());
            // and after every solve and repair of a run with its strikes jittered (the runner checks each)
            var c = MineCases.ByName(g, crew);
            var world = c.World();
            var runner = new RouteRunner(world, c.Area, c.Plan(world), c.Home, c.Starts, jitter: 7);
            int checks = 0, slots = 0;
            runner.Watch = (r, _) =>
            {
                checks++;
                slots = Math.Max(slots, r.Slots.All.Count);
                Assert.False(r.Slots.Overlaps());
            };
            runner.Run(20_000);
            output.WriteLine($"{g} x{crew}: {routes.Zones.Count} zones; {checks} solves and repairs checked, up to {slots} slots");
            Assert.True(checks > 0);
        }
    }

    [Fact]
    public void No_planned_dig_takes_a_planned_stand()
    {
        foreach (string g in Geometries)
        foreach (int crew in new[] { 2, 3 })
        {
            var start = Begin(MineCases.ByName(g, crew));
            var routes = MineRoutePlanner.Solve(start.Input());
            var stops = routes.Routes.Values.SelectMany(r => r.Stops.Where(s => s.Kind == MineStopKind.Station).Select(s => (r.Member, Stop: s)))
                .OrderBy(p => p.Stop.Finish).ToList();
            var state = start.Live.Clone();
            foreach (var (member, stop) in stops)
            {
                // a member at work at its stand while this one's dig lands keeps its footing and its way out
                var working = stops.Where(p => p.Member != member && p.Stop.Start <= stop.Finish + 1e-9 && p.Stop.Finish >= stop.Finish - 1e-9)
                    .Select(p => p.Stop.At).ToList();
                Assert.False(MineDispatch.Footing(working, stop.Cells), $"{g} x{crew}: #{stop.Station}'s dig is the footing of a stand at work");
                var outBefore = working.Where(state.Walk.Out).ToList();
                state.Apply(stop.Cells.Where(state.Remaining.Contains).ToList());
                Assert.All(outBefore, n => Assert.True(state.Walk.Out(n), $"{g} x{crew}: #{stop.Station}'s dig cuts off a stand at work, {n}"));
            }
            // and every stop's stand is a place to stand, and out, when its work starts
            state = start.Live.Clone();
            var done = new HashSet<int>();
            foreach (var (member, stop) in stops.OrderBy(p => p.Stop.Start))
            {
                foreach (var (_, o) in stops.Where(p => p.Stop.Finish <= stop.Start + 1e-9 && done.Add(p.Stop.Seq)))
                    state.Apply(o.Cells.Where(state.Remaining.Contains).ToList());
                Assert.True(state.Site.Standable(stop.At) && state.Walk.Out(stop.At), $"{g} x{crew}: #{stop.Station}'s stand {stop.At} is not out at {stop.Start:0.0}");
            }
        }
    }

    [Fact]
    public void A_spare_member_in_a_narrow_trench_opens_the_next_bench_rather_than_wait()
    {
        var c = MineCases.Trench(2);
        var world = c.World();
        var plan = c.Plan(world);
        var runner = new RouteRunner(world, c.Area, plan, c.Home, c.Starts);
        runner.Run(20_000);
        output.WriteLine($"makespan {runner.Makespan:0.0}, idle {string.Join("/", runner.Idle.Select(i => $"{i:0.0}"))}, ahead {runner.AheadStops}, refused {runner.AheadRefused}; {runner.IdleWhy}");
        Assert.True(runner.AheadStops > 0, "no stop was worked ahead of its band");
        Assert.All(runner.Idle, i => Assert.True(i <= 0.25 * runner.Makespan, $"one waited {i:0.0} s of {runner.Makespan:0.0}"));
        Assert.DoesNotContain(runner.Live.Remaining, cell => !plan.WayOut.Contains(cell));
        Assert.Equal(0, runner.AheadRefused);
        Assert.True(runner.AllLeave(c.Home));
    }

    [Fact]
    public void In_a_two_wide_tunnel_the_spare_works_the_ground_in_front_while_the_other_drives_the_face()
    {
        var c = MineCases.NarrowTunnel(2);
        var world = c.World();
        var plan = c.Plan(world);
        var runner = new RouteRunner(world, c.Area, plan, c.Home, c.Starts);
        runner.Run(20_000);
        output.WriteLine($"makespan {runner.Makespan:0.0}, idle {string.Join("/", runner.Idle.Select(i => $"{i:0.0}"))}; {runner.IdleWhy}");
        // never two at the face at once: while one drives it, the other works elsewhere
        var faces = runner.Strikes.Where(s => s.Kind == MineStationKind.Face).ToList();
        Assert.NotEmpty(faces);
        Assert.DoesNotContain(faces, a => faces.Any(b => b.Member != a.Member && a.From < b.To - 1e-9 && b.From < a.To - 1e-9));
        Assert.Contains(runner.Strikes, s => s.Kind != MineStationKind.Face && faces.Any(f => f.Member != s.Member && s.From < f.To && f.From < s.To));
        // neither waits over 10 s at a time before the mound in front is done
        double mound = runner.Strikes.Where(s => s.Cells.Any(cell => cell.X < 0)).Max(s => s.To);
        for (int m = 0; m < 2; m++)
            foreach (var (from, to) in Merged(runner.Waited(m)).Where(w => w.From < mound))
                Assert.True(Math.Min(to, mound) - from <= 10, $"{m + 1} waited {from:0.0}..{to:0.0} before the mound was done at {mound:0.0}");
        Assert.DoesNotContain(runner.Live.Remaining, cell => !plan.WayOut.Contains(cell));
        Assert.True(runner.AllLeave(c.Home));
    }

    // idle spells run together into waits
    private static List<(double From, double To)> Merged(IReadOnlyList<(double From, double To)> idle)
    {
        var merged = new List<(double From, double To)>();
        foreach (var (from, to) in idle.OrderBy(i => i.From))
            if (merged.Count > 0 && from <= merged[^1].To + 1e-6)
                merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, to));
            else
                merged.Add((from, to));
        return merged;
    }

    // how much of a's stations are still in b, in the same relative order (the longest common subsequence)
    private static double Kept(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count == 0)
            return 1;
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (int i = 1; i <= a.Count; i++)
        for (int j = 1; j <= b.Count; j++)
            lcs[i, j] = a[i - 1] == b[j - 1] ? lcs[i - 1, j - 1] + 1 : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
        return (double)lcs[a.Count, b.Count] / a.Count;
    }

    private static Dictionary<long, List<int>> Orders(MineRoutes routes) =>
        routes.Routes.ToDictionary(p => p.Key, p => p.Value.Stops.Where(s => s.Kind == MineStopKind.Station).Select(s => s.Station).ToList());

    [Theory]
    [InlineData("early")]
    [InlineData("late")]
    [InlineData("broken")]
    public void Repair_keeps_most_of_a_route(string event_)
    {
        var start = Begin(MineCases.WidePit(3));
        var routes = MineRoutePlanner.Solve(start.Input());
        var before = Orders(routes);
        var live = start.Live;
        string? wanted;
        if (event_ == "broken")
        {
            // a player breaks every cell of one planned station, the second of member 2's
            var stop = routes.Routes[2].Stops.Where(s => s.Kind == MineStopKind.Station).ElementAt(1);
            live.Apply(stop.Cells);
            wanted = routes.Sweep(live, 5);
            Assert.DoesNotContain(Stations(routes), s => s.Cells.Count > 0 && !s.Cells.Any(live.Remaining.Contains));
            before[2].Remove(stop.Station);
        }
        else
        {
            // member 1 begins its head and finishes it 30 % early or 50 % late
            var head = routes.Routes[1].Head!;
            Assert.Null(routes.Started(1, head.Station, head.Start));
            double finish = head.Start + head.WorkSeconds * (event_ == "early" ? 0.7 : 1.5);
            live.Apply(head.Cells);
            wanted = routes.Finished(1, head.Station, finish, live);
            before[1].RemoveAt(0);
            Assert.True(routes.Routes[1].Head is not { } next || next.Depart >= finish - 1e-9);
        }
        output.WriteLine($"{event_}: wanted {wanted ?? "none"}; {string.Join(" | ", routes.Log)}");
        Assert.Null(wanted);
        Assert.False(routes.Slots.Overlaps());
        var after = Orders(routes);
        foreach (var (member, list) in before)
            Assert.True(Kept(list, after[member]) >= 0.8, $"{member}: {string.Join(" ", list)} became {string.Join(" ", after[member])}");
    }

    [Fact]
    public void A_member_gone_leaves_its_stations_to_the_others()
    {
        // room in the others' routes: every freed station placed (the two-wide tunnel's routes are short; routes
        // asked for, though the planner would leave this job to today's dispatch)
        var start = Begin(MineCases.NarrowTunnel(3));
        var routes = MineRoutePlanner.Solve(start.Input() with { Keep = 0 });
        var freed = routes.Routes[3].Stops.Where(s => s.Kind == MineStopKind.Station).Select(s => s.Station).ToList();
        output.WriteLine($"3 had {string.Join(" ", freed)}; others {string.Join(" | ", Orders(routes).Where(p => p.Key != 3).Select(p => string.Join(" ", p.Value)))}");
        Assert.NotEmpty(freed);
        Assert.Null(routes.Gone(3, 1, start.Live));
        Assert.False(routes.Routes.ContainsKey(3));
        Assert.All(freed, s => Assert.Contains(Stations(routes), t => t.Station == s));
        Assert.False(routes.Slots.Overlaps());
        // the others full: a solve is asked for
        var full = Begin(MineCases.WidePit(3));
        var full3 = MineRoutePlanner.Solve(full.Input());
        Assert.All(full3.Routes.Values, r => Assert.Equal(MineRoutes.MaxStops, r.Stops.Count(s => s.Kind == MineStopKind.Station)));
        Assert.Equal("members", full3.Gone(3, 1, full.Live));
    }

    [Fact]
    public void The_job_s_choice_holds_across_a_new_station_plan_while_its_crew_and_area_are_the_same()
    {
        var start = Begin(MineCases.WidePit(3));
        var area = start.Case.Area;
        long[] crew = [1, 2, 3];
        var members = start.Input().Members;
        // the plan made again from the world as it is: a new station plan for the same job
        var again = MinePlanner.Run(new MinePlanInput(start.Live, start.Case.Home, _ => false, new HashSet<PathCell>()));
        MineRoutes OnNewPlan(MineRouteChoice choice, IEnumerable<long> present, MarkArea on) =>
            MineRoutePlanner.Solve(new MineRouteInput(again, 2, start.Live.Clone(), members, [], 0, null, null) { Keep = choice.KeepFor(present, on) });
        // whatever the first solve chose (as chosen, a variant forced, or today's) the new plan's solve keeps
        foreach (int? forced in new int?[] { null, 5, MineRoutePlanner.TodayIndex })
        {
            var choice = new MineRouteChoice();
            var first = MineRoutePlanner.Solve(start.Input() with { Keep = forced ?? choice.KeepFor(crew, area) });
            choice.Made(first, crew, area);
            var kept = OnNewPlan(choice, crew, area);
            output.WriteLine($"forced {forced?.ToString() ?? "none"}: first {first.VariantIndex}, on the new plan {kept.VariantIndex}");
            Assert.Equal(first.VariantIndex, kept.VariantIndex);
            Assert.Equal(first.VariantIndex, choice.KeepFor(crew.Reverse(), area));
        }
        // another crew, or another area: chosen again
        var made = new MineRouteChoice();
        made.Made(MineRoutePlanner.Solve(start.Input() with { Keep = 5 }), crew, area);
        Assert.Null(made.KeepFor([1, 2], area));
        Assert.Null(made.KeepFor([1, 2, 3, 4], area));
        Assert.Null(made.KeepFor(crew, MarkArea.Between(area.Min, new MarkPos(area.Max.X + 1, area.Max.Y, area.Max.Z))));
        Assert.Null(new MineRouteChoice().KeepFor(crew, area));
    }

    [Fact]
    public void A_re_solve_replaces_routes_only_when_much_better()
    {
        var start = Begin(MineCases.WidePit(3));
        var routes = MineRoutePlanner.Solve(start.Input());
        var again = MineRoutePlanner.Solve(start.Input(current: routes.Copy()));
        output.WriteLine($"made {routes.Score.J:0.0}, again {again.Score.J:0.0}: {again.Stats}");
        Assert.True(again.Stats.KeptCurrent);
        Assert.Equal(Orders(routes), Orders(again));
        // routes deliberately bad, each member's stations in reverse
        var reversed = new Dictionary<long, MineRoute>();
        foreach (var (member, r) in routes.Routes)
            reversed[member] = r with { Stops = r.Stops.Where(s => s.Kind == MineStopKind.Station).Reverse().ToList() };
        var bad = new MineRoutes(routes.Plan, 1, 0, reversed, routes.Zones, routes.ZoneIndex, new MineSlots(), routes.Score, routes.Stats, 1000);
        var replaced = MineRoutePlanner.Solve(start.Input(current: bad));
        output.WriteLine($"against reversed: {replaced.Stats}, J {replaced.Score.J:0.0}");
        Assert.False(replaced.Stats.KeptCurrent);
    }

    private static string Fingerprint(MineRoutes routes) =>
        string.Join("\n", routes.Routes.OrderBy(p => p.Key).Select(p => $"{p.Key}: " + string.Join(" ", p.Value.Stops.Select(s =>
            $"{s.Kind} #{s.Station} {s.At} {s.Depart:R} {s.Arrive:R} {s.Start:R} {s.Finish:R} [{string.Join(",", s.Leg)}] [{string.Join(",", s.Cells)}]"))))
        + "\n" + string.Join(" ", routes.Slots.All.Select(s => $"{s.Zone}/{s.Member}/{s.From:R}/{s.To:R}/{s.Stop}"))
        + $"\n{routes.Score}";

    [Fact]
    public void Routes_are_deterministic()
    {
        foreach (string g in new[] { "G1", "G3", "G5" })
        {
            var start = Begin(MineCases.ByName(g, 3));
            string a = Fingerprint(MineRoutePlanner.Solve(start.Input()));
            string b = Fingerprint(MineRoutePlanner.Solve(start.Input()));
            string c = Fingerprint(Task.Run(() => MineRoutePlanner.Solve(start.Input())).GetAwaiter().GetResult());
            Assert.Equal(a, b);
            Assert.Equal(a, c);
        }
    }

    // the owner's bound: 250 ms in Release (128 x 1 x 128 measured about 220 ms there; the design's was 100 ms)
#if DEBUG
    private const int SolveBoundMs = 1000;
#else
    private const int SolveBoundMs = 250;
#endif

    [Theory]
    [InlineData(128, 1, 128)]
    [InlineData(16, 64, 16)]
    public void Routes_are_made_in_bounded_time(int sizeX, int sizeY, int sizeZ)
    {
        int top = L + 70;
        var world = new MineTestWorld().Box(-8, sizeX + 8, 1, top - 1, -8, sizeZ + 8);
        var area = Area(0, sizeX - 1, top - sizeY, top - 1, 0, sizeZ - 1);
        var home = new PathCell(-4, top, sizeZ / 2);
        var plan = world.Plan(area, home);
        var live = new MineState(world.Site(area), home);
        live.BuildNow(plan.Targets);
        live.Bands = plan.Bands(live.Remaining);
        var members = new[] { new PathCell(-4, top, sizeZ / 2 - 3), new PathCell(-4, top, sizeZ / 2 + 3), new PathCell(-7, top, sizeZ / 2) }
            .Select((s, i) => new MineRouteMember(i + 1, PlanRunner.Start(live.Site, s), null, false, 0)).ToList();
        MineRouteInput Input() => new(plan, 1, live.Clone(), members, [], 0, null, null);
        MineRoutePlanner.Solve(Input()); // warm, as a server that has solved before
        var clone = System.Diagnostics.Stopwatch.StartNew();
        var copy = live.Clone();
        var cloned = clone.Elapsed;
        var input = new MineRouteInput(plan, 1, copy, members, [], 0, null, null);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var routes = MineRoutePlanner.Solve(input);
        output.WriteLine($"{sizeX} x {sizeY} x {sizeZ}: solved in {clock.ElapsedMilliseconds} ms ({routes.Stats}), "
            + $"the live state copied in {cloned.TotalMilliseconds:0.0} ms; {string.Join(" | ", Orders(routes).Select(p => p.Value.Count))} stations");
        Assert.True(clock.ElapsedMilliseconds <= SolveBoundMs, $"{clock.ElapsedMilliseconds} ms");
        Assert.All(routes.Routes.Values, r => Assert.True(r.Stops.Count(s => s.Kind == MineStopKind.Station) >= 1));
    }
}
