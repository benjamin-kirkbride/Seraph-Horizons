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
/// tested with the whole box: level, up one block (both where it stands and where it goes free one
/// higher), or down a drop of at most <see cref="MaxFall"/>; a diagonal step must also be free half
/// way, so it does not cut a corner.
/// </summary>
public sealed class WidePath(IWideSpace space, int maxFall, bool stepUp = true)
{
    public const float Impassable = 10000f;

    private static readonly (int Dx, int Dz)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    public int MaxFall { get; } = maxFall;

    /// <summary>Nodes visited by the last search.</summary>
    public int Visited { get; private set; }

    /// <summary>The node whose corner is nearest the point (x, z), in the block its feet are in.</summary>
    public static PathCell NodeAt(double x, double y, double z) =>
        new((int)Math.Round(x, MidpointRounding.AwayFromZero), (int)Math.Floor(y + 0.01), (int)Math.Round(z, MidpointRounding.AwayFromZero));

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
        else if (stepUp && space.Free(tx, y + 1, tz) && space.Free(from.X, y + 1, from.Z))
            y++;
        else
            return null;
        if (dx != 0 && dz != 0 && !space.Free(from.X + dx * 0.5, Math.Max(y, from.Y), from.Z + dz * 0.5))
            return null;
        var to = new PathCell(tx, y, tz);
        cost = space.Cost(to);
        return cost > Impassable ? null : to;
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
