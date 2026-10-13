using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>A voxel world for the mine planner's tests: solid blocks (each mined, unless a rule says
/// otherwise), rule overrides, and block columns not loaded; with builders for the ground, boxes of rock
/// and hills.</summary>
internal sealed class MineTestWorld : IMineWorld
{
    public readonly HashSet<(int X, int Y, int Z)> Solid = new();
    public readonly Dictionary<MarkPos, MineVoxel> Rules = new();
    public readonly HashSet<(int X, int Z)> Unloaded = new();

    public bool Loaded(int x, int z) => !Unloaded.Contains((x, z));

    public MineVoxel Read(int x, int y, int z) =>
        Rules.TryGetValue(new MarkPos(x, y, z), out var v) ? v : Solid.Contains((x, y, z)) ? new MineVoxel(true, MineRule.Mine) : new MineVoxel(false, MineRule.Air);

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
/// (<see cref="MineStations.NextGroup"/>, <see cref="MineDispatch.StrikeSeconds"/> each), every strike
/// checked by the guard against the live state with the others where they are. At every strike it checks
/// that claims are disjoint and no cell is in another's box columns; after every strike, that every
/// eidolon still stands and is out.
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
    }

    private readonly MineTestWorld _world;
    private readonly PathCell _home;
    private MinePlan _plan;
    private bool _replanned;
    private readonly MineState _live;
    private readonly JobBoard<(int X, int Z), MineJobCells> _board = new();
    private readonly List<Worker> _workers;

    public TeamRunner(MineTestWorld world, MarkArea area, MinePlan plan, PathCell home, IEnumerable<PathCell> starts)
    {
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
                    // nothing for it now: it walks out of the others' way, to where it started, and waits there;
                    // done when nothing is left anyone may dig, or nobody has had anything for a long while
                    if (w.At != w.Rest && path.Find(w.At, w.Rest, PlanRunner.MaxNodes) is { } away)
                    {
                        w.FreeAt = now + (away.Count - 1) * MineDispatch.WalkSecondsPerBlock;
                        w.At = w.Rest;
                        continue;
                    }
                    if (!_live.Remaining.Any(c => _live.Bands!.Eligible(c)) || ++idle > 400)
                        w.Done = _workers.All(o => o == w || o.Station == null);
                    w.FreeAt = now + 1;
                    continue;
                }
                idle = 0;
                AssertClaimsApart(now);
                var route = path.Find(w.At, next.Stand, PlanRunner.MaxNodes);
                Assert.True(route != null, $"eidolon {w.Id}: no way from {w.At} to station {next.Id} at {next.Stand}");
                w.Station = next;
                w.FreeAt = now + (route!.Count - 1) * MineDispatch.WalkSecondsPerBlock;
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
            w.FreeAt = now + MineDispatch.StrikeSeconds;
        }
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
