namespace SeraphHorizons.Mod.MapReveal.Core;

/// <summary>A chunk column's position, in chunks (block X / 32, block Z / 32).</summary>
public readonly record struct ColumnPos(int X, int Z);

/// <summary>
/// Which chunk columns a reveal covers, and in what order.
/// </summary>
public static class RevealArea
{
    /// <summary>The largest radius the command takes, in chunks (8192 blocks): about 206,000 columns,
    /// a few hundred MB sent to the client. Beyond that a reveal would run for hours.</summary>
    public const int MaxRadius = 256;

    /// <summary>
    /// Every column whose position is within <paramref name="radius"/> chunks of the centre column
    /// (dx² + dz² ≤ radius²), inside the world (0 ≤ X &lt; <paramref name="mapChunksX"/>, same for Z).
    /// Row by row, north to south, each row west to east: a column's west and north neighbours come
    /// before it, so the reader's caches already hold their heights (<see cref="ColumnSampler"/>
    /// shades a column from them).
    /// </summary>
    public static List<ColumnPos> Columns(int centerX, int centerZ, int radius, int mapChunksX, int mapChunksZ)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must not be negative");
        var columns = new List<ColumnPos>();
        long r2 = (long)radius * radius;
        for (int dz = -radius; dz <= radius; dz++)
        {
            int z = centerZ + dz;
            if (z < 0 || z >= mapChunksZ) continue;
            int half = (int)Math.Floor(Math.Sqrt(r2 - (long)dz * dz));
            int x0 = Math.Max(0, centerX - half), x1 = Math.Min(mapChunksX - 1, centerX + half);
            for (int x = x0; x <= x1; x++)
                columns.Add(new ColumnPos(x, z));
        }
        return columns;
    }
}
