using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The mine planner (README "Eidolon", mining, *Stations*): tall tunnels into slopes finished
/// from the footing the hill gives, the top of a tunnel into a flat face reported out of reach with the
/// floor under it kept, two eidolons sharing a tunnel, a ledge, a stair dug out of a pit, stranding, bands,
/// order, replanning, slicing and cost.</summary>
public class MinePlannerTests(ITestOutputHelper output)
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private static readonly PathCell Home = new(-6, L, 0);

    private void Dump(MinePlan plan)
    {
        output.WriteLine($"stations {plan.Stations.Count}, out of reach {plan.OutOfReach.Count}, held {plan.Held.Count}, way out {plan.WayOut.Count}, reruns {plan.Reruns}");
        foreach (var s in plan.Stations)
            output.WriteLine($"{s.Id}: {s.Kind} band {s.Band} lane {s.Lane} slice {s.Slice} at {s.Stand}: {string.Join(" ", s.Cells.Select(c => $"({c.X},{c.Y},{c.Z})"))}");
    }

    // ---- the owner's cases ----

    [Fact]
    public void A_tunnel_six_high_into_a_slope_with_a_step_against_its_face_is_finished()
    {
        // ground at y -1; a hill x 0..12, y 0..9; a step 2 high against its face, outside the area
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L).Box(-2, -1, L, L + 1, -3, 4);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var plan = world.Plan(area, Home);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Empty(plan.WayOut);
        Assert.Equal(0, plan.Reruns);
        Assert.Equal(96, plan.Stations.Sum(s => s.Cells.Count));
        int lastTop = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Cells.Any(c => c.Y >= L + 2)).Max(p => p.i);
        int firstLow = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Cells.Any(c => c.Y <= L + 1)).Min(p => p.i);
        Assert.True(lastTop < firstLow, "the top band is driven before any of the layers under it");
        PlanRunner.Run(world, area, plan, Home);
        Assert.DoesNotContain(Cells(0, 7, L, L + 5, 0, 1), world.Has);
    }

    // hill x 0..12 to y 11, z -3..4 (wider for a wider tunnel); a step 4 high (x -2..-1) inside the area,
    // and one 2 high (x -4..-3) outside, in front of it
    private static MineTestWorld SteppedSlope(int height, int z1 = 4) =>
        new MineTestWorld().Ground(L - 1).Hill(0, 12, L + height + 3, -3, z1, L).Box(-2, -1, L, L + 3, -3, z1).Box(-4, -3, L, L + 1, -3, z1);

    [Fact]
    public void A_tunnel_eight_high_into_a_stepped_slope_is_finished()
    {
        var world = SteppedSlope(8);
        var area = Area(-2, 7, L, L + 7, 0, 1);
        var plan = world.Plan(area, Home);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Equal(0, plan.Reruns);
        // band 0 (y 4..7) is struck first from the 4-high step, and all of it before any cell of the step
        Assert.Equal(new PathCell(-1, L + 4, plan.Stations[0].Stand.Z), plan.Stations[0].Stand);
        int lastTop = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Cells.Any(c => c.Y >= L + 4)).Max(p => p.i);
        int firstStep = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Cells.Any(c => c.X < 0)).Min(p => p.i);
        Assert.True(lastTop < firstStep, "band 0 is driven before any of the step is dug");
        // finished from the floor, out over the 2-high step (PlanRunner: the way back home)
        PlanRunner.Run(world, area, plan, Home);
        Assert.DoesNotContain(Cells(-2, 7, L, L + 7, 0, 1), world.Has);
    }

    // granite ground y -3..-1; hill x 0..10 to y 11, z -3..4; the area from the ground's open part (x -4..-1,
    // a pit 2 deep in front of the face) into the hill (x 0..7), H high from the pit's floor
    private static (MineTestWorld World, MarkArea Area) FlatFace(int height) =>
        (new MineTestWorld().Ground(L - 1).Hill(0, 10, L + 11, -3, 4, L), Area(-4, 7, L - 2, L + height - 3, 0, 1));

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void A_tunnel_into_a_flat_ground_face_reports_its_top_and_keeps_the_floor_under_it(int high)
    {
        // "high": the face over the ground (layers 0..high-1); the area goes 2 under it, the pit in front
        var (world, area) = FlatFace(high + 2);
        var plan = world.Plan(area, Home);
        Dump(plan);
        var covered = Cells(0, 7, L + 4, L + high - 1, 0, 1).ToHashSet();
        Assert.Equal(covered, plan.OutOfReach.ToHashSet());
        Assert.Equal(high == 6 ? 32 : 64, plan.OutOfReach.Count);
        Assert.Equal(Cells(0, 7, L - 2, L + 3, 0, 1).ToHashSet(), plan.Held.ToHashSet());
        Assert.All(plan.Stations, s => Assert.All(s.Cells, c => Assert.True(c.X < 0, $"{c} is in a covered column")));
        Assert.Equal(high - 4, plan.StepNeeded);
        var boxes = MineOutlines.Boxes(plan.OutOfReach);
        Assert.Equal(covered, boxes.SelectMany(b => Cells(b.Min.X, b.Max.X, b.Min.Y, b.Max.Y, b.Min.Z, b.Max.Z)).ToHashSet());
        PlanRunner.Run(world, area, plan, Home);
        // the pit in front all dug, every covered cell still there
        Assert.DoesNotContain(Cells(-4, -1, L - 2, L - 1, 0, 1), world.Has);
        Assert.All(Cells(0, 7, L - 2, L + high - 1, 0, 1), c => Assert.True(world.Has(c)));
    }

    [Fact]
    public void A_step_put_in_front_of_a_flat_face_brings_its_top_in_reach()
    {
        var (world, area) = FlatFace(8);
        var input = world.Input(area, Home);
        Assert.NotEmpty(MinePlanner.Run(input).OutOfReach);
        // a player fills a step 2 over the ground in front of the face (in the area's open part)
        foreach (var c in Cells(-2, -1, L - 2, L + 1, -3, 4))
        {
            world.Solid.Add((c.X, c.Y, c.Z));
            input.State.Changed(c, world.Read(c.X, c.Y, c.Z));
        }
        var plan = MinePlanner.Run(input);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Equal(0, plan.Reruns);
        PlanRunner.Run(world, area, plan, Home);
        Assert.DoesNotContain(Cells(-4, 7, L - 2, L + 5, 0, 1), world.Has);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(6)]
    public void Two_workers_finish_a_tunnel_four_wide_into_a_stepped_slope(int high)
    {
        var world = SteppedSlope(high, z1: 6);
        var area = Area(-2, 7, L, L + high - 1, 0, 3);
        var plan = world.Plan(area, Home);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        int total = plan.Stations.Sum(s => s.Cells.Count);
        var team = new TeamRunner(world, area, plan, Home, [new PathCell(-6, L, 0), new PathCell(-6, L, 4)]);
        team.Run();
        output.WriteLine($"mined {string.Join(", ", team.Mined)} of {total} in {team.Seconds:0} s, {team.Replans} replans, left {string.Join(" ", team.Live.Remaining)}");
        Assert.DoesNotContain(Cells(-2, 7, L, L + high - 1, 0, 3), world.Has);
        Assert.All(team.Mined, m => Assert.True(m >= total / 4, $"one mined {m} of {total}"));
        Assert.True(team.AllLeave(Home), "both leave by the mouth");
        Assert.Equal(0, team.Replans);
    }

    // ---- a ledge, a stair, stranding ----

    [Fact]
    public void A_tunnel_six_high_with_a_ledge_two_columns_off_its_face_is_benched_from_it()
    {
        // the bench geometry of the tunnel tests: a ledge 2 high (x -6..-3) two columns off the cliff, the
        // floor at the cliff's foot (x -2, -1) for the lower band's own face
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 15, L + 7, -6, 6, L).Box(-6, -3, L, L + 1, -6, 6);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var plan = world.Plan(area, Home);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        Assert.Equal(0, plan.Reruns);
        Assert.NotNull(plan.Tunnel);
        Assert.Equal((1, 0), (plan.Tunnel!.DriveX, plan.Tunnel.DriveZ));
        int lastTop = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Band == 0).Max(p => p.i);
        int firstLow = plan.Stations.Select((s, i) => (s, i)).Where(p => p.s.Band == 1).Min(p => p.i);
        Assert.True(lastTop < firstLow, "the top band first");
        // the top band's first face from the ledge's edge, the lower band's first from the floor at the foot
        Assert.Equal(L + 2, plan.Stations[0].Stand.Y);
        Assert.Equal(L, plan.Stations[firstLow].Stand.Y);
        PlanRunner.Run(world, area, plan, Home);
        Assert.DoesNotContain(Cells(0, 7, L, L + 5, 0, 1), world.Has);
    }

    [Fact]
    public void A_pit_five_deep_with_no_shelf_digs_a_stair_and_leaves_it_as_the_way_out()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 5, L - 5, L - 1, 0, 5);
        var home = new PathCell(-6, L, 2);
        var plan = world.Plan(area, home);
        Dump(plan);
        output.WriteLine($"stair {string.Join(" ", plan.Stair.OrderBy(c => c.X).ThenBy(c => c.Z).ThenBy(c => c.Y))}; way out {plan.WayOut.Count}");
        Assert.Empty(plan.OutOfReach);
        Assert.Equal(0, plan.Reruns);
        // against the side nearest home (x 0), 2 wide, rising 2 a column: x 0 up to its tread at y -2,
        // x 1 up to y -4
        Assert.NotEmpty(plan.Stair);
        Assert.All(plan.Stair, c => Assert.InRange(c.X, 0, 1));
        Assert.Equal(2, plan.Stair.Select(c => c.Z).Distinct().Count());
        Assert.Equal(L - 2, plan.Stair.Where(c => c.X == 0).Max(c => c.Y));
        Assert.Equal(L - 4, plan.Stair.Where(c => c.X == 1).Max(c => c.Y));
        Assert.NotEmpty(plan.WayOut);
        Assert.Subset(plan.Stair.ToHashSet(), plan.WayOut.ToHashSet());
        Assert.All(plan.Stations.Where(s => s.Kind == MineStationKind.Stair), s => Assert.True(s.Id > plan.Stations.Where(t => t.Kind != MineStationKind.Stair).Max(t => t.Id)));
        // everything else dug, and it walks out over the stair
        PlanRunner.Run(world, area, plan, home);
        Assert.Equal(plan.WayOut.ToHashSet(), Cells(0, 5, L - 5, L - 1, 0, 5).Where(world.Has).ToHashSet());
    }

    [Theory]
    [InlineData(16, 64)]
    [InlineData(8, 20)]
    public void A_pit_too_deep_for_a_straight_stair_digs_a_spiral_one_and_clears_the_rest(int side, int depth)
    {
        int top = L + 70;
        var world = new MineTestWorld().Box(-8, side + 8, 1, top - 1, -8, side + 8);
        var area = Area(0, side - 1, top - depth, top - 1, 0, side - 1);
        var home = new PathCell(-4, top, side / 2);
        var plan = world.Plan(area, home);
        output.WriteLine($"{side} x {side} x {depth}: {plan.Stations.Count} stations, stair {plan.Stair.Count}, way out {plan.WayOut.Count}, reruns {plan.Reruns}");
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Equal(0, plan.Reruns);
        // a spiral: its treads in the band 2 wide round the walls, one block each, all the way down
        Assert.NotEmpty(plan.Stair);
        Assert.All(plan.Stair, c => Assert.True(Math.Min(Math.Min(c.X, side - 1 - c.X), Math.Min(c.Z, side - 1 - c.Z)) < 2, $"{c} is off the walls"));
        Assert.True(plan.Stair.Select(c => c.Y).Distinct().Count() * 2 >= depth - 2);
        Assert.Subset(plan.Stair.ToHashSet(), plan.WayOut.ToHashSet());
        // everything but the stair dug, and what is left of it is all that stays
        Assert.Equal(Cells(0, side - 1, top - depth, top - 1, 0, side - 1).ToHashSet(), plan.Stations.SelectMany(s => s.Cells).Concat(plan.WayOut).ToHashSet());
        PlanRunner.Run(world, area, plan, home);
        Assert.Equal(plan.WayOut.ToHashSet(), Cells(0, side - 1, top - depth, top - 1, 0, side - 1).Where(world.Has).ToHashSet());
    }

    [Theory]
    [InlineData(6, 14)]
    [InlineData(8, 20)]
    [InlineData(16, 64)]
    public void Two_workers_dig_a_pit_too_deep_for_a_straight_stair_and_both_climb_out_by_its_spiral(int side, int depth)
    {
        int top = L + 70;
        var world = new MineTestWorld().Box(-8, side + 8, 1, top - 1, -8, side + 8);
        var area = Area(0, side - 1, top - depth, top - 1, 0, side - 1);
        var home = new PathCell(-4, top, side / 2);
        var plan = world.Plan(area, home);
        Assert.Empty(plan.OutOfReach);
        Assert.NotEmpty(plan.Stair);
        int total = plan.Stations.Sum(s => s.Cells.Count);
        var starts = new[] { new PathCell(-4, top, side / 2 - 3), new PathCell(-4, top, side / 2 + 3) };
        // TeamRunner checks every strike: claims apart, nothing in another's columns at any height, and
        // after each, every eidolon still standing and with a way out (the stair never cut under one)
        var team = new TeamRunner(world, area, plan, home, starts);
        team.Run(200_000);
        output.WriteLine($"{side} x {side} x {depth}: mined {string.Join(", ", team.Mined)} of {total} in {team.Seconds:0} s, {team.Replans} replans, "
            + $"left {team.Live.Remaining.Count(c => !plan.Stair.Contains(c))} off the stair, ended at {string.Join(" ", team.Positions)}");
        var cells = Cells(0, side - 1, top - depth, top - 1, 0, side - 1).Where(world.Has).ToHashSet();
        // all dug but the stair's way out, each a real share, and both out of the pit
        Assert.Subset(plan.WayOut.ToHashSet(), cells);
        // A pit 16 wide has room for both at once, each a real share; one 8 wide has room for one station's
        // claim at a time (the boxes a column apart), so the second only works when the first leaves it room.
        Assert.All(team.Mined, m => Assert.True(side >= 16 ? m >= total / 4 : m > 0, $"one mined {m} of {total}"));
        Assert.All(team.Positions, p => Assert.True(p.Y >= top, $"{p} is still in the pit"));
        Assert.True(team.AllLeave(home), "both leave");
    }

    [Fact]
    public void A_pit_whose_only_shelf_is_on_one_side_keeps_its_way_out_to_the_end()
    {
        // a pit 4 x 4, 3 deep; the ground west of it 2 lower (a shelf at y -3, feet -2), the only side it
        // climbs out by from the pit's floor (feet -3): the cells on that side are not dug before the rest
        var world = new MineTestWorld().Ground(L - 1);
        world.Solid.RemoveWhere(c => c.X < 0 && c.Y >= L - 2);
        var area = Area(0, 3, L - 3, L - 1, 0, 3);
        var home = new PathCell(-6, L - 2, 2);
        var plan = world.Plan(area, home);
        Dump(plan);
        Assert.Empty(plan.OutOfReach);
        Assert.Equal(0, plan.Reruns);
        PlanRunner.Run(world, area, plan, home);
        Assert.DoesNotContain(Cells(0, 3, L - 3, L - 1, 0, 3), world.Has);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void No_plan_digs_under_a_cell_out_of_its_reach(int high)
    {
        var (world, area) = FlatFace(high + 2);
        var plan = world.Plan(area, Home);
        var columns = plan.OutOfReach.Select(c => (c.X, c.Z)).ToHashSet();
        int lowest = plan.OutOfReach.Min(c => c.Y);
        Assert.All(plan.Stations, s => Assert.All(s.Cells, c => Assert.False(columns.Contains((c.X, c.Z)) && c.Y < lowest, $"{c} is under a cell out of reach")));
        Assert.All(plan.Held, c => Assert.DoesNotContain(plan.Stations, s => s.Cells.Contains(c)));
    }

    // ---- order ----

    [Fact]
    public void A_pit_is_dug_top_down_its_floors_within_a_climb_and_its_rows_toward_home()
    {
        // a pit 18 x 12, 2 deep, home to the south (z low): rows along x, the farthest (north) first
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 17, L - 2, L - 1, 0, 11);
        var home = new PathCell(8, L, -6);
        var plan = world.Plan(area, home);
        Assert.Equal(0, plan.Reruns);
        // no station digs two layers under the highest layer still left (the floors within a climb)
        var left = Cells(0, 17, L - 2, L - 1, 0, 11).ToHashSet();
        foreach (var s in plan.Stations)
        {
            int highest = left.Max(c => c.Y);
            Assert.All(s.Cells, c => Assert.True(c.Y >= highest - 1));
            left.ExceptWith(s.Cells);
        }
        // the top layer's stations: rows from the north (far from home) to the south, the way along a row
        // turning each row
        var top = plan.Stations.Where(s => s.Top == L - 1).ToList();
        var rows = top.Select(s => Math.Clamp(s.Stand.Z, 0, 11) / 6).ToList();
        Assert.Equal(rows.OrderByDescending(r => r), rows);
        var first = top.Where(s => Math.Clamp(s.Stand.Z, 0, 11) / 6 == 1).Select(s => s.Stand.X).ToList();
        var second = top.Where(s => Math.Clamp(s.Stand.Z, 0, 11) / 6 == 0).Select(s => s.Stand.X).ToList();
        Assert.True(first.Count > 1 && second.Count > 1);
        Assert.True(Math.Sign(first[^1] - first[0]) == -Math.Sign(second[^1] - second[0]), "the way along turns each row");
        PlanRunner.Run(world, area, plan, home);
    }

    [Fact]
    public void A_face_station_is_struck_slice_by_slice_its_top_half_before_its_bottom_half()
    {
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L).Box(-2, -1, L, L + 1, -3, 4);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var plan = world.Plan(area, Home);
        var station = plan.Stations[0];
        Assert.Equal(MineStationKind.Face, station.Kind);
        var left = station.Cells.ToHashSet();
        var order = new List<MineGroup>();
        while (MineStations.NextGroup(station, plan.Tunnel, left.Contains, station.Stand.X, station.Stand.Y, station.Stand.Z) is { } g)
        {
            order.Add(g);
            left.ExceptWith(g.Cells);
        }
        Assert.Empty(left);
        Assert.All(order, g => Assert.True(g.Upright));
        // slice 0 (x 0) top half, bottom half, then slice 1
        Assert.Equal([(0, L + 4), (0, L + 2), (1, L + 4), (1, L + 2)], order.Select(g => (g.Cells.Min(c => c.X), g.Cells.Min(c => c.Y))));
        // on open ground: the highest layer first
        var pit = new MineTestWorld().Ground(L - 1);
        var pitPlan = pit.Plan(Area(0, 5, L - 2, L - 1, 0, 5), new PathCell(-6, L, 2));
        var bench = pitPlan.Stations.First(s => s.Cells.Select(c => c.Y).Distinct().Count() > 1);
        var first = MineStations.NextGroup(bench, null, _ => true, bench.Stand.X, bench.Stand.Y, bench.Stand.Z)!;
        Assert.Contains(first.Cells, c => c.Y == bench.Top);
    }

    // ---- replanning, slicing, unloaded columns ----

    [Fact]
    public void A_plan_made_again_from_the_live_state_is_the_plan_of_the_world_as_it_is()
    {
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L).Box(-2, -1, L, L + 1, -3, 4);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var input = world.Input(area, Home);
        var plan = MinePlanner.Run(input);
        var live = input.State;
        live.BuildNow(plan.Targets);
        live.Bands = plan.Bands(live.Remaining);
        // the first two stations dug
        foreach (var s in plan.Stations.Take(2))
        {
            live.Apply(s.Cells);
            foreach (var c in s.Cells)
                world.Dig(c);
        }
        var again = MinePlanner.Run(new MinePlanInput(live, Home, _ => false, new HashSet<PathCell>()));
        var fresh = world.Plan(area, Home);
        Assert.Equal(Describe(fresh), Describe(again));
        Assert.Empty(again.OutOfReach);
    }

    private static List<string> Describe(MinePlan plan) =>
        plan.Stations.Select(s => $"{s.Kind} {s.Band} {s.Lane} {s.Slice} {s.Stand} {string.Join(",", s.Cells)}").ToList();

    /// <summary>A fake clock for <see cref="MinePlanner"/>: each reading a <paramref name="tick"/> later than
    /// the last, noting the thread's units of work at it (<see cref="MineWork"/>). The planner reads its clock
    /// as a step starts and after each checkpoint, so the units between two readings in a step are one
    /// checkpoint's work, the same on every machine.</summary>
    private sealed class CheckpointClock(TimeSpan tick)
    {
        private long _readings, _last;
        private bool _atStart;

        /// <summary>The checkpoints' units, in order, and how many checkpoints each step made.</summary>
        public List<long> Gaps { get; } = new();
        public List<int> PerStep { get; } = new();

        public TimeSpan Read()
        {
            long now = MineWork.Units;
            if (_atStart)
                _atStart = false;
            else
            {
                Gaps.Add(now - _last);
                PerStep[^1]++;
            }
            _last = now;
            return tick * _readings++;
        }

        /// <summary>Runs <paramref name="planner"/> (made with <see cref="Read"/>) to its plan, a step of
        /// <paramref name="budget"/> at a time.</summary>
        public void Run(MinePlanner planner, TimeSpan budget)
        {
            do
            {
                PerStep.Add(0);
                _atStart = true;
            }
            while (!planner.Step(budget));
        }
    }

    /// <summary>The most units of work one checkpoint may do (<see cref="MineWork"/>, about a cell or node
    /// looked at each). The benchmark (benchmarks/, README "Eidolon", mining) measures a unit at about
    /// 2.5 ns on the largest areas and 15 to 30 ns on small tunnels (warm, Release; the hash sets and the
    /// guard's searches cost more than a unit says), and the longest checkpoint, collections and the JIT's
    /// first tier aside, under 1 ms on the tunnels and 1 to 2 ms on the largest areas: so a step, which may
    /// overrun the tick's <see cref="MinePlanner.BudgetMs"/> by one checkpoint, stays within about twice it. The
    /// longest in units now: about 40 000 on the stepped slope (64 stands of the reach bound's search, each
    /// its reach box and joins), and on the largest areas about 86 000, a graph's window-sized arrays
    /// allocated and filled (the 1024 x 1 x 16 area's window is 319 088 blocks). A loop with no checkpoint
    /// over the window, or over the area's cells with any work in it, goes past it on the largest areas.</summary>
    private const long MaxCheckpointUnits = 100_000;

    [Fact]
    public void A_plan_made_in_slices_is_the_plan_made_at_once_and_keeps_to_its_slices()
    {
        var world = SteppedSlope(8, z1: 6);
        var area = Area(-2, 7, L, L + 7, 0, 3);
        var whole = MinePlanner.Run(world.Input(area, Home));

        // On a fake clock a quarter of the budget a checkpoint, every step but the last stops at its
        // fourth: the first checkpoint past its budget, exactly.
        var budget = TimeSpan.FromMilliseconds(MinePlanner.BudgetMs);
        var clock = new CheckpointClock(budget / 4);
        var sliced = new MinePlanner(world.Input(area, Home), clock.Read);
        clock.Run(sliced, budget);
        Assert.Equal(Describe(whole), Describe(sliced.Plan!));
        Assert.Equal(MinePlanPhase.Done, sliced.Phase);
        Assert.True(sliced.Steps > 1);
        Assert.Equal(sliced.Steps, clock.PerStep.Count);
        Assert.All(clock.PerStep.SkipLast(1), n => Assert.Equal(4, n));
        Assert.InRange(clock.PerStep[^1], 1, 4);
        Assert.Equal(budget / 4 * clock.Gaps.Count, sliced.Spent.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b));

        // and no checkpoint does much work (a step overruns its budget by at most one)
        long longest = clock.Gaps.Max();
        output.WriteLine($"{sliced.Steps} steps, {clock.Gaps.Count} checkpoints, {clock.Gaps.Sum()} units; the longest checkpoint {longest} units");
        Assert.True(longest <= MaxCheckpointUnits, $"a checkpoint did {longest} units of work");
    }

    [Fact]
    public void A_plan_made_on_another_thread_is_the_plan_made_on_this_one()
    {
        // as the game makes it: its own copies of the site, the planner and the live state over it on a
        // worker, nothing shared; the plan, and the state's targets and walk graph, as made here
        var world = SteppedSlope(8, z1: 6);
        var area = Area(-2, 7, L, L + 7, 0, 3);
        var here = world.Plan(area, Home);
        var site = world.Site(area);
        var (plan, state) = Task.Run(() =>
        {
            var made = MinePlanner.Run(new MinePlanInput(new MineState(site.Clone(), Home), Home, _ => false, new HashSet<PathCell>()));
            var live = new MineState(site.Clone(), Home);
            live.AdoptNow(made);
            return (made, live);
        }).Result;
        Assert.Equal(Describe(here), Describe(plan));
        var live = new MineState(world.Site(area), Home);
        live.AdoptNow(here);
        Assert.Equal(live.Remaining.ToHashSet(), state.Remaining.ToHashSet());
        Assert.Equal(live.Walk.Nodes.ToHashSet(), state.Walk.Nodes.ToHashSet());
    }

    [Fact]
    public void Columns_not_loaded_are_no_targets_until_they_load()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 7, L - 2, L - 1, 0, 7);
        world.Unloaded.UnionWith([(6, 6), (6, 7), (7, 6), (7, 7)]);
        var input = world.Input(area, new PathCell(-6, L, 4));
        var site = input.State.Site;
        Assert.True(site.Solid(7, L + 2, 7));
        Assert.Equal(4, site.UnloadedColumns.Count);
        var plan = MinePlanner.Run(input);
        Assert.True(plan.Partial);
        Assert.DoesNotContain(plan.Stations.SelectMany(s => s.Cells), c => c.X >= 6 && c.Z >= 6);
        Assert.Empty(plan.OutOfReach);
        // loaded: planned again, they are dug too
        world.Unloaded.Clear();
        foreach (var (x, z) in site.UnloadedColumns.ToList())
            site.Reload(world, x, z);
        Assert.Empty(site.UnloadedColumns);
        var again = MinePlanner.Run(input);
        Assert.False(again.Partial);
        Assert.Equal(8 * 8 * 2, again.Stations.Sum(s => s.Cells.Count));
        PlanRunner.Run(world, area, again, new PathCell(-6, L, 4));
    }

    // ---- cost ----

    // The bound is the design's 1 s for code built to run (Release, as the game loads it); a test build's
    // unoptimised code runs several times slower, so it is given 5 s. Either way the time is logged.
#if DEBUG
    private const int CostBoundMs = 5000;
#else
    private const int CostBoundMs = 1000;
#endif

    // A slice of planning (one step of its work) is bounded too, so a tick spends no more than the budget
    // and the step it overran by: 3 ms for Release; Debug's unoptimised code is given 8 times that. A busy
    // machine stops a thread now and then for longer (the collector, other processes), so a few steps of a
    // run (out of tens of thousands) may go over; a step of real work over it comes every run.
#if DEBUG
    private const double StepBoundMs = 24;
#else
    private const double StepBoundMs = 3;
#endif
    private const int StepsOverAllowed = 6;

    [Theory]
    [InlineData(128, 1, 128)]
    [InlineData(16, 64, 16)]
    [InlineData(1024, 1, 16)]
    public void A_largest_area_is_planned_in_bounded_time(int sizeX, int sizeY, int sizeZ)
    {
        int top = L + 70;
        var world = new MineTestWorld().Box(-8, sizeX + 8, 1, top - 1, -8, sizeZ + 8);
        var area = Area(0, sizeX - 1, top - sizeY, top - 1, 0, sizeZ - 1);
        Assert.Equal(EidolonMining.MaxAreaBlocks, area.Volume);
        var input = world.Input(area, new PathCell(-4, top, sizeZ / 2));
        // once to warm the code up, as a server that has planned before, and to bound its checkpoints' work
        var units = new CheckpointClock(TimeSpan.Zero);
        units.Run(new MinePlanner(input, units.Read), TimeSpan.MaxValue);
        long longest = units.Gaps.Max();
        Assert.True(longest <= MaxCheckpointUnits, $"a checkpoint did {longest} units of work");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var planner = MinePlanner.Make(input);
        var plan = planner.Plan!;
        output.WriteLine($"{sizeX} x {sizeY} x {sizeZ}: {clock.ElapsedMilliseconds} ms, {units.Gaps.Count} checkpoints, the longest {longest} units, {plan.Stations.Count} stations, {plan.Stations.Sum(s => s.Cells.Count)} cells, out of reach {plan.OutOfReach.Count}, stair {plan.Stair.Count}, reruns {plan.Reruns}; {string.Join(", ", planner.Spent.Select(p => $"{p.Key} {p.Value.TotalMilliseconds:0} ms"))}");
        Assert.True(clock.ElapsedMilliseconds < CostBoundMs, $"{clock.ElapsedMilliseconds} ms");
        // each step of its work, as the game takes them (Step returns after the step that spends its budget)
        int fewest = int.MaxValue;
        for (int run = 0; run < 3 && fewest > StepsOverAllowed; run++)
        {
            var sliced = new MinePlanner(input);
            var steps = new List<double>();
            while (true)
            {
                var step = System.Diagnostics.Stopwatch.StartNew();
                bool done = sliced.Step(TimeSpan.Zero);
                steps.Add(step.Elapsed.TotalMilliseconds);
                if (done)
                    break;
            }
            int over = steps.Count(ms => ms > StepBoundMs);
            fewest = Math.Min(fewest, over);
            output.WriteLine($"  sliced: {steps.Count} steps, the longest {steps.Max():0.0} ms, {over} over {StepBoundMs} ms");
        }
        Assert.True(fewest <= StepsOverAllowed, $"{fewest} steps over {StepBoundMs} ms in the best of three runs");
    }
}
