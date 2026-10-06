using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// Stepping a pulled trunk up a rise, game-independent. The trunk's boxes (entity-local, already
/// turned, <see cref="TrunkBoxes.Turned"/>) are pushed <see cref="Probe"/> blocks along the pull;
/// if that runs into a solid block and the same boxes lifted onto the top of the block layer the
/// trunk's underside sits in are clear, both where the trunk is and ahead, the trunk may rise
/// that far. Anything higher (a cliff) or a ceiling in the way gives no step. Blocks are taken as
/// whole cubes: a cell is solid or not.
/// </summary>
public static class TrunkStep
{
    /// <summary>How far ahead along the pull, blocks, the boxes are tried.</summary>
    public const double Probe = 0.15;

    /// <summary>The least horizontal motion, blocks per 1/60 s, that counts as a pull (0.3 blocks
    /// per second).</summary>
    public const double MinMotion = 0.005;

    /// <summary>The most a trunk rises in one tick, blocks, so a step takes a few ticks.</summary>
    public const double MaxLiftPerTick = 0.25;

    private const double Skin = 0.01;

    /// <summary>How far, blocks, a trunk at (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) moving along (<paramref name="mx"/>, <paramref name="mz"/>) should
    /// rise this tick: 0 for no step, else at most <see cref="MaxLiftPerTick"/>.
    /// <paramref name="solid"/> says whether the block cell (x, y, z) is solid.</summary>
    public static double Lift(IReadOnlyList<Box> boxes, double x, double y, double z,
        double mx, double mz, Func<int, int, int, bool> solid)
    {
        double m = Math.Sqrt(mx * mx + mz * mz);
        if (m < MinMotion || boxes.Count == 0)
            return 0;
        double ax = x + mx / m * Probe, az = z + mz / m * Probe;
        if (!Blocked(boxes, ax, y, az, solid))
            return 0;
        double baseY = y + boxes.Min(b => b.Y1);
        double rise = Math.Floor(baseY + Skin) + 1 - baseY;
        if (rise <= Skin || rise > 1 + Skin)
            return 0;
        double ny = y + rise + Skin;
        if (Blocked(boxes, ax, ny, az, solid) || Blocked(boxes, x, ny, z, solid))
            return 0;
        return Math.Min(MaxLiftPerTick, rise + Skin);
    }

    /// <summary>Whether any of the boxes at (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) overlaps a solid cell.</summary>
    public static bool Blocked(IReadOnlyList<Box> boxes, double x, double y, double z, Func<int, int, int, bool> solid)
    {
        foreach (var b in boxes)
        {
            int x1 = (int)Math.Floor(x + b.X1 + Skin), x2 = (int)Math.Floor(x + b.X2 - Skin);
            int y1 = (int)Math.Floor(y + b.Y1 + Skin), y2 = (int)Math.Floor(y + b.Y2 - Skin);
            int z1 = (int)Math.Floor(z + b.Z1 + Skin), z2 = (int)Math.Floor(z + b.Z2 - Skin);
            for (int cx = x1; cx <= x2; cx++)
                for (int cy = y1; cy <= y2; cy++)
                    for (int cz = z1; cz <= z2; cz++)
                        if (solid(cx, cy, cz))
                            return true;
        }
        return false;
    }
}
