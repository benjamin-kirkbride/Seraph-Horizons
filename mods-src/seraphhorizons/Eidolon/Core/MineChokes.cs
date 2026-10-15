namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A narrow way's kind: a lane, a ramp, a shelf or a tunnel mouth; or the stair.</summary>
public enum MineZoneKind { Narrow, Stair }

/// <summary>A narrow way (README "Eidolon", working together, *Narrow ways*): out nodes where two eidolons'
/// boxes cannot pass each other, joined; one eidolon at a time. <paramref name="Key"/> is its least node,
/// which stays while the way does (live holders are kept by it).</summary>
public sealed record MineZone(int Id, PathCell Key, MineZoneKind Kind, IReadOnlySet<PathCell> Nodes);

/// <summary>
/// Where two eidolons cannot pass (README "Eidolon", working together, *Narrow ways*). Nodes are block
/// corners and a box covers 2 × 2 columns, so two boxes 1.7 wide pass only two nodes apart across: a way is
/// open at a node when some 3 × 3 square of node positions holding it has an out node within a step of its
/// height at all nine, the middle one joined to the other eight; anything less (a lane 2 or 3 wide, a stair,
/// a ramp or shelf under 4 columns, a 2- or 3-wide tunnel's mouth) is narrow. The stair's nodes under the
/// rim are narrow whatever their width. Narrow nodes joined are one zone. Only nodes whose box comes within
/// <see cref="WorkMargin"/> columns of the area are looked at: farther off the ground is open.
/// </summary>
public static class MineChokes
{
    /// <summary>Columns round the area where zones are looked for.</summary>
    public const int WorkMargin = 2;

    /// <summary>The zones of <paramref name="walk"/>'s out nodes over the area grown by
    /// <see cref="WorkMargin"/>, in node order (x, y, z); <paramref name="stair"/>: the plan's stair cells.</summary>
    public static IReadOnlyList<MineZone> Find(MineWalk walk, MarkArea area, IReadOnlySet<MarkPos> stair)
    {
        var site = walk.Space;
        int x0 = area.Min.X - WorkMargin, x1 = area.Max.X + WorkMargin + 1;
        int z0 = area.Min.Z - WorkMargin, z1 = area.Max.Z + WorkMargin + 1;
        int rim = area.Max.Y + 1 - EidolonTeamwork.FloorStep;
        var stairColumns = new Dictionary<(int X, int Z), List<int>>();
        foreach (var c in stair)
        {
            if (!stairColumns.TryGetValue((c.X, c.Z), out var ys))
                stairColumns[(c.X, c.Z)] = ys = new List<int>();
            ys.Add(c.Y);
        }
        var narrow = new SortedSet<PathCell>(NodeOrder.Instance);
        var onStair = new HashSet<PathCell>();
        var joins = new Dictionary<PathCell, List<PathCell>>();
        for (int x = Math.Max(x0, site.Window.Min.X + 1); x <= Math.Min(x1, site.Window.Max.X); x++)
        for (int z = Math.Max(z0, site.Window.Min.Z + 1); z <= Math.Min(z1, site.Window.Max.Z); z++)
        for (int y = site.Window.Min.Y; y <= site.Window.Max.Y; y++)
        {
            var n = new PathCell(x, y, z);
            if (!walk.Out(n))
                continue;
            bool stairNode = y < rim && OnStair(stairColumns, n);
            if (stairNode)
                onStair.Add(n);
            if (stairNode || Narrow(walk, n, joins))
                narrow.Add(n);
        }
        var zones = new List<MineZone>();
        var seen = new HashSet<PathCell>();
        foreach (var n in narrow)
        {
            if (!seen.Add(n))
                continue;
            var nodes = new HashSet<PathCell> { n };
            var queue = new Queue<PathCell>([n]);
            bool isStair = false;
            while (queue.TryDequeue(out var u))
            {
                isStair |= onStair.Contains(u);
                foreach (var v in walk.Joins(u))
                    if (narrow.Contains(v) && seen.Add(v))
                    {
                        nodes.Add(v);
                        queue.Enqueue(v);
                    }
            }
            zones.Add(new MineZone(zones.Count, nodes.Min(NodeOrder.Instance), isStair ? MineZoneKind.Stair : MineZoneKind.Narrow, nodes));
        }
        return zones;
    }

    // a node on a stair: a stair cell in its box's columns from under its feet to its head
    private static bool OnStair(Dictionary<(int X, int Z), List<int>> stair, PathCell n)
    {
        if (stair.Count == 0)
            return false;
        for (int x = n.X - 1; x <= n.X; x++)
        for (int z = n.Z - 1; z <= n.Z; z++)
            if (stair.TryGetValue((x, z), out var ys) && ys.Any(y => y >= n.Y - 1 && y <= n.Y + 3))
                return true;
        return false;
    }

    /// <summary>Whether an out node is narrow: no 3 × 3 square of node positions holding it has an out node
    /// within a step of its height at all nine, the middle one joined to the other eight.</summary>
    public static bool Narrow(MineWalk walk, PathCell node) => Narrow(walk, node, new Dictionary<PathCell, List<PathCell>>());

    // with each centre's joins kept for the next node's squares
    private static bool Narrow(MineWalk walk, PathCell node, Dictionary<PathCell, List<PathCell>> cache)
    {
        int step = walk.Space.Step;
        // the square round it first (on open ground the one that holds), then the others
        foreach (var (ox, oz) in Squares)
        for (int dy = 0; dy <= 2 * step; dy++)
        {
            int cy = node.Y + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
            var centre = new PathCell(node.X + ox, cy, node.Z + oz);
            if (!walk.Out(centre))
                continue;
            if (!cache.TryGetValue(centre, out var joins))
                cache[centre] = joins = Joined(walk, centre);
            bool all = true;
            for (int dx = -1; dx <= 1 && all; dx++)
            for (int dz = -1; dz <= 1 && all; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                int px = centre.X + dx, pz = centre.Z + dz;
                all = false;
                foreach (var j in joins)
                    if (j.X == px && j.Z == pz && Math.Abs(j.Y - node.Y) <= step)
                    {
                        all = true;
                        break;
                    }
            }
            if (all)
                return false;
        }
        return true;
    }

    private static readonly (int X, int Z)[] Squares = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    // the out nodes joined to an out node
    private static List<PathCell> Joined(MineWalk walk, PathCell centre)
    {
        var site = walk.Space;
        Span<int> into = stackalloc int[40];
        int count = walk.JoinedTo(site.Index(centre.X, centre.Y, centre.Z), into);
        var list = new List<PathCell>(count);
        for (int k = 0; k < count; k++)
            if (walk.OutAt(into[k]))
                list.Add(site.At(into[k]).ToCell());
        return list;
    }

    /// <summary>The zone a node is in, or null.</summary>
    public static MineZone? ZoneOf(IReadOnlyList<MineZone> zones, PathCell node) => new MineZoneIndex(zones).Of(node);

    /// <summary>Nodes in x, then y, then z order.</summary>
    internal sealed class NodeOrder : IComparer<PathCell>
    {
        public static readonly NodeOrder Instance = new();

        public int Compare(PathCell a, PathCell b) =>
            a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
    }
}

/// <summary>Which zone each node is in (README "Eidolon", working together, *Narrow ways*), built once from a
/// zone list.</summary>
public sealed class MineZoneIndex
{
    private readonly Dictionary<PathCell, MineZone> _of = new();

    public MineZoneIndex(IReadOnlyList<MineZone> zones)
    {
        Zones = zones;
        foreach (var z in zones)
        foreach (var n in z.Nodes)
            _of[n] = z;
    }

    public IReadOnlyList<MineZone> Zones { get; }

    public MineZone? Of(PathCell node) => _of.GetValueOrDefault(node);

    /// <summary>The runs of a path in each zone, as indices of its nodes (first and last in the zone); a
    /// zone the path goes in and out of more than once is one run from its first node there to its last.</summary>
    public IReadOnlyList<(MineZone Zone, int From, int To)> Along(IReadOnlyList<PathCell> path)
    {
        var runs = new List<(MineZone Zone, int From, int To)>();
        var at = new Dictionary<int, int>();
        for (int k = 0; k < path.Count; k++)
        {
            if (Of(path[k]) is not { } z)
                continue;
            if (at.TryGetValue(z.Id, out int r))
                runs[r] = (z, runs[r].From, k);
            else
            {
                at[z.Id] = runs.Count;
                runs.Add((z, k, k));
            }
        }
        return runs;
    }

    /// <summary><see cref="Along"/> in seconds after setting off: when it enters each zone and leaves it,
    /// at <see cref="MineCosts.WalkSecondsPerBlock"/> along the path's steps (<see cref="MineCosts.StepCost"/>),
    /// a stand at the end that is in a zone held on (<paramref name="holdEnd"/>: Leave +∞).</summary>
    public IReadOnlyList<(int Zone, double Enter, double Leave)> Timed(IReadOnlyList<PathCell> path, bool holdEnd)
    {
        var offsets = new double[path.Count];
        for (int k = 1; k < path.Count; k++)
            offsets[k] = offsets[k - 1] + MineCosts.StepCost(path[k - 1], path[k]) * MineCosts.WalkSecondsPerBlock;
        var runs = new List<(int Zone, double Enter, double Leave)>();
        foreach (var (zone, from, to) in Along(path))
        {
            // in it from the step onto its first node to the step off its last
            double enter = from == 0 ? 0 : offsets[from - 1];
            double leave = to == path.Count - 1 ? (holdEnd ? double.PositiveInfinity : offsets[to]) : offsets[to + 1];
            runs.Add((zone.Id, enter, leave));
        }
        return runs;
    }
}
