namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A stop of a route: a station to work, or a planned wait.</summary>
public enum MineStopKind { Station, Wait }

/// <summary>One stop of a member's route (README "Eidolon", working together, *Routes*): the station it works
/// (or -1 for a wait) at <paramref name="At"/>, whether ahead of its band (<see cref="MineAhead"/>), the
/// nodes walked from the previous stop's place (first: that place), and when it sets off, gets there,
/// starts work and is done (a wait: from getting there until <paramref name="Finish"/>), in seconds on the
/// job's clock.</summary>
public sealed record MineStop(MineStopKind Kind, int Station, PathCell At, bool Ahead,
    IReadOnlyList<PathCell> Leg,
    double Depart, double Arrive, double Start, double Finish)
{
    /// <summary>Its number in the plan, rising along each route: its slots' <see cref="MineSlot.Stop"/>.</summary>
    public int Seq { get; init; }

    /// <summary>The cells it was planned to dig (the station's, cut to fit round the others' claims).</summary>
    public IReadOnlyList<MarkPos> Cells { get; init; } = [];

    /// <summary>The leg's walking seconds and the work's, as estimated (repair re-times with them).</summary>
    public double LegSeconds { get; init; }

    public double WorkSeconds { get; init; }

    /// <summary>A head the member already walks to or works (a re-solve keeps it first).</summary>
    public bool Pinned { get; init; }
}

/// <summary>A member's route (README "Eidolon", working together, *Routes*): its stops in order.</summary>
public sealed record MineRoute(long Member, IReadOnlyList<MineStop> Stops)
{
    /// <summary>When it is done with its pinned head (the solve's time if none).</summary>
    public double FreeAt { get; init; }

    public MineStop? Head => Stops.Count > 0 ? Stops[0] : null;
}

/// <summary>A present member as a solve takes it: the node it is at, its pinned head (the station it walks to
/// or works, null for none) and whether that is ahead, and when it is done with it.</summary>
public readonly record struct MineRouteMember(long Id, PathCell At, int? Head, bool HeadAhead, double FreeAt);

/// <summary>What a solve takes: the station plan and its version, a copy of the live state (the solve digs
/// in it), the present members, eidolons parked over the area (no holds), the time, the routes running now
/// (for a re-solve, <see cref="MineRoutePlanner.Solve"/> compares), and which cells may be dug for now.</summary>
public sealed record MineRouteInput(MinePlan Plan, int PlanVersion, MineState State, IReadOnlyList<MineRouteMember> Members,
    IReadOnlyList<MineWorkerAt> Parked, double Now, MineRoutes? Current, Func<MarkPos, bool>? Usable);

/// <summary>What a solve did, for the debug command: decisions taken, guard checks, searches, relocations
/// kept, validations run, whether the improved routes held, whether the routes running were kept, and how
/// long it took.</summary>
public sealed record MineRouteStats(int Decisions, int GuardChecks, int Searches, int Relocations, int Validations,
    bool Improved, bool KeptCurrent, TimeSpan Took);

/// <summary>
/// A job's route plan (README "Eidolon", working together, *Routes*): each present member's next stations
/// (<see cref="MinStops"/> to <see cref="MaxStops"/>), in the order that finishes the whole job soonest,
/// timed; the narrow ways of the dug-so-far world (<see cref="Zones"/>) and who may be in each when
/// (<see cref="Slots"/>). Made off the main thread (<see cref="MineRoutePlanner"/>), then main thread only
/// once swapped in, where small events are repaired in place rather than solved again: each repair re-times
/// the member's stops from the stored leg and work seconds, its slots moved with them and the others' never,
/// and says when a full solve is wanted, and why (null: none).
/// </summary>
public sealed class MineRoutes
{
    /// <summary>How far ahead each member is planned: its stations are this many seconds' work, within
    /// <see cref="MinStops"/> and <see cref="MaxStops"/>.</summary>
    public const double HorizonSeconds = 120;

    public const int MinStops = 5, MaxStops = 10;

    /// <summary>A re-solve replaces routes only this much better.</summary>
    public const double ImproveShare = 0.10;

    /// <summary>A periodic re-solve at most this often.</summary>
    public const double ResolveSeconds = 15;

    /// <summary>A member may set off this early for its slot.</summary>
    public const double SlotEarly = 2;

    /// <summary>Later than this past its slot, its slots are made again.</summary>
    public const double SlotLate = 10;

    /// <summary>A second drop this soon after one asks for a solve.</summary>
    public const double DropsSeconds = 10;

    private const int LogLines = 32;

    private readonly Dictionary<long, MineRoute> _routes;
    private readonly List<string> _log = new();
    private readonly Dictionary<long, double> _dropped = new();
    private int _seq;

    internal MineRoutes(MinePlan plan, int planVersion, double madeAt, Dictionary<long, MineRoute> routes, IReadOnlyList<MineZone> zones,
        MineZoneIndex zoneIndex, MineSlots slots, (double J, double Walk, double Wait) score, MineRouteStats stats, int seq)
    {
        Plan = plan;
        PlanVersion = planVersion;
        MadeAt = madeAt;
        _routes = routes;
        Zones = zones;
        ZoneIndex = zoneIndex;
        Slots = slots;
        Score = score;
        Stats = stats;
        _seq = seq;
    }

    /// <summary>The station plan its stops are of.</summary>
    public MinePlan Plan { get; private set; }

    public int PlanVersion { get; private set; }

    public double MadeAt { get; }

    public IReadOnlyDictionary<long, MineRoute> Routes => _routes;

    public IReadOnlyList<MineZone> Zones { get; }

    public MineZoneIndex ZoneIndex { get; }

    public MineSlots Slots { get; private set; }

    /// <summary>The objective (<see cref="MineRoutePlanner"/>): the estimated finish of the whole job from
    /// when it was made, the walking, and the waiting, in seconds.</summary>
    public (double J, double Walk, double Wait) Score { get; }

    public MineRouteStats Stats { get; internal set; }

    /// <summary>Which of the planner's ways of choosing made them (a re-solve against them keeps it).</summary>
    public int VariantIndex { get; internal set; } = -1;

    /// <summary>Stops dropped when these routes were evaluated again (<see cref="MineRoutePlanner.Evaluate"/>).</summary>
    public int Dropped { get; internal set; }

    /// <summary>The last repairs, for the debug command.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>Its stations' ids, for "taken" in the fallback dispatcher.</summary>
    public IReadOnlySet<int> Assigned =>
        _routes.Values.SelectMany(r => r.Stops).Where(s => s.Kind == MineStopKind.Station).Select(s => s.Station).ToHashSet();

    // ---- repair (main thread) ----

    /// <summary>It begins its first strike at <paramref name="station"/>: that stop pinned first, its work
    /// from now, the rest re-timed.</summary>
    public string? Started(long member, int station, double now)
    {
        if (!_routes.TryGetValue(member, out var route) || Find(route, station) is not { } k)
            return null;
        var stop = route.Stops[k];
        var stops = route.Stops.Skip(k + 1).Prepend(stop with { Start = now, Finish = now + stop.WorkSeconds, Pinned = true }).ToList();
        Set(member, stops);
        Note($"{member} started #{station}{(k > 0 ? $", {k} stops before it skipped" : "")}");
        return Retime(member, now) ? null : "late";
    }

    /// <summary>Its head has no cells left: taken off, the rest re-timed from now at its stand; with no
    /// station left while one may be dug that no route holds, a solve is wanted ("empty").</summary>
    public string? Finished(long member, int station, double now, MineState live)
    {
        if (!_routes.TryGetValue(member, out var route) || Find(route, station) is not { } k)
            return null;
        Set(member, route.Stops.Skip(k + 1).ToList());
        Note($"{member} finished #{station}");
        bool timed = Retime(member, now, route.Stops[k].At);
        if (!_routes[member].Stops.Any(s => s.Kind == MineStopKind.Station) && Unrouted(live))
            return "empty";
        return timed ? null : "late";
    }

    /// <summary>A planned wait done: taken off, the rest re-timed from now.</summary>
    public string? Waited(long member, double now)
    {
        if (!_routes.TryGetValue(member, out var route) || route.Head is not { Kind: MineStopKind.Wait } head)
            return null;
        Set(member, route.Stops.Skip(1).ToList());
        return Retime(member, now, head.At) ? null : "late";
    }

    /// <summary>Its stop at <paramref name="station"/> refused live (the guard, its claim, a bad stand, a
    /// deadlock): taken off, its slots let go and its later legs placed again; a second drop within
    /// <see cref="DropsSeconds"/>, or no station left, asks for a solve ("drops").</summary>
    public string? Drop(long member, int station, MineRefusal why, double now)
    {
        if (!_routes.TryGetValue(member, out var route) || Find(route, station) is not { } k)
            return null;
        var at = route.Stops[k].At;
        Set(member, route.Stops.Where((_, i) => i != k).ToList());
        Note($"{member} dropped #{station}: {why}");
        bool again = _dropped.TryGetValue(member, out double last) && now - last <= DropsSeconds;
        _dropped[member] = now;
        bool timed = Retime(member, now, k == 0 ? null : at);
        if (again || !_routes[member].Stops.Any(s => s.Kind == MineStopKind.Station))
            return "drops";
        return timed ? null : "late";
    }

    /// <summary>The live state moved: stops whose station has no cell left (another dug it, a player broke
    /// it) are taken off every route; one whose cells shrank keeps its place, its work estimated again.</summary>
    public string? Sweep(MineState live, double now)
    {
        foreach (long member in _routes.Keys.Order().ToList())
        {
            var route = _routes[member];
            bool changed = false;
            var stops = new List<MineStop>();
            foreach (var s in route.Stops)
            {
                if (s.Kind == MineStopKind.Wait)
                {
                    stops.Add(s);
                    continue;
                }
                var left = s.Cells.Where(live.Remaining.Contains).ToList();
                if (left.Count == 0)
                {
                    changed = true;
                    Note($"{member} #{s.Station} gone (no cells left)");
                    continue;
                }
                if (left.Count < s.Cells.Count)
                {
                    changed = true;
                    stops.Add(s with { Cells = left, WorkSeconds = MineCosts.WorkSeconds(live.Site, left) });
                }
                else
                    stops.Add(s);
            }
            if (!changed)
                continue;
            Set(member, stops);
            Retime(member, now);
        }
        return null;
    }

    /// <summary>A member away or gone: its stations go, the cheapest first, each into the route and place
    /// that adds least to that route's finish (estimates only), where every stop before it may come first and
    /// none after it must; one that fits nowhere (every route full) asks for a solve ("members").</summary>
    public string? Gone(long member, double now, MineState live)
    {
        if (!_routes.Remove(member, out var route))
            return null;
        Slots.Release(member);
        Note($"{member} gone, {route.Stops.Count(s => s.Kind == MineStopKind.Station)} stations freed");
        var freed = route.Stops.Where(s => s.Kind == MineStopKind.Station)
            .Select(s => s with { Cells = s.Cells.Where(live.Remaining.Contains).ToList(), Pinned = false })
            .Where(s => s.Cells.Count > 0)
            .OrderBy(s => s.WorkSeconds).ThenBy(s => s.Station).ToList();
        bool all = true;
        var touched = new SortedSet<long>();
        foreach (var stop in freed)
        {
            (long Member, int Index, double Added)? best = null;
            foreach (long m in _routes.Keys.Order())
            {
                var stops = _routes[m].Stops;
                if (stops.Count(s => s.Kind == MineStopKind.Station) >= MaxStops)
                    continue;
                int first = stops.Count > 0 && stops[0].Pinned ? 1 : 0;
                for (int i = first; i <= stops.Count; i++)
                {
                    if (!Allowed(stops, i, stop))
                        continue;
                    double added = Added(stops, i, stop, now);
                    if (best is not { } b || added < b.Added - 1e-9)
                        best = (m, i, added);
                }
            }
            if (best is not { } chosen)
            {
                all = false;
                continue;
            }
            var list = _routes[chosen.Member].Stops.ToList();
            var from = chosen.Index > 0 ? list[chosen.Index - 1].At : list.Count > 0 ? list[0].Leg.FirstOrDefault(list[0].At) : stop.At;
            list.Insert(chosen.Index, stop with { Leg = [from, stop.At], LegSeconds = MineCosts.Estimate(from, stop.At) });
            Set(chosen.Member, list);
            touched.Add(chosen.Member);
            Note($"#{stop.Station} to {chosen.Member} at {chosen.Index}");
        }
        foreach (long m in touched)
            Retime(m, now);
        return all ? null : "members";
    }

    /// <summary>It sets off later than <see cref="SlotLate"/> past its slot: its slots made again from now,
    /// later than anyone's already placed, never moving another's; its head pushed past the horizon asks for
    /// a solve ("late").</summary>
    public string? Late(long member, double now)
    {
        if (!_routes.ContainsKey(member))
            return null;
        Note($"{member} late");
        bool timed = Retime(member, now);
        var head = _routes[member].Head;
        return !timed || head != null && head.Start > now + HorizonSeconds ? "late" : null;
    }

    /// <summary>A new station plan swapped in: each stop kept as the new plan's station at the same stand, of
    /// the same kind, with a cell of it still to dig, else taken off; a solve is always wanted ("plan").</summary>
    public string? Remap(MinePlan plan, int planVersion, MineState live, double now)
    {
        var byStand = plan.Stations.GroupBy(s => s.Stand).ToDictionary(g => g.Key, g => g.ToList());
        foreach (long member in _routes.Keys.Order().ToList())
        {
            var stops = new List<MineStop>();
            foreach (var s in _routes[member].Stops)
            {
                if (s.Kind == MineStopKind.Wait)
                {
                    stops.Add(s);
                    continue;
                }
                var kind = Plan.Stations.ElementAtOrDefault(s.Station)?.Kind;
                var cells = s.Cells.ToHashSet();
                var match = byStand.GetValueOrDefault(s.At)?.FirstOrDefault(n => n.Kind == kind && n.Cells.Any(c => cells.Contains(c) && live.Remaining.Contains(c)));
                if (match != null)
                    stops.Add(s with { Station = match.Id, Cells = match.Cells.Where(live.Remaining.Contains).ToList() });
            }
            Set(member, stops);
        }
        Plan = plan;
        PlanVersion = planVersion;
        Note($"remapped to plan {planVersion}");
        foreach (long member in _routes.Keys.Order().ToList())
            Retime(member, now);
        return "plan";
    }

    /// <summary>Its first stop, or null.</summary>
    public MineStop? Head(long member) => _routes.TryGetValue(member, out var r) ? r.Head : null;

    /// <summary>Its stop after the head.</summary>
    public MineStop? Next(long member) => _routes.TryGetValue(member, out var r) && r.Stops.Count > 1 ? r.Stops[1] : null;

    /// <summary>The fallback dispatcher gave it <paramref name="station"/>: its pinned head, taken off any other route.</summary>
    public void Adopt(long member, MineStation station, bool ahead, double now)
    {
        foreach (long m in _routes.Keys.Order().ToList())
            if (Find(_routes[m], station.Id) is { } k)
                Set(m, _routes[m].Stops.Where((_, i) => i != k).ToList());
        double work = Groups(station.Cells.Count);
        var head = new MineStop(MineStopKind.Station, station.Id, station.Stand, ahead, [station.Stand], now, now, now, now + work)
        {
            Cells = station.Cells, WorkSeconds = work, Pinned = true,
        };
        var stops = _routes.TryGetValue(member, out var route) ? route.Stops.Where(s => !s.Pinned).Prepend(head).ToList() : [head];
        Set(member, stops);
        Note($"{member} adopted #{station.Id}");
        foreach (long m in _routes.Keys.Order().ToList())
            Retime(m, now);
    }

    /// <summary>A deep copy, for a solve's <see cref="MineRouteInput.Current"/>.</summary>
    public MineRoutes Copy()
    {
        var copy = new MineRoutes(Plan, PlanVersion, MadeAt, new Dictionary<long, MineRoute>(_routes), Zones, ZoneIndex, Slots.Clone(), Score, Stats, _seq)
        {
            Dropped = Dropped, VariantIndex = VariantIndex,
        };
        copy._log.AddRange(_log);
        foreach (var (k, v) in _dropped)
            copy._dropped[k] = v;
        return copy;
    }

    // ---- inside ----

    private static int? Find(MineRoute route, int station)
    {
        for (int k = 0; k < route.Stops.Count; k++)
            if (route.Stops[k].Kind == MineStopKind.Station && route.Stops[k].Station == station)
                return k;
        return null;
    }

    private void Set(long member, List<MineStop> stops)
    {
        double free = stops.Count > 0 && stops[0].Pinned ? stops[0].Finish : _routes.TryGetValue(member, out var r) ? r.FreeAt : MadeAt;
        _routes[member] = new MineRoute(member, stops) { FreeAt = free };
    }

    private void Note(string line)
    {
        _log.Add(line);
        if (_log.Count > LogLines)
            _log.RemoveAt(0);
    }

    // a strike of granite at the steel pick a group of four: an estimate where the site's paces are not to hand
    private static double Groups(int cells) =>
        (cells + 3) / 4 * EidolonMining.Timing(EidolonMining.StrikeSeconds([MineCosts.DefaultPlayerSeconds])).Seconds;

    // some station with cells it may dig now (or ahead) that no route holds
    private bool Unrouted(MineState live)
    {
        var assigned = Assigned;
        foreach (var s in Plan.Stations)
            if (!assigned.Contains(s.Id) && s.Cells.Any(live.Remaining.Contains)
                && (MineDispatch.Cells(live, s).Count > 0 || MineDispatch.Cells(live, s, true).Count > 0))
                return true;
        return false;
    }

    // whether a stop may go in at position i: all that must come before it are before, all it must come before after
    private bool Allowed(IReadOnlyList<MineStop> stops, int i, MineStop stop)
    {
        for (int k = 0; k < stops.Count; k++)
        {
            if (stops[k].Kind != MineStopKind.Station)
                continue;
            if (k < i && MineRoutePlanner.Must(Plan, stop, stops[k]))
                return false;
            if (k >= i && MineRoutePlanner.Must(Plan, stops[k], stop))
                return false;
        }
        return true;
    }

    // how much later a route finishes with a stop put in at i, from estimates
    private double Added(IReadOnlyList<MineStop> stops, int i, MineStop stop, double now)
    {
        PathCell? prev = i > 0 ? stops[i - 1].At : stops.Count > 0 ? stops[0].Leg.FirstOrDefault(stops[0].At) : null;
        PathCell? next = i < stops.Count ? stops[i].At : null;
        double added = stop.WorkSeconds + MineCosts.ArriveSeconds;
        if (prev is { } p)
            added += MineCosts.Estimate(p, stop.At);
        if (next is { } n)
            added += MineCosts.Estimate(stop.At, n) - (prev is { } q ? MineCosts.Estimate(q, n) : 0);
        return added;
    }

    /// <summary>
    /// The member's stops timed again in order from <paramref name="now"/> (and from <paramref name="at"/>,
    /// where it is, when its first stop's leg no longer starts there): each sets off when the one before is
    /// done and its zones' slots allow (<see cref="MineSlots.Earliest"/>), its leg and work as stored; a leg
    /// whose start moved is estimated. Its slots are made again with them; the others' are never moved. False
    /// when a leg found no slot within the horizon.
    /// </summary>
    private bool Retime(long member, double now, PathCell? at = null)
    {
        if (!_routes.TryGetValue(member, out var route))
            return true;
        Slots.Release(member);
        var timed = new List<MineStop>();
        PathCell? place = at;
        double free = now;
        bool ok = true;
        for (int i = 0; i < route.Stops.Count; i++)
        {
            var s = route.Stops[i] with { Seq = ++_seq };
            if (i == 0 && s.Pinned)
            {
                double finish = Math.Max(s.Finish, now);
                if (ZoneIndex.Of(s.At) is { } z)
                    Slots.Hold(member, s.Seq, z.Id, now, double.PositiveInfinity);
                timed.Add(s with { Finish = finish });
                place = s.At;
                free = finish;
                continue;
            }
            var leg = s.Leg;
            double legSeconds = s.LegSeconds;
            if (place is { } p && (leg.Count == 0 || leg[0] != p))
            {
                leg = p == s.At ? [p] : [p, s.At];
                legSeconds = MineCosts.Estimate(p, s.At);
            }
            var runs = Runs(leg, legSeconds);
            double depart = Slots.Earliest(member, free, runs);
            if (double.IsPositiveInfinity(depart))
            {
                ok = false;
                depart = free;
                runs = [];
            }
            Slots.Reserve(member, s.Seq, depart, runs);
            double arrive = depart + legSeconds;
            double start = s.Kind == MineStopKind.Station ? arrive + MineCosts.ArriveSeconds : arrive;
            double end = s.Kind == MineStopKind.Station ? start + s.WorkSeconds : Math.Max(s.Finish, arrive);
            timed.Add(s with { Leg = leg, LegSeconds = legSeconds, Depart = depart, Arrive = arrive, Start = start, Finish = end });
            place = s.At;
            free = end;
        }
        _routes[member] = route with { Stops = timed, FreeAt = timed.Count > 0 && timed[0].Pinned ? timed[0].Finish : now };
        return ok;
    }

    // a leg's zone runs, scaled to its seconds (an estimated leg of two nodes is taken as straight), its end held on
    private IReadOnlyList<(int Zone, double Enter, double Leave)> Runs(IReadOnlyList<PathCell> leg, double legSeconds)
    {
        var runs = ZoneIndex.Timed(leg, holdEnd: true);
        double walked = 0;
        for (int k = 1; k < leg.Count; k++)
            walked += MineCosts.StepCost(leg[k - 1], leg[k]) * MineCosts.WalkSecondsPerBlock;
        if (walked <= 0 || Math.Abs(walked - legSeconds) < 1e-9)
            return runs;
        double scale = legSeconds / walked;
        return runs.Select(r => (r.Zone, r.Enter * scale, double.IsPositiveInfinity(r.Leave) ? r.Leave : r.Leave * scale)).ToList();
    }
}
