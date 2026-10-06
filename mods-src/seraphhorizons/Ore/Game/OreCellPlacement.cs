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
/// of its metal from that chunk, in the pass over it (a prefix on <c>GeneratePartial</c> starts
/// each pass), whose vein can start: its centre in a rock its ore takes
/// (<see cref="CentreInHostRock"/>). Every neighbouring chunk sees the same tries in the same
/// order and approves the same one, so the vein comes out whole. Which ore that is (malachite or
/// native copper, a tube or a disc) follows from IOG's tries there and the rock. Metals with few
/// tries get more (<see cref="BindTries"/>), so an anchor chunk usually has some.
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

    private static readonly AccessTools.FieldRef<GenDeposits, IBlockAccessor>? WorldgenAccessor =
        Try(() => AccessTools.FieldRefAccess<GenDeposits, IBlockAccessor>("blockAccessor"));

    // The pass GenDeposits is making on this thread: its source chunk, its worldgen block accessor,
    // and per metal whether the anchor's try is decided (approved, or undecidable here).
    [ThreadStatic] private static ChunkPos? _passChunk;
    [ThreadStatic] private static IBlockAccessor? _passAccessor;
    [ThreadStatic] private static HashSet<string>? _passDecided;

    private readonly ICoreServerAPI _api;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<(string, CellPos), OreSpot[]> _spots = new();
    private OreCellBook _book;
    private bool _dirty;
    private bool _warned;
    private string?[]? _metalByBlockId;
    private readonly ConcurrentDictionary<DepositGeneratorBase, HashSet<int>> _hostRocks = new();

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
        if (WorldgenAccessor is null) return "GenDeposits.blockAccessor is gone";
        if (GeneratePartial is null || GenChunkColumn is null || InitAssets is null)
            return "GenDeposits.GeneratePartial, GenChunkColumn or initAssets is gone";
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

    private static MethodInfo? InitAssets => AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.initAssets));

    /// <summary>
    /// Raises the tries of managed metals' IOG variants, in a postfix on <c>GenDeposits.initAssets</c>
    /// (bound in <c>Start</c>: the generators are built in AssetsFinalize, before the server side
    /// starts). Under the cell rule a try counts only in an anchor chunk, so a metal with fewer
    /// than <see cref="OreCells.MinTriesPerChunk"/> tries per chunk (borax has 0.01) would mostly
    /// find none there. Each of its variants is scaled up by the same factor, so its ores keep their
    /// mix; every other try is turned down by the rule before anything is drawn.
    /// </summary>
    public static void BindTries(Harmony harmony) =>
        harmony.Patch(InitAssets, postfix: new HarmonyMethod(typeof(OreCellPlacement), nameof(RaiseTriesPostfix)));

    private static void RaiseTriesPostfix(GenDeposits __instance)
    {
        if (GeneratorType is not { } type) return;
        var byMetal = (__instance.Deposits ?? [])
            .Where(v => v.TriesPerChunk > 0 && v.GeneratorInst != null && type.IsInstanceOfType(v.GeneratorInst))
            .GroupBy(v => OreMetals.MetalOf(v.Code))
            .Where(g => g.Key != null);
        foreach (var metal in byMetal)
        {
            float total = metal.Sum(v => v.TriesPerChunk);
            if (total >= OreCells.MinTriesPerChunk) continue;
            float k = OreCells.MinTriesPerChunk / total;
            foreach (var variant in metal) variant.TriesPerChunk *= k;
        }
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

    private bool Approve(DepositGeneratorBase generator, string metal, BlockPos pos)
    {
        int x = pos.X, z = pos.Z;
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
            _passDecided?.Clear();
        }
        if (spot?.Chunk != chunk) return false;
        var decided = _passDecided ??= [];
        if (decided.Contains(metal)) return false;
        switch (CentreInHostRock(generator, pos))
        {
            case false:
                return false; // this try's vein would not start; the next one of the metal may
            case null:
                decided.Add(metal); // the anchor's rock can't be read from here: none is approved
                return false;
        }
        decided.Add(metal);
        return OreCells.Approves(spot, x, z, firstOfMetalInChunk: true);
    }

    /// <summary>
    /// Whether the try's vein centre is in a rock its variant can host, or null if that can't be
    /// read from this pass (the anchor's chunk is not reachable). IOG draws the centre's height
    /// right after this filter, from the try's own <c>DepositRand</c>: its radius, one float, then
    /// <c>YPosRel</c> (TiltedDiscDepositGenerator.GenDeposit, TiltedAnywhereDiscGenerator.
    /// beforeGenDeposit). Those draws are replayed on a copy of the random, and the block there
    /// looked up in the variant's host rocks (<c>GetBearingBlocks</c>), as its disc and tube veins
    /// do at their centre. Every pass over the chunk computes the same, so they agree on the try.
    /// </summary>
    private bool? CentreInHostRock(DepositGeneratorBase generator, BlockPos pos)
    {
        // A chimney checks the rock at every step of its tendrils, not at its centre, and grows
        // through whatever host rock it meets: its first try is taken as it comes.
        if (VariantOf!(generator)?.Attributes?["inblock"]?["veintype"]?.AsString()?.StartsWith("chimney", StringComparison.Ordinal) == true)
            return true;
        var type = generator.GetType();
        if (AccessTools.Field(type, "YPosRel")?.GetValue(generator) is not NatFloat yPosRel
            || AccessTools.Field(type, "Radius")?.GetValue(generator) is not NatFloat radius
            || generator.DepositRand is not { } source)
            return true; // not the generator this was written against: the first try, as before
        var rand = new LCGRandom { worldSeed = source.worldSeed, mapGenSeed = source.mapGenSeed, currentSeed = source.currentSeed };
        if ((int)radius.nextFloat(1f, rand) <= 0) return false;
        rand.NextFloat();
        int y = (int)yPosRel.nextFloat(1f, rand);
        if (y <= 0 || y >= _api.WorldManager.MapSizeY) return false;
        if (_passAccessor?.GetChunkAtBlockPos(new BlockPos(pos.X, y, pos.Z)) is not IServerChunk { Data: { } data })
            return null;
        int size = OreCells.ChunkSize;
        int id = data.GetBlockIdUnsafe((GameMath.Mod(y, size) * size + GameMath.Mod(pos.Z, size)) * size + GameMath.Mod(pos.X, size));
        return HostRocks(generator).Contains(id);
    }

    private HashSet<int> HostRocks(DepositGeneratorBase generator) =>
        _hostRocks.GetOrAdd(generator, g =>
            AccessTools.Method(g.GetType(), "GetBearingBlocks")?.Invoke(g, null) is int[] ids ? [.. ids] : []);

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
                    string outcome = has ? "deposit placed"
                        : after.None ? "no vein, and no spot left: the cell has none"
                        : $"no vein, spot {after.Active} is next";
                    _api.Logger.Notification("[seraphhorizons] Ore cells: {0} cell {1}, {2} spot {3} at {4}, {5}: {6}",
                        metal, cell.X, cell.Z, spot.Index, spot.X, spot.Z, outcome);
                    SeraphHorizons.Mod.Admin.AdminLogs.Ore?.Write("placement", $"{metal} cell {cell.X},{cell.Z} spot {spot.Index} at {spot.X},{spot.Z}: {outcome}");
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
            __result = placement.Approve(__instance, metal, __0);
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
    private static void GeneratePartialPrefix(GenDeposits __instance, int __1, int __2, int __3, int __4)
    {
        _passChunk = new ChunkPos(__1 + __3, __2 + __4);
        _passAccessor = WorldgenAccessor?.Invoke(__instance);
        _passDecided?.Clear();
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
