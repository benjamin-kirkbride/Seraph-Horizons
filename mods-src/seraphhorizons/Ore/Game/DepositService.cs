using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// A deposit as the seed and the world's state know it: a metal's (or a gravel field's) cell, its
/// active spot, whether that spot's chunk has been generated with the deposit in it
/// (<see cref="Generated"/>), where it is (the spot, or once measured or placed, the ore's centre or
/// the field's), how far from the asking position, and its registry record.
/// </summary>
public sealed record DepositCandidate(DepositKey Key, int Spot, int X, int Y, int Z, bool Generated, double Distance, DepositRecord Record)
{
    public string Metal => Key.Kind;
}

public enum VerifyStatus
{
    /// <summary>Measured: <see cref="VerifyResult.Ingots"/> and <see cref="VerifyResult.Tier"/> are set
    /// (sold out if <see cref="VerifyResult.WorkedOut"/>).</summary>
    Measured,
    /// <summary>A gravel field, generated and placed: <see cref="VerifyResult.Field"/> is set.</summary>
    Field,
    /// <summary>The cell turned out to have none (every spot failed), or the key names nothing.</summary>
    None,
}

/// <summary>What <see cref="DepositService.Verify"/> found.</summary>
public sealed record VerifyResult(VerifyStatus Status, DepositCandidate? Candidate, double Ingots = 0, int OreBlocks = 0,
    SizeTier? Tier = null, bool WorkedOut = false, PlacedField? Field = null, string? Why = null);

/// <summary>
/// The deposit registry's service (#443), behind ore and gravel maps (<see cref="MapIssuer"/>) and
/// the traders that sell them (#455). Server side, from <see cref="OreSystem.Deposits"/>; null when
/// neither ore cells nor placer fields are on in the world.
/// <list type="bullet">
/// <item><see cref="Candidates"/>, <see cref="GravelFields"/>: from the seed and the cell books
/// alone, no chunk generated: every metal's deposit (or gravel field) whose spot is within a radius,
/// nearest first, with its registry state. A cell whose spots all failed has none.</item>
/// <item><see cref="Verify"/>: generates what is needed and measures. An ore deposit whose spot's
/// column isn't generated yet is generated first (the cell rule then places the vein, or moves to
/// the next spot, which is followed); then its column and the eight around it are generated and
/// the metal's ore in them counted, as ingots (<see cref="DepositSizing"/>), classed small, medium or
/// large for the metal, and recorded; below a tenth of the metal's small size it is marked sold
/// out. A gravel field's column is generated, which places it or not.</item>
/// <item><see cref="Registry"/>: unsold / sold / sold out per deposit, saved under
/// <see cref="RegistryKey"/>.</item>
/// </list>
/// Calls are made on the main thread; <see cref="Verify"/>'s callback comes on it too.
/// </summary>
public sealed class DepositService
{
    public const string RegistryKey = "seraphhorizons:deposits";

    private readonly ICoreServerAPI _api;
    private readonly OreCellPlacement? _ore;
    private readonly PlacerFields? _gravel;
    private readonly OreSizeTable _sizes;
    private readonly Dictionary<string, double[]> _unitsByMetal = new();

    public DepositRegistry Registry { get; }

    /// <summary>The metals deposits are listed for: those the cell rule manages that have a size range
    /// (not coal and the minerals).</summary>
    public IReadOnlyList<string> Metals { get; }

    public bool HasOre => _ore != null;

    public bool HasGravel => _gravel != null;

    public DepositService(ICoreServerAPI api, OreCellPlacement? ore, PlacerFields? gravel)
    {
        _api = api;
        _ore = ore;
        _gravel = gravel;
        _sizes = new OreSizeTable(api.Assets.TryGet(DepositSizes.SizesAsset)?.ToObject<Dictionary<string, OreSizeTable.Entry>>()
                                  ?? new Dictionary<string, OreSizeTable.Entry>());
        Metals = ore?.Managed.Where(m => _sizes.TargetsFor(m) != null).ToArray() ?? [];
        Registry = Load(api);
        api.Event.GameWorldSave += Save;
    }

    public void Dispose() => _api.Event.GameWorldSave -= Save;

    public SizeTargets? TargetsFor(string metal) => _sizes.TargetsFor(metal);

    /// <summary>Every listed metal's deposit (or just <paramref name="metal"/>'s) within
    /// <paramref name="radius"/> blocks of (x, z), nearest first.</summary>
    public List<DepositCandidate> Candidates(int x, int z, int radius, string? metal = null)
    {
        var list = new List<DepositCandidate>();
        if (_ore == null) return list;
        foreach (var m in Metals)
        {
            if (metal != null && m != metal) continue;
            foreach (var cell in CellSearch.Within(_ore.Cells.CellSize(m), x, z, radius))
                if (Candidate(new DepositKey(m, cell), x, z) is { } c && c.Distance <= radius)
                    list.Add(c);
        }
        return list.OrderBy(c => c.Distance).ToList();
    }

    /// <summary>Every gravel field within <paramref name="radius"/> blocks of (x, z), nearest first.</summary>
    public List<DepositCandidate> GravelFields(int x, int z, int radius)
    {
        var list = new List<DepositCandidate>();
        if (_gravel == null) return list;
        foreach (var cell in CellSearch.Within(_gravel.Cells.CellSize, x, z, radius))
            if (Candidate(new DepositKey(PlacerCells.Kind, cell), x, z) is { } c && c.Distance <= radius)
                list.Add(c);
        return list.OrderBy(c => c.Distance).ToList();
    }

    /// <summary>A deposit by its key, its distance measured from (x, z); null if the cell has none
    /// left or the key's kind isn't listed here.</summary>
    public DepositCandidate? Candidate(DepositKey key, int fromX = 0, int fromZ = 0)
    {
        var record = Registry.Get(key);
        if (key.IsGravel)
        {
            if (_gravel == null) return null;
            var state = _gravel.StateOf(key.Cell);
            if (state.None) return null;
            if (state.Placed && _gravel.FieldIn(key.Cell) is { } f)
                return new DepositCandidate(key, f.Spot, f.X, f.Y, f.Z, true, Dist(f.X, f.Z, fromX, fromZ), record);
            var spot = _gravel.Cells.Spots(key.Cell)[state.Active];
            return new DepositCandidate(key, spot.Index, spot.X, 0, spot.Z, false, Dist(spot.X, spot.Z, fromX, fromZ), record);
        }
        if (_ore == null || !Metals.Contains(key.Kind)) return null;
        var cellState = _ore.StateOf(key.Kind, key.Cell);
        if (cellState.None) return null;
        var s = _ore.SpotsOf(key.Kind, key.Cell)[cellState.Active];
        // Once measured, the ore's centre stands for the deposit.
        int px = record.X ?? s.X, pz = record.Z ?? s.Z;
        return new DepositCandidate(key, s.Index, px, record.Y ?? 0, pz, cellState.Placed, Dist(px, pz, fromX, fromZ), record);
    }

    /// <summary>The placed gravel field of a gravel key, if it is generated.</summary>
    public PlacedField? FieldOf(DepositKey key) => key.IsGravel ? _gravel?.FieldIn(key.Cell) : null;

    /// <summary>
    /// Generates what a deposit needs and measures it (see the class); <paramref name="done"/> is
    /// called once, on the main thread, possibly before this returns (when nothing needed
    /// generating).
    /// </summary>
    public void Verify(DepositKey key, Action<VerifyResult> done) => Verify(key, done, 0);

    private void Verify(DepositKey key, Action<VerifyResult> done, int round)
    {
        var candidate = Candidate(key);
        if (candidate == null)
        {
            done(new VerifyResult(VerifyStatus.None, null, Why: "none"));
            return;
        }
        int spotCount = key.IsGravel ? PlacerCells.SpotCount : OreCells.SpotCount;
        if (!candidate.Generated)
        {
            if (round > spotCount)
            {
                done(new VerifyResult(VerifyStatus.None, candidate, Why: "unresolved"));
                return;
            }
            // Generating the spot's column settles it: placed, or the next spot (followed next round).
            var chunk = OreCells.ChunkOf(candidate.X, candidate.Z);
            Load([chunk], () => Verify(key, done, round + 1));
            return;
        }
        if (key.IsGravel)
        {
            done(new VerifyResult(VerifyStatus.Field, candidate, Field: _gravel!.FieldIn(key.Cell)));
            return;
        }
        var spot = _ore!.SpotsOf(key.Kind, key.Cell)[candidate.Spot];
        var columns = new List<ChunkPos>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                columns.Add(new ChunkPos(spot.Chunk.X + dx, spot.Chunk.Z + dz));
        Load(columns, () => done(Measure(key, columns)));
    }

    private VerifyResult Measure(DepositKey key, List<ChunkPos> columns)
    {
        var units = UnitsTable(key.Kind);
        const int size = OreCells.ChunkSize;
        double total = 0;
        long sx = 0, sy = 0, sz = 0;
        int blocks = 0;
        int chunksHigh = _api.WorldManager.MapSizeY / size;
        foreach (var column in columns)
            for (int cy = 0; cy < chunksHigh; cy++)
            {
                if (_api.WorldManager.GetChunk(column.X, cy, column.Z) is not { } chunk) continue;
                chunk.Unpack();
                var data = chunk.Data;
                for (int i = 0; i < size * size * size; i++)
                {
                    int id = data.GetBlockIdUnsafe(i);
                    if (id <= 0 || id >= units.Length || units[id] <= 0) continue;
                    total += units[id];
                    blocks++;
                    sx += column.X * size + i % size;
                    sz += column.Z * size + i / size % size;
                    sy += cy * size + i / (size * size);
                }
            }
        var targets = _sizes.TargetsFor(key.Kind)!.Value;
        double ingots = DepositSizing.Ingots(total);
        var tier = DepositSizing.Classify(ingots, targets);
        bool workedOut = DepositSizing.IsWorkedOut(ingots, targets);
        var spot = Candidate(key)!;
        int x = blocks > 0 ? (int)(sx / blocks) : spot.X;
        int y = blocks > 0 ? (int)(sy / blocks) : 0;
        int z = blocks > 0 ? (int)(sz / blocks) : spot.Z;
        Registry.RecordMeasure(key, Math.Round(ingots, 1), tier, x, y, z, _api.World.Calendar.TotalDays, workedOut);
        _api.Logger.Notification("[seraphhorizons] Deposits: {0} measured: {1} ore blocks, {2:0} ingots, {3}{4}",
            key, blocks, ingots, tier, workedOut ? ", worked out: sold out" : "");
        SeraphHorizons.Mod.Admin.AdminLogs.Ore?.Write("verify", $"{key} at {x},{y},{z}: {blocks} ore blocks, {ingots:0} ingots, {tier}{(workedOut ? ", worked out: sold out" : "")}");
        return new VerifyResult(VerifyStatus.Measured, Candidate(key), ingots, blocks, tier, workedOut);
    }

    /// <summary>Metal units per block id for a metal's ore blocks (0 for any other block): the sum
    /// over the block's drops of the average count times the dropped item's <c>metalUnits</c>, as the
    /// survey reads them (1.25 ore chunks plus a little crystallised ore).</summary>
    private double[] UnitsTable(string metal)
    {
        if (_unitsByMetal.TryGetValue(metal, out var table)) return table;
        var blocks = _api.World.Blocks;
        table = new double[blocks.Count];
        for (int id = 0; id < table.Length; id++)
        {
            var block = blocks[id];
            if (block?.Code is not { } code || OreMetals.MetalOf(OreMetals.OreOfBlockPath(code.Path)) != metal) continue;
            double units = 0;
            foreach (var drop in block.Drops ?? [])
                if (drop?.ResolvedItemstack?.Collectible?.Attributes?["metalUnits"] is { Exists: true } mu)
                    units += drop.Quantity.avg * mu.AsDouble();
            table[id] = units;
        }
        return _unitsByMetal[metal] = table;
    }

    /// <summary>Loads (generating if need be) the columns, then calls back once all are loaded.</summary>
    private void Load(IReadOnlyList<ChunkPos> columns, Action then)
    {
        int left = columns.Count;
        foreach (var c in columns)
            _api.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions
            {
                OnLoaded = () =>
                {
                    if (--left == 0) then();
                },
            });
    }

    private static double Dist(int x, int z, int fx, int fz) =>
        Math.Sqrt((double)(x - fx) * (x - fx) + (double)(z - fz) * (z - fz));

    private static DepositRegistry Load(ICoreServerAPI api)
    {
        try
        {
            var bytes = api.WorldManager.SaveGame.GetData(RegistryKey);
            return DepositRegistry.Parse(bytes == null ? null : Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Deposits: could not read the deposit registry from the savegame, starting afresh: {0}", e.Message);
            return new DepositRegistry();
        }
    }

    private void Save() =>
        _api.WorldManager.SaveGame.StoreData(RegistryKey, Encoding.UTF8.GetBytes(Registry.Serialize()));
}
