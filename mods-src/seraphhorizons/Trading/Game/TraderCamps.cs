using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Trading;

/// <summary>What <c>config/trading/camps.json</c> says about the game's structures.</summary>
public sealed class CampsConfig
{
    /// <summary>Structure groups whose structures become camp schematics: taken out of the game's
    /// random placement and placed only on the grid.</summary>
    public string[] CampGroups { get; set; } = ["trader"];
    /// <summary>Structure groups taken out and not placed at all (other mods' extra traders).</summary>
    public string[] DropGroups { get; set; } = [];
    /// <summary>Schematic paths (<c>domain:path</c>, wildcards as in structures.json) of
    /// camp-group structures with several traders: kept for the settlements, not lone camps.</summary>
    public string[] SettlementSchematics { get; set; } = [];
}

/// <summary>
/// Trader camps on the grid (#447), server side.
///
/// <list type="bullet">
/// <item>At worldgen init, after the game's GenStructures has loaded <c>worldgen/structures.json</c>
/// (every mod's), the camp-group structures (vanilla's nine kinds of camp, BetterTraders' five,
/// Domestic Animal Trader's and Culinary Artillery's wagons, all group <c>trader</c>) are taken out of
/// its list, so its random placement never places them; <see cref="CampsConfig"/> says which.</item>
/// <item>Each chunk column, after the game's structure passes (TerrainFeatures, this system's
/// ExecuteOrder after GenStructuresPosPass), checks whether it holds a cell's spot whose turn it is
/// (<see cref="CampRegistry"/>), and there runs the game's own WorldGenStructure.TryGenerate for the
/// camp structures in a seeded weighted order: each checks its own climate and forest range and
/// placement, so the camp is the kind vanilla would put there. Their minimum group distance is set
/// to 0: the grid spaces camps now. A placed camp is recorded as the game records one (a generated
/// structure of group <c>trader</c> in the map region, and its land claim).</item>
/// <item>Camp schematics hold an entity spawner with vanilla trader codes. The spawner asks the
/// <c>onattemptspawnerspawn</c> event bus before every spawn (vanilla's
/// ModSystemClimateSpecificTraderTypes turns <c>-temperate</c> into <c>-cold</c>/<c>-desert</c>
/// there); this class answers with the pack's trader of the cell's type, same gender, outfit set
/// by climate as vanilla's. The spawner keeps one trader, and spawns again when it is gone.</item>
/// </list>
/// Only in worlds that had the grid from their first start (<see cref="TradingSystem.GridActive"/>).
/// </summary>
public sealed class TraderCamps
{
    public const string RegistryKey = "seraphhorizons:tradercamps";
    public const string CampGroup = "trader";

    private static readonly FieldInfo? StructuresConfigField = AccessTools.Field(typeof(GenStructures), "scfg");
    private static readonly MethodInfo? TryGenerateMethod = AccessTools.Method(typeof(WorldGenStructure), "TryGenerate");

    private readonly ICoreServerAPI _api;
    private readonly TradingSystem _system;
    private readonly CampsConfig _config;
    private readonly Dictionary<string, bool> _traderCodes = new();
    private IWorldGenBlockAccessor? _blocks;
    private GenStructures? _gen;
    private WorldGenStructure[] _camps = [];
    private double[] _weights = [];
    private int _regionChunkSize;

    public CampRegistry Registry { get; private set; } = new();

    /// <summary>The camp structures the grid places, after <see cref="CaptureStructures"/>.</summary>
    public IReadOnlyList<WorldGenStructure> Structures => _camps;

    public TraderCamps(ICoreServerAPI api, TradingSystem system)
    {
        _api = api;
        _system = system;
        _config = api.Assets.TryGet(new AssetLocation(SeraphHorizonsSystem.HarmonyId, "config/trading/camps.json"))
                      ?.ToObject<CampsConfig>() ?? new CampsConfig();
    }

    public void Register()
    {
        _api.Event.InitWorldGenerator(CaptureStructures, "standard");
        _api.Event.GetWorldgenBlockAccessor(provider => _blocks = provider.GetBlockAccessor(false));
        _api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.TerrainFeatures, "standard");
        _api.Event.RegisterEventBusListener(OnSpawnerSpawn, 0.5, "onattemptspawnerspawn");
        _api.Event.SaveGameLoaded += LoadRegistry;
        _api.Event.GameWorldSave += SaveRegistry;
    }

    private void LoadRegistry()
    {
        try
        {
            var data = _api.WorldManager.SaveGame.GetData(RegistryKey);
            if (data != null)
                Registry = new CampRegistry(JsonConvert.DeserializeObject<List<CampRecord>>(System.Text.Encoding.UTF8.GetString(data)) ?? []);
        }
        catch (Exception e)
        {
            _api.Logger.Error("[seraphhorizons] Trading: could not read the saved trader camps, starting over: {0}", e.Message);
        }
    }

    private void SaveRegistry()
    {
        string json = JsonConvert.SerializeObject(Registry.Snapshot());
        _api.WorldManager.SaveGame.StoreData(RegistryKey, System.Text.Encoding.UTF8.GetBytes(json));
    }

    private void CaptureStructures()
    {
        if (!_system.GridActive) return;
        _gen = _api.ModLoader.GetModSystem<GenStructures>();
        if (StructuresConfigField?.GetValue(_gen) is not WorldGenStructuresConfig scfg || scfg.Structures is null || TryGenerateMethod is null)
        {
            _api.Logger.Warning("[seraphhorizons] Trading: the game's GenStructures looks different (no scfg or TryGenerate); trader camps are left to the game");
            _system.DisableGrid("the game's structure generator looks different");
            return;
        }
        _regionChunkSize = _api.WorldManager.RegionSize / GlobalConstants.ChunkSize;
        var keep = new List<WorldGenStructure>();
        var camps = new List<WorldGenStructure>();
        int dropped = 0, settlement = 0;
        foreach (var s in scfg.Structures)
        {
            if (_config.DropGroups.Contains(s.Group)) dropped++;
            else if (!_config.CampGroups.Contains(s.Group)) keep.Add(s);
            else if (s.Schematics?.Any(loc => _config.SettlementSchematics.Any(p => WildcardUtil.Match(new AssetLocation(p), loc))) == true) settlement++;
            else
            {
                s.MinGroupDistance = 0;
                camps.Add(s);
            }
        }
        scfg.Structures = keep.ToArray();
        _camps = camps.ToArray();
        _weights = _camps.Select(s => (double)Math.Max(0, s.Chance)).ToArray();
        _api.Logger.Notification("[seraphhorizons] Trading: {0} camp structures on the grid ({1}), {2} kept for settlements, {3} dropped",
            _camps.Length, string.Join(", ", _camps.Select(s => s.Code).Distinct()), settlement, dropped);
    }

    private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
    {
        if (!_system.GridActive || _camps.Length == 0 || _blocks is null || _gen is null || !TerraGenConfig.GenerateStructures) return;
        var grid = _system.Grid;
        if (grid is null) return;
        var cell = TraderGrid.CellOfChunk(request.ChunkX, request.ChunkZ);
        var spots = grid.Spots(cell);
        foreach (int attempt in grid.AttemptsInChunk(cell, request.ChunkX, request.ChunkZ))
        {
            if (Registry.OnChunk(cell, attempt, spots.Count) != AttemptVerdict.Try) continue;
            try
            {
                if (!TryPlace(request, grid, cell, attempt, spots[attempt]))
                    Registry.Missed(cell, attempt, spots.Count);
            }
            catch (Exception e)
            {
                _api.Logger.Error("[seraphhorizons] Trading: placing the camp of cell {0} failed: {1}", cell, e);
                Registry.Missed(cell, attempt, spots.Count);
            }
        }
    }

    private bool TryPlace(IChunkColumnGenerateRequest request, TraderGrid grid, CellKey cell, int attempt, Spot spot)
    {
        // Story locations (and their exclusion zones) keep other structures out, as in GenStructures.
        if (_gen!.GetIntersectingStructure(spot.X, spot.Z, ModStdWorldGen.StructuresHashCode) != null) return false;
        var mapChunk = request.Chunks[0].MapChunk;
        var heights = mapChunk.WorldGenTerrainHeightMap;
        int sea = _api.World.SeaLevel, top = _api.WorldManager.MapSizeY - 15;
        var region = mapChunk.MapRegion;
        var (climate, forest) = ClimateCorners(region, request.ChunkX, request.ChunkZ);
        var order = grid.StructureOrder(cell, attempt, _weights);
        int baseX = request.ChunkX * GlobalConstants.ChunkSize, baseZ = request.ChunkZ * GlobalConstants.ChunkSize;
        _blocks!.BeginColumn();
        int tried = 0;
        foreach (var (lx, lz) in grid.PositionsInChunk(spot))
        {
            int height = heights[lz * 32 + lx];
            // Shallow-water camps stand a little below sea level.
            if (height < sea - 4 || height >= top || !Flat(heights, lx, lz, height)) continue;
            if (++tried > MaxPositions) break;
            int forestAt = GameMath.BiLerpRgbColor(lx / 32f, lz / 32f, forest[0], forest[1], forest[2], forest[3]);
            var pos = new BlockPos(baseX + lx, height, baseZ + lz);
            foreach (int i in order)
            {
                var structure = _camps[i];
                bool placed = (bool)TryGenerateMethod!.Invoke(structure,
                    [_blocks, _api.World, pos.Copy(), climate[0], climate[1], climate[2], climate[3], forestAt, null])!;
                if (!placed) continue;
                Record(region, structure);
                var location = structure.LastPlacedSchematicLocation;
                var centre = new BlockPos(location.CenterX, location.Y1, location.CenterZ);
                var where = RegionProbe.At(_blocks, centre, _system.Classifier);
                string name = (structure.LastPlacedSchematic?.FromFile?.GetNameWithDomain() ?? "") + "/" + structure.Code;
                Registry.Placed(cell, attempt, grid.TypeOf(cell), centre.X, location.Y1, centre.Z, where.ToString(), name);
                return true;
            }
        }
        return false;
    }

    /// <summary>Flat positions tried per spot, at most: each costs a TryGenerate per camp structure.</summary>
    private const int MaxPositions = 48;

    // The game's surface placement wants the terrain under the schematic's corners at one height;
    // camp schematics are 8 to 16 blocks across. Within the chunk's own heightmap: a quick filter
    // before asking the game.
    private static bool Flat(ushort[] heights, int lx, int lz, int height)
    {
        foreach (int d in FlatProbe)
        {
            int x = lx + d, z = lz + d;
            if (x < 32 && heights[lz * 32 + x] != height) return false;
            if (z < 32 && heights[z * 32 + lx] != height) return false;
            if (x < 32 && z < 32 && heights[z * 32 + x] != height) return false;
        }
        return true;
    }

    private static readonly int[] FlatProbe = [4, 8, 12];

    /// <summary>The four climate and forest map values around a chunk, as GenStructures reads them.</summary>
    private (int[] Climate, int[] Forest) ClimateCorners(IMapRegion region, int chunkX, int chunkZ)
    {
        int rx = chunkX % _regionChunkSize, rz = chunkZ % _regionChunkSize;
        var cm = region.ClimateMap;
        float cf = (float)cm.InnerSize / _regionChunkSize;
        int[] climate =
        [
            cm.GetUnpaddedInt((int)(rx * cf), (int)(rz * cf)),
            cm.GetUnpaddedInt((int)(rx * cf + cf), (int)(rz * cf)),
            cm.GetUnpaddedInt((int)(rx * cf), (int)(rz * cf + cf)),
            cm.GetUnpaddedInt((int)(rx * cf + cf), (int)(rz * cf + cf)),
        ];
        var fm = region.ForestMap;
        float ff = (float)fm.InnerSize / _regionChunkSize;
        int[] forest =
        [
            fm.GetUnpaddedInt((int)(rx * ff), (int)(rz * ff)),
            fm.GetUnpaddedInt((int)(rx * ff + ff), (int)(rz * ff)),
            fm.GetUnpaddedInt((int)(rx * ff), (int)(rz * ff + ff)),
            fm.GetUnpaddedInt((int)(rx * ff + ff), (int)(rz * ff + ff)),
        ];
        return (climate, forest);
    }

    private void Record(IMapRegion region, WorldGenStructure structure)
    {
        var location = structure.LastPlacedSchematicLocation;
        string code = (structure.LastPlacedSchematic?.FromFile?.GetNameWithDomain() ?? "") + "/" + structure.Code;
        region.AddGeneratedStructure(new GeneratedStructure
        {
            Code = code,
            Group = CampGroup,
            Location = location.Clone(),
            SuppressTreesAndShrubs = structure.SuppressTrees,
            SuppressRivulets = structure.SuppressWaterfalls,
        });
        if (structure.BuildProtected)
            _api.World.Claims.Add(new LandClaim
            {
                Areas = [location.Clone()],
                Description = structure.BuildProtectionDesc,
                ProtectionLevel = structure.ProtectionLevel,
                LastKnownOwnerName = structure.BuildProtectionName,
                AllowUseEveryone = structure.AllowUseEveryone,
                AllowTraverseEveryone = structure.AllowTraverseEveryone,
            });
    }

    private void OnSpawnerSpawn(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (!_system.GridActive || data is not TreeAttribute tree || _system.Grid is not { } grid) return;
        string? code = tree.GetString("type");
        if (code is null || !IsOtherTrader(code)) return;
        var pos = tree.GetBlockPos("pos");
        if (pos is null) return;
        string type = grid.TypeOf(TraderGrid.CellOf(pos.X, pos.Z));
        var climate = _api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
        string outfit = climate is null ? "temperate" : TraderTypes.OutfitClimate(climate.Temperature, climate.Rainfall);
        string gender = code.Contains("-female") ? "female" : code.Contains("-male") ? "male" : ((pos.X ^ pos.Z) & 1) == 0 ? "male" : "female";
        tree.SetString("type", $"{SeraphHorizonsSystem.HarmonyId}:{TraderTypes.EntityPath(gender, type, outfit)}");
    }

    /// <summary>A trader entity code that is not the pack's: vanilla's, Culinary Artillery's,
    /// Domestic Animal Trader's (by entity class EntityTrader or a trader code).</summary>
    private bool IsOtherTrader(string code)
    {
        lock (_traderCodes)
        {
            if (_traderCodes.TryGetValue(code, out bool known)) return known;
            var loc = new AssetLocation(code);
            var props = _api.World.GetEntityType(loc);
            bool trader = loc.Domain != SeraphHorizonsSystem.HarmonyId
                          && props != null
                          && (props.Class == "EntityTrader" || loc.Path.StartsWith("trader-") || loc.Path.Contains("-trader-"));
            _traderCodes[code] = trader;
            return trader;
        }
    }
}
