namespace SeraphHorizons.Mod.MapReveal.Core;

/// <summary>What the map colours a cell by (<see cref="ColumnSample.Kinds"/>).</summary>
public enum CellKind : byte
{
    /// <summary>Coloured by its block's map colour, and shaded by the slope.</summary>
    Land = 0,
    /// <summary>Water (or ice that is not glacier ice) surrounded by more: its block's colour, unshaded.</summary>
    Lake = 1,
    /// <summary>Water next to something that is not: the map's <c>wateredge</c> colour, unshaded.</summary>
    WaterEdge = 2,
    /// <summary>A rain height above the world (the game skips the cell): colour 0, unshaded.</summary>
    None = 3,
}

/// <summary>
/// The terrain a reveal reads, as the game's savegame holds it. Only fully generated columns count:
/// a column still part-way through world generation is treated as missing, as the game's client
/// never receives one.
/// </summary>
public interface ISurfaceSource
{
    /// <summary>The column's rain height map (1024 cells, <c>z * 32 + x</c>), or null if the column
    /// does not exist or is not fully generated.</summary>
    ushort[]? Heights(ColumnPos column);

    /// <summary>The block at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) within
    /// the column (x and z 0..31), the fluid if there is one, else the solid block (the game's block
    /// layer 3, which the map reads); false if that chunk does not exist.</summary>
    bool TryBlock(ColumnPos column, int x, int y, int z, out int blockId);
}

/// <summary>The block properties the map's colouring depends on, by block id.</summary>
/// <param name="IsLake">Water, or ice other than glacier ice (the game's <c>ChunkMapLayer.isLake</c>).</param>
/// <param name="IsSnow">Snow: the map shows the block under it, unless the world map is colour-accurate.</param>
public sealed record BlockTraits(bool[] IsLake, bool[] IsSnow)
{
    public bool Lake(int id) => (uint)id < (uint)IsLake.Length && IsLake[id];
    public bool Snow(int id) => (uint)id < (uint)IsSnow.Length && IsSnow[id];
}

/// <summary>
/// One revealed chunk column: per cell, the block the map colours it by, how (<see cref="CellKind"/>)
/// and its unblurred shadow. The client turns it into the map's pixels with its own colours
/// (<see cref="TerrainShade.Render"/>).
/// </summary>
public sealed class ColumnSample
{
    public int X { get; init; }
    public int Z { get; init; }
    public int[] BlockIds { get; init; } = new int[TerrainShade.Area];
    public CellKind[] Kinds { get; init; } = new CellKind[TerrainShade.Area];
    public byte[] Shadow { get; init; } = new byte[TerrainShade.Area];

    /// <summary>The y of each cell's block, sent only for a colour-accurate map, whose colours depend
    /// on the block's position (climate tint); null otherwise.</summary>
    public ushort[]? Heights { get; init; }

    public ColumnPos Pos => new(X, Z);
}

/// <summary>
/// Reads a column's map data the way the 1.22.7 <c>ChunkMapLayer.GenerateChunkImage</c> does on the
/// client, from an <see cref="ISurfaceSource"/> instead of the client's loaded chunks.
/// </summary>
public static class ColumnSampler
{
    /// <summary>
    /// The column's sample, or null if the column is missing (or a chunk the map needs is): the reveal
    /// skips it. <paramref name="sectionsY"/> is the world height in chunks.
    /// <paramref name="colorAccurate"/> is the world map's colour-accurate mode, where snow is not
    /// looked through, water is not outlined and every cell is shaded.
    /// </summary>
    public static ColumnSample? Sample(ISurfaceSource source, ColumnPos column, BlockTraits traits, int sectionsY, bool colorAccurate)
    {
        var heights = source.Heights(column);
        if (heights is null || heights.Length != TerrainShade.Area) return null;

        var sample = new ColumnSample
        {
            X = column.X,
            Z = column.Z,
            Heights = colorAccurate ? new ushort[TerrainShade.Area] : null,
        };
        var shaded = new bool[TerrainShade.Area];
        int maxY = sectionsY * TerrainShade.Size;
        for (int k = 0; k < TerrainShade.Area; k++)
        {
            int x = k % TerrainShade.Size, z = k / TerrainShade.Size;
            int y = heights[k];
            if (y >= maxY)
            {
                sample.Kinds[k] = CellKind.None;
                continue;
            }
            if (!source.TryBlock(column, x, y, z, out int id)) return null;
            if (!colorAccurate && traits.Snow(id) && y > 0)
            {
                y--;
                if (!source.TryBlock(column, x, y, z, out id)) return null;
            }
            sample.BlockIds[k] = id;
            if (sample.Heights is { } ys) ys[k] = (ushort)y;

            if (colorAccurate || !traits.Lake(id))
            {
                sample.Kinds[k] = CellKind.Land;
                shaded[k] = true;
            }
            else
                sample.Kinds[k] = ShoreKind(source, column, traits, x, y, z);
        }

        var edges = NeighbourEdges.From(
            source.Heights(new ColumnPos(column.X - 1, column.Z)),
            source.Heights(new ColumnPos(column.X, column.Z - 1)),
            source.Heights(new ColumnPos(column.X - 1, column.Z - 1)));
        TerrainShade.ShadowMap(heights, edges, shaded).CopyTo(sample.Shadow, 0);
        return sample;
    }

    // Water whose four neighbours at the same height are all water is lake; otherwise it is shore.
    // If any neighbour's chunk is missing the game cannot tell, and colours it as lake.
    private static CellKind ShoreKind(ISurfaceSource source, ColumnPos column, BlockTraits traits, int x, int y, int z)
    {
        bool allLake = true;
        foreach (var (dx, dz) in Neighbours)
        {
            if (!TryBlockAcross(source, column, x + dx, y, z + dz, out int id)) return CellKind.Lake;
            allLake &= traits.Lake(id);
        }
        return allLake ? CellKind.Lake : CellKind.WaterEdge;
    }

    private static readonly (int dx, int dz)[] Neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    // A block that may be in a neighbouring column (x or z one past the edge).
    private static bool TryBlockAcross(ISurfaceSource source, ColumnPos column, int x, int y, int z, out int blockId)
    {
        int cx = column.X, cz = column.Z;
        if (x < 0) { cx--; x += TerrainShade.Size; }
        else if (x >= TerrainShade.Size) { cx++; x -= TerrainShade.Size; }
        if (z < 0) { cz--; z += TerrainShade.Size; }
        else if (z >= TerrainShade.Size) { cz++; z -= TerrainShade.Size; }
        return source.TryBlock(new ColumnPos(cx, cz), x, y, z, out blockId);
    }
}
