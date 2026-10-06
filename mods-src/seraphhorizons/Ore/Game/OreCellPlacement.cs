using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Ore cells in the world (#438): replaces Interesting Ore Gen 2.3.8's spacing filter,
/// <c>TiltedDiscDepositGenerator.TryApproveOreSpawnSeed(BlockPos)</c> (private, instance, returns
/// bool), with <see cref="OreCells"/>' rule for managed ores. IOG asks it once per vein try per
/// chunk being generated, before the try's depth, rock or shape are drawn, with the try's centre
/// (X and Z set, Y not yet); its own answer comes from static distance dictionaries filled in the
/// order chunks happen to generate, emptied every server start. For gems, quartz and olivine
/// (<see cref="OreMetals.MetalOf"/> null) IOG's own method still runs.
///
/// The game's <c>GenDeposits</c> makes every vein try from a source chunk with the chunk's own
/// seeded random sequence, once for each chunk within three chunks of it being generated. A try is
/// approved when its source chunk is the anchor of its cell's active spot and it is the first try
/// of its metal from that chunk in the pass over it (a prefix on <c>GeneratePartial</c> starts
/// each pass), so every neighbouring chunk approves the same try and the vein comes out whole.
/// Which ore that is (malachite or native copper, a tube or a disc) is whatever IOG's tries in
/// that chunk give for the rock there.
///
/// When an anchor's own column has been generated (a postfix on <c>GenChunkColumn</c>, after all
/// deposits reaching it are placed), its blocks are checked for the metal's ore; without any the
/// spot failed and the next fallback becomes active (<see cref="OreCellBook"/>). The book is
/// kept in the savegame under <see cref="OreSystem.CellBookKey"/>.
/// </summary>
public sealed class OreCellPlacement
{
    public const string IogModId = "interestingoregen";
    public const string GeneratorTypeName = "InterestingOreGen.Generators.TiltedDiscDepositGenerator";
    public const string ApproveMethodName = "TryApproveOreSpawnSeed";

    private static readonly AccessTools.FieldRef<DepositGeneratorBase, DepositVariant>? VariantOf =
        Try(() => AccessTools.FieldRefAccess<DepositGeneratorBase, DepositVariant>("variant"));

    private static OreCellPlacement? _current;

    // The source chunk of the pass GenDeposits is making on this thread, and the metals whose first
    // try from it has been seen.
    [ThreadStatic] private static ChunkPos? _passChunk;
    [ThreadStatic] private static HashSet<string>? _passSeen;

    private readonly ICoreServerAPI _api;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<(string, CellPos), OreSpot[]> _spots = new();
    private OreCellBook _book;
    private bool _dirty;
    private bool _warned;
    private string?[]? _metalByBlockId;

    public OreCells Cells { get; }

    /// <summary>The metals that have IOG vein variants with tries: the ones cells are kept for.</summary>
    public IReadOnlyList<string> Managed { get; private set; } = [];

    private OreCellPlacement(ICoreServerAPI api, OreCells cells, OreCellBook book)
    {
        _api = api;
        Cells = cells;
        _book = book;
    }

    public static Type? GeneratorType => AccessTools.TypeByName(GeneratorTypeName);

    public static MethodInfo? ApproveMethod =>
        GeneratorType is { } type
        && AccessTools.Method(type, ApproveMethodName, [typeof(BlockPos)]) is { } m
        && m.ReturnType == typeof(bool) && !m.IsStatic
            ? m
            : null;

    private static MethodInfo? GeneratePartial => AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.GeneratePartial),
        [typeof(IServerChunk[]), typeof(int), typeof(int), typeof(int), typeof(int)]);

    private static MethodInfo? GenChunkColumn => AccessTools.Method(typeof(GenDeposits), "GenChunkColumn",
        [typeof(IChunkColumnGenerateRequest)]);

    /// <summary>Why the rule can't bind here, or null if it can.</summary>
    public static string? Unsupported(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(IogModId)) return "Interesting Ore Gen is not loaded";
        if (GeneratorType is null) return $"{GeneratorTypeName} is gone";
        if (ApproveMethod is null) return $"{GeneratorTypeName}.{ApproveMethodName}(BlockPos) is gone or changed";
        if (VariantOf is null) return "DepositGeneratorBase.variant is gone";
        if (GeneratePartial is null || GenChunkColumn is null) return "GenDeposits.GeneratePartial or GenChunkColumn is gone";
        return null;
    }

    public static OreCellPlacement Bind(ICoreServerAPI api, Harmony harmony, OreWorldRecord world)
    {
        var placement = new OreCellPlacement(api,
            new OreCells(api.WorldManager.Seed, world.CellSize, world.CellSizeByMetal), LoadBook(api));
        placement.Managed = ManagedMetals(api);
        harmony.Patch(ApproveMethod, prefix: new HarmonyMethod(typeof(OreCellPlacement), nameof(ApprovePrefix)));
        harmony.Patch(GeneratePartial, prefix: new HarmonyMethod(typeof(OreCellPlacement), nameof(GeneratePartialPrefix)));
        harmony.Patch(GenChunkColumn, postfix: new HarmonyMethod(typeof(OreCellPlacement), nameof(GenChunkColumnPostfix)));
        api.Event.GameWorldSave += placement.Save;
        _current = placement;
        api.Logger.Notification(
            "[seraphhorizons] Ore cells: bound {0}.{1}; one deposit per {2} m cell for {3}",
            GeneratorTypeName, ApproveMethodName, world.CellSize, string.Join(", ", placement.Managed));
        return placement;
    }

    public void Unbind()
    {
        if (_current == this) _current = null;
        _api.Event.GameWorldSave -= Save;
    }

    /// <summary>The metals of every IOG vein variant the game's deposit generators will try.</summary>
    private static string[] ManagedMetals(ICoreServerAPI api)
    {
        var type = GeneratorType;
        return api.ModLoader.Systems.OfType<GenDeposits>()
            .SelectMany(g => g.Deposits ?? [])
            .Where(v => v.TriesPerChunk > 0 && v.GeneratorInst != null && type != null && type.IsInstanceOfType(v.GeneratorInst))
            .Select(v => OreMetals.MetalOf(v.Code))
            .OfType<string>()
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public OreSpot[] SpotsOf(string metal, CellPos cell) =>
        _spots.GetOrAdd((metal, cell), key => Cells.Spots(key.Item1, key.Item2));

    public CellState StateOf(string metal, CellPos cell)
    {
        lock (_lock) return _book.Get(metal, cell).Clone();
    }

    public SpotStatus StatusOf(string metal, CellPos cell, int spot)
    {
        lock (_lock) return _book.StatusOf(metal, cell, spot);
    }

    private bool Approve(string metal, int x, int z)
    {
        var cell = Cells.CellOf(metal, x, z);
        int? active;
        lock (_lock) active = _book.ActiveIndex(metal, cell);
        OreSpot? spot = active is int i ? SpotsOf(metal, cell)[i] : null;
        var chunk = OreCells.ChunkOf(x, z);
        // A pass starts in the GeneratePartial prefix; a try from another chunk also starts one,
        // in case a GenDeposits subclass overrides GeneratePartial.
        if (_passChunk != chunk)
        {
            _passChunk = chunk;
            _passSeen?.Clear();
        }
        bool first = spot?.Chunk == chunk && (_passSeen ??= []).Add(metal);
        return OreCells.Approves(spot, x, z, first);
    }

    private void OnColumnGenerated(IChunkColumnGenerateRequest request)
    {
        int cx = request.ChunkX, cz = request.ChunkZ;
        var column = new ChunkPos(cx, cz);
        int bx = cx * OreCells.ChunkSize + OreCells.ChunkSize / 2, bz = cz * OreCells.ChunkSize + OreCells.ChunkSize / 2;
        HashSet<string>? present = null;
        foreach (var metal in Managed)
        {
            var cell = Cells.CellOf(metal, bx, bz);
            foreach (var spot in SpotsOf(metal, cell))
            {
                if (spot.Chunk != column) continue;
                lock (_lock)
                {
                    var state = _book.Get(metal, cell);
                    if (spot.Index != state.Active || state.Placed || state.None)
                    {
                        _dirty |= _book.OnFallbackGenerated(metal, cell, spot.Index);
                        continue;
                    }
                    present ??= MetalsIn(request.Chunks);
                    bool has = present.Contains(metal);
                    _dirty |= _book.OnAnchorGenerated(metal, cell, spot.Index, has);
                    var after = _book.Get(metal, cell);
                    _api.Logger.Notification("[seraphhorizons] Ore cells: {0} cell {1}, {2} spot {3} at {4}, {5}: {6}",
                        metal, cell.X, cell.Z, spot.Index, spot.X, spot.Z,
                        has ? "deposit placed"
                            : after.None ? "no vein, and no spot left: the cell has none"
                            : $"no vein, spot {after.Active} is next");
                }
            }
        }
    }

    private HashSet<string> MetalsIn(IServerChunk[] chunks)
    {
        var table = _metalByBlockId ??= MetalByBlockId();
        var found = new HashSet<string>();
        const int volume = OreCells.ChunkSize * OreCells.ChunkSize * OreCells.ChunkSize;
        foreach (var chunk in chunks)
        {
            var data = chunk?.Data;
            if (data == null) continue;
            for (int i = 0; i < volume; i++)
            {
                int id = data.GetBlockIdUnsafe(i);
                if (id > 0 && id < table.Length && table[id] is { } metal) found.Add(metal);
            }
        }
        return found;
    }

    private string?[] MetalByBlockId()
    {
        var blocks = _api.World.Blocks;
        var table = new string?[blocks.Count];
        for (int id = 0; id < table.Length; id++)
            if (blocks[id]?.Code is { } code)
                table[id] = OreMetals.MetalOf(OreMetals.OreOfBlockPath(code.Path));
        return table;
    }

    private static OreCellBook LoadBook(ICoreServerAPI api)
    {
        try
        {
            var bytes = api.WorldManager.SaveGame.GetData(OreSystem.CellBookKey);
            return OreCellBook.Parse(bytes == null ? null : Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Ore cells: could not read the cell states from the savegame, starting afresh: {0}", e.Message);
            return new OreCellBook();
        }
    }

    private void Save()
    {
        string text;
        lock (_lock)
        {
            if (!_dirty) return;
            text = _book.Serialize();
            _dirty = false;
        }
        _api.WorldManager.SaveGame.StoreData(OreSystem.CellBookKey, Encoding.UTF8.GetBytes(text));
    }

    private void Warn(Exception e)
    {
        if (_warned) return;
        _warned = true;
        _api.Logger.Error("[seraphhorizons] Ore cells: {0}; Interesting Ore Gen's own rule decides where this failed", e);
    }

    // Harmony: IOG's TiltedDiscDepositGenerator.TryApproveOreSpawnSeed(BlockPos depoCenterPos).
    private static bool ApprovePrefix(DepositGeneratorBase __instance, BlockPos __0, ref bool __result)
    {
        var placement = _current;
        if (placement == null) return true;
        try
        {
            var metal = OreMetals.MetalOf(VariantOf!(__instance)?.Code);
            if (metal == null) return true;
            __result = placement.Approve(metal, __0.X, __0.Z);
            return false;
        }
        catch (Exception e)
        {
            placement.Warn(e);
            return true;
        }
    }

    // Harmony: GenDeposits.GeneratePartial(chunks, chunkX, chunkZ, chunkdX, chunkdZ), one pass over
    // the source chunk (chunkX + chunkdX, chunkZ + chunkdZ).
    private static void GeneratePartialPrefix(int __1, int __2, int __3, int __4)
    {
        _passChunk = new ChunkPos(__1 + __3, __2 + __4);
        _passSeen?.Clear();
    }

    // Harmony: GenDeposits.GenChunkColumn(request), after every deposit reaching the column is placed.
    private static void GenChunkColumnPostfix(IChunkColumnGenerateRequest __0)
    {
        var placement = _current;
        if (placement == null) return;
        try { placement.OnColumnGenerated(__0); }
        catch (Exception e) { placement.Warn(e); }
    }

    private static T? Try<T>(Func<T> get) where T : class
    {
        try { return get(); }
        catch (Exception) { return null; }
    }
}
