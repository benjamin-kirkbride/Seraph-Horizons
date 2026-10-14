using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>A voxel world for the mine planner's tests: solid blocks (each mined, unless a rule says
/// otherwise), rule overrides, each block's pace (<see cref="Pace"/>, granite at the steel pick where not
/// set), and block columns not loaded; with builders for the ground, boxes of rock and hills.</summary>
internal sealed class MineTestWorld : IMineWorld
{
    public readonly HashSet<(int X, int Y, int Z)> Solid = new();
    public readonly Dictionary<MarkPos, MineVoxel> Rules = new();
    public readonly HashSet<(int X, int Z)> Unloaded = new();

    /// <summary>Player seconds to break a block (<see cref="MineVoxel.Seconds"/>), where not
    /// <see cref="MineCosts.DefaultPlayerSeconds"/>.</summary>
    public readonly Dictionary<MarkPos, float> Pace = new();

    public bool Loaded(int x, int z) => !Unloaded.Contains((x, z));

    public MineVoxel Read(int x, int y, int z) =>
        Rules.TryGetValue(new MarkPos(x, y, z), out var v) ? v
            : Solid.Contains((x, y, z)) ? new MineVoxel(true, MineRule.Mine, Pace.GetValueOrDefault(new MarkPos(x, y, z), (float)MineCosts.DefaultPlayerSeconds))
            : new MineVoxel(false, MineRule.Air);

    public bool Has(MarkPos c) => Solid.Contains((c.X, c.Y, c.Z));

    public void Dig(MarkPos c)
    {
        Solid.Remove((c.X, c.Y, c.Z));
        Rules.Remove(c);
    }

    public MineTestWorld Box(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        foreach (var c in Cells(x0, x1, y0, y1, z0, z1))
            Solid.Add((c.X, c.Y, c.Z));
        return this;
    }

    /// <summary>Flat ground: solid from <paramref name="bottom"/> up to <paramref name="top"/>, wide.</summary>
    public MineTestWorld Ground(int top, int bottom = 1, int from = -40, int to = 40) => Box(from, to, bottom, top, from, to);

    /// <summary>A hill: a box of rock on the ground.</summary>
    public MineTestWorld Hill(int x0, int x1, int y1, int z0, int z1, int y0 = 0) => Box(x0, x1, y0, y1, z0, z1);

    public static IEnumerable<MarkPos> Cells(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            yield return new MarkPos(x, y, z);
    }

    public static MarkArea Area(int x0, int x1, int y0, int y1, int z0, int z1) =>
        MarkArea.Between(new MarkPos(x0, y0, z0), new MarkPos(x1, y1, z1));

    public MineSite Site(MarkArea area, int step = 2)
    {
        var site = new MineSite(area, step);
        foreach (var _ in site.Load(this)) { }
        return site;
    }

    public MinePlanInput Input(MarkArea area, PathCell home, Func<MarkPos, bool>? skip = null) =>
        new(new MineState(Site(area), home), home, skip ?? (_ => false), new HashSet<PathCell>());

    public MinePlan Plan(MarkArea area, PathCell home) => MinePlanner.Run(Input(area, home));
}

/// <summary>
/// Runs a plan in a <see cref="MineTestWorld"/> station by station, checking it as the eidolon would live
/// it, with a real <see cref="WidePath"/> (climb and drop 2): every stand reached from the last (or from
/// home), every cell in reach and none under it, the stand still a place it may stand after its dig and
/// a way from it to the window's side; at the end, a way back home.
/// </summary>
internal static class PlanRunner
{
    public const int MaxNodes = 200_000;

    public static MineSite Run(MineTestWorld world, MarkArea area, MinePlan plan, PathCell home)
    {
        var site = world.Site(area);
        var path = new WidePath(site, 2, 2);
        var start = Start(site, home);
        var at = start;
        foreach (var station in plan.Stations)
        {
            var s = station.Stand;
            Assert.All(station.Cells, c => Assert.True(EidolonMining.InReach(s.X, s.Y, s.Z, c), $"station {station.Id}: {c} out of reach of {s}"));
            Assert.True(site.Standable(s), $"station {station.Id}: no place to stand at {s}");
            Assert.True(path.Find(at, s, MaxNodes) != null, $"station {station.Id}: no way from {at} to {s}");
            foreach (var c in station.Cells)
            {
                Assert.True(site.Solid(c.X, c.Y, c.Z), $"station {station.Id}: {c} already dug");
                site.Dig(c);
                world.Dig(c);
            }
            Assert.True(site.Standable(s), $"station {station.Id}: its dig left no place to stand at {s}");
            Assert.True(ReachesSide(site, s), $"station {station.Id}: no way out from {s}");
            at = s;
        }
        Assert.True(path.Find(at, start, MaxNodes) != null || ReachesSide(site, at), $"no way back from {at}");
        return site;
    }

    /// <summary>Home if it stands there in the window, else the way out nearest it.</summary>
    public static PathCell Start(MineSite site, PathCell home) =>
        site.Standable(home) ? home : site.Exits(home).MinBy(e => (e.X - home.X) * (e.X - home.X) + (e.Z - home.Z) * (e.Z - home.Z) + Math.Abs(e.Y - home.Y));

    /// <summary>Whether a walk from <paramref name="from"/> gets to the window's side (the world past it).</summary>
    public static bool ReachesSide(MineSite site, PathCell from)
    {
        var path = new WidePath(site, 2, 2);
        var seen = new HashSet<PathCell> { from };
        var queue = new Queue<PathCell>([from]);
        (int, int)[] dirs = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
        while (queue.TryDequeue(out var n))
        {
            if (site.OnRing(n.X, n.Z))
                return true;
            if (seen.Count > MaxNodes)
                return false;
            foreach (var (dx, dz) in dirs)
                if (path.Step(n, dx, dz, out _) is { } m && seen.Add(m))
                    queue.Enqueue(m);
        }
        return false;
    }
}

/// <summary>
/// Several eidolons working one plan together, a discrete-event loop: each takes stations by
/// <see cref="MineDispatch.Next"/>, claims them on a <see cref="JobBoard{TUnit, TShared}"/>, walks there
/// (<see cref="MineDispatch.WalkSecondsPerBlock"/> a block of its path) and strikes group by group
/// (<see cref="MineStations.NextGroup"/>, each as long as the game's strike at the pace of its slowest block,
/// <see cref="StrikeSeconds"/>, times a seeded jitter if asked), every strike checked by the guard against the
/// live state with the others where they are. At every strike it checks that claims are disjoint and no cell
/// is in another's box columns; after every strike, that every eidolon still stands and is out. It counts what
/// the routes are measured by (README "Eidolon", working together, *Routes*): the makespan, each one's idle
/// seconds (with no station), blocks walked and waits.
/// </summary>
internal sealed class TeamRunner
{
    private sealed class Worker(long id, PathCell at)
    {
        public readonly long Id = id;
        public readonly PathCell Rest = at;
        public PathCell At = at;
        public double FreeAt;
        public MineStation? Station;
        public int Mined;
        public bool Done;
        public bool Waiting;
        public double? ThroughSince;
        public int Waits;
        public double Walked;
        public readonly List<(double From, double To)> Idle = new();
    }

    private readonly Random? _jitter;
    private readonly bool _crossing;

    private readonly MineTestWorld _world;
    private readonly PathCell _home;
    private MinePlan _plan;
    private bool _replanned;
    private readonly MineState _live;
    private readonly JobBoard<(int X, int Z), MineJobCells> _board = new();
    private readonly List<Worker> _workers;

    /// <param name="crossing">Whether it waits, as the game does today (MineOrder's CrossesClaims), while its way
    /// to a station crosses columns another holds, up to <see cref="ThroughSeconds"/>, then goes through; off,
    /// it walks through at once (the planner's tests).</param>
    public TeamRunner(MineTestWorld world, MarkArea area, MinePlan plan, PathCell home, IEnumerable<PathCell> starts, int? jitter = null,
        bool crossing = false)
    {
        _crossing = crossing;
        _jitter = jitter is { } seed ? new Random(seed) : null;
        _world = world;
        _home = home;
        _plan = plan;
        _live = new MineState(world.Site(area), home);
        _live.BuildNow(plan.Targets);
        _live.Bands = plan.Bands(_live.Remaining);
        _workers = starts.Select((s, i) => new Worker(i + 1, PlanRunner.Start(_live.Site, s))).ToList();
    }

    public MineState Live => _live;

    public List<string> Log { get; } = new();

    public IReadOnlyList<int> Mined => _workers.Select(w => w.Mined).ToList();

    public IReadOnlyList<PathCell> Positions => _workers.Select(w => w.At).ToList();

    public double Seconds { get; private set; }

    /// <summary>When the last strike ended.</summary>
    public double Makespan { get; private set; }

    /// <summary>Each one's seconds with no station, up to the makespan.</summary>
    public IReadOnlyList<double> Idle => _workers.Select(w => w.Idle.Sum(i => Math.Max(0, Math.Min(i.To, Makespan) - i.From))).ToList();

    public IReadOnlyList<double> Walked => _workers.Select(w => w.Walked).ToList();

    public IReadOnlyList<int> Waits => _workers.Select(w => w.Waits).ToList();

    /// <summary>A strike's seconds at the pace of the group's slowest block, as the game times it.</summary>
    public static double StrikeSeconds(MineSite site, IEnumerable<MarkPos> cells) =>
        EidolonMining.Timing(EidolonMining.StrikeSeconds(cells.Select(site.PlayerSeconds))).Seconds;

    /// <summary>How many times the plan was made again from the live state, every station left being
    /// one nobody could work while targets were left (the plan's stands gone with the order the
    /// eidolons worked it in).</summary>
    public int Replans { get; private set; }

    public void Run(double maxSeconds = 20000)
    {
        var path = new WidePath(_live.Site, 2, 2);
        int idle = 0;
        while (_workers.Any(w => !w.Done))
        {
            var w = _workers.Where(w => !w.Done).MinBy(w => (w.FreeAt, w.Id))!;
            double now = w.FreeAt;
            Seconds = now;
            Assert.True(now < maxSeconds, "the team ran out of time");
            if (w.Station == null)
            {
                var me = Me(w);
                var others = _workers.Where(o => o != w).Select(Me).ToList();
                var next = MineDispatch.Next(_plan, _live, me, others,
                    s => MineDispatch.Claim(s, _plan.Tunnel).All(u => _board.Free(w.Id, u, now)),
                    fit: s => MineDispatch.Fit(s, _plan.Tunnel, u => _board.Free(w.Id, u, now)));
                if (next == null && !_replanned && _workers.All(o => o.Station == null) && _live.Remaining.Any(c => _live.Bands!.Eligible(c)))
                {
                    foreach (var st in _plan.Stations.Where(st => st.Cells.Any(_live.Remaining.Contains)))
                    {
                        MineDispatch.Executable(_plan, _live, st, [], out var why);
                        Log.Add($"stuck at {now:0}: station {st.Id} at {st.Stand} {why}, standable {_live.Site.Standable(st.Stand)}; workers at {string.Join(" ", _workers.Select(o => o.At))}");
                    }
                    _plan = MinePlanner.Run(new MinePlanInput(_live, _home, _ => false, new HashSet<PathCell>()));
                    _live.AdoptNow(_plan);
                    _replanned = true;
                    Replans++;
                    continue;
                }
                if (next == null || !_board.TryClaim(w.Id, next.Id, MineDispatch.Claim(next, _plan.Tunnel), now))
                {
                    if (!w.Waiting)
                        w.Waits++;
                    w.Waiting = true;
                    // nothing for it now: it walks out of the others' way, to where it started, and waits there;
                    // done when nothing is left anyone may dig, or nobody has had anything for a long while
                    if (w.At != w.Rest && path.Find(w.At, w.Rest, PlanRunner.MaxNodes) is { } away)
                    {
                        w.FreeAt = now + (away.Count - 1) * MineDispatch.WalkSecondsPerBlock;
                        w.Walked += away.Count - 1;
                        w.Idle.Add((now, w.FreeAt));
                        w.At = w.Rest;
                        continue;
                    }
                    if (!_live.Remaining.Any(c => _live.Bands!.Eligible(c)) || ++idle > 400)
                        w.Done = _workers.All(o => o == w || o.Station == null);
                    w.FreeAt = now + 1;
                    w.Idle.Add((now, w.FreeAt));
                    continue;
                }
                idle = 0;
                w.Waiting = false;
                AssertClaimsApart(now);
                var route = path.Find(w.At, next.Stand, PlanRunner.MaxNodes);
                Assert.True(route != null, $"eidolon {w.Id}: no way from {w.At} to station {next.Id} at {next.Stand}");
                if (_crossing && Crosses(w, route!, now) && (w.ThroughSince ??= now) > now - ThroughSeconds)
                {
                    // the game drops its queue and waits for room, at most ThroughSeconds, then goes through
                    _board.Unclaim(w.Id, next.Id);
                    w.Waits++;
                    w.FreeAt = now + 0.5;
                    w.Idle.Add((now, w.FreeAt));
                    continue;
                }
                w.ThroughSince = null;
                w.Station = next;
                w.FreeAt = now + (route!.Count - 1) * MineDispatch.WalkSecondsPerBlock;
                w.Walked += route.Count - 1;
                w.At = next.Stand;
                _board.Struck(w.Id, w.FreeAt);
                continue;
            }
            var station = w.Station;
            var group = MineStations.NextGroup(station, _plan.Tunnel, c => _live.Remaining.Contains(c) && _live.Bands!.Eligible(c),
                w.At.X, w.At.Y, w.At.Z);
            var at = _workers.Where(o => o != w).Select(o => new MineWorkerAt(o.Id, o.At.X, o.At.Y, o.At.Z,
                o.Station is { } s ? [s.Stand] : [])).ToList();
            var before = group == null ? [] : MineDispatch.Before(_plan, _live, station);
            if (group == null || MineDispatch.Footing(before, group.Cells) || _live.Check(w.At, group.Cells, at, null, before) != MineRefusal.None)
            {
                _board.Unclaim(w.Id, station.Id);
                w.Station = null;
                w.FreeAt = now + (group == null ? 0 : 0.5);
                w.Idle.Add((now, w.FreeAt));
                continue;
            }
            foreach (var o in _workers.Where(o => o != w))
                Assert.All(group.Cells, c => Assert.False(EidolonMining.UnderFoot(o.At.X, o.At.Z, c),
                    $"eidolon {w.Id} digs {c} in eidolon {o.Id}'s columns at {o.At}"));
            AssertClaimsApart(now);
            _live.Apply(group.Cells);
            foreach (var c in group.Cells)
                _world.Dig(c);
            Log.Add($"{now:0}: {w.Id} at {w.At} station {station.Id} digs {string.Join(" ", group.Cells)}");
            w.Mined += group.Cells.Count;
            _replanned = false;
            _board.Struck(w.Id, now);
            foreach (var o in _workers)
            {
                Assert.True(_live.Site.Standable(o.At), $"after a strike by {w.Id}, eidolon {o.Id} has no place to stand at {o.At}");
                Assert.True(_live.Walk.Out(o.At), $"after a strike by {w.Id}, eidolon {o.Id} at {o.At} is not out");
            }
            w.FreeAt = now + StrikeSeconds(_live.Site, group.Cells) * (_jitter is { } j ? 0.7 + 0.6 * j.NextDouble() : 1);
            Makespan = Math.Max(Makespan, w.FreeAt);
        }
    }

    /// <summary>The game's cap on waiting for a way across another's columns (MineOrder.ThroughSeconds).</summary>
    public const double ThroughSeconds = 20;

    private bool Crosses(Worker w, IReadOnlyList<PathCell> route, double now)
    {
        foreach (var node in route.Skip(2))
            for (int x = node.X - 1; x <= node.X; x++)
            for (int z = node.Z - 1; z <= node.Z; z++)
                if (_board.Holder((x, z), now) is { } h && h != w.Id)
                    return true;
        return false;
    }

    private MineWorker Me(Worker w) =>
        new(w.Id, w.At.X, w.At.Y, w.At.Z, w.Station is { } s ? [s] : [], Math.Max(0, w.FreeAt - Seconds));

    private void AssertClaimsApart(double now)
    {
        var seen = new Dictionary<(int, int), long>();
        foreach (var w in _workers)
        foreach (var u in _board.Held(w.Id))
        {
            if (_board.Holder(u, now) != w.Id)
                continue;
            Assert.False(seen.TryGetValue(u, out long other) && other != w.Id, $"column {u} claimed by {w.Id} and {other}");
            seen[u] = w.Id;
        }
    }

    /// <summary>Whether every eidolon has a way home (or to the window's side) from where it ended.</summary>
    public bool AllLeave(PathCell home)
    {
        var path = new WidePath(_live.Site, 2, 2);
        var start = PlanRunner.Start(_live.Site, home);
        return _workers.All(w => path.Find(w.At, start, PlanRunner.MaxNodes) != null);
    }
}

/// <summary>A mining case: a world made fresh each time (a runner digs it), the area, where it was ordered
/// from, and where the eidolons start.</summary>
internal sealed record MineCase(string Name, Func<MineTestWorld> World, MarkArea Area, PathCell Home, PathCell[] Starts)
{
    public MinePlan Plan(MineTestWorld world) => world.Plan(Area, Home);
}

/// <summary>The cases the routes are measured on (README "Eidolon", working together, *Routes*), lifted
/// <see cref="L"/> as the planner's tests are.</summary>
internal static class MineCases
{
    public const int L = 10;

    /// <summary>G1: the wide pit of the sector scenario: granite four thick, a terrace a block lower north of
    /// the pit, 16 × 3 × 12.</summary>
    public static MineCase WidePit(int crew) => new("wide pit",
        () =>
        {
            var w = new MineTestWorld().Box(-14, 14, L - 4, L - 1, -14, 14);
            foreach (var c in MineTestWorld.Cells(-14, 14, L - 1, L - 1, -10, -7))
                w.Dig(c);
            return w;
        },
        MineTestWorld.Area(-8, 7, L - 3, L - 1, -6, 5), new PathCell(0, L, -12),
        new[] { new PathCell(-6, L, -12), new PathCell(6, L, -12), new PathCell(0, L, -12) }.Take(crew).ToArray());

    /// <summary>G2: a tunnel four wide and eight high into a stepped slope.</summary>
    public static MineCase TallTunnel(int crew) => new("tall tunnel",
        () => new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 8 + 3, -3, 6, L).Box(-2, -1, L, L + 3, -3, 6).Box(-4, -3, L, L + 1, -3, 6),
        MineTestWorld.Area(-2, 7, L, L + 7, 0, 3), new PathCell(-6, L, 0),
        new[] { new PathCell(-6, L, 0), new PathCell(-6, L, 4), new PathCell(-9, L, 2) }.Take(crew).ToArray());

    /// <summary>G3: a tunnel two wide through a ridge, with a mound of ground in front of it.</summary>
    public static MineCase NarrowTunnel(int crew) => new("two-wide tunnel",
        () => new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 5, 0, 1, L).Box(-6, -1, L, L + 3, -2, 3),
        MineTestWorld.Area(-6, 7, L, L + 3, -2, 3), new PathCell(-9, L, 0),
        new[] { new PathCell(-9, L, -2), new PathCell(-9, L, 3), new PathCell(-12, L, 0) }.Take(crew).ToArray());

    /// <summary>G4: a trench 16 long, 5 wide and 4 deep in flat granite.</summary>
    public static MineCase Trench(int crew) => new("narrow trench",
        () => new MineTestWorld().Ground(L - 1, bottom: L - 7),
        MineTestWorld.Area(0, 15, L - 4, L - 1, 0, 4), new PathCell(-6, L, 2),
        new[] { new PathCell(-6, L, 0), new PathCell(-6, L, 4), new PathCell(-9, L, 2) }.Take(crew).ToArray());

    /// <summary>G5: a pit 8 × 8 × 20, too deep for a straight stair: a spiral one.</summary>
    public static MineCase SpiralPit(int crew)
    {
        int top = L + 70;
        return new("spiral pit",
            () => new MineTestWorld().Box(-8, 16, 1, top - 1, -8, 16),
            MineTestWorld.Area(0, 7, top - 20, top - 1, 0, 7), new PathCell(-4, top, 4),
            new[] { new PathCell(-4, top, 1), new PathCell(-4, top, 7), new PathCell(-7, top, 4) }.Take(crew).ToArray());
    }

    public static MineCase ByName(string name, int crew) => name switch
    {
        "G1" => WidePit(crew),
        "G2" => TallTunnel(crew),
        "G3" => NarrowTunnel(crew),
        "G4" => Trench(crew),
        "G5" => SpiralPit(crew),
        _ => throw new ArgumentException(name),
    };
}

/// <summary>
/// Several eidolons working one plan by routes (README "Eidolon", working together, *Routes*), a
/// discrete-event loop beside <see cref="TeamRunner"/> and timed as it is (a block of path
/// <see cref="MineDispatch.WalkSecondsPerBlock"/>, a strike at its group's pace, the same jitter): it solves
/// (<see cref="MineRoutePlanner.Solve"/> on a copy of the live state) and each member follows its route,
/// claiming its head on the <see cref="JobBoard{TUnit, TShared}"/>, setting off only when its way crosses no
/// other's claim and its narrow ways are its turn (no other in one, held by none, its slot come or every
/// earlier one past), striking group by group with the live guard before each, and waiting where planned.
/// Events are repaired (<see cref="MineRoutes"/>) and solved again by the routes' rules, at once. It checks
/// what <see cref="TeamRunner"/> checks, and that the narrow ways were held by one at a time as walked, that
/// no way set off on crossed another's claim, and that the slots never overlap after a solve or a repair.
/// </summary>
internal sealed class RouteRunner
{
    private const double Check = 0.5;
    private const double ClaimPatience = 5, StrikePatience = 10, ThroughSeconds = 20, DeadlockSeconds = 8;

    private sealed class Worker(long id, PathCell at)
    {
        public readonly long Id = id;
        public readonly PathCell Rest = at;
        public PathCell At = at;
        public double FreeAt;
        public MineStation? Station;
        public bool Ahead, Started;
        public int Mined;
        public bool Done, Waiting;
        public int Waits, TurnWaits;
        public double Walked;
        public double? HeadWait, TurnSince;
        public PathCell? TurnZone;
        public (PathCell Zone, double Since)? InZone;
        public readonly List<(double From, double To)> Idle = new();
        public readonly Dictionary<string, double> IdleBy = new();
        public string? Why;
    }

    private void AddIdle(Worker w, double now)
    {
        w.Idle.Add((now, w.FreeAt));
        string why = w.Why is { } y ? Regex.Replace(y, @"[#0-9.]+|\(.*\)|zone .*", "").Trim() : "?";
        if (w.Why?.Contains("crossing") == true) why += " crossing";
        if (w.Why?.Contains("claim") == true) why += " claim";
        w.IdleBy[why] = w.IdleBy.GetValueOrDefault(why) + (w.FreeAt - now);
    }

    /// <summary>Idle seconds by why, all members.</summary>
    public string IdleWhy => string.Join(", ", _workers.SelectMany(w => w.IdleBy).GroupBy(p => p.Key).Select(g => $"{g.Key} {g.Sum(p => p.Value):0}"));

    // a wait begun, logged once with its reason
    private void Waiting(Worker w, double now, string why)
    {
        if (w.Waiting && w.Why == why)
            return;
        if (!w.Waiting)
            w.Waits++;
        w.Waiting = true;
        w.Why = why;
        Log.Add($"{now:0.0}: {w.Id} waits: {why}");
    }

    private readonly MineTestWorld _world;
    private readonly PathCell _home;
    private MinePlan _plan;
    private int _planVersion = 1;
    private readonly MineState _live;
    private readonly JobBoard<(int X, int Z), MineJobCells> _board = new();
    private readonly List<Worker> _workers;
    private readonly Random? _jitter;
    private readonly Dictionary<PathCell, (long Member, double Until)> _holders = new();
    private readonly List<(PathCell Zone, long Member, double From, double To)> _occupied = new();
    private MineRoutes? _routes;
    private string? _wanted;
    private bool _urgent, _replanned;
    private double _lastSolve = double.NegativeInfinity;
    private int _emptyVersion = -1;

    public RouteRunner(MineTestWorld world, MarkArea area, MinePlan plan, PathCell home, IEnumerable<PathCell> starts, int? jitter = null)
    {
        _world = world;
        _home = home;
        _plan = plan;
        _live = new MineState(world.Site(area), home);
        _live.BuildNow(plan.Targets);
        _live.Bands = plan.Bands(_live.Remaining);
        _workers = starts.Select((s, i) => new Worker(i + 1, PlanRunner.Start(_live.Site, s))).ToList();
        _jitter = jitter is { } seed ? new Random(seed) : null;
    }

    public MineState Live => _live;

    public MineRoutes? Routes => _routes;

    public List<string> Log { get; } = new();

    public IReadOnlyList<int> Mined => _workers.Select(w => w.Mined).ToList();

    public IReadOnlyList<PathCell> Positions => _workers.Select(w => w.At).ToList();

    public double Seconds { get; private set; }

    /// <summary>When the last strike ended.</summary>
    public double Makespan { get; private set; }

    public IReadOnlyList<double> Idle => _workers.Select(w => w.Idle.Sum(i => Math.Max(0, Math.Min(i.To, Makespan) - i.From))).ToList();

    public IReadOnlyList<double> Walked => _workers.Select(w => w.Walked).ToList();

    public IReadOnlyList<int> Waits => _workers.Select(w => w.Waits).ToList();

    public IReadOnlyList<int> TurnWaits => _workers.Select(w => w.TurnWaits).ToList();

    public int Replans { get; private set; }

    public int Solves { get; private set; }

    /// <summary>Solves whose routes were not taken (the running ones kept).</summary>
    public int Kept { get; private set; }

    public int Repairs { get; private set; }

    /// <summary>Stops struck ahead of their band.</summary>
    public int AheadStops { get; private set; }

    /// <summary>Ahead stops the live guard refused at their first strike.</summary>
    public int AheadRefused { get; private set; }

    /// <summary>Every strike: who, at what kind of station, from when to when, and what it broke.</summary>
    public List<(long Member, MineStationKind Kind, double From, double To, IReadOnlyList<MarkPos> Cells)> Strikes { get; } = new();

    /// <summary>Called after every solve and every repair, with the routes (tests look at them there).</summary>
    public Action<MineRoutes, string>? Watch { get; set; }

    /// <summary>The longest any member waited at a time (no station, a turn, its claim), and when it began.</summary>
    public IReadOnlyList<(double From, double To)> Waited(int member) => _workers[member].Idle;

    public void Run(double maxSeconds = 20000)
    {
        var path = new WidePath(_live.Site, 2, 2);
        Solve("first", 0, null);
        int idle = 0;
        while (_workers.Any(w => !w.Done))
        {
            var w = _workers.Where(w => !w.Done).MinBy(w => (w.FreeAt, w.Id))!;
            double now = w.FreeAt;
            Seconds = now;
            Assert.True(now < maxSeconds, "the team ran out of time");
            MaybeSolve(now);
            if (w.Station != null)
            {
                Work(w, now);
                continue;
            }
            var head = _routes!.Routes.GetValueOrDefault(w.Id)?.Head;
            if (head == null)
            {
                NoStop(w, now, path, ref idle);
                continue;
            }
            idle = 0;
            if (head.Kind == MineStopKind.Wait)
                PlannedWait(w, head, now, path);
            else
                Depart(w, head, now, path);
        }
        foreach (var w in _workers)
            CloseZone(w, Seconds);
        AssertZonesApart();
    }

    // ---- solving ----

    private void Want(string? why)
    {
        if (why == null)
            return;
        _wanted = why;
        _urgent |= why is "empty" or "plan" or "deadlock";
    }

    private void MaybeSolve(double now)
    {
        bool due = _wanted != null && (_urgent || now - _lastSolve >= 2) || now - _lastSolve >= MineRoutes.ResolveSeconds;
        if (!due)
            return;
        string why = _wanted ?? "periodic";
        Solve(why, now, why == "periodic" ? _routes!.Copy() : null);
    }

    public Action<double>? Probe { get; set; }

    private void Solve(string why, double now, MineRoutes? current)
    {
        Probe?.Invoke(now);
        var members = _workers.Where(w => !w.Done).Select(w => new MineRouteMember(w.Id, w.At, w.Station?.Id, w.Ahead,
            w.Station is { } s ? Math.Max(w.FreeAt, now) + MineCosts.WorkSeconds(_live.Site, s.Cells.Where(_live.Remaining.Contains).ToList()) : now)).ToList();
        var input = new MineRouteInput(_plan, _planVersion, _live.Clone(), members, [], now, current, null);
        var made = MineRoutePlanner.Solve(input);
        Solves++;
        if (made.Stats.KeptCurrent && _routes != null)
            Kept++;
        else
            _routes = made;
        _wanted = null;
        _urgent = false;
        _lastSolve = now;
        _routes.Sweep(_live, now);
        Log.Add($"{now:0.0}: solve ({why}) {(made.Stats.KeptCurrent ? "kept" : "taken")}, J {made.Score.J:0.0}, {made.Stats}, "
            + string.Join(" | ", _routes.Routes.Values.OrderBy(r => r.Member).Select(r => $"{r.Member}: {string.Join(" ", r.Stops.Select(s => s.Kind == MineStopKind.Wait ? "w" : $"#{s.Station}{(s.Ahead ? "a" : "")}"))}")));
        Assert.False(_routes.Slots.Overlaps(), $"slots overlap after a solve at {now}");
        Watch?.Invoke(_routes, "solve");
        // stuck: no route has a station, nobody works one, yet cells may be dug: the plan made again from the world as it is
        if (!_replanned && !_routes.Routes.Values.Any(r => r.Stops.Any(s => s.Kind == MineStopKind.Station))
            && _workers.All(w => w.Station == null) && _live.Remaining.Any(c => _live.Bands!.Eligible(c)))
        {
            _plan = MinePlanner.Run(new MinePlanInput(_live, _home, _ => false, new HashSet<PathCell>()));
            _live.AdoptNow(_plan);
            _planVersion++;
            _replanned = true;
            Replans++;
            Log.Add($"{now:0.0}: replanned, {_plan.Stations.Count} stations");
            Repair(_routes.Remap(_plan, _planVersion, _live, now), now);
            Solve("plan", now, null);
        }
    }

    private void Repair(string? why, double now)
    {
        Repairs++;
        Assert.False(_routes!.Slots.Overlaps(), $"slots overlap after a repair at {now}");
        Watch?.Invoke(_routes, "repair");
        Want(why);
    }

    // ---- a member with no stop ----

    private void NoStop(Worker w, double now, WidePath path, ref int idle)
    {
        if (Unrouted() && _emptyVersion != _live.Version)
        {
            _emptyVersion = _live.Version;
            Want("empty");
            w.FreeAt = now + 0.01;
            return;
        }
        Waiting(w, now, "no stop");
        // nothing for it: as TeamRunner, it walks out of the others' way to where it started and waits there
        if (w.At != w.Rest && path.Find(w.At, w.Rest, PlanRunner.MaxNodes) is { } away)
        {
            CloseZone(w, now);
            w.FreeAt = now + (away.Count - 1) * MineDispatch.WalkSecondsPerBlock;
            w.Walked += away.Count - 1;
            AddIdle(w, now);
            w.At = w.Rest;
            return;
        }
        if (!_live.Remaining.Any(c => _live.Bands!.Eligible(c) || MineAhead.Eligible(_live, c)) || ++idle > 400)
            w.Done = _workers.All(o => o == w || o.Station == null);
        w.FreeAt = now + 1;
        AddIdle(w, now);
    }

    private bool Unrouted()
    {
        var assigned = _routes!.Assigned;
        foreach (var s in _plan.Stations)
            if (!assigned.Contains(s.Id) && !_workers.Any(w => w.Station?.Id == s.Id) && s.Cells.Any(_live.Remaining.Contains)
                && (MineDispatch.Cells(_live, s).Count > 0 || MineDispatch.Cells(_live, s, true).Count > 0))
                return true;
        return false;
    }

    private void PlannedWait(Worker w, MineStop head, double now, WidePath path)
    {
        if (w.At != head.At && _live.Site.Standable(head.At) && path.Find(w.At, head.At, PlanRunner.MaxNodes) is { } way && way[^1] == head.At)
        {
            CloseZone(w, now);
            w.FreeAt = now + (way.Count - 1) * MineDispatch.WalkSecondsPerBlock;
            w.Walked += way.Count - 1;
            w.At = head.At;
            return;
        }
        if (now < head.Finish - 1e-9 && !Ready(w, _routes!.Next(w.Id), now, path))
        {
            Waiting(w, now, $"planned until {head.Finish:0.0}");
            w.FreeAt = Math.Min(head.Finish, now + 2);
            AddIdle(w, now);
            return;
        }
        Repair(_routes!.Waited(w.Id, now), now);
        w.FreeAt = now;
    }

    // whether the stop after a planned wait may be set off for now: the world ran ahead of the plan
    private bool Ready(Worker w, MineStop? next, double now, WidePath path)
    {
        if (next is not { Kind: MineStopKind.Station } stop || stop.Station >= _plan.Stations.Count)
            return false;
        var station = _plan.Stations[stop.Station];
        var cells = (stop.Cells.Count > 0 ? stop.Cells : station.Cells).Where(_live.Remaining.Contains).ToList();
        if (cells.Count == 0 || MineDispatch.Cells(_live, station with { Cells = cells }, stop.Ahead).Count == 0 || !_live.Walk.Out(station.Stand))
            return false;
        if (MineDispatch.Fit(station with { Cells = cells }, _plan.Tunnel, u => _board.Free(w.Id, u, now)) == null)
            return false;
        if (_workers.Any(o => o != w && Math.Abs(o.At.X - station.Stand.X) < 1.7 && Math.Abs(o.At.Z - station.Stand.Z) < 1.7 && Math.Abs(o.At.Y - station.Stand.Y) < 4))
            return false;
        var leg = path.Find(w.At, station.Stand, PlanRunner.MaxNodes);
        return leg != null && leg[^1] == station.Stand && !Crosses(w, leg, now);
    }

    // ---- setting off ----

    private void Depart(Worker w, MineStop head, double now, WidePath path)
    {
        if (head.Station < 0 || head.Station >= _plan.Stations.Count)
        {
            Repair(_routes!.Drop(w.Id, head.Station, MineRefusal.None, now), now);
            return;
        }
        var station = _plan.Stations[head.Station];
        // the cells its stop was planned for (what is left of them)
        var cells = (head.Cells.Count > 0 ? head.Cells : station.Cells).Where(_live.Remaining.Contains).ToList();
        if (cells.Count == 0)
        {
            Repair(_routes!.Finished(w.Id, station.Id, now, _live), now);
            w.FreeAt = now;
            return;
        }
        // it sets off when the plan has it set off (its way clear of the others' claims then), or sooner if its
        // station is ready for it: the world ran ahead of the plan
        if (now < head.Depart - 1e-9 && !Ready(w, head, now, path))
        {
            Waiting(w, now, $"planned until {head.Depart:0.0}");
            w.FreeAt = head.Depart;
            AddIdle(w, now);
            return;
        }
        // it holds nothing while it waits to set off (the game drops its queue to wait): its claim is taken as it goes
        _board.Release(w.Id);
        var fit = MineDispatch.Fit(station with { Cells = cells }, _plan.Tunnel, u => _board.Free(w.Id, u, now));
        bool taken = _workers.Any(o => o != w && Math.Abs(o.At.X - station.Stand.X) < 1.7 && Math.Abs(o.At.Z - station.Stand.Z) < 1.7 && Math.Abs(o.At.Y - station.Stand.Y) < 4);
        if (fit == null || taken)
        {
            Hold(w, now, ClaimPatience, MineRefusal.None);
            return;
        }
        var leg = Valid(w.At, head) ? head.Leg.ToList() : path.Find(w.At, station.Stand, PlanRunner.MaxNodes);
        if (leg == null || leg[^1] != station.Stand)
        {
            Repair(_routes!.Drop(w.Id, station.Id, MineRefusal.NotOut, now), now);
            w.FreeAt = now + Check;
            return;
        }
        if (Crosses(w, leg, now))
        {
            // a way round the columns the others hold, if not much longer; else it waits
            var held = new HashSet<(int X, int Z)>(_workers.Where(o => o != w).SelectMany(o => _board.Held(o.Id)).Where(u => _board.Holder(u, now) is { } h && h != w.Id));
            var round = MineCosts.From(_live.Walk, w.At, [station.Stand],
                avoid: n => held.Contains((n.X - 1, n.Z - 1)) || held.Contains((n.X, n.Z - 1)) || held.Contains((n.X - 1, n.Z)) || held.Contains((n.X, n.Z)));
            var way = round.Path(station.Stand);
            if (way.Count > 0 && way[0] == w.At && (way.Count - 1) <= (leg.Count - 1) + MineRoutePlanner.DetourSeconds / MineDispatch.WalkSecondsPerBlock && !Crosses(w, way, now))
                leg = way.ToList();
            else
            {
                Hold(w, now, ThroughSeconds, MineRefusal.None);
                return;
            }
        }
        if (Gate(w, head, leg, now) is { } blocked)
        {
            TurnWait(w, head, blocked, now, path);
            return;
        }
        Assert.True(_board.TryClaim(w.Id, station.Id, MineDispatch.Claim(fit, _plan.Tunnel), now), "a claim that fit is taken");
        // it sets off: its ways' narrow ways held until it leaves them, a stand in one until it sets off again
        var index = _routes!.ZoneIndex;
        var runs = index.Along(leg);
        double step = MineDispatch.WalkSecondsPerBlock;
        var stood = w.InZone;
        w.InZone = null;
        foreach (var (zone, from, to) in runs)
        {
            double enter = now + Math.Max(0, from - 1) * step;
            bool atEnd = to == leg.Count - 1;
            double leave = now + (to + 1) * step;
            if (stood is { } held && held.Zone == zone.Key && from == 0)
            {
                // it stood in this one: held on from then
                enter = held.Since;
                stood = null;
            }
            if (atEnd)
                w.InZone = (zone.Key, enter);
            else
                _occupied.Add((zone.Key, w.Id, enter, leave));
            _holders[zone.Key] = (w.Id, atEnd ? double.PositiveInfinity : leave);
        }
        if (stood is { } left)
        {
            _occupied.Add((left.Zone, w.Id, left.Since, now));
            if (_holders.TryGetValue(left.Zone, out var lh) && lh.Member == w.Id)
                _holders[left.Zone] = (w.Id, now);
        }
        foreach (var node in leg.Skip(2))
            for (int x = node.X - 1; x <= node.X; x++)
            for (int z = node.Z - 1; z <= node.Z; z++)
                Assert.False(_board.Holder((x, z), now) is { } h && h != w.Id, $"{w.Id} sets off at {now} through {(x, z)}, held by another");
        w.FreeAt = now + (leg.Count - 1) * step;
        w.Walked += leg.Count - 1;
        w.At = station.Stand;
        w.Station = fit;
        w.Ahead = head.Ahead;
        w.Started = false;
        w.HeadWait = null;
        w.TurnSince = null;
        w.TurnZone = null;
        w.Waiting = false;
        _board.Struck(w.Id, now);
    }

    // its route's leg still holds: from where it is, every node a place it may stand and out
    private bool Valid(PathCell at, MineStop head) =>
        head.Leg.Count > 0 && head.Leg[0] == at && head.Leg[^1] == head.At && head.Leg.All(n => _live.Site.Standable(n) && _live.Walk.Out(n));

    private bool Crosses(Worker w, IReadOnlyList<PathCell> leg, double now)
    {
        foreach (var node in leg.Skip(2))
            for (int x = node.X - 1; x <= node.X; x++)
            for (int z = node.Z - 1; z <= node.Z; z++)
                if (_board.Holder((x, z), now) is { } h && h != w.Id)
                    return true;
        return false;
    }

    // waits for its head (a claim, a way across another's): past its patience the stop is dropped
    private void Hold(Worker w, double now, double patience, MineRefusal why)
    {
        w.HeadWait ??= now;
        Waiting(w, now, $"head #{_routes!.Head(w.Id)?.Station} ({(patience == ThroughSeconds ? "crossing" : "claim")})");
        if (now - w.HeadWait > patience && _routes!.Head(w.Id) is { Kind: MineStopKind.Station } head)
        {
            _board.Unclaim(w.Id, head.Station);
            w.HeadWait = null;
            Repair(_routes.Drop(w.Id, head.Station, why, now), now);
        }
        w.FreeAt = now + Check;
        AddIdle(w, now);
    }

    // the narrow way it may not enter now, or null: one another stands in, one another holds, or one whose
    // turn (slot) is another's still
    private MineZone? Gate(Worker w, MineStop head, IReadOnlyList<PathCell> leg, double now)
    {
        var routes = _routes!;
        foreach (var (zone, _, _) in routes.ZoneIndex.Along(leg))
        {
            if (_workers.Any(o => o != w && zone.Nodes.Contains(o.At)))
                return zone;
            if (_holders.TryGetValue(zone.Key, out var h) && h.Member != w.Id && h.Until > now)
                return zone;
            var slots = routes.Slots.Of(zone.Id);
            var mine = slots.FirstOrDefault(s => s.Member == w.Id && s.Stop == head.Seq);
            if (mine != null && mine.From > now + MineRoutes.SlotEarly
                && slots.Any(s => s.Member != w.Id && s.From < mine.From && !(s.To < now && !_workers.Any(o => o.Id == s.Member && zone.Nodes.Contains(o.At)))))
                return zone;
        }
        return null;
    }

    private void TurnWait(Worker w, MineStop head, MineZone zone, double now, WidePath path)
    {
        if (w.TurnSince == null)
            w.TurnWaits++;
        w.TurnSince ??= now;
        w.TurnZone = zone.Key;
        Waiting(w, now, $"turn at zone {zone.Key}");
        var mine = _routes!.Slots.Of(zone.Id).FirstOrDefault(s => s.Member == w.Id && s.Stop == head.Seq);
        if (mine != null && now > mine.From + MineRoutes.SlotLate)
            Repair(_routes.Late(w.Id, now), now);
        // the deadlock breaker: the one in its way waits on a way it holds itself; the larger id gives way
        if (now - w.TurnSince > DeadlockSeconds && _holders.TryGetValue(zone.Key, out var h) && h.Member != w.Id
            && _workers.FirstOrDefault(o => o.Id == h.Member) is { TurnZone: { } theirs } holder
            && _holders.TryGetValue(theirs, out var mineHeld) && mineHeld.Member == w.Id && w.Id > holder.Id)
        {
            _board.Unclaim(w.Id, head.Station);
            Repair(_routes.Drop(w.Id, head.Station, MineRefusal.None, now), now);
            Want("deadlock");
            StepAside(w, now);
            w.TurnSince = null;
            w.TurnZone = null;
            return;
        }
        w.FreeAt = now + Check;
        AddIdle(w, now);
    }

    // out of every narrow way and every other's claim, a few joins off
    private void StepAside(Worker w, double now)
    {
        var index = _routes!.ZoneIndex;
        var parent = new Dictionary<PathCell, PathCell> { [w.At] = w.At };
        var queue = new Queue<PathCell>([w.At]);
        while (queue.TryDequeue(out var u))
        {
            bool clear = index.Of(u) == null;
            for (int x = u.X - 1; x <= u.X && clear; x++)
            for (int z = u.Z - 1; z <= u.Z && clear; z++)
                clear = !(_board.Holder((x, z), now) is { } h && h != w.Id);
            if (clear && !_workers.Any(o => o != w && Math.Abs(o.At.X - u.X) < 2 && Math.Abs(o.At.Z - u.Z) < 2))
            {
                int steps = 0;
                for (var n = u; n != w.At; n = parent[n])
                    steps++;
                CloseZone(w, now + steps * MineDispatch.WalkSecondsPerBlock);
                w.FreeAt = now + steps * MineDispatch.WalkSecondsPerBlock;
                w.Walked += steps;
                w.At = u;
                return;
            }
            if (parent.Count > 2000)
                break;
            foreach (var v in _live.Walk.Joins(u))
                if (_live.Walk.Out(v) && parent.TryAdd(v, u))
                    queue.Enqueue(v);
        }
        w.FreeAt = now + Check;
    }

    // it leaves the narrow way its stand is in (or was): held until then
    private void CloseZone(Worker w, double at)
    {
        if (w.InZone is not { } held)
            return;
        _occupied.Add((held.Zone, w.Id, held.Since, at));
        if (_holders.TryGetValue(held.Zone, out var h) && h.Member == w.Id)
            _holders[held.Zone] = (w.Id, at);
        w.InZone = null;
    }

    // ---- at work ----

    private void Work(Worker w, double now)
    {
        var station = w.Station!;
        var others = _workers.Where(o => o != w).ToList();
        bool Left(MarkPos c) => _live.Remaining.Contains(c) && (_live.Bands!.Eligible(c) || w.Ahead && MineAhead.Eligible(_live, c))
            && !others.Any(o => EidolonMining.UnderFoot(o.At.X, o.At.Z, c));
        var group = MineStations.NextGroup(station, _plan.Tunnel, Left, w.At.X, w.At.Y, w.At.Z);
        if (group == null)
        {
            if (!station.Cells.Any(_live.Remaining.Contains))
            {
                _board.Unclaim(w.Id, station.Id);
                w.Station = null;
                w.FreeAt = now;
                Repair(_routes!.Finished(w.Id, station.Id, now, _live), now);
                return;
            }
            // cells left it may not strike yet: it waits at the stand a while, then lets the stop go
            w.HeadWait ??= now;
            if (now - w.HeadWait > StrikePatience)
            {
                Drop(w, now, MineRefusal.None);
                return;
            }
            _board.Struck(w.Id, now);
            Waiting(w, now, $"at #{station.Id}, no cell to strike");
            w.FreeAt = now + Check;
            AddIdle(w, now);
            return;
        }
        var at = others.Select(o => new MineWorkerAt(o.Id, o.At.X, o.At.Y, o.At.Z, Holds(o))).ToList();
        var before = MineDispatch.Before(_plan, _live, station);
        if (MineDispatch.Footing(before, group.Cells))
        {
            if (!w.Started && w.Ahead)
                AheadRefused++;
            Drop(w, now, MineRefusal.CutsOffStation);
            return;
        }
        if (_live.Check(w.At, group.Cells, at, null, before) is var why and not MineRefusal.None)
        {
            if (!w.Started && w.Ahead)
                AheadRefused++;
            Drop(w, now, why);
            return;
        }
        if (!w.Started)
        {
            w.Started = true;
            if (group.Cells.Any(c => !_live.Bands!.Eligible(c)))
                AheadStops++;
            Repair(_routes!.Started(w.Id, station.Id, now), now);
        }
        foreach (var o in others)
            Assert.All(group.Cells, c => Assert.False(EidolonMining.UnderFoot(o.At.X, o.At.Z, c), $"eidolon {w.Id} digs {c} in eidolon {o.Id}'s columns at {o.At}"));
        AssertClaimsApart(now);
        double seconds = TeamRunner.StrikeSeconds(_live.Site, group.Cells) * (_jitter is { } j ? 0.7 + 0.6 * j.NextDouble() : 1);
        _live.Apply(group.Cells);
        foreach (var c in group.Cells)
            _world.Dig(c);
        Log.Add($"{now:0.0}: {w.Id} at {w.At} station {station.Id} digs {string.Join(" ", group.Cells)}");
        w.Mined += group.Cells.Count;
        _replanned = false;
        _board.Struck(w.Id, now);
        foreach (var o in _workers)
        {
            Assert.True(_live.Site.Standable(o.At), $"after a strike by {w.Id}, eidolon {o.Id} has no place to stand at {o.At}");
            Assert.True(_live.Walk.Out(o.At), $"after a strike by {w.Id}, eidolon {o.Id} at {o.At} is not out");
        }
        w.HeadWait = null;
        w.Waiting = false;
        w.FreeAt = now + seconds;
        Strikes.Add((w.Id, station.Kind, now, w.FreeAt, group.Cells));
        Makespan = Math.Max(Makespan, w.FreeAt);
    }

    // what another holds as the guard sees it: its head's stand and its next stop's
    private List<PathCell> Holds(Worker o)
    {
        var holds = new List<PathCell>();
        if (o.Station is { } s)
            holds.Add(s.Stand);
        var route = _routes!.Routes.GetValueOrDefault(o.Id);
        foreach (var stop in route?.Stops.Take(2) ?? [])
            if (stop.Kind == MineStopKind.Station && !holds.Contains(stop.At))
                holds.Add(stop.At);
        return holds;
    }

    private void Drop(Worker w, double now, MineRefusal why)
    {
        var station = w.Station!;
        _board.Unclaim(w.Id, station.Id);
        w.Station = null;
        w.Started = false;
        w.HeadWait = null;
        Log.Add($"{now:0.0}: {w.Id} drops #{station.Id}: {why}");
        Repair(_routes!.Drop(w.Id, station.Id, why, now), now);
        w.FreeAt = now + Check;
        AddIdle(w, now);
    }

    private void AssertClaimsApart(double now)
    {
        var seen = new Dictionary<(int, int), long>();
        foreach (var w in _workers)
        foreach (var u in _board.Held(w.Id))
        {
            if (_board.Holder(u, now) != w.Id)
                continue;
            Assert.False(seen.TryGetValue(u, out long other) && other != w.Id, $"column {u} claimed by {w.Id} and {other}");
            seen[u] = w.Id;
        }
    }

    // every narrow way held by one at a time, as walked and stood in
    private void AssertZonesApart()
    {
        foreach (var zone in _occupied.GroupBy(o => o.Zone))
        {
            var list = zone.ToList();
            for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                Assert.False(a.Member != b.Member && a.From < b.To - 1e-6 && b.From < a.To - 1e-6,
                    $"zone {zone.Key}: {a.Member} {a.From:0.0}..{a.To:0.0} and {b.Member} {b.From:0.0}..{b.To:0.0}");
            }
        }
    }

    /// <summary>Whether every eidolon has a way home (or to the window's side) from where it ended.</summary>
    public bool AllLeave(PathCell home)
    {
        var path = new WidePath(_live.Site, 2, 2);
        var start = PlanRunner.Start(_live.Site, home);
        return _workers.All(w => path.Find(w.At, start, PlanRunner.MaxNodes) != null);
    }
}
