namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>A cell of the ore grid, in cells (block X / cell size, floored).</summary>
public readonly record struct CellPos(int X, int Z);

/// <summary>A chunk column, in chunks (block X / 32, floored).</summary>
public readonly record struct ChunkPos(int X, int Z);

/// <summary>A candidate deposit spot: its block position and the chunk column holding it.</summary>
public readonly record struct OreSpot(int Index, int X, int Z)
{
    public ChunkPos Chunk => new(OreCells.FloorDiv(X, OreCells.ChunkSize), OreCells.FloorDiv(Z, OreCells.ChunkSize));
}

/// <summary>
/// The ore grid: the world is cut into square cells (5 km by default), and each metal gets at most
/// one deposit per cell. For every (world seed, metal, cell) a stable hash picks
/// <see cref="SpotCount"/> spots inside the cell, in order: the first is the deposit's spot, the
/// rest are fallbacks used in turn when a spot turns out unable to hold a vein
/// (<see cref="OreCellBook"/>). Spots keep <see cref="Margin"/> from the cell's edge, so deposits of
/// neighbouring cells are never closer than twice that.
///
/// A spot is a chunk column, its anchor: Interesting Ore Gen's vein tries are made per source
/// chunk, each with its own seeded random sequence, so "the first try of this metal from that
/// chunk that can start a vein" is the same try whichever neighbouring chunk is being generated
/// when it is asked (<see cref="Approves"/>). Nothing here depends on the order chunks are
/// generated in.
/// </summary>
public sealed class OreCells
{
    public const int ChunkSize = 32;
    public const int SpotCount = 8;
    public const int DefaultCellSize = 5000;

    /// <summary>The smallest cell the rule takes: below it, the spots' margin and the veins' own
    /// reach (up to about 100 blocks) would leave neighbouring deposits touching.</summary>
    public const int MinCellSize = 500;

    /// <summary>The fewest vein tries per chunk a managed metal gets (all its ores together), so
    /// that an anchor chunk usually has one: with 2, about 6 anchors in 7.</summary>
    public const float MinTriesPerChunk = 2f;

    private readonly long _seed;
    private readonly int _defaultCellSize;
    private readonly IReadOnlyDictionary<string, int> _cellSizeByMetal;

    public OreCells(long seed, int cellSize = DefaultCellSize, IReadOnlyDictionary<string, int>? cellSizeByMetal = null)
    {
        _seed = seed;
        _defaultCellSize = ClampCellSize(cellSize);
        _cellSizeByMetal = (cellSizeByMetal ?? new Dictionary<string, int>())
            .ToDictionary(kv => kv.Key, kv => ClampCellSize(kv.Value));
    }

    public long Seed => _seed;

    public static int ClampCellSize(int size) => Math.Max(MinCellSize, size);

    /// <summary>The cell size of a metal, in blocks.</summary>
    public int CellSize(string metal) =>
        _cellSizeByMetal.TryGetValue(metal, out var size) ? size : _defaultCellSize;

    /// <summary>A tenth of the cell: 500 m for 5 km cells.</summary>
    public int Margin(string metal) => CellSize(metal) / 10;

    public CellPos CellOf(string metal, int x, int z)
    {
        int size = CellSize(metal);
        return new CellPos(FloorDiv(x, size), FloorDiv(z, size));
    }

    public static ChunkPos ChunkOf(int x, int z) => new(FloorDiv(x, ChunkSize), FloorDiv(z, ChunkSize));

    /// <summary>The cell's spots, primary first.</summary>
    public OreSpot[] Spots(string metal, CellPos cell)
    {
        int size = CellSize(metal);
        int margin = Margin(metal);
        int span = size - 2 * margin;
        ulong metalHash = Fnv1a(metal);
        var spots = new OreSpot[SpotCount];
        for (int i = 0; i < SpotCount; i++)
        {
            ulong h = Hash(_seed, metalHash, cell.X, cell.Z, i);
            int dx = (int)(h % (ulong)span);
            int dz = (int)(Mix(h ^ 0x5bd1e9955bd1e995UL) % (ulong)span);
            spots[i] = new OreSpot(i, cell.X * size + margin + dx, cell.Z * size + margin + dz);
        }
        return spots;
    }

    /// <summary>
    /// Whether a vein try of a metal at (x, z) may place its vein: its source chunk is the anchor
    /// of the cell's active spot, and it is the first try of the metal from that chunk whose vein
    /// can start (<paramref name="firstOfMetalInChunk"/>, which the caller decides per pass over
    /// the chunk).
    /// No active spot (null) means the cell has no deposit of the metal.
    /// </summary>
    public static bool Approves(OreSpot? active, int x, int z, bool firstOfMetalInChunk) =>
        active is { } spot && spot.Chunk == ChunkOf(x, z) && firstOfMetalInChunk;

    public static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? q - 1 : q;
    }

    /// <summary>A stable 64-bit hash of a spot's inputs (SplitMix64 steps): the same on every
    /// machine and run, unlike <see cref="string.GetHashCode()"/>.</summary>
    public static ulong Hash(long seed, ulong metalHash, int cellX, int cellZ, int attempt)
    {
        ulong h = Mix((ulong)seed ^ 0x9e3779b97f4a7c15UL);
        h = Mix(h ^ metalHash);
        h = Mix(h ^ (uint)cellX);
        h = Mix(h ^ ((ulong)(uint)cellZ << 32));
        return Mix(h ^ (ulong)attempt);
    }

    public static ulong Fnv1a(string s)
    {
        ulong h = 0xcbf29ce484222325UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 0x100000001b3UL;
        }
        return h;
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9e3779b97f4a7c15UL;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }
}
