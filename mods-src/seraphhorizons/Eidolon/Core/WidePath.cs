namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A path node: for an entity an even number of blocks wide, the block corner its feet's
/// centre stands on (world x and z are the node's own; y is the block its feet are in).</summary>
public readonly record struct PathCell(int X, int Y, int Z);

/// <summary>
/// The space an entity moves through, asked by its collision box: the game's collision tester, or a
/// test's voxel grid.
/// </summary>
public interface IWideSpace
{
    /// <summary>Whether the entity's collision box, its feet's centre at (x, y, z), touches nothing solid.</summary>
    bool Free(double x, double y, double z);

    /// <summary>The extra cost of standing at <paramref name="cell"/> (water, a path's speed), or above
    /// <see cref="WidePath.Impassable"/> where it must never stand (lava, fire).</summary>
    float Cost(PathCell cell);
}

/// <summary>
/// A* for a big walker (Eidolon/README.md, "Pathfinding"). The game's own (<c>AStar</c>) puts the
/// entity's centre near a block's middle, so a box 1.7 wide always spans three blocks and never fits
/// a two-block gate; here the nodes are block corners, so a box up to 2 wide spans two. Each step is
/// tested with the whole box: level, up at most <see cref="MaxClimb"/> blocks (free where it stands at
/// every height up to the one it lands at, and where it goes only at that one), or down a drop of at
/// most <see cref="MaxFall"/>; a diagonal step must also be free half way, at the higher end's height,
/// so it does not cut a corner. A climb costs <see cref="ClimbCost"/> for each block past the first, so
/// it takes a gentler way where there is one near.
/// </summary>
public sealed class WidePath(IWideSpace space, int maxFall, int maxClimb = 1)
{
    public const float Impassable = 10000f;

    /// <summary>The extra cost of a step up for each block past the first: one block is walked, two are
    /// climbed (<see cref="EidolonClimb"/>), two seconds at the foot of the ledge.</summary>
    public const float ClimbCost = 3f;

    private static readonly (int Dx, int Dz)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    public int MaxFall { get; } = maxFall;

    public int MaxClimb { get; } = maxClimb;

    /// <summary>Nodes visited by the last search.</summary>
    public int Visited { get; private set; }

    /// <summary>Whether the last <see cref="FindAround"/> found its path round the others (false: it
    /// found none round them, and the one it gave goes through them).</summary>
    public bool WentAround { get; private set; }

    /// <summary>The node whose corner is nearest the point (x, z), in the block its feet are in.</summary>
    public static PathCell NodeAt(double x, double y, double z) =>
        new((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Floor(y + 0.01), (int)Math.Round(z, MidpointRounding.AwayFromZero));

    /// <summary>Where its feet stand on a floor at (<paramref name="x"/>, <paramref name="z"/>): the
    /// first of the heights <paramref name="y"/> + each of <paramref name="offsets"/> (in their order)
    /// where its box is free and would not be one block lower; null for none. A walk's target is put
    /// there, not at the height of what it goes to (a trunk resting on a stump lies above the floor).</summary>
    public static int? Feet(IWideSpace space, double x, int y, double z, ReadOnlySpan<int> offsets)
    {
        foreach (int dy in offsets)
            if (space.Free(x, y + dy, z) && !space.Free(x, y + dy - 1, z))
                return y + dy;
        return null;
    }

    /// <summary>Where a step from <paramref name="from"/> by (dx, dz) lands, or null when it cannot.</summary>
    public PathCell? Step(PathCell from, int dx, int dz, out float cost)
    {
        cost = 0;
        int tx = from.X + dx, tz = from.Z + dz, y = from.Y;
        if (space.Free(tx, y, tz))
        {
            int fall = 0;
            while (space.Free(tx, y - 1, tz))
            {
                y--;
                if (++fall > MaxFall)
                    return null;
            }
        }
        else
        {
            // up: the lowest height it is free there, with headroom where it stands all the way up
            int rise = 0;
            do
            {
                if (++rise > MaxClimb || !space.Free(from.X, from.Y + rise, from.Z))
                    return null;
            }
            while (!space.Free(tx, from.Y + rise, tz));
            y += rise;
        }
        if (dx != 0 && dz != 0 && !space.Free(from.X + dx * 0.5, Math.Max(y, from.Y), from.Z + dz * 0.5))
            return null;
        var to = new PathCell(tx, y, tz);
        cost = space.Cost(to);
        if (cost > Impassable)
            return null;
        cost += Math.Max(0, y - from.Y - 1) * ClimbCost;
        return to;
    }

    /// <summary>The cheapest path from <paramref name="start"/> to within <paramref name="tolerance"/>
    /// blocks of <paramref name="goal"/> horizontally (and two vertically), start and end included;
    /// null when there is none within <paramref name="maxNodes"/> nodes visited.</summary>
    public List<PathCell>? Find(PathCell start, PathCell goal, int maxNodes, int tolerance = 0)
    {
        Visited = 0;
        var open = new PriorityQueue<PathCell, float>();
        var gCost = new Dictionary<PathCell, float> { [start] = 0 };
        var parent = new Dictionary<PathCell, PathCell>();
        var closed = new HashSet<PathCell>();
        open.Enqueue(start, Heuristic(start, goal));
        while (open.TryDequeue(out var node, out _))
        {
            if (!closed.Add(node))
                continue;
            if (Reached(node, goal, tolerance))
                return Retrace(parent, start, node);
            if (++Visited > maxNodes)
                return null;
            float g = gCost[node];
            foreach (var (dx, dz) in Directions)
            {
                if (Step(node, dx, dz, out float extra) is not { } next || closed.Contains(next))
                    continue;
                float ng = g + Distance(node, next) + extra;
                if (gCost.TryGetValue(next, out float old) && old <= ng)
                    continue;
                gCost[next] = ng;
                parent[next] = node;
                open.Enqueue(next, ng + Heuristic(next, goal));
            }
        }
        return null;
    }

    /// <summary>
    /// <see cref="Find"/> with other walkers' boxes in the way (<see cref="Others"/>, each at the node
    /// it stands nearest), each grown by <see cref="Others.Margin"/>: first round them, and finding no
    /// way round, through them (<see cref="WentAround"/> false), so two in a narrow way never both wait
    /// on each other; <see cref="EidolonSpacing"/>'s push keeps them apart there. One whose box touches
    /// the start's or the goal's is left out: it is not a wall to walk round, and the goal stays reachable.
    /// </summary>
    public List<PathCell>? FindAround(PathCell start, PathCell goal, Others others, int maxNodes, int tolerance = 0)
    {
        var kept = others.Without(start).Without(goal);
        if (kept.Count > 0)
        {
            var round = new WidePath(new AvoidingSpace(space, kept), MaxFall, MaxClimb);
            var path = round.Find(start, goal, maxNodes, tolerance);
            int visited = round.Visited;
            if (path != null)
            {
                Visited = visited;
                WentAround = true;
                return path;
            }
            var through = Find(start, goal, maxNodes, tolerance);
            Visited += visited;
            WentAround = false;
            return through;
        }
        WentAround = true;
        return Find(start, goal, maxNodes, tolerance);
    }

    /// <summary>The space with <see cref="Others"/>' boxes made solid.</summary>
    private sealed class AvoidingSpace(IWideSpace space, Others others) : IWideSpace
    {
        public bool Free(double x, double y, double z) => !others.Touches(x, y, z) && space.Free(x, y, z);

        public float Cost(PathCell cell) => space.Cost(cell);
    }

    private static bool Reached(PathCell node, PathCell goal, int tolerance) =>
        Math.Abs(node.X - goal.X) <= tolerance && Math.Abs(node.Z - goal.Z) <= tolerance && Math.Abs(node.Y - goal.Y) <= 2;

    private static float Distance(PathCell a, PathCell b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static float Heuristic(PathCell a, PathCell goal) => Distance(a, goal);

    private static List<PathCell> Retrace(Dictionary<PathCell, PathCell> parent, PathCell start, PathCell end)
    {
        var path = new List<PathCell> { end };
        while (!path[^1].Equals(start))
            path.Add(parent[path[^1]]);
        path.Reverse();
        return path;
    }
}

/// <summary>
/// Other walkers of the same box in the way of a search (<see cref="WidePath.FindAround"/>): where
/// each stands (its feet's centre), its box <paramref name="halfWidth"/> each side and
/// <paramref name="height"/> tall, as the walker's own. A node is blocked where the walker's box there
/// would come within <see cref="Margin"/> of one's.
/// </summary>
public sealed class Others(IEnumerable<(double X, double Y, double Z)> at, double halfWidth, double height)
{
    /// <summary>The room, in blocks, kept between its box and another's.</summary>
    public const double Margin = 0.3;

    private readonly List<(double X, double Y, double Z)> _at = at.ToList();

    public int Count => _at.Count;

    public IReadOnlyList<(double X, double Y, double Z)> At => _at;

    /// <summary>Whether its box with its feet's centre at (x, y, z) comes within <see cref="Margin"/> of
    /// one of theirs.</summary>
    public bool Touches(double x, double y, double z)
    {
        double reach = 2 * halfWidth + Margin;
        foreach (var o in _at)
            if (Math.Abs(o.X - x) < reach && Math.Abs(o.Z - z) < reach && y < o.Y + height && o.Y < y + height)
                return true;
        return false;
    }

    /// <summary>These without any whose box comes within <see cref="Margin"/> of its own at <paramref name="node"/>.</summary>
    public Others Without(PathCell node) =>
        new(_at.Where(o => !new Others([o], halfWidth, height).Touches(node.X, node.Y, node.Z)), halfWidth, height);

    /// <summary>Where it stands at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), and
    /// walking at (<paramref name="perSecondX"/>, <paramref name="perSecondZ"/>) blocks a second, where it
    /// will be every <paramref name="step"/> seconds for <paramref name="seconds"/>: its way to keep clear of.</summary>
    public static IEnumerable<(double X, double Y, double Z)> Sweep(double x, double y, double z, double perSecondX, double perSecondZ,
        double seconds, double step)
    {
        yield return (x, y, z);
        for (double t = step; t <= seconds + 1e-9; t += step)
            yield return (x + perSecondX * t, y, z + perSecondZ * t);
    }
}
