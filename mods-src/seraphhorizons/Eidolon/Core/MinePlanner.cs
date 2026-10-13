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
public sealed class MinePlanner(MinePlanInput input)
{
    public const double BudgetMs = 2;
    public const int MaxReruns = 2;

    private IEnumerator<bool>? _work;

    public MinePlanPhase Phase { get; private set; }

    public MinePlan? Plan { get; private set; }

    /// <summary>The state at the plan's end (for tests).</summary>
    public MineState? Simulated { get; private set; }

    /// <summary>Works for at most <paramref name="budget"/> (past it by at most one checkpoint, every 64
    /// units of work); true once <see cref="Plan"/> is set.</summary>
    public bool Step(TimeSpan budget)
    {
        if (Plan != null)
            return true;
        _work ??= Work().GetEnumerator();
        Steps++;
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        while (true)
        {
            var phase = Phase;
            bool more = _work.MoveNext();
            var now = clock.Elapsed;
            _spent[phase] = _spent.GetValueOrDefault(phase) + (now - last);
            last = now;
            if (!more || Plan != null)
                return Plan != null;
            if (now >= budget)
                return false;
        }
    }

    private readonly Dictionary<MinePlanPhase, TimeSpan> _spent = new();

    /// <summary>The time spent in each phase so far, and the <see cref="Step"/> calls it took (for the
    /// game's log: what planning costs on real ground).</summary>
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
            var targets = EidolonMining.Targets(area, state.Site, input.Skip, outOfReach.Count > 0 ? outOfReach : null);
            yield return false;

            Phase = MinePlanPhase.Reach;
            while (true)
            {
                foreach (bool _ in state.Retarget(targets.Dig))
                    yield return false;
                var lost = targets.Dig.Where(c => !state.Reach.Clearable.Contains(c)).ToList();
                if (lost.Count == 0)
                    break;
                outOfReach.UnionWith(lost);
                targets = EidolonMining.Targets(area, state.Site, input.Skip, outOfReach);
            }

            Phase = MinePlanPhase.Shape;
            var site = state.Site;
            var covered = EidolonTunnels.Covered(area, input.Roof ?? (c => site.Solid(c.X, c.Y, c.Z)));
            var tunnel = EidolonTunnels.Shape(area, covered, state.Reach, input.Home);
            yield return false;

            Phase = MinePlanPhase.Stair;
            var (stair, finalWalk) = Stairs(state, area);
            yield return false;

            Phase = MinePlanPhase.Simulate;
            state.Bands = new MineBands(area, tunnel, stair, state.Remaining);
            var simulation = new Simulation(state, tunnel, input.Home);
            foreach (bool _ in simulation.Run())
                yield return false;

            var leftovers = state.Remaining.Where(c => !stair.Contains(c)).ToHashSet();
            outOfReach.UnionWith(leftovers);
            if (leftovers.Count > 0 && run < MaxReruns)
                continue;

            // The last run's leftovers are reported out of reach, with their floor: no station digs under them.
            var report = EidolonMining.Targets(area, baseSite, input.Skip, outOfReach);
            var stations = simulation.Stations;
            if (leftovers.Count > 0)
                stations = stations
                    .Select(s => s with { Cells = s.Cells.Where(c => !report.Held.Contains(c)).ToList() })
                    .Where(s => s.Cells.Count > 0).Select((s, i) => s with { Id = i }).ToList();
            var dug = stations.SelectMany(s => s.Cells).ToHashSet();
            Simulated = state;
            Plan = new MinePlan
            {
                Stations = stations,
                OutOfReach = outOfReach,
                Held = report.Held,
                Left = report.Left,
                Stair = stair,
                WayOut = stair.Where(state.Remaining.Contains).ToHashSet(),
                Tunnel = tunnel,
                SiteVersion = version,
                Partial = report.Unloaded.Count > 0,
                StepNeeded = StepNeeded(outOfReach, finalWalk),
                Area = area,
                Targets = dug.Concat(stair).ToHashSet(),
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
    /// gives them one. Returns its cells, and the final world's walk graph with it.
    /// </summary>
    private (HashSet<MarkPos> Stair, MineWalk Final) Stairs(MineState state, MarkArea area)
    {
        var clearable = state.Remaining;
        var finalSite = state.Site.Clone();
        foreach (var c in clearable)
            finalSite.Dig(c);
        var final = new MineWalk(finalSite, input.Home, input.BadStands);
        final.BuildNow();
        var stair = new HashSet<MarkPos>();
        var stuck = FloorNodes(final, finalSite, clearable, area).Where(n => !final.Out(n)).ToList();
        if (stuck.Count == 0)
            return (stair, final);
        foreach (var component in Components(final, stuck))
        {
            (List<MarkPos> Cells, double D2, MineSite Site, MineWalk Walk)? best = null;
            foreach (var (cells, d2) in Candidates(component, final, clearable).OrderBy(c => c.D2).Take(16))
            {
                var site = finalSite.Clone();
                foreach (var c in cells)
                    site.Set(c, new MineVoxel(true, MineRule.Mine));
                var walk = new MineWalk(site, input.Home, input.BadStands);
                walk.BuildNow();
                var still = component.Where(walk.Node).ToList();
                if (still.Count == 0 || !still.All(walk.Out))
                    continue;
                if (best == null || cells.Count < best.Value.Cells.Count || (cells.Count == best.Value.Cells.Count && d2 < best.Value.D2))
                    best = (cells, d2, site, walk);
            }
            if (best is not { } b)
                continue;
            stair.UnionWith(b.Cells);
            finalSite = b.Site;
            final = b.Walk;
        }
        return (stair, final);
    }

    // Nodes whose box is over a column of the area and that reach a clearable cell.
    private static IEnumerable<PathCell> FloorNodes(MineWalk walk, MineSite site, IReadOnlySet<MarkPos> clearable, MarkArea area)
    {
        var w = site.Window;
        for (int x = area.Min.X; x <= area.Max.X + 1; x++)
        for (int z = area.Min.Z; z <= area.Max.Z + 1; z++)
        for (int y = w.Min.Y; y <= w.Max.Y; y++)
        {
            var n = new PathCell(x, y, z);
            if (walk.Node(n) && Reaches(n, clearable))
                yield return n;
        }
    }

    private static bool Reaches(PathCell n, IReadOnlySet<MarkPos> cells)
    {
        for (int x = n.X - 3; x <= n.X + 2; x++)
        for (int z = n.Z - 3; z <= n.Z + 2; z++)
        for (int y = n.Y - 1; y <= n.Y + 3; y++)
            if (cells.Contains(new MarkPos(x, y, z)) && EidolonMining.InReach(n.X, n.Y, n.Z, new MarkPos(x, y, z)))
                return true;
        return false;
    }

    // The stuck nodes grouped by the parts of the graph (not out) they are joined in.
    private static IEnumerable<List<PathCell>> Components(MineWalk walk, List<PathCell> stuck)
    {
        var seen = new HashSet<PathCell>();
        var stuckSet = stuck.ToHashSet();
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
            }
            yield return component;
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
    private static int? StepNeeded(IReadOnlySet<MarkPos> outOfReach, MineWalk final)
    {
        if (outOfReach.Count == 0)
            return null;
        var u = outOfReach.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).First();
        int? best = null;
        foreach (var n in final.Nodes)
        {
            int dx = Math.Max(0, Math.Max(n.X - 1 - u.X, u.X - n.X)), dz = Math.Max(0, Math.Max(n.Z - 1 - u.Z, u.Z - n.Z));
            if (dx <= 4 && dz <= 4 && (best == null || n.Y > best))
                best = n.Y;
        }
        if (best is not { } f)
            return null;
        int need = u.Y - EidolonMining.ReachAbove - f;
        return need > 0 ? need : null;
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

        private readonly MineState _state;
        private readonly MineSite _site;
        private readonly MineBands _bands;
        private readonly MineTunnel? _tunnel;
        private readonly MarkArea _area;
        private readonly PathCell? _home;
        private readonly int[] _band;
        private readonly PriorityQueue<PathCell, StationKey> _queue = new(KeyOrder.Instance);
        private readonly HashSet<PathCell> _queued = new();
        private readonly HashSet<PathCell> _parked = new();
        private readonly Dictionary<(int, int), List<PathCell>> _parkedAt = new();
        private readonly Dictionary<(int Lane, int Y), int> _front = new();
        private int? _covered;
        private int _openFloor;
        private bool _stairOpen;

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
            foreach (var n in _state.Walk.Nodes)
                Push(n);
            yield return false;
            int units = 0;
            while (_queue.TryDequeue(out var stand, out var key))
            {
                _queued.Remove(stand);
                if (++units % 64 == 0)
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
                Emit(stand, candidate);
                yield return false;
            }
        }

        private void Emit(PathCell stand, Candidate candidate)
        {
            var cells = candidate.Cells.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).ToList();
            int slice = candidate.Kind == MineStationKind.Face ? cells.Min(c => _tunnel!.Slice(c.X, c.Z)) : -1;
            Stations.Add(new MineStation(Stations.Count, stand, cells, candidate.Band, candidate.Kind, candidate.Lane, slice));
            _state.Apply(cells);
            foreach (var c in cells)
                _band[_site.Index(c.X, c.Y, c.Z)] = MineBands.Never;
            Eligibility();
            var widened = new List<MarkPos>(_bands.TakeWidened());
            if (candidate.Kind == MineStationKind.Face)
                foreach (var (lane, y) in cells.Select(c => (candidate.Lane, c.Y)).Distinct().ToList())
                {
                    int before = _front.GetValueOrDefault((lane, y), 0);
                    int now = Front(lane, y);
                    for (int s = before + 2; s <= now + 1; s++)
                        widened.AddRange(SliceCells(lane, s, y));
                }
            foreach (var c in widened)
                PushReaching(c);
            foreach (var n in _state.Walk.TakeOpened())
                Push(n);
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
                        if (band != _covered || lane < 0 || !InLane(c, lane) || _tunnel!.Slice(x, z) > Front(lane, y) + 1)
                            continue;
                        (covered ??= new()).Add(c);
                    }
                    else if (y >= _openFloor)
                        (open ??= new()).Add(c);
                }
            }
            if (covered != null)
                return Make(s, covered, _covered!.Value, MineStationKind.Face, lane);
            if (open != null)
                return Make(s, open, _bands.OpenBand, MineStationKind.Bench, -1);
            if (stair != null)
                return Make(s, stair, MineBands.Stair, MineStationKind.Stair, -1);
            return null;
        }

        private Candidate Make(PathCell s, List<MarkPos> cells, int band, MineStationKind kind, int lane)
        {
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
                if (!_parkedAt.Remove((bx, bz), out var list))
                    continue;
                foreach (var s in list)
                {
                    _parked.Remove(s);
                    Push(s);
                }
            }
        }
    }
}
