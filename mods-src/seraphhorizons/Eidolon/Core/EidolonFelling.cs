namespace SeraphHorizons.Mod.Eidolon.Core;

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

    /// <summary>Whether a wild log is a stump: upright, and what is under it is not a log of the same
    /// tree (it stands on the ground). <paramref name="belowSameGroup"/>: the block below has the
    /// same felling group.</summary>
    public static bool IsStump(string codePath, bool belowSameGroup) =>
        !belowSameGroup && (codePath.EndsWith("-ud", StringComparison.Ordinal) || codePath.Contains("-ud-", StringComparison.Ordinal)
                            || codePath.StartsWith("logsection-", StringComparison.Ordinal));

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
