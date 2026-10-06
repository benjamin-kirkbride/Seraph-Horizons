using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>A placer field's seeded shape: how many rich gravel blocks it aims at, in a disc how
/// many blocks thick, and whether its top is flush with the surface (0) or one block under it
/// (1, covered by the ground it replaced the lower layer of).</summary>
public readonly record struct PlacerFieldSpec(int Blocks, int Thickness, int Depth)
{
    /// <summary>The disc's radius: Blocks = π r² × Thickness.</summary>
    public double Radius => Math.Sqrt(Blocks / (Math.PI * Thickness));
}

/// <summary>
/// Placer fields (#442): the world is cut into square cells (1.5 km by default), each with at most
/// one rich gravel field. For every (world seed, cell) a stable hash picks <see cref="SpotCount"/>
/// spots, in order, at least a tenth of the cell from its edge, exactly as ore cells do
/// (<see cref="OreCells"/>, whose hash this reuses under its own salt, so a cell's gravel spot is
/// unrelated to its metals'). The first spot is the field's, the rest are fallbacks taken in turn
/// when a spot's terrain doesn't suit (<see cref="PlacerSite"/>). Placer fields need water or a
/// valley floor, which most chunks lack, so a cell has twice the ore cells' fallbacks.
/// </summary>
public sealed class PlacerCells
{
    public const int DefaultCellSize = 1500;
    public const int SpotCount = 16;
    public const int MinBlocks = 300;
    public const int MaxBlocks = 600;

    /// <summary>The kind name under which gravel fields are kept wherever metals are
    /// (<see cref="DepositRegistry"/>, <see cref="PlacerBook"/>).</summary>
    public const string Kind = "gravel";

    private static readonly ulong SpotSaltX = OreCells.Fnv1a("placer:richgravel:x");
    private static readonly ulong SpotSaltZ = OreCells.Fnv1a("placer:richgravel:z");
    private static readonly ulong FieldSalt = OreCells.Fnv1a("placer:richgravel:field");

    public PlacerCells(long seed, int cellSize = DefaultCellSize)
    {
        Seed = seed;
        CellSize = OreCells.ClampCellSize(cellSize);
    }

    public long Seed { get; }

    public int CellSize { get; }

    public int Margin => CellSize / 10;

    public CellPos CellOf(int x, int z) => new(OreCells.FloorDiv(x, CellSize), OreCells.FloorDiv(z, CellSize));

    /// <summary>The cell's spots, primary first.</summary>
    public OreSpot[] Spots(CellPos cell)
    {
        int span = CellSize - 2 * Margin;
        var spots = new OreSpot[SpotCount];
        for (int i = 0; i < SpotCount; i++)
        {
            int dx = (int)(OreCells.Hash(Seed, SpotSaltX, cell.X, cell.Z, i) % (ulong)span);
            int dz = (int)(OreCells.Hash(Seed, SpotSaltZ, cell.X, cell.Z, i) % (ulong)span);
            spots[i] = new OreSpot(i, cell.X * CellSize + Margin + dx, cell.Z * CellSize + Margin + dz);
        }
        return spots;
    }

    /// <summary>The field a spot would get: 300–600 blocks, one or two thick, flush or one under.</summary>
    public PlacerFieldSpec Field(CellPos cell, int spot)
    {
        ulong h = OreCells.Hash(Seed, FieldSalt, cell.X, cell.Z, spot);
        int blocks = MinBlocks + (int)(h % (ulong)(MaxBlocks - MinBlocks + 1));
        int thickness = 1 + (int)((h >> 20) & 1);
        int depth = (int)((h >> 21) & 1);
        return new PlacerFieldSpec(blocks, thickness, depth);
    }
}

/// <summary>
/// The surface of one chunk column as worldgen leaves it, for <see cref="PlacerSite"/>: per
/// (x, z), row-major z * Size + x, the terrain height (the top solid block), whether water stands
/// on it, and whether that top block may be replaced by rich gravel (soil, gravel, sand).
/// </summary>
public sealed class TerrainPatch
{
    public const int Size = OreCells.ChunkSize;

    public TerrainPatch(int[] heights, bool[] water, bool[] replaceable)
    {
        if (heights.Length != Size * Size || water.Length != Size * Size || replaceable.Length != Size * Size)
            throw new ArgumentException($"a patch is {Size} x {Size}");
        Heights = heights;
        Water = water;
        Replaceable = replaceable;
    }

    public int[] Heights { get; }
    public bool[] Water { get; }
    public bool[] Replaceable { get; }

    public static int Index(int x, int z) => z * Size + x;
}

/// <summary>
/// Where in a spot's chunk column its field goes, if anywhere. The field is kept inside the one
/// column so that it is decided and placed when that column generates, from its own terrain, whatever
/// order the world is explored in: its centre is at least the disc's radius from the column's
/// edges. A centre suits when:
/// <list type="bullet">
/// <item>the ground is gentle: the disc's heights span at most <see cref="MaxRelief"/> blocks;</item>
/// <item>it is mostly land (at least <see cref="MinLandShare"/> of the disc dry) whose top block can
/// take the gravel (at least <see cref="MinReplaceableShare"/> of it soil, gravel or sand);</item>
/// <item>water is near (within <see cref="NearWater"/> blocks of the disc's edge), or it lies on a
/// valley floor (<see cref="ValleyDepth"/> or more blocks under the mean height of the column's
/// edges).</item>
/// </list>
/// Of the centres that suit, the one nearest the spot is taken.
/// </summary>
public static class PlacerSite
{
    public const int MaxRelief = 3;
    public const double MinLandShare = 2.0 / 3;
    public const double MinReplaceableShare = 0.8;
    public const int NearWater = 8;
    public const int ValleyDepth = 4;

    /// <summary>The disc's cells, as offsets from its centre.</summary>
    public static IReadOnlyList<(int Dx, int Dz)> Disc(double radius)
    {
        int r = (int)Math.Ceiling(radius);
        var cells = new List<(int, int)>();
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dz * dz <= radius * radius)
                    cells.Add((dx, dz));
        return cells;
    }

    /// <summary>The centre (in-column x, z) for the field nearest the preferred point, or null if
    /// no centre in the column suits.</summary>
    public static (int X, int Z)? FindCentre(TerrainPatch patch, PlacerFieldSpec field, int preferX, int preferZ)
    {
        var disc = Disc(field.Radius);
        int r = (int)Math.Ceiling(field.Radius);
        int lo = r, hi = TerrainPatch.Size - 1 - r;
        if (lo > hi) return null;
        double edgeMean = EdgeMean(patch);
        var waterCells = new List<(int, int)>();
        for (int z = 0; z < TerrainPatch.Size; z++)
            for (int x = 0; x < TerrainPatch.Size; x++)
                if (patch.Water[TerrainPatch.Index(x, z)]) waterCells.Add((x, z));
        var candidates = new List<(int X, int Z, int D)>();
        for (int z = lo; z <= hi; z++)
            for (int x = lo; x <= hi; x++)
                candidates.Add((x, z, (x - preferX) * (x - preferX) + (z - preferZ) * (z - preferZ)));
        foreach (var (x, z, _) in candidates.OrderBy(c => c.D).ThenBy(c => c.Z).ThenBy(c => c.X))
            if (Suits(patch, disc, field.Radius, x, z, edgeMean, waterCells))
                return (x, z);
        return null;
    }

    /// <summary>Whether a field of this radius centred at (x, z) suits (see the class).</summary>
    public static bool Suits(TerrainPatch patch, PlacerFieldSpec field, int x, int z)
    {
        var water = new List<(int, int)>();
        for (int wz = 0; wz < TerrainPatch.Size; wz++)
            for (int wx = 0; wx < TerrainPatch.Size; wx++)
                if (patch.Water[TerrainPatch.Index(wx, wz)]) water.Add((wx, wz));
        return Suits(patch, Disc(field.Radius), field.Radius, x, z, EdgeMean(patch), water);
    }

    private static bool Suits(TerrainPatch patch, IReadOnlyList<(int Dx, int Dz)> disc, double radius, int cx, int cz,
        double edgeMean, List<(int X, int Z)> waterCells)
    {
        int min = int.MaxValue, max = int.MinValue, land = 0, replaceable = 0, inside = 0;
        foreach (var (dx, dz) in disc)
        {
            int x = cx + dx, z = cz + dz;
            if (x < 0 || z < 0 || x >= TerrainPatch.Size || z >= TerrainPatch.Size) return false;
            int i = TerrainPatch.Index(x, z);
            inside++;
            int h = patch.Heights[i];
            min = Math.Min(min, h);
            max = Math.Max(max, h);
            if (patch.Water[i]) continue;
            land++;
            if (patch.Replaceable[i]) replaceable++;
        }
        if (inside == 0 || max - min > MaxRelief) return false;
        if (land < MinLandShare * inside || replaceable < MinReplaceableShare * land) return false;
        double reach = radius + NearWater;
        foreach (var (wx, wz) in waterCells)
            if ((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz) <= reach * reach)
                return true;
        return patch.Heights[TerrainPatch.Index(cx, cz)] <= edgeMean - ValleyDepth;
    }

    private static double EdgeMean(TerrainPatch patch)
    {
        const int n = TerrainPatch.Size;
        long sum = 0;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            sum += patch.Heights[TerrainPatch.Index(i, 0)] + patch.Heights[TerrainPatch.Index(i, n - 1)];
            count += 2;
            if (i == 0 || i == n - 1) continue;
            sum += patch.Heights[TerrainPatch.Index(0, i)] + patch.Heights[TerrainPatch.Index(n - 1, i)];
            count += 2;
        }
        return (double)sum / count;
    }
}

/// <summary>A placed field: its spot, its centre (y: the top of the gravel), its rock and how many
/// rich gravel blocks went in.</summary>
public sealed record PlacedField(int Spot, int X, int Y, int Z, string Rock, int Blocks);

/// <summary>
/// The placer cells' state, kept in the savegame: per cell which spot is active and how each fared
/// (an <see cref="OreCellBook"/> of <see cref="PlacerCells.SpotCount"/> spots under the kind
/// <see cref="PlacerCells.Kind"/>), and for each placed field where it is.
/// </summary>
public sealed class PlacerBook
{
    private readonly Dictionary<CellPos, PlacedField> _fields = new();

    public PlacerBook() : this(new OreCellBook(PlacerCells.SpotCount)) { }

    private PlacerBook(OreCellBook cells) => Cells = cells;

    public OreCellBook Cells { get; }

    public CellState Get(CellPos cell) => Cells.Get(PlacerCells.Kind, cell);

    public PlacedField? FieldIn(CellPos cell) => _fields.TryGetValue(cell, out var f) ? f : null;

    public IEnumerable<(CellPos Cell, PlacedField Field)> Fields => _fields.Select(kv => (kv.Key, kv.Value));

    /// <summary>The active spot's column generated: <paramref name="field"/> is what was placed there,
    /// or null if its terrain didn't suit. Returns true if anything changed.</summary>
    public bool OnAnchorGenerated(CellPos cell, int spot, PlacedField? field)
    {
        bool changed = Cells.OnAnchorGenerated(PlacerCells.Kind, cell, spot, field != null);
        if (changed && field != null && Get(cell).Placed)
            _fields[cell] = field;
        return changed;
    }

    public bool OnFallbackGenerated(CellPos cell, int spot) => Cells.OnFallbackGenerated(PlacerCells.Kind, cell, spot);

    // "placer v1", the cell book's own text, then "fields" and one line per placed field:
    // "cellX cellZ spot x y z rock blocks".
    public string Serialize()
    {
        var sb = new StringBuilder("placer v1\n");
        sb.Append(Cells.Serialize());
        sb.Append("fields\n");
        foreach (var (cell, f) in _fields.OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Z).Select(kv => (kv.Key, kv.Value)))
            sb.Append(CultureInfo.InvariantCulture, $"{cell.X} {cell.Z} {f.Spot} {f.X} {f.Y} {f.Z} {f.Rock} {f.Blocks}\n");
        return sb.ToString();
    }

    public static PlacerBook Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new PlacerBook();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines[0] != "placer v1") throw new FormatException($"unknown placer format '{lines[0]}'");
        int split = Array.IndexOf(lines, "fields");
        if (split < 0) throw new FormatException("placer state has no fields section");
        var book = new PlacerBook(OreCellBook.Parse(string.Join('\n', lines[1..split]), PlacerCells.SpotCount));
        foreach (var line in lines[(split + 1)..])
        {
            var f = line.Split(' ');
            if (f.Length != 8) throw new FormatException($"bad placer field line '{line}'");
            int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
            book._fields[new CellPos(I(0), I(1))] = new PlacedField(I(2), I(3), I(4), I(5), f[6], I(7));
        }
        return book;
    }
}
