namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>A 2 km cell of the camp grid, by index (block coordinate / <see cref="TraderGrid.CellSize"/>).</summary>
public readonly record struct CellKey(int X, int Z)
{
    public override string ToString() => $"{X},{Z}";

    public static bool TryParse(string text, out CellKey key)
    {
        key = default;
        var parts = text.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int z)) return false;
        key = new CellKey(x, z);
        return true;
    }
}

/// <summary>A candidate camp spot: block x and z, and the chunk column holding it.</summary>
public readonly record struct Spot(int X, int Z)
{
    public int ChunkX => X >> 5;
    public int ChunkZ => Z >> 5;
}

/// <summary>
/// The camp grid (#447), all of it a function of the world seed. The world is cut into 2 km cells
/// (<see cref="CellSize"/>, 64 chunks, so a cell is whole chunks); each cell gets at most one lone
/// camp, at the first of its <see cref="Attempts"/> seeded spots to take one as their chunks generate
/// (the worldgen side decides that, chunk by chunk; <see cref="CampRegistry"/>), or failing all of
/// them in one of the cell's later chunks (<see cref="SecondChanceChunk"/>). Spots keep
/// <see cref="Margin"/> from the cell's edge, so neighbouring camps are usually about a cell apart.
///
/// Every 8 km cell (<see cref="SettlementSize"/>, 4×4 camp cells) is reserved for a settlement of
/// three to five traders (deferred): its centre is a corner shared by four camp cells, and no lone
/// camp spot lies within <see cref="SettlementReserve"/> of it, so a settlement can be dropped there
/// later without moving any camp.
///
/// Types: the prospector sits on a lattice of 3×3-cell blocks, one per block at a seeded offset of
/// 0 or 1 along each axis, so every cell has a prospector within two cells (Chebyshev) and no two
/// prospectors are neighbours. Every other cell takes one of the other ten types by weight, never one
/// of its eight neighbours': cells are coloured greedily in a seeded priority order (a neighbour of
/// higher priority picks first), which needs only the cell's neighbourhood and is the same whatever
/// order cells are asked in. Ten types against at most eight neighbours always leave a choice.
/// </summary>
public sealed class TraderGrid
{
    public const int CellSize = 2048;
    public const int SettlementSize = 8192;
    public const int Margin = 192;
    public const int SettlementReserve = 512;
    public const int Attempts = 8;
    public const int ProspectorBlock = 3;
    /// <summary>Of an open cell's chunks (every spot missed), one in this many is a second chance.</summary>
    public const int SecondChanceEvery = 4;
    /// <summary>Second chances a cell gets at most, after which it has no camp for good.</summary>
    public const int SecondChances = 24;

    private const string SpotSalt = "trader";
    private const string ProspectorSalt = "trader-prospector";
    private const string PrioritySalt = "trader-priority";
    private const string TypeSalt = "trader-type";

    private readonly long _seed;
    private readonly (string Type, double Weight)[] _weighted;
    private readonly Dictionary<CellKey, string> _types = new();
    private readonly object _lock = new();

    /// <param name="weights">Camp weight by type (the lists' <c>campWeight</c>); the prospector's is
    /// not used (its lattice decides), and a type missing or at 0 never gets a cell.</param>
    public TraderGrid(long seed, IReadOnlyDictionary<string, double> weights)
    {
        _seed = seed;
        _weighted = TraderTypes.All.Where(t => t != TraderTypes.Prospector)
            .Select(t => (t, weights.TryGetValue(t, out double w) ? Math.Max(0, w) : 0))
            .Where(p => p.Item2 > 0).ToArray();
        if (_weighted.Length < 9)
            throw new ArgumentException("at least nine non-prospector types need a camp weight above 0, so neighbours can differ");
    }

    public long Seed => _seed;

    public static CellKey CellOf(int x, int z) => new(FloorDiv(x, CellSize), FloorDiv(z, CellSize));

    public static CellKey CellOfChunk(int chunkX, int chunkZ) => CellOf(chunkX << 5, chunkZ << 5);

    /// <summary>The settlement (8 km) cell centre nearest to a block.</summary>
    public static (int X, int Z) SettlementCentre(int x, int z) =>
        (FloorDiv(x, SettlementSize) * SettlementSize + SettlementSize / 2, FloorDiv(z, SettlementSize) * SettlementSize + SettlementSize / 2);

    public static bool InSettlementReserve(int x, int z)
    {
        // The nearest centre may be the next 8 km cell's when the spot is near an edge: test the
        // centres of its own cell and the eight around it.
        int bx = FloorDiv(x, SettlementSize) * SettlementSize + SettlementSize / 2;
        int bz = FloorDiv(z, SettlementSize) * SettlementSize + SettlementSize / 2;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long ex = x - (bx + dx * (long)SettlementSize), ez = z - (bz + dz * (long)SettlementSize);
                if (ex * ex + ez * ez < (long)SettlementReserve * SettlementReserve) return true;
            }
        return false;
    }

    /// <summary>The cell's candidate spots in the order they are tried, those in a settlement reserve
    /// left out (so a cell next to a settlement centre has fewer).</summary>
    public IReadOnlyList<Spot> Spots(CellKey cell)
    {
        var spots = new List<Spot>(Attempts);
        int inner = CellSize - 2 * Margin;
        for (int n = 0; spots.Count < Attempts && n < Attempts * 4; n++)
        {
            ulong h = StableHash.Of(_seed, SpotSalt, cell.X, cell.Z, n);
            int x = cell.X * CellSize + Margin + (int)((h & 0xFFFFFFFF) % (ulong)inner);
            int z = cell.Z * CellSize + Margin + (int)((h >> 32) % (ulong)inner);
            if (!InSettlementReserve(x, z)) spots.Add(new Spot(x, z));
        }
        return spots;
    }

    /// <summary>Which of the cell's spots (index into <see cref="Spots"/>) lie in a chunk column.</summary>
    public IEnumerable<int> AttemptsInChunk(CellKey cell, int chunkX, int chunkZ)
    {
        var spots = Spots(cell);
        for (int i = 0; i < spots.Count; i++)
            if (spots[i].ChunkX == chunkX && spots[i].ChunkZ == chunkZ) yield return i;
    }

    /// <summary>
    /// Where in a spot's chunk column a camp is tried, in order: the spot itself, then every
    /// <see cref="PositionStep"/>th block of the chunk in a seeded order, as offsets (0..31) from the
    /// chunk's corner, each the corner of a schematic's footprint. A camp needs nearly level ground
    /// under the game's five sample points (<see cref="CampGround"/>), which a single point rarely
    /// has; vanilla gets there by rolling many structures at random points per chunk, a spot by
    /// trying its chunk's points (the worldgen side skips, without counting them, the ones where no
    /// camp schematic's samples pass).
    /// </summary>
    public IEnumerable<(int X, int Z)> PositionsInChunk(Spot spot)
    {
        int ox = spot.X & 31, oz = spot.Z & 31;
        yield return (ox, oz);
        var rest = new List<(double Key, int X, int Z)>();
        for (int x = 0; x < 32; x += PositionStep)
            for (int z = 0; z < 32; z += PositionStep)
                if (x != ox || z != oz)
                    rest.Add((StableHash.Unit(_seed, "trader-position", spot.X - ox + x, spot.Z - oz + z), x, z));
        foreach (var r in rest.OrderBy(r => r.Key)) yield return (r.X, r.Z);
    }

    public const int PositionStep = 2;

    /// <summary>
    /// Whether a chunk column is one of the cell's second chances (#599): wholly inside the cell's
    /// <see cref="Margin"/>, like the spots, its middle out of the settlement reserve, and picked by
    /// the seed, one chunk in <see cref="SecondChanceEvery"/>. Once every spot of a cell has missed,
    /// these chunks try the camp as they generate (at their middle first, then the chunk's other
    /// points, as a spot does), at most <see cref="SecondChances"/> of them, so a cell whose spots
    /// all landed on bad ground can still get a camp where the player goes, at a bounded cost.
    /// </summary>
    public bool SecondChanceChunk(int chunkX, int chunkZ)
    {
        var cell = CellOfChunk(chunkX, chunkZ);
        int x0 = chunkX << 5, z0 = chunkZ << 5;
        int lo = Margin, hi = CellSize - Margin;
        int ox = x0 - cell.X * CellSize, oz = z0 - cell.Z * CellSize;
        if (ox < lo || ox + 32 > hi || oz < lo || oz + 32 > hi) return false;
        if (InSettlementReserve(x0 + 16, z0 + 16)) return false;
        return StableHash.Unit(_seed, SecondChanceSalt, chunkX, chunkZ) * SecondChanceEvery < 1;
    }

    /// <summary>A second chance's stand-in spot: the chunk's middle, tried first.</summary>
    public static Spot SecondChanceSpot(int chunkX, int chunkZ) => new((chunkX << 5) + 16, (chunkZ << 5) + 16);

    /// <summary>
    /// The order a camp structure's schematics are tried in at a position (#599): every schematic in
    /// all four rotations, as (schematic index, rotation 0..3) in a seeded shuffle, where the game's
    /// own placement draws one schematic and one rotation at random and gives up if that one doesn't
    /// fit. <paramref name="attempt"/> is the spot (or <see cref="Attempts"/> plus the second chance's
    /// number).
    /// </summary>
    public IEnumerable<(int Schematic, int Rotation)> CandidateOrder(CellKey cell, int attempt, int structure, int schematics)
    {
        var keys = new List<(double Key, int Schematic, int Rotation)>(schematics * 4);
        for (int s = 0; s < schematics; s++)
            for (int r = 0; r < 4; r++)
                keys.Add((StableHash.Unit(_seed, CandidateSalt, cell.X, cell.Z, ((attempt * 1000 + structure) * 1000 + s) * 4 + r), s, r));
        return keys.OrderBy(k => k.Key).ThenBy(k => k.Schematic).ThenBy(k => k.Rotation).Select(k => (k.Schematic, k.Rotation));
    }

    private const string SecondChanceSalt = "trader-second-chance";
    private const string CandidateSalt = "trader-candidate";

    /// <summary>The order a spot tries the camp structures in: a seeded shuffle weighted by each
    /// structure's chance (Efraimidis–Spirakis), so common camp kinds come up as often as vanilla
    /// places them. Structures at weight 0 are left out.</summary>
    public int[] StructureOrder(CellKey cell, int attempt, IReadOnlyList<double> weights)
    {
        var keys = new List<(double Key, int Index)>();
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;
            double u = Math.Max(1e-12, StableHash.Unit(_seed, "trader-structure", cell.X, cell.Z, attempt * 1000 + i));
            keys.Add((Math.Pow(u, 1 / weights[i]), i));
        }
        return keys.OrderByDescending(k => k.Key).Select(k => k.Index).ToArray();
    }

    public bool IsProspector(CellKey cell)
    {
        int bx = FloorDiv(cell.X, ProspectorBlock), bz = FloorDiv(cell.Z, ProspectorBlock);
        ulong h = StableHash.Of(_seed, ProspectorSalt, bx, bz);
        int ox = (int)(h & 1), oz = (int)((h >> 1) & 1);
        return cell.X - bx * ProspectorBlock == ox && cell.Z - bz * ProspectorBlock == oz;
    }

    /// <summary>The cell's trader type.</summary>
    public string TypeOf(CellKey cell)
    {
        lock (_lock) return TypeOfLocked(cell);
    }

    private string TypeOfLocked(CellKey cell)
    {
        if (_types.TryGetValue(cell, out var known)) return known;
        string type;
        if (IsProspector(cell))
            type = TraderTypes.Prospector;
        else
        {
            double priority = Priority(cell);
            var taken = new HashSet<string>();
            foreach (var n in Neighbours(cell))
                if (!IsProspector(n) && Priority(n) > priority)
                    taken.Add(TypeOfLocked(n));
            type = Pick(cell, taken);
        }
        _types[cell] = type;
        return type;
    }

    private string Pick(CellKey cell, HashSet<string> taken)
    {
        var allowed = _weighted.Where(p => !taken.Contains(p.Type)).ToArray();
        double total = allowed.Sum(p => p.Weight);
        double r = StableHash.Unit(_seed, TypeSalt, cell.X, cell.Z) * total;
        foreach (var (type, weight) in allowed)
        {
            if (r < weight) return type;
            r -= weight;
        }
        return allowed[^1].Type;
    }

    // Ties are as good as impossible (53 bits); the cell coordinates break them anyway.
    private double Priority(CellKey cell) => StableHash.Unit(_seed, PrioritySalt, cell.X, cell.Z) + cell.X * 1e-12 + cell.Z * 1e-15;

    public static IEnumerable<CellKey> Neighbours(CellKey cell)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dz != 0) yield return new CellKey(cell.X + dx, cell.Z + dz);
    }

    /// <summary>Cells whose square overlaps a radius around a block, nearest first.</summary>
    public static IEnumerable<CellKey> CellsAround(int x, int z, int radius)
    {
        var lo = CellOf(x - radius, z - radius);
        var hi = CellOf(x + radius, z + radius);
        var cells = new List<CellKey>();
        for (int cx = lo.X; cx <= hi.X; cx++)
            for (int cz = lo.Z; cz <= hi.Z; cz++)
                cells.Add(new CellKey(cx, cz));
        return cells.OrderBy(c =>
        {
            double mx = (c.X + 0.5) * CellSize - x, mz = (c.Z + 0.5) * CellSize - z;
            return mx * mx + mz * mz;
        }).ThenBy(c => c.X).ThenBy(c => c.Z);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
