namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// The crew order's rules (#679; README "Eidolon", the crew), game-independent: which trunk
/// lying is the one a tree threw, and the loop's timings.
/// </summary>
public static class EidolonCrew
{
    /// <summary>How far (horizontally) from the stump a felled tree's trunk may come to lie: Logging
    /// Expanded throws it along the tree's fall, up to about its height.</summary>
    public const double TrunkReach = 14;

    /// <summary>How far above and below the stump the trunk may lie (the ground falls away).</summary>
    public const double TrunkDrop = 10;

    /// <summary>How long after a tree falls it waits before looking for its trunk (the trunk lands and
    /// settles: a trunk that moves under a hauler is lost to it).</summary>
    public const double SettleSeconds = 1.5;

    /// <summary>How long it looks for a felled tree's trunk before it carries on without one (a tree
    /// too small for a trunk, or no Logging Expanded: its logs fell as items).</summary>
    public const double LookSeconds = 6;

    /// <summary>Times a trunk may be lost to the hauler (it rolled, it had no way to it, it took too
    /// long to reach) before it is put by until the next tree falls (which may open a way to it), or
    /// left once the area is clear.</summary>
    public const int Tries = 3;

    /// <summary>The longest it goes for a trunk before it counts it lost (a path that never arrives:
    /// the trunk lies in another tree's crown).</summary>
    public const double FetchSeconds = 45;

    /// <summary>With the area clear, how often it looks again for a grown tree (a replanted one grows).</summary>
    public const double RelookSeconds = 60;

    /// <summary>Whether a trunk lying at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
    /// may be the one the tree whose stump is at (<paramref name="sx"/>, <paramref name="sy"/>,
    /// <paramref name="sz"/>) threw: within <see cref="TrunkReach"/> and <see cref="TrunkDrop"/> of the
    /// stump's centre. (It must also be new: not lying there before the tree fell.)</summary>
    public static bool NearStump(int sx, int sy, int sz, double x, double y, double z)
    {
        double dx = x - (sx + 0.5), dz = z - (sz + 0.5);
        return dx * dx + dz * dz <= TrunkReach * TrunkReach && Math.Abs(y - sy) <= TrunkDrop;
    }
}
