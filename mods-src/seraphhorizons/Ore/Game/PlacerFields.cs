using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Placer fields (#442; README "Placer fields"): one rich gravel field per placer cell, placed when
/// the chunk column holding the cell's active spot generates (<see cref="PlacerCells"/>,
/// <see cref="PlacerSite"/>), at the TerrainFeatures pass, after the terrain, its soil layers and
/// water and before plants. The column's own heightmap (<c>WorldGenTerrainHeightMap</c>, which the
/// game's surface deposits follow too) and blocks decide whether it suits; the field is the local
/// rock's <c>richgravel-{rock}</c> (Wilderlands Panning's block, whose pan tables then decide its
/// metals), replacing the top soil, gravel or sand. A spot that doesn't suit fails and the next
/// fallback becomes active, as ore cells do; the state is kept in the savegame under
/// <see cref="BookKey"/>.
///
/// Also, in worlds with placer fields: the scattered rich gravel deposit (Wilderlands Panning's
/// <c>game:worldgen/deposits/rock/richgravel.json</c>, 75 tries per chunk) is cut to a quarter, by a
/// postfix on <c>GenDeposits.initAssets</c> like <see cref="DepositSizes"/>; and every rock's rich
/// gravel gives native copper in the pan (<see cref="AddPanCopper"/>).
/// </summary>
public sealed class PlacerFields
{
    public const string BookKey = "seraphhorizons:placercells";
    public const string WpanningModId = "wpanning";
    public const string ScatteredGravelCode = "richgravel";
    public const float ScatteredGravelShare = 0.25f;

    /// <summary>The copper given to a rock's rich gravel whose pan table has none: lower than the
    /// 1.5–3% of the rocks Wilderlands Panning gives it to.</summary>
    public const float AddedCopperChance = 0.01f;

    private static MethodInfo? InitAssets => AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.initAssets));

    private readonly ICoreServerAPI _api;
    private readonly object _lock = new();
    private PlacerBook _book;
    private bool _dirty;
    private bool _warned;
    private Dictionary<string, int>? _gravelIds;

    public PlacerCells Cells { get; }

    private PlacerFields(ICoreServerAPI api, PlacerCells cells, PlacerBook book)
    {
        _api = api;
        Cells = cells;
        _book = book;
    }

    /// <summary>Why fields can't be placed here, or null if they can.</summary>
    public static string? Unsupported(ICoreAPI api) =>
        api.ModLoader.IsModEnabled(WpanningModId) ? null : "Wilderlands Panning (rich gravel) is not loaded";

    public static PlacerFields Bind(ICoreServerAPI api, OreWorldRecord world)
    {
        var fields = new PlacerFields(api, new PlacerCells(api.WorldManager.Seed, world.PlacerCellSize), LoadBook(api));
        api.Event.ChunkColumnGeneration(fields.OnColumn, EnumWorldGenPass.TerrainFeatures, "standard");
        api.Event.GameWorldSave += fields.Save;
        api.Logger.Notification("[seraphhorizons] Placer fields: one rich gravel field per {0} m cell", fields.Cells.CellSize);
        return fields;
    }

    public void Unbind() => _api.Event.GameWorldSave -= Save;

    public CellState StateOf(CellPos cell)
    {
        lock (_lock) return _book.Get(cell).Clone();
    }

    public SpotStatus StatusOf(CellPos cell, int spot)
    {
        lock (_lock) return _book.Cells.StatusOf(PlacerCells.Kind, cell, spot);
    }

    public PlacedField? FieldIn(CellPos cell)
    {
        lock (_lock) return _book.FieldIn(cell);
    }

    /// <summary>Binds the cut of the scattered rich gravel (in <c>Start</c>: the deposit generators
    /// are built in AssetsFinalize).</summary>
    public static void BindScatteredCut(Harmony harmony) =>
        harmony.Patch(InitAssets, postfix: new HarmonyMethod(typeof(PlacerFields), nameof(CutScatteredPostfix)));

    private static void CutScatteredPostfix(GenDeposits __instance)
    {
        foreach (var variant in __instance.Deposits ?? [])
            if (variant.Code == ScatteredGravelCode && variant.TriesPerChunk > 0)
                variant.TriesPerChunk *= ScatteredGravelShare;
    }

    /// <summary>
    /// Gives native copper to the rich gravel of every rock in the pan. Wilderlands Panning 1.0.9
    /// has a table for all rich gravel (<c>@(richgravel)-.*</c>, copper 3%) and fourteen per-rock
    /// tables, which win for their rock (the pan takes the last matching key); nine of those have
    /// no copper: conglomerate, limestone, peridotite, phyllite, slate, chalk, claystone, granite and
    /// shale, among the commonest surface rocks. Each rich gravel table without copper gets it at
    /// <see cref="AddedCopperChance"/>, on the server in AssetsLoaded, after the patch loader, as
    /// <see cref="PanningDrops"/> trims the same table. Returns the tables changed.
    /// </summary>
    public static List<string> AddPanCopper(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PanningDrops.PanAsset);
        if (asset == null) return [];
        var json = JObject.Parse(asset.ToText());
        var changed = AddCopperTo(json);
        if (changed.Count > 0)
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        return changed;
    }

    public static List<string> AddCopperTo(JObject pan)
    {
        var changed = new List<string>();
        if (pan.GetValue("attributes", StringComparison.OrdinalIgnoreCase) is not JObject attributes
            || attributes.GetValue("panningDrops", StringComparison.OrdinalIgnoreCase) is not JObject table)
            return changed;
        foreach (var entry in table.Properties())
        {
            if (!entry.Name.Contains("richgravel", StringComparison.Ordinal) || entry.Value is not JArray drops) continue;
            bool hasCopper = drops.OfType<JObject>().Any(d =>
                (d.GetValue("code", StringComparison.OrdinalIgnoreCase)?.ToString() ?? "").EndsWith("nugget-nativecopper", StringComparison.Ordinal));
            if (hasCopper) continue;
            drops.Add(new JObject
            {
                ["type"] = "item",
                ["code"] = "nugget-nativecopper",
                ["chance"] = new JObject { ["avg"] = AddedCopperChance, ["var"] = 0 },
            });
            changed.Add(entry.Name);
        }
        return changed;
    }

    private void OnColumn(IChunkColumnGenerateRequest request)
    {
        try
        {
            int size = OreCells.ChunkSize;
            int bx = request.ChunkX * size + size / 2, bz = request.ChunkZ * size + size / 2;
            var cell = Cells.CellOf(bx, bz);
            var column = new ChunkPos(request.ChunkX, request.ChunkZ);
            foreach (var spot in Cells.Spots(cell))
            {
                if (spot.Chunk != column) continue;
                lock (_lock)
                {
                    var state = _book.Get(cell);
                    if (spot.Index != state.Active || state.Placed || state.None)
                    {
                        _dirty |= _book.OnFallbackGenerated(cell, spot.Index);
                        continue;
                    }
                }
                var field = TryPlace(request, cell, spot, out string why);
                lock (_lock)
                {
                    _dirty |= _book.OnAnchorGenerated(cell, spot.Index, field);
                    var after = _book.Get(cell);
                    string outcome = field != null ? $"{field.Blocks} blocks of richgravel-{field.Rock} at {field.X}, {field.Y}, {field.Z}"
                        : after.None ? $"unsuitable ({why}), and no spot left: the cell has none"
                        : $"unsuitable ({why}), spot {after.Active} is next";
                    _api.Logger.Notification("[seraphhorizons] Placer fields: cell {0}, {1} spot {2} at {3}, {4}: {5}",
                        cell.X, cell.Z, spot.Index, spot.X, spot.Z, outcome);
                    SeraphHorizons.Mod.Admin.AdminLogs.Ore?.Write("placement", $"gravel cell {cell.X},{cell.Z} spot {spot.Index} at {spot.X},{spot.Z}: {outcome}");
                }
            }
        }
        catch (Exception e)
        {
            if (_warned) return;
            _warned = true;
            _api.Logger.Error("[seraphhorizons] Placer fields: {0}", e);
        }
    }

    private PlacedField? TryPlace(IChunkColumnGenerateRequest request, CellPos cell, OreSpot spot, out string why)
    {
        why = "";
        const int size = OreCells.ChunkSize;
        var chunks = request.Chunks;
        var heightMap = chunks[0].MapChunk.WorldGenTerrainHeightMap;
        var blocks = _api.World.Blocks;
        int mapHeight = chunks.Length * size;
        var heights = new int[size * size];
        var water = new bool[size * size];
        var replaceable = new bool[size * size];
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int i = TerrainPatch.Index(x, z);
                int h = heightMap[i];
                heights[i] = h;
                if (h + 1 < mapHeight)
                    water[i] = chunks[(h + 1) / size].Data.GetFluid(Index3d(x, h + 1, z)) != 0;
                if (h > 0 && h < mapHeight)
                    replaceable[i] = IsLoose(blocks[chunks[h / size].Data.GetBlockIdUnsafe(Index3d(x, h, z))]);
            }
        var patch = new TerrainPatch(heights, water, replaceable);
        var spec = Cells.Field(cell, spot.Index);
        int lx = spot.X - spot.Chunk.X * size, lz = spot.Z - spot.Chunk.Z * size;
        if (PlacerSite.FindCentre(patch, spec, lx, lz) is not { } centre)
        {
            why = PlacerSite.Describe(patch);
            return null;
        }
        var (cx, cz) = centre;
        int top = heights[TerrainPatch.Index(cx, cz)] - spec.Depth;
        if (RockAt(chunks, cx, top, cz) is not { } local)
        {
            why = "no rock with rich gravel under it";
            return null;
        }
        var (rock, gravelId) = local;

        int placed = 0;
        foreach (var (dx, dz) in PlacerSite.Disc(spec.Radius))
        {
            int x = cx + dx, z = cz + dz;
            int surface = heights[TerrainPatch.Index(x, z)] - spec.Depth;
            for (int y = surface; y > surface - spec.Thickness && y > 0; y--)
            {
                var data = chunks[y / size].Data;
                int index = Index3d(x, y, z);
                if (!IsLoose(blocks[data.GetBlockIdUnsafe(index)], allowStone: true)) continue;
                data.SetBlockUnsafe(index, gravelId);
                placed++;
            }
        }
        if (placed == 0)
        {
            why = "nothing to replace";
            return null;
        }
        int baseX = request.ChunkX * size, baseZ = request.ChunkZ * size;
        return new PlacedField(spot.Index, baseX + cx, top, baseZ + cz, rock, placed);
    }

    /// <summary>The local rock under (x, top, z) that has a rich gravel block: the first block down
    /// with a <c>rock</c> variant (soil has none; sand, gravel and the rock itself do).</summary>
    private (string Rock, int BlockId)? RockAt(IServerChunk[] chunks, int x, int top, int z)
    {
        const int size = OreCells.ChunkSize;
        var ids = _gravelIds ??= GravelIds();
        for (int y = top; y > Math.Max(0, top - 24); y--)
        {
            var block = _api.World.Blocks[chunks[y / size].Data.GetBlockIdUnsafe(Index3d(x, y, z))];
            if (block?.Variant?["rock"] is { } rock && ids.TryGetValue(rock, out int id))
                return (rock, id);
        }
        return null;
    }

    private Dictionary<string, int> GravelIds()
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in _api.World.Blocks)
            if (block?.Code is { Domain: "game" } code && code.Path.StartsWith("richgravel-", StringComparison.Ordinal)
                && block.Variant?["rock"] is { } rock && code.Path == "richgravel-" + rock)
                ids[rock] = block.BlockId;
        return ids;
    }

    private static bool IsLoose(Block? block, bool allowStone = false) =>
        block != null && block.BlockId != 0 && !block.Code.Path.StartsWith("ore-", StringComparison.Ordinal)
        && (block.BlockMaterial is EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand
            || (allowStone && block.BlockMaterial == EnumBlockMaterial.Stone));

    private static int Index3d(int x, int y, int z) =>
        ((y % OreCells.ChunkSize) * OreCells.ChunkSize + z) * OreCells.ChunkSize + x;

    private static PlacerBook LoadBook(ICoreServerAPI api)
    {
        try
        {
            var bytes = api.WorldManager.SaveGame.GetData(BookKey);
            return PlacerBook.Parse(bytes == null ? null : Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Placer fields: could not read the cell states from the savegame, starting afresh: {0}", e.Message);
            return new PlacerBook();
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
        _api.WorldManager.SaveGame.StoreData(BookKey, Encoding.UTF8.GetBytes(text));
    }
}
