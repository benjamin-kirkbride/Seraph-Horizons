using System.Diagnostics;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What a plan is made from: the job's live state (copied, never changed), where the order was
/// given, the cells it leaves for the order's life (<see cref="MineJobCells.Skip"/>), the stands found bad
/// in the world, and what roofs a column (<see cref="EidolonTunnels.Covered"/>; the site's solid blocks
/// without it).</summary>
public sealed record MinePlanInput(MineState State, PathCell? Home, Func<MarkPos, bool> Skip, IReadOnlySet<PathCell> BadStands)
{
    public Func<MarkPos, bool>? Roof { get; init; }
}

/// <summary>Where a plan is: its targets, the reach bound and the holds under what is out of reach, the
/// covered part's shape, the stair, the simulation, done.</summary>
public enum MinePlanPhase { Targets, Reach, Shape, Stair, Simulate, Done }

/// <summary>
/// Plans a mine order's area as stations (README "Eidolon", mining, *Stations*), a slice of time at a
/// time (<see cref="Step"/>, <see cref="BudgetMs"/> a tick across every job). On a copy of the job's state:
/// its targets (<see cref="EidolonMining.Targets"/>); what any order could reach (<see cref="MineReach"/>),
/// the floor under the rest held and the bound found again until it holds; the covered part's shape
/// (<see cref="EidolonTunnels.Shape"/>); a stair dug and kept until last where the floor it leaves would
/// have no way out (<see cref="MinePlan.Stair"/>); then the dig simulated station by station, the highest
/// band first, each station accepted only past the guard (<see cref="MineState.Check"/>). What the
/// simulation strands is held as out of reach and the plan made again, at most <see cref="MaxReruns"/>
/// times. Every tie ends on coordinates: a plan is a pure function of its input.
/// </summary>
public sealed class MinePlanner(MinePlanInput input, Func<TimeSpan>? clock = null)
{
    public const double BudgetMs = 2;
    public const int MaxReruns = 2;

    private IEnumerator<bool>? _work;

    // The time now, as a span from any fixed start: the stopwatch's by default; a test's own clock
    // (README "Eidolon", mining, *Stations*: planning in slices) where one is given.
    private readonly Func<TimeSpan> _clock = clock ?? RealClock;

    private static readonly Func<TimeSpan> RealClock = () => Stopwatch.GetElapsedTime(0);

    public MinePlanPhase Phase { get; private set; }

    public MinePlan? Plan { get; private set; }

    /// <summary>The state at the plan's end (for tests).</summary>
    public MineState? Simulated { get; private set; }

    /// <summary>Works for at most <paramref name="budget"/> by its clock (past it by at most one
    /// checkpoint, a bounded number of units of work, <see cref="MineWork"/>); true once
    /// <see cref="Plan"/> is set. The clock is read as the step starts and after each checkpoint.</summary>
    public bool Step(TimeSpan budget)
    {
        if (Plan != null)
            return true;
        _work ??= Work().GetEnumerator();
        Steps++;
        var start = _clock();
        var last = start;
        while (true)
        {
            var phase = Phase;
            bool more = _work.MoveNext();
            var now = _clock();
            _spent[phase] = _spent.GetValueOrDefault(phase) + (now - last);
            last = now;
            if (!more || Plan != null)
                return Plan != null;
            if (now - start >= budget)
                return false;
        }
    }

    private readonly Dictionary<MinePlanPhase, TimeSpan> _spent = new();

    /// <summary>The time spent in each phase so far by its clock, and the <see cref="Step"/> calls it took
    /// (for the game's log: what planning costs on real ground).</summary>
    public IReadOnlyDictionary<MinePlanPhase, TimeSpan> Spent => _spent;

    public int Steps { get; private set; }

    /// <summary>A plan to the end, with no budget (tests, small areas).</summary>
    public static MinePlan Run(MinePlanInput input) => Make(input).Plan!;

    /// <summary>A planner run to the end (tests: <see cref="Simulated"/>).</summary>
    public static MinePlanner Make(MinePlanInput input)
    {
        var planner = new MinePlanner(input);
        while (!planner.Step(TimeSpan.MaxValue)) { }
        return planner;
    }

    private IEnumerable<bool> Work()
    {
        var area = input.State.Site.Area;
        var baseSite = input.State.Site.Clone();
        int version = baseSite.Version;
        var outOfReach = new HashSet<MarkPos>();
        for (int run = 0; ; run++)
        {
            Phase = MinePlanPhase.Targets;
            var state = new MineState(baseSite.Clone(), input.Home, input.BadStands);
            foreach (bool _ in state.BuildWalk())
                yield return false;
            MineTargets targets = null!;
            foreach (bool _ in EidolonMining.Targets(area, state.Site, input.Skip, outOfReach.Count > 0 ? outOfReach : null, t => targets = t))
                yield return false;

            Phase = MinePlanPhase.Reach;
            while (true)
            {
                foreach (bool _ in state.Retarget(targets.Dig))
                    yield return false;
                MineWork.Add(targets.Dig.Count);
                var lost = targets.Dig.Where(c => !state.Reach.Clearable.Contains(c)).ToList();
                if (lost.Count == 0)
                    break;
                outOfReach.UnionWith(lost);
                foreach (bool _ in EidolonMining.Targets(area, state.Site, input.Skip, outOfReach, t => targets = t))
                    yield return false;
            }

            Phase = MinePlanPhase.Shape;
            var site = state.Site;
            HashSet<(int X, int Z)> covered = null!;
            foreach (bool _ in EidolonTunnels.Covered(area, input.Roof ?? (c => site.Solid(c.X, c.Y, c.Z)), c => covered = c))
                yield return false;
            MineTunnel? tunnel = null;
            foreach (bool _ in EidolonTunnels.Shape(area, covered, state.Reach, input.Home, t => tunnel = t))
                yield return false;

            Phase = MinePlanPhase.Stair;
            var stairs = new StairWork();
            foreach (bool _ in Stairs(state, area, stairs))
                yield return false;
            var (stair, finalWalk) = (stairs.Stair, stairs.Final!);

            Phase = MinePlanPhase.Simulate;
            state.Bands = new MineBands(area, tunnel, stair, state.Remaining);
            yield return false;
            var simulation = new Simulation(state, tunnel, input.Home);
            yield return false;
            foreach (bool _ in simulation.Run())
                yield return false;

            MineWork.Add(state.Remaining.Count);
            var leftovers = state.Remaining.Where(c => !stair.Contains(c)).ToHashSet();
            // Of what it strands, only the cells it could have dug next (the bands' precedence let it) are out of
            // its reach; the rest only waited behind them (a covered band not driven caps the open floor and
            // holds the bands under it) and are planned again with them held, but in the last run.
            outOfReach.UnionWith(run < MaxReruns ? leftovers.Where(state.Bands!.Eligible) : leftovers);
            if (leftovers.Count > 0 && run < MaxReruns)
                continue;

            // The last run's leftovers are reported out of reach, with their floor: no station digs under them.
            MineTargets report = null!;
            foreach (bool _ in EidolonMining.Targets(area, baseSite, input.Skip, outOfReach, t => report = t))
                yield return false;
            int? stepNeeded = null;
            foreach (bool _ in StepNeeded(outOfReach, finalWalk, n => stepNeeded = n))
                yield return false;
            var stations = simulation.Stations;
            MineWork.Add(2 * stations.Sum(s => s.Cells.Count));
            if (leftovers.Count > 0)
                stations = stations
                    .Select(s => s with { Cells = s.Cells.Where(c => !report.Held.Contains(c)).ToList() })
                    .Where(s => s.Cells.Count > 0).Select((s, i) => s with { Id = i }).ToList();
            var dug = stations.SelectMany(s => s.Cells).ToHashSet();
            yield return false;
            MineWork.Add(2 * stair.Count + dug.Count);
            var wayOut = stair.Where(state.Remaining.Contains).ToHashSet();
            var planned = dug.Concat(stair).ToHashSet();
            yield return false;
            Simulated = state;
            Plan = new MinePlan
            {
                Stations = stations,
                OutOfReach = outOfReach,
                Held = report.Held,
                Left = report.Left,
                Stair = stair,
                WayOut = wayOut,
                Tunnel = tunnel,
                SiteVersion = version,
                Partial = report.Unloaded.Count > 0,
                StepNeeded = stepNeeded,
                Area = area,
                Targets = planned,
                Reruns = run,
            };
            Phase = MinePlanPhase.Done;
            yield return true;
            yield break;
        }
    }

    // ---- the stair ----

    /// <summary>
    /// The stair (the final-world test): in the window with every clearable cell dug, the
    /// nodes it would stand at to clear the last of them (the final floor); where some of those have no
    /// way out, a straight stair 2 wide is left against a side of theirs facing a way out, rising a step
    /// a column from their floor to the level beside it, the one of fewest cells (then nearest home) that
    /// gives them one; where none does (a pit deeper than about twice its width), a spiral round its walls
    /// (<see cref="Spirals"/>). Returns its cells, and the final world's walk graph with it.
    /// </summary>
    private sealed class StairWork
    {
        public HashSet<MarkPos> Stair { get; } = new();
        public MineWalk? Final { get; set; }
    }

    private sealed class Chosen
    {
        public (List<MarkPos> Cells, double D2, MineSite Site, MineWalk Walk)? Best { get; set; }
    }

    // How many units of work (cells dug, nodes looked at) between checkpoints in the stair's search.
    private const int StairUnits = 512;

    private IEnumerable<bool> Stairs(MineState state, MarkArea area, StairWork result)
    {
        var clearable = state.Remaining;
        var finalSite = state.Site.Clone();
        int units = 0;
        foreach (var c in clearable)
        {
            finalSite.Dig(c);
            if (++units % (8 * StairUnits) == 0)
                yield return false;
        }
        var final = new MineWalk(finalSite, input.Home, input.BadStands);
        foreach (bool _ in final.Build())
            yield return false;
        result.Final = final;
        var stuck = new List<PathCell>();
        var w = finalSite.Window;
        for (int x = area.Min.X; x <= area.Max.X + 1; x++)
        for (int z = area.Min.Z; z <= area.Max.Z + 1; z++)
        {
            MineWork.Add(w.SizeY);
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
            {
                var n = new PathCell(x, y, z);
                if (final.Node(n) && !final.Out(n) && Reaches(n, clearable))
                    stuck.Add(n);
            }
            if (++units % 16 == 0)
                yield return false;
        }
        if (stuck.Count == 0)
            yield break;
        var components = new List<List<PathCell>>();
        foreach (bool _ in Components(final, stuck, components))
            yield return false;
        foreach (var component in components)
        {
            // a straight stair where one fits, else a spiral round the walls
            var chosen = new Chosen();
            foreach (bool _ in Best(component, finalSite, Candidates(component, final, clearable).OrderBy(c => c.D2).Take(16), chosen))
                yield return false;
            if (chosen.Best == null)
                foreach (bool _ in Best(component, finalSite, Spirals(component, final, clearable).OrderBy(c => c.D2).Take(8), chosen))
                    yield return false;
            if (chosen.Best is not { } b)
                continue;
            result.Stair.UnionWith(b.Cells);
            finalSite = b.Site;
            final = b.Walk;
            result.Final = final;
        }
    }

    // Of the candidates, those with whose cells kept the component has its way out in the final world:
    // the one of fewest cells, then nearest home; each candidate's walk graph built a step at a time.
    private IEnumerable<bool> Best(List<PathCell> component, MineSite finalSite, IEnumerable<(List<MarkPos> Cells, double D2)> candidates,
        Chosen chosen)
    {
        foreach (var (cells, d2) in candidates)
        {
            var site = finalSite.Clone();
            foreach (var c in cells)
                site.Set(c, new MineVoxel(true, MineRule.Mine));
            yield return false;
            var walk = new MineWalk(site, input.Home, input.BadStands);
            foreach (bool _ in walk.Build())
                yield return false;
            MineWork.Add(component.Count);
            var still = component.Where(walk.Node).ToList();
            if (still.Count == 0 || !still.All(walk.Out))
                continue;
            if (chosen.Best is not { } best || cells.Count < best.Cells.Count || (cells.Count == best.Cells.Count && d2 < best.D2))
                chosen.Best = (cells, d2, site, walk);
        }
    }

    // Spiral stairs (an open pit's ramp): a band 2 wide round the inside of the component's bounds, from
    // the level beside it down to its floor, a step lower each column along a side, a landing 2 x 2 at each
    // corner, lap after lap where it is deep (a lap goes down more than the box is tall, so the treads of
    // one are clear over the next). Each tread is the one block under it: what is under that is dug, so a
    // lap under another has its headroom. Four ways to start (the start of each side), both ways round.
    private IEnumerable<(List<MarkPos> Cells, double D2)> Spirals(List<PathCell> component, MineWalk final, IReadOnlySet<MarkPos> clearable)
    {
        int minX = component.Min(n => n.X - 1), maxX = component.Max(n => n.X);
        int minZ = component.Min(n => n.Z - 1), maxZ = component.Max(n => n.Z);
        int floor = component.Min(n => n.Y);
        int step = input.State.Site.Step;
        int sizeX = maxX - minX + 1, sizeZ = maxZ - minZ + 1;
        if (sizeX < 5 || sizeZ < 5 || 2 * step * (sizeX - 4 + sizeZ - 4) < EidolonTunnels.HeadingHeight + 2)
            yield break;
        // the level beside it: the highest way out round its bounds
        int top = int.MinValue;
        for (int x = minX - 1; x <= maxX + 2; x++)
        for (int z = minZ - 1; z <= maxZ + 2; z++)
        {
            if (x - 1 >= minX && x <= maxX && z - 1 >= minZ && z <= maxZ)
                continue;
            MineWork.Add(input.State.Site.Window.Max.Y - floor + 1);
            for (int y = floor; y <= input.State.Site.Window.Max.Y; y++)
                if (final.Out(new PathCell(x, y, z)))
                    top = Math.Max(top, y);
        }
        if (top - floor <= step)
            yield break;
        // the band clockwise: each side's slices (two columns across it), then the corner after it
        var units = new List<(List<(int X, int Z)> Columns, bool Slice, int Side)>();
        for (int side = 0; side < 4; side++)
        {
            var slices = new List<List<(int X, int Z)>>();
            if (side == 0)
                for (int x = minX + 2; x <= maxX - 2; x++) slices.Add([(x, minZ), (x, minZ + 1)]);
            else if (side == 1)
                for (int z = minZ + 2; z <= maxZ - 2; z++) slices.Add([(maxX - 1, z), (maxX, z)]);
            else if (side == 2)
                for (int x = maxX - 2; x >= minX + 2; x--) slices.Add([(x, maxZ - 1), (x, maxZ)]);
            else
                for (int z = maxZ - 2; z >= minZ + 2; z--) slices.Add([(minX, z), (minX + 1, z)]);
            foreach (var slice in slices)
                units.Add((slice, true, side));
            var (cx, cz) = side switch { 0 => (maxX - 1, minZ), 1 => (maxX - 1, maxZ - 1), 2 => (minX, maxZ - 1), _ => (minX, minZ) };
            units.Add(([(cx, cz), (cx + 1, cz), (cx, cz + 1), (cx + 1, cz + 1)], false, side));
        }
        foreach (bool clockwise in new[] { true, false })
        {
            var round = clockwise ? units : Enumerable.Reverse(units).ToList();
            for (int side = 0; side < 4; side++)
            {
                // start on the first slice of the side, going its way
                int start = round.FindIndex(u => u.Slice && u.Side == side);
                if (start < 0)
                    continue;
                var cells = new List<MarkPos>();
                int feet = top - step;
                for (int k = 0; feet > floor; k++)
                {
                    var unit = round[(start + k) % round.Count];
                    if (k > 0 && unit.Slice)
                        feet -= step;
                    if (feet <= floor)
                        break;
                    MineWork.Add(unit.Columns.Count);
                    foreach (var (x, z) in unit.Columns)
                        if (clearable.Contains(new MarkPos(x, feet - 1, z)))
                            cells.Add(new MarkPos(x, feet - 1, z));
                }
                if (cells.Count == 0)
                    continue;
                var first = round[start].Columns;
                double cx = first.Average(c => c.X + 0.5), cz = first.Average(c => c.Z + 0.5);
                double d2 = input.Home is { } h ? (cx - h.X) * (cx - h.X) + (cz - h.Z) * (cz - h.Z) : 0;
                yield return (cells.Distinct().ToList(), d2);
            }
        }
    }

    private static bool Reaches(PathCell n, IReadOnlySet<MarkPos> cells)
    {
        MineWork.Add(6 * 6 * 5);
        for (int x = n.X - 3; x <= n.X + 2; x++)
        for (int z = n.Z - 3; z <= n.Z + 2; z++)
        for (int y = n.Y - 1; y <= n.Y + 3; y++)
            if (cells.Contains(new MarkPos(x, y, z)) && EidolonMining.InReach(n.X, n.Y, n.Z, new MarkPos(x, y, z)))
                return true;
        return false;
    }

    // The stuck nodes grouped by the parts of the graph (not out) they are joined in, into components.
    private static IEnumerable<bool> Components(MineWalk walk, List<PathCell> stuck, List<List<PathCell>> components)
    {
        var seen = new HashSet<PathCell>();
        var stuckSet = stuck.ToHashSet();
        int units = 0;
        foreach (var start in stuck)
        {
            if (!seen.Add(start))
                continue;
            var component = new List<PathCell>();
            var queue = new Queue<PathCell>([start]);
            while (queue.TryDequeue(out var n))
            {
                if (stuckSet.Contains(n))
                    component.Add(n);
                foreach (var m in walk.Joins(n))
                    if (seen.Add(m))
                        queue.Enqueue(m);
                if (++units % StairUnits == 0)
                    yield return false;
            }
            components.Add(component);
        }
    }

    // Straight stairs 2 wide against each side of a component's bounds that a way out is beside, every
    // 2 columns along it: a tread a column, each a step higher, the highest against the side.
    private IEnumerable<(List<MarkPos> Cells, double D2)> Candidates(List<PathCell> component, MineWalk final, IReadOnlySet<MarkPos> clearable)
    {
        int minX = component.Min(n => n.X - 1), maxX = component.Max(n => n.X);
        int minZ = component.Min(n => n.Z - 1), maxZ = component.Max(n => n.Z);
        int floor = component.Min(n => n.Y);
        int step = input.State.Site.Step;
        // (along +1 or -1 into the component, along x?, the side's column)
        var sides = new (int Dir, bool AlongX, int Edge)[] { (1, true, minX), (-1, true, maxX), (1, false, minZ), (-1, false, maxZ) };
        foreach (var (dir, alongX, edge) in sides)
        {
            int acrossMin = alongX ? minZ : minX, acrossMax = alongX ? maxZ : maxX;
            int length = alongX ? maxX - minX + 1 : maxZ - minZ + 1;
            for (int a = acrossMin; a + 1 <= acrossMax; a += 2)
            {
                // the ways out just outside the side, over the stair's two columns
                int top = int.MinValue;
                int outside = dir > 0 ? edge - 1 : edge + 2; // the node whose box is the two columns past the edge
                MineWork.Add(3 * 17);
                for (int corner = a; corner <= a + 2; corner++)
                for (int y = floor; y <= floor + 16; y++)
                {
                    var n = alongX ? new PathCell(outside, y, corner) : new PathCell(corner, y, outside);
                    if (final.Out(n))
                        top = Math.Max(top, y);
                }
                if (top - floor <= step)
                    continue;
                int treads = (top - floor + step - 1) / step - 1;
                if (treads >= length)
                    continue;
                var cells = new List<MarkPos>();
                for (int k = 1; k <= treads; k++)
                {
                    int along = edge + dir * (treads - k);
                    int tread = floor + k * step;
                    MineWork.Add(2 * (tread - floor));
                    for (int across = a; across <= a + 1; across++)
                    for (int y = floor; y < tread; y++)
                    {
                        var c = alongX ? new MarkPos(along, y, across) : new MarkPos(across, y, along);
                        if (clearable.Contains(c))
                            cells.Add(c);
                    }
                }
                if (cells.Count == 0)
                    continue;
                double cx = alongX ? edge + 0.5 : a + 1, cz = alongX ? a + 1 : edge + 0.5;
                double d2 = input.Home is { } h ? (cx - h.X) * (cx - h.X) + (cz - h.Z) * (cz - h.Z) : 0;
                yield return (cells, d2);
            }
        }
    }

    /// <summary>How high a step in front of the face would bring the highest cell out of reach in reach
    /// (the out-of-reach report's hint): its stand's feet would be <see cref="EidolonMining.ReachAbove"/> under it; of the
    /// final world's out nodes within 4 columns of it, the highest feet; the difference, if any.</summary>
    private static IEnumerable<bool> StepNeeded(IReadOnlySet<MarkPos> outOfReach, MineWalk final, Action<int?> done)
    {
        if (outOfReach.Count == 0)
        {
            done(null);
            yield break;
        }
        MineWork.Add(outOfReach.Count);
        var u = outOfReach.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).First();
        int? best = null;
        int units = 0;
        foreach (var n in final.Nodes)
        {
            int dx = Math.Max(0, Math.Max(n.X - 1 - u.X, u.X - n.X)), dz = Math.Max(0, Math.Max(n.Z - 1 - u.Z, u.Z - n.Z));
            if (dx <= 4 && dz <= 4 && (best == null || n.Y > best))
                best = n.Y;
            if (++units % (8 * StairUnits) == 0)
                yield return false;
        }
        int need = best is { } f ? u.Y - EidolonMining.ReachAbove - f : 0;
        done(need > 0 ? need : null);
    }

    // ---- the simulation ----

    private readonly record struct StationKey(int Band, int Top, int Order, int Lane, int OnTarget, int Count, int X, int Y, int Z);

    private sealed class KeyOrder : IComparer<StationKey>
    {
        public static readonly KeyOrder Instance = new();

        public int Compare(StationKey a, StationKey b)
        {
            int c = a.Band.CompareTo(b.Band);
            if (c == 0) c = b.Top.CompareTo(a.Top);
            if (c == 0) c = a.Order.CompareTo(b.Order);
            if (c == 0) c = a.Lane.CompareTo(b.Lane);
            if (c == 0) c = a.OnTarget.CompareTo(b.OnTarget);
            if (c == 0) c = b.Count.CompareTo(a.Count);
            if (c == 0) c = a.X.CompareTo(b.X);
            if (c == 0) c = a.Y.CompareTo(b.Y);
            if (c == 0) c = a.Z.CompareTo(b.Z);
            return c;
        }
    }

    private sealed record Candidate(List<MarkPos> Cells, int Band, MineStationKind Kind, int Lane, int Slice, StationKey Key);

    /// <summary>
    /// The dig run forward: stands in a priority queue, keyed by band, then the highest
    /// layer first, then in a covered band slice by slice from the mouth and lane by lane, in the open a
    /// serpentine over rows a station wide, from the side farthest from home toward it, the way along a row
    /// turning each row and each layer; a stand on a remaining target after one that is not; the most
    /// cells first. Keys are looked at again as they come out (they only grow worse as cells go, but for
    /// what a dig opens, which is pushed again). A stand refused by the guard waits until a cell near it
    /// is dug.
    /// </summary>
    private sealed class Simulation
    {
        private const int ParkReach = 8;
        private const int MaxSweeps = 8;

        private readonly MineState _state;
        private readonly MineSite _site;
        private readonly MineBands _bands;
        private readonly MineTunnel? _tunnel;
        private readonly MarkArea _area;
        private readonly PathCell? _home;
        private readonly int[] _band;
        private readonly PriorityQueue<PathCell, StationKey> _queue = new(KeyOrder.Instance);
        private readonly HashSet<PathCell> _queued = new();
        private readonly List<MarkPos> _widened = new();
        private readonly List<PathCell> _stands = new();
        private readonly HashSet<PathCell> _parked = new();
        private readonly Dictionary<(int, int), List<PathCell>> _parkedAt = new();
        private readonly Dictionary<(int Lane, int Y), int> _front = new();
        private int? _covered;
        private int _openFloor;
        private bool _stairOpen;
        private bool _offLane;
        private int _sweptAt = -1, _sweeps;

        public Simulation(MineState state, MineTunnel? tunnel, PathCell? home)
        {
            _state = state;
            _site = state.Site;
            _bands = state.Bands!;
            _tunnel = tunnel;
            _area = _site.Area;
            _home = home;
            _band = new int[_site.Volume];
            Array.Fill(_band, MineBands.Never);
            MineWork.Bytes(_band.Length * 8L);
            MineWork.Add(state.Remaining.Count);
            foreach (var c in state.Remaining)
                if (_site.InWindow(c.X, c.Y, c.Z))
                    _band[_site.Index(c.X, c.Y, c.Z)] = _bands.Band(c);
            Eligibility();
        }

        public List<MineStation> Stations { get; } = new();

        private void Eligibility()
        {
            _covered = _bands.Covered;
            _openFloor = _bands.OpenFloor;
            _stairOpen = _bands.StairOpen;
        }

        public IEnumerable<bool> Run()
        {
            int units = 0;
            foreach (var n in _state.Walk.Nodes)
            {
                Push(n);
                if (++units % 32 == 0)
                    yield return false;
            }
            yield return false;
            while (true)
            {
                for (int k = 0; k < _widened.Count; k++)
                {
                    PushReaching(_widened[k]);
                    if (k % 16 == 15)
                        yield return false;
                }
                _widened.Clear();
                for (int k = 0; k < _stands.Count; k++)
                {
                    Push(_stands[k]);
                    if (k % 32 == 31)
                        yield return false;
                }
                _stands.Clear();
                if (!_queue.TryDequeue(out var stand, out var key))
                {
                    // Dry: every stand asked again (once since the last station, at most MaxSweeps times: each
                    // costs what the first push did), since a stand parked by the guard is woken only by a dig
                    // near it and what refused it may have gone far off; and a covered band its lanes alone
                    // cannot finish (no stand in a lane reaches its face, which would hold every band and
                    // layer under it) struck from then on from any stand that reaches it.
                    if (_sweptAt == Stations.Count || _sweeps == MaxSweeps)
                        break;
                    _sweptAt = Stations.Count;
                    _sweeps++;
                    _offLane |= _covered != null;
                    _parked.Clear();
                    _parkedAt.Clear();
                    foreach (var n in _state.Walk.Nodes)
                    {
                        Push(n);
                        if (++units % 32 == 0)
                            yield return false;
                    }
                    continue;
                }
                _queued.Remove(stand);
                // a stand's cells are looked at afresh (a few hundred cells): a step every few
                if (++units % 8 == 0)
                    yield return false;
                if (!_state.Walk.Out(stand))
                    continue;
                if (Of(stand) is not { } candidate)
                    continue;
                if (KeyOrder.Instance.Compare(candidate.Key, key) > 0)
                {
                    Enqueue(stand, candidate.Key);
                    continue;
                }
                // a step after every guard asked (one cut off searches its whole part of the graph)
                yield return false;
                var blame = new HashSet<MarkPos>();
                var refusal = _state.Check(stand, candidate.Cells, [], blame);
                if (refusal is MineRefusal.CutsOff or MineRefusal.Starves && blame.Count > 0)
                {
                    var rest = candidate.Cells.Where(c => !blame.Contains(c)).ToList();
                    if (rest.Count > 0 && _state.Check(stand, rest, []) == MineRefusal.None)
                    {
                        candidate = candidate with { Cells = rest };
                        refusal = MineRefusal.None;
                    }
                }
                if (refusal != MineRefusal.None)
                {
                    Park(stand);
                    continue;
                }
                yield return false;
                Emit(stand, candidate);
                yield return false;
            }
        }

        private void Emit(PathCell stand, Candidate candidate)
        {
            MineWork.Add(candidate.Cells.Count * (int.Log2(candidate.Cells.Count + 1) + 2));
            var cells = candidate.Cells.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).ToList();
            int slice = candidate.Kind == MineStationKind.Face ? cells.Min(c => _tunnel!.Slice(c.X, c.Z)) : -1;
            Stations.Add(new MineStation(Stations.Count, stand, cells, candidate.Band, candidate.Kind, candidate.Lane, slice));
            _state.Apply(cells);
            foreach (var c in cells)
                _band[_site.Index(c.X, c.Y, c.Z)] = MineBands.Never;
            Eligibility();
            var widened = new List<MarkPos>(_bands.TakeWidened());
            if (candidate.Kind == MineStationKind.Face)
                foreach (var (lane, y) in cells.Select(c => (CellLane(c), c.Y)).Where(p => p.Item1 >= 0).Distinct().ToList())
                {
                    int before = _front.GetValueOrDefault((lane, y), 0);
                    int now = Front(lane, y);
                    for (int s = before + 2; s <= now + 1; s++)
                        widened.AddRange(SliceCells(lane, s, y));
                }
            // what the dig opened is pushed by the run, a few cells a step (a band or layer opening is
            // a whole layer of them)
            _widened.AddRange(widened);
            _stands.AddRange(_state.Walk.TakeOpened());
            Unpark(cells);
            Push(stand);
        }

        // ---- what a stand would clear ----

        private Candidate? Of(PathCell s)
        {
            if (_state.BadStands.Contains(s))
                return null;
            int lane = LaneOf(s);
            List<MarkPos>? covered = null, open = null, stair = null;
            int coveredBands = _bands.CoveredBands;
            MineWork.Add(6 * 6 * 5);
            for (int x = Math.Max(s.X - 3, _area.Min.X); x <= Math.Min(s.X + 2, _area.Max.X); x++)
            for (int z = Math.Max(s.Z - 3, _area.Min.Z); z <= Math.Min(s.Z + 2, _area.Max.Z); z++)
            {
                if (x >= s.X - 1 && x <= s.X && z >= s.Z - 1 && z <= s.Z)
                    continue;
                int column = _site.Column(x, z);
                for (int y = Math.Max(s.Y - 1, _area.Min.Y); y <= Math.Min(s.Y + 3, _area.Max.Y); y++)
                {
                    int band = _band[column + y];
                    if (band == MineBands.Never)
                        continue;
                    var c = new MarkPos(x, y, z);
                    if (band == MineBands.Stair)
                    {
                        if (_stairOpen)
                            (stair ??= new()).Add(c);
                    }
                    else if (band < coveredBands)
                    {
                        // its own lane's; once the lanes alone have stalled, any lane's in its reach
                        int cl = lane >= 0 && InLane(c, lane) ? lane : _offLane ? CellLane(c) : -1;
                        if (band != _covered || cl < 0 || _tunnel!.Slice(x, z) > Front(cl, y) + 1)
                            continue;
                        (covered ??= new()).Add(c);
                    }
                    else if (y >= _openFloor)
                        (open ??= new()).Add(c);
                }
            }
            if (covered != null)
            {
                // the lane it strikes: the stand's where its cells all lie in it, else theirs if one, else none
                int faceLane = lane >= 0 && covered.All(c => InLane(c, lane)) ? lane
                    : covered.Select(CellLane).Distinct().ToList() is [var only] ? only : -1;
                return Make(s, covered, _covered!.Value, MineStationKind.Face, faceLane);
            }
            if (open != null)
                return Make(s, open, _bands.OpenBand, MineStationKind.Bench, -1);
            if (stair != null)
                return Make(s, stair, MineBands.Stair, MineStationKind.Stair, -1);
            return null;
        }

        private Candidate Make(PathCell s, List<MarkPos> cells, int band, MineStationKind kind, int lane)
        {
            MineWork.Add(cells.Count + 4);
            int top = int.MinValue, minSlice = int.MaxValue;
            foreach (var c in cells)
            {
                top = Math.Max(top, c.Y);
                if (kind == MineStationKind.Face)
                    minSlice = Math.Min(minSlice, _tunnel!.Slice(c.X, c.Z));
            }
            int order = kind == MineStationKind.Face ? minSlice : Serpentine(s, top);
            int onTarget = 0;
            for (int x = s.X - 1; x <= s.X; x++)
            for (int z = s.Z - 1; z <= s.Z; z++)
                if (_site.InWindow(x, s.Y - 1, z) && _band[_site.Index(x, s.Y - 1, z)] != MineBands.Never)
                    onTarget = 1;
            // a stair comes off from the bottom up, where it may at all
            if (kind == MineStationKind.Stair)
                top = -cells.Min(c => c.Y);
            var key = new StationKey(band, top, order, kind == MineStationKind.Face ? lane : 0, onTarget, cells.Count, s.X, s.Y, s.Z);
            return new Candidate(cells, band, kind, lane, kind == MineStationKind.Face ? minSlice : -1, key);
        }

        // The serpentine (CC turtles' sweep): rows a station wide along the area's longer side, the
        // row farthest from home first, the way along each row turning each row and each layer, a
        // station's swath at a time.
        private int Serpentine(PathCell s, int top)
        {
            bool alongX = _area.SizeX >= _area.SizeZ;
            int length = alongX ? _area.SizeX : _area.SizeZ, width = alongX ? _area.SizeZ : _area.SizeX;
            int along = Math.Clamp((alongX ? s.X : s.Z) - (alongX ? _area.Min.X : _area.Min.Z), -8, length + 8) + 8;
            int across = Math.Clamp((alongX ? s.Z : s.X) - (alongX ? _area.Min.Z : _area.Min.X), 0, width - 1);
            const int rowWidth = 2 * EidolonMining.Reach + 2;
            int rows = (width + rowWidth - 1) / rowWidth, row = across / rowWidth;
            double middle = alongX ? (_area.Min.Z + _area.Max.Z + 1) / 2.0 : (_area.Min.X + _area.Max.X + 1) / 2.0;
            bool homeLow = _home is not { } h || (alongX ? h.Z : h.X) < middle;
            int order = homeLow ? rows - 1 - row : row;
            bool forward = ((order + (top & 1)) & 1) == 0;
            // along the row a station's swath at a time too: within one, the stand clearing most goes first
            int block = (forward ? along : length + 16 - along) / rowWidth;
            return order * 100000 + block;
        }

        // ---- lanes and fronts ----

        private int LaneOf(PathCell s)
        {
            if (_tunnel == null)
                return -1;
            int a0 = _tunnel.AlongX ? s.Z - 1 : s.X - 1;
            int best = -1, bestOverlap = 0;
            bool tie = false;
            var lanes = _tunnel.Lanes;
            for (int i = 0; i < lanes.Count; i++)
            {
                int min = _tunnel.AlongX ? lanes[i].MinZ : lanes[i].MinX, max = _tunnel.AlongX ? lanes[i].MaxZ : lanes[i].MaxX;
                int overlap = (a0 >= min && a0 <= max ? 1 : 0) + (a0 + 1 >= min && a0 + 1 <= max ? 1 : 0);
                if (overlap > bestOverlap)
                {
                    (best, bestOverlap, tie) = (i, overlap, false);
                }
                else if (overlap == bestOverlap && overlap > 0)
                    tie = true;
            }
            return tie ? -1 : best;
        }

        private int CellLane(MarkPos c)
        {
            for (int i = 0; i < _tunnel!.Lanes.Count; i++)
                if (InLane(c, i))
                    return i;
            return -1;
        }

        private bool InLane(MarkPos c, int lane)
        {
            var l = _tunnel!.Lanes[lane];
            int across = _tunnel.AlongX ? c.Z : c.X;
            return _tunnel.AlongX ? across >= l.MinZ && across <= l.MaxZ : across >= l.MinX && across <= l.MaxX;
        }

        // The first slice of a lane with a cell left at layer y (it only moves in as cells go).
        private int Front(int lane, int y)
        {
            int s = _front.GetValueOrDefault((lane, y), 0);
            while (s < _tunnel!.Length && !SliceCells(lane, s, y).Any())
                s++;
            _front[(lane, y)] = s;
            return s;
        }

        private IEnumerable<MarkPos> SliceCells(int lane, int slice, int y)
        {
            var l = _tunnel!.Lanes[lane];
            int min = _tunnel.AlongX ? l.MinZ : l.MinX, max = _tunnel.AlongX ? l.MaxZ : l.MaxX;
            MineWork.Add(max - min + 1);
            for (int across = min; across <= max; across++)
            {
                var (x, z) = _tunnel.Column(slice, across);
                if (_site.InWindow(x, y, z) && _band[_site.Index(x, y, z)] != MineBands.Never)
                    yield return new MarkPos(x, y, z);
            }
        }

        // ---- the queue ----

        private void Push(PathCell s)
        {
            if (_queued.Contains(s) || !_state.Walk.Out(s))
                return;
            if (Of(s) is { } candidate)
                Enqueue(s, candidate.Key);
        }

        private void Enqueue(PathCell s, StationKey key)
        {
            _queued.Add(s);
            _queue.Enqueue(s, key);
        }

        // The stands that reach a cell.
        private void PushReaching(MarkPos c)
        {
            MineWork.Add(6 * 6 * 5);
            for (int x = c.X - 2; x <= c.X + 3; x++)
            for (int z = c.Z - 2; z <= c.Z + 3; z++)
            for (int y = c.Y - 3; y <= c.Y + 1; y++)
                if (EidolonMining.InReach(x, y, z, c))
                    Push(new PathCell(x, y, z));
        }

        private void Park(PathCell s)
        {
            if (!_parked.Add(s))
                return;
            var key = (s.X >> 3, s.Z >> 3);
            if (!_parkedAt.TryGetValue(key, out var list))
                _parkedAt[key] = list = new List<PathCell>();
            list.Add(s);
        }

        private void Unpark(IReadOnlyList<MarkPos> cells)
        {
            int minX = cells.Min(c => c.X) - ParkReach, maxX = cells.Max(c => c.X) + ParkReach;
            int minZ = cells.Min(c => c.Z) - ParkReach, maxZ = cells.Max(c => c.Z) + ParkReach;
            for (int bx = minX >> 3; bx <= maxX >> 3; bx++)
            for (int bz = minZ >> 3; bz <= maxZ >> 3; bz++)
            {
                MineWork.Add(1);
                if (!_parkedAt.Remove((bx, bz), out var list))
                    continue;
                MineWork.Add(list.Count);
                foreach (var s in list)
                {
                    _parked.Remove(s);
                    _stands.Add(s);
                }
            }
        }
    }
}
