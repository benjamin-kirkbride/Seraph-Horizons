namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Chopper output (<c>ChopperOutput</c>): where a powered chopper's finished batch is dropped.
/// Game-independent, so tests/ runs it without the game.
///
/// Every piece is dropped at rest over the middle of the cell directly in front of the chopper's
/// master block, on its output side, just above the machine's floor (<see cref="Height"/>),
/// spread across the output side by at most <see cref="Spread"/> and jittered by at most
/// <see cref="Jitter"/>: at least 0.3 from the cell's edges. With no velocity it drops straight
/// down and stays in that cell, so a hopper sunk into the floor there catches all of it. That cell
/// is not part of the chopper's footprint, so the spawn point is clear of the machine.
///
/// The drop is kept short because the game blows a falling item along with the wind (an
/// <c>EntityItem</c> takes the wind's speed while it touches nothing): in a wind of about 1,
/// firewood dropped from the sawmill's 0.4 drifted 0.17 sideways, from 0.1 about 0.06.
/// </summary>
public static class ChopperEject
{
    /// <summary>From the master block's centre to the drop point, along the output side: the
    /// middle of the cell in front, as the sawmill drops in the middle of the cell behind its
    /// footprint.</summary>
    public const double Distance = 1.0;

    /// <summary>Above the bottom of the master block (the sawmill drops from 0.4).</summary>
    public const double Height = 0.1;

    /// <summary>Total width the pieces are spread over across the output side, centres from -0.15
    /// to +0.15 for many pieces (the chopper spreads them over 0.4, the sawmill over 0.5).</summary>
    public const double Spread = 0.3;

    /// <summary>Random offset on each horizontal axis, at most this either way (the chopper's own).</summary>
    public const double Jitter = 0.05;

    /// <summary>One item entity to spawn: where, and how many items.</summary>
    public readonly record struct Drop(double X, double Y, double Z, int Count);

    /// <summary><paramref name="amount"/> split into <paramref name="dropCount"/> (at least 1, at
    /// most <paramref name="amount"/>) parts that differ by at most one, the larger first: Immersive
    /// Woodworking's own <c>DropDistribution.SplitEvenly</c>.</summary>
    public static int[] SplitEvenly(int amount, int dropCount)
    {
        if (amount <= 0)
            return [];
        int parts = Math.Min(Math.Max(1, dropCount), amount);
        var split = new int[parts];
        for (int i = 0; i < parts; i++)
            split[i] = amount / parts + (i < amount % parts ? 1 : 0);
        return split;
    }

    /// <summary>The drops for a batch of <paramref name="amount"/> items in
    /// <paramref name="dropCount"/> piles, from the chopper whose master block is at
    /// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) and whose output side is
    /// (<paramref name="outX"/>, 0, <paramref name="outZ"/>), a unit horizontal direction. A pile
    /// larger than <paramref name="maxStackSize"/> is spawned as several entities at the same
    /// point. <paramref name="nextDouble"/> gives [0, 1) for the jitter (the world's random).</summary>
    public static List<Drop> Plan(int x, int y, int z, int outX, int outZ, int amount, int dropCount,
        int maxStackSize, Func<double> nextDouble)
    {
        var drops = new List<Drop>();
        int[] piles = SplitEvenly(amount, dropCount);
        double cx = x + 0.5 + outX * Distance, cz = z + 0.5 + outZ * Distance;
        // Across the output side: x for an output along z, z for one along x.
        double acrossX = outZ != 0 ? 1 : 0, acrossZ = outX != 0 ? 1 : 0;
        int perEntity = Math.Max(1, maxStackSize);
        for (int i = 0; i < piles.Length; i++)
        {
            double across = piles.Length > 1 ? ((i + 0.5) / piles.Length - 0.5) * Spread : 0;
            double px = cx + acrossX * across + (nextDouble() * 2 - 1) * Jitter;
            double pz = cz + acrossZ * across + (nextDouble() * 2 - 1) * Jitter;
            for (int left = piles[i]; left > 0; left -= perEntity)
                drops.Add(new Drop(px, y + Height, pz, Math.Min(left, perEntity)));
        }
        return drops;
    }
}
