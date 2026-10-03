namespace SeraphHorizons.Mod.MapReveal.Core;

/// <summary>
/// The world map's hill shading and final pixels, ported from the 1.22.7 game's
/// <c>ChunkMapLayer.GenerateChunkImage</c> (VSEssentials), with the two helpers it calls,
/// <c>BlurTool.Blur</c> and <c>ColorUtil.ColorMultiply3Clamped</c>, so a revealed chunk looks like
/// an explored one. The arithmetic (float, its order and casts) is kept as the game has it; the
/// Atlas scenarios compare the two helpers with the game's own.
///
/// A chunk's image is 32 × 32 pixels, one per block column, indexed <c>z * 32 + x</c>.
/// </summary>
public static class TerrainShade
{
    public const int Size = 32;
    public const int Area = Size * Size;

    /// <summary>The shadow value of a cell nothing shades (lakes, cells the game skips).</summary>
    public const byte Flat = 128;

    /// <summary>
    /// The shadow map before blurring: <see cref="Flat"/> for cells where <paramref name="shaded"/>
    /// is false, else 128 times the game's slope factor, from the rain height of the cell and of its
    /// north-west, west and north neighbours (brighter facing up the slope to the north-west, darker
    /// facing away). Neighbours across the chunk's edge come from <paramref name="edges"/>; a missing
    /// neighbour chunk counts as level with the cell, as on the game's client when that chunk is not
    /// loaded.
    /// </summary>
    public static byte[] ShadowMap(ReadOnlySpan<ushort> heights, NeighbourEdges edges, ReadOnlySpan<bool> shaded)
    {
        if (heights.Length != Area || shaded.Length != Area) throw new ArgumentException("expected 32 × 32 cells");
        var shadow = new byte[Area];
        for (int k = 0; k < Area; k++)
        {
            shadow[k] = Flat;
            if (!shaded[k]) continue;
            int x = k % Size, z = k / Size;
            int h = heights[k];
            // The game's three neighbours: a = (x-1, z-1), b = (x-1, z), c = (x, z-1).
            int? a = HeightAt(heights, edges, x - 1, z - 1);
            int? b = HeightAt(heights, edges, x - 1, z);
            int? c = HeightAt(heights, edges, x, z - 1);
            int da = a is { } ha ? h - ha : 0;
            int db = b is { } hb ? h - hb : 0;
            int dc = c is { } hc ? h - hc : 0;
            shadow[k] = (byte)((float)Flat * SlopeFactor(da, db, dc));
        }
        return shadow;
    }

    /// <summary>The game's slope factor from the height differences to the three neighbours.</summary>
    public static float SlopeFactor(int da, int db, int dc)
    {
        float sum = Math.Sign(da) + Math.Sign(db) + Math.Sign(dc);
        float max = Math.Max(Math.Max(Math.Abs(da), Math.Abs(db)), Math.Abs(dc));
        float factor = 1f;
        if (sum > 0f) factor = 1.08f + Math.Min(0.5f, max / 10f) / 1.25f;
        if (sum < 0f) factor = 0.92f - Math.Min(0.5f, max / 10f) / 1.25f;
        return factor;
    }

    // The game looks a neighbour up in the map chunk it falls in. Where only one coordinate is
    // outside the chunk, it reads both west-ish lookups (or both north-ish) from that one neighbour,
    // which is what reading x-1 / z-1 through the edges gives.
    private static int? HeightAt(ReadOnlySpan<ushort> heights, NeighbourEdges edges, int x, int z)
    {
        if (x >= 0 && z >= 0) return heights[z * Size + x];
        if (x < 0 && z < 0) return edges.Corner;
        if (x < 0) return edges.West is { } w ? w[z] : null;
        return edges.North is { } n ? n[x] : null;
    }

    /// <summary>
    /// The chunk's final pixels (ABGR as the game stores them, opaque) from each cell's colour and
    /// <paramref name="shadow"/> (<see cref="ShadowMap"/>): the shadow is blurred, quantized to
    /// fifths, and the unblurred value's remainder added back, as the game does.
    /// </summary>
    public static int[] Render(ReadOnlySpan<int> colors, ReadOnlySpan<byte> shadow)
    {
        if (colors.Length != Area || shadow.Length != Area) throw new ArgumentException("expected 32 × 32 cells");
        byte[] blurred = shadow.ToArray();
        Blur(blurred, Size, Size, 2);
        var pixels = new int[Area];
        float scale = 1f;
        for (int m = 0; m < Area; m++)
        {
            float b = (float)(int)(((float)(int)blurred[m] / 128f - 1f) * 5f) / 5f;
            b += ((float)(int)shadow[m] / 128f - 1f) * 5f % 1f / 5f;
            pixels[m] = ColorMultiply3Clamped(colors[m], b * scale + 1f) | unchecked((int)0xFF000000);
        }
        return pixels;
    }

    /// <summary>The game's <c>ColorUtil.ColorMultiply3Clamped</c>: multiplies the three colour
    /// channels, clamped to 0..255, and keeps alpha.</summary>
    public static int ColorMultiply3Clamped(int color, float multiplier) =>
        (int)(color & 0xFF000000u)
        | ((int)Clamp((float)((color >> 16) & 0xFF) * multiplier, 0f, 255f) << 16)
        | ((int)Clamp((float)((color >> 8) & 0xFF) * multiplier, 0f, 255f) << 8)
        | (int)Clamp((float)(color & 0xFF) * multiplier, 0f, 255f);

    private static float Clamp(float val, float min, float max) => val < min ? min : val > max ? max : val;

    /// <summary>The game's <c>BlurTool.Blur</c>: a horizontal then a vertical box blur, in place.</summary>
    public static void Blur(byte[] data, int sizeX, int sizeZ, int range)
    {
        BoxBlurHorizontal(data, range, 0, 0, sizeX, sizeZ);
        BoxBlurVertical(data, range, 0, 0, sizeX, sizeZ);
    }

    private static void BoxBlurHorizontal(byte[] map, int range, int xStart, int yStart, int xEnd, int yEnd)
    {
        int width = xEnd - xStart;
        int half = range / 2;
        int row = yStart * width;
        var line = new byte[width];
        for (int y = yStart; y < yEnd; y++)
        {
            int count = 0, sum = 0;
            for (int x = xStart - half; x < xEnd; x++)
            {
                int old = x - half - 1;
                if (old >= xStart)
                {
                    sum -= map[row + old];
                    count--;
                }
                int next = x + half;
                if (next < xEnd)
                {
                    sum += map[row + next];
                    count++;
                }
                if (x >= xStart) line[x] = (byte)(sum / count);
            }
            for (int x = xStart; x < xEnd; x++) map[row + x] = line[x];
            row += width;
        }
    }

    private static void BoxBlurVertical(byte[] map, int range, int xStart, int yStart, int xEnd, int yEnd)
    {
        int width = xEnd - xStart;
        int height = yEnd - yStart;
        int half = range / 2;
        var column = new byte[height];
        int oldOffset = -(half + 1) * width;
        int nextOffset = half * width;
        for (int x = xStart; x < xEnd; x++)
        {
            int count = 0, sum = 0;
            int index = yStart * width - half * width + x;
            for (int y = yStart - half; y < yEnd; y++)
            {
                if (y - half - 1 >= yStart)
                {
                    sum -= map[index + oldOffset];
                    count--;
                }
                if (y + half < yEnd)
                {
                    sum += map[index + nextOffset];
                    count++;
                }
                if (y >= yStart) column[y] = (byte)(sum / count);
                index += width;
            }
            for (int y = yStart; y < yEnd; y++) map[y * width + x] = column[y];
        }
    }
}

/// <summary>
/// The rain heights just across a chunk's north-west edges, which <see cref="TerrainShade.ShadowMap"/>
/// shades the edge cells with. Null where that neighbour chunk does not exist.
/// </summary>
/// <param name="West">The west neighbour's easternmost column of cells (x = 31), by z.</param>
/// <param name="North">The north neighbour's southernmost row of cells (z = 31), by x.</param>
/// <param name="Corner">The north-west neighbour's south-east cell (31, 31).</param>
public readonly record struct NeighbourEdges(ushort[]? West, ushort[]? North, int? Corner)
{
    public static readonly NeighbourEdges None = new(null, null, null);

    /// <summary>The edges from the three neighbours' full height maps (null where one is missing).</summary>
    public static NeighbourEdges From(ushort[]? west, ushort[]? north, ushort[]? northWest)
    {
        ushort[]? w = null, n = null;
        if (west is not null)
        {
            w = new ushort[TerrainShade.Size];
            for (int z = 0; z < TerrainShade.Size; z++) w[z] = west[z * TerrainShade.Size + TerrainShade.Size - 1];
        }
        if (north is not null)
        {
            n = new ushort[TerrainShade.Size];
            Array.Copy(north, (TerrainShade.Size - 1) * TerrainShade.Size, n, 0, TerrainShade.Size);
        }
        int? corner = northWest is null ? null : northWest[TerrainShade.Area - 1];
        return new NeighbourEdges(w, n, corner);
    }
}
