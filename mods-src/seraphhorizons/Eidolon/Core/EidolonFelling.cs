namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What the fellers of one shared job share (README "Eidolon", working together): the stumps
/// every member passes over for what they are (a young tree, one in a land claim its owner may not break
/// in, one that would not fall), and the stumps some member gave up for want of a way to them, for the
/// report. A tree one member cannot reach another may, so each keeps its own unreachable stumps for its
/// search; only the report reads them all here.</summary>
public sealed class FellJobCells
{
    public HashSet<MarkPos> Skip { get; } = new();
    public HashSet<MarkPos> Unreachable { get; } = new();
}

/// <summary>
/// The trees one feller gave up for want of a way to them (README "Eidolon", felling): passed over in
/// its search, but a way is often wanting only for a while (another eidolon or a felled trunk lying in
/// it), so once it finds nothing else it tries each that still stands once more (<see cref="Retry"/>)
/// before it says it has nothing left; one given up again stays given up for the order's life. Its
/// own: a tree one eidolon cannot reach another may.
/// </summary>
public sealed class FellGiveUps
{
    private readonly HashSet<MarkPos> _given = new();
    private readonly HashSet<MarkPos> _retried = new();

    /// <summary>The stumps it passes over now.</summary>
    public IReadOnlyCollection<MarkPos> Given => _given;

    public bool Has(MarkPos stump) => _given.Contains(stump);

    public void GiveUp(MarkPos stump) => _given.Add(stump);

    /// <summary>Gives back to its search the stumps given up only once that still stand
    /// (<paramref name="standing"/>): true when there were any, and it searches again before it is done.</summary>
    public bool Retry(Func<MarkPos, bool> standing)
    {
        var again = _given.Where(s => !_retried.Contains(s) && standing(s)).ToList();
        foreach (var s in again)
        {
            _given.Remove(s);
            _retried.Add(s);
        }
        return again.Count > 0;
    }
}

/// <summary>
/// The fell order's rules (#677; README "Eidolon", felling), game-independent: which logs are a wild
/// tree's stump, when a tree is grown enough to fell, where it stands to swing, how long the swing
/// takes, the area's height slack, and what replants a tree.
/// </summary>
public static class EidolonFelling
{
    /// <summary>How far below the area's lower corner and above its higher one a stump may stand: the
    /// player marks the corners on the ground, which rises and falls inside.</summary>
    public const int AreaHeightSlack = 8;

    /// <summary>Swings of the axe per tree; the last one's cut fells it.</summary>
    public const int Swings = 3;

    /// <summary>The shape's <c>fell</c> animation (Eidolon/README.md): 40 frames at 30 a second,
    /// the cut landing on frame 15 (<c>make_shape.py</c>'s <c>EVENTS</c>).</summary>
    public const double SwingSeconds = 40 / 30.0;
    public const double CutSeconds = 15 / 30.0;

    /// <summary>Where it stands to swing: its centre this far (horizontally) from the trunk's centre.
    /// The cut lands about 1.4 blocks in front of its centre and the axe is a block long, so a trunk
    /// 2 to 2.5 blocks in front is right; up to <see cref="StandFar"/> still reaches.</summary>
    public const double StandNear = 1.5;
    public const double StandBest = 2.8;
    public const double StandFar = 3.6;

    /// <summary>The furthest (horizontally, centre to the trunk's centre) it swings from.</summary>
    public const double Reach = 3.8;

    /// <summary>
    /// Whether a block is a wild tree's log the axe fells: it has the game's tree felling group
    /// (<paramref name="hasFellingGroup"/>, <c>treeFellingGroupCode</c>), is wood, is not a fruit
    /// tree's branch (an orchard is the player's), and is grown, not placed (<c>log-grown-*</c>,
    /// <c>logsection-grown-*</c>, <c>lognarrow-grown-*</c>, and the resin logs, which only grow).
    /// A player's log building has placed logs, which have no felling group, so it is never felled.
    /// </summary>
    public static bool IsWildLog(string codePath, bool hasFellingGroup, bool isWood) =>
        hasFellingGroup && isWood
        && !codePath.StartsWith("fruittree", StringComparison.Ordinal)
        && (codePath.Contains("-grown-", StringComparison.Ordinal)
            || codePath.StartsWith("log-resin", StringComparison.Ordinal));

    /// <summary>Whether a wild log is a stump: upright, what is under it is not a log of the same tree,
    /// and it stands on the ground (<paramref name="belowSolid"/>: the block below is solid on top). An
    /// upright log of a crown over air or leaves (an oak's boughs) is no stump: there is no place beside
    /// it to stand. <paramref name="belowSameGroup"/>: the block below has the same felling group.</summary>
    public static bool IsStump(string codePath, bool belowSameGroup, bool belowSolid) =>
        !belowSameGroup && belowSolid
        && (codePath.EndsWith("-ud", StringComparison.Ordinal) || codePath.Contains("-ud-", StringComparison.Ordinal)
            || codePath.StartsWith("logsection-", StringComparison.Ordinal));

    /// <summary>How many trees <paramref name="stumps"/> are: stumps side by side (a big tree's 2 × 2
    /// trunk of log sections) are one tree.</summary>
    public static int Trees(IEnumerable<MarkPos> stumps)
    {
        var left = stumps.ToHashSet();
        int trees = 0;
        while (left.Count > 0)
        {
            trees++;
            var next = new Stack<MarkPos>([left.First()]);
            left.Remove(next.Peek());
            while (next.TryPop(out var s))
                foreach (var o in left.Where(o => Math.Abs(o.X - s.X) <= 1 && Math.Abs(o.Z - s.Z) <= 1 && Math.Abs(o.Y - s.Y) <= 1).ToList())
                {
                    left.Remove(o);
                    next.Push(o);
                }
        }
        return trees;
    }

    /// <summary>Whether a tree of <paramref name="woodBlocks"/> wood blocks (the axe's tree search
    /// from its stump) is grown: at least <paramref name="minLogs"/>. A sapling is not a log at all;
    /// a young tree the world grew small has few logs.</summary>
    public static bool Mature(int woodBlocks, int minLogs) => woodBlocks >= minLogs;

    /// <summary>
    /// The block corners it may stand on to fell the tree at block (<paramref name="stumpX"/>,
    /// <paramref name="stumpZ"/>), as the wide pathfinder's nodes: every corner whose distance to the
    /// trunk's centre is from <see cref="StandNear"/> to <see cref="StandFar"/>, the ones up to
    /// <see cref="StandBest"/> first, then nearest to (<paramref name="fromX"/>, <paramref name="fromZ"/>).
    /// </summary>
    public static IReadOnlyList<(int X, int Z)> StandCorners(int stumpX, int stumpZ, double fromX, double fromZ)
    {
        double cx = stumpX + 0.5, cz = stumpZ + 0.5;
        var corners = new List<(int X, int Z, bool Best, double From)>();
        for (int x = stumpX - 4; x <= stumpX + 5; x++)
        for (int z = stumpZ - 4; z <= stumpZ + 5; z++)
        {
            double d = Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
            if (d < StandNear || d > StandFar)
                continue;
            corners.Add((x, z, d <= StandBest, (x - fromX) * (x - fromX) + (z - fromZ) * (z - fromZ)));
        }
        return corners.OrderByDescending(c => c.Best).ThenBy(c => c.From).ThenBy(c => c.X).ThenBy(c => c.Z)
            .Select(c => (c.X, c.Z)).ToList();
    }

    /// <summary>Whether a centre at (<paramref name="x"/>, <paramref name="z"/>) reaches the trunk at
    /// block (<paramref name="stumpX"/>, <paramref name="stumpZ"/>).</summary>
    public static bool InReach(double x, double z, int stumpX, int stumpZ)
    {
        double dx = x - (stumpX + 0.5), dz = z - (stumpZ + 0.5);
        return dx * dx + dz * dz <= Reach * Reach;
    }

    /// <summary>The yaw that faces from (<paramref name="x"/>, <paramref name="z"/>) to (<paramref name="toX"/>,
    /// <paramref name="toZ"/>), as the game's creatures turn (<c>atan2(dx, dz)</c>).</summary>
    public static float Yaw(double x, double z, double toX, double toZ) => (float)Math.Atan2(toX - x, toZ - z);

    /// <summary>When, in seconds from the first swing's start, each swing's cut lands; the last fells.</summary>
    public static double CutAt(int swing) => swing * SwingSeconds + CutSeconds;

    /// <summary>The game's sapling block that replants a tree of <paramref name="wood"/> (the log's
    /// <c>wood</c> variant), as a tree seed plants it: <c>game:sapling-{wood}-free</c>.</summary>
    public static string SaplingCode(string wood) => "game:sapling-" + wood + "-free";

    /// <summary>Whether a carried stack (its full code, domain included) replants a tree of
    /// <paramref name="wood"/>: that tree's sapling (any cover) or its seed.</summary>
    public static bool Replants(string stackCode, string wood) =>
        stackCode.StartsWith("game:sapling-" + wood + "-", StringComparison.Ordinal)
        || stackCode == "game:treeseed-" + wood;
}
