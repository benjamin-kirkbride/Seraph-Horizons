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
using Vintagestory.GameContent;
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
    /// <summary>How uneven the ground under a surface camp may be (#599): highest minus lowest of the
    /// game's five samples around its footprint. 0 is the game's own rule (exactly level).</summary>
    public int SlopeTolerance { get; set; } = CampGround.DefaultTolerance;
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
/// ExecuteOrder after GenStructuresPosPass), checks whether it holds a spot of a cell still without
/// a camp, or is one of an open cell's second chances (<see cref="CampRegistry"/>, #599), and there
/// tries the camp structures in a seeded weighted order, each with every schematic and rotation in
/// a seeded order, at the spot and then the chunk's other points. The tests are the game's own
/// (<c>WorldGenStructure.TryGenerate</c> and its surface and shallow-water placements, VSEssentials
/// 1.22.7): climate and forest range, the five terrain samples, sea depth, liquids, the above- and
/// underground check positions and overlap with other structures; but a surface camp takes ground
/// whose samples differ by up to <see cref="CampsConfig.SlopeTolerance"/>, seated on their median,
/// with the terrain under it levelled (<see cref="CampGround"/>). So the camp is the kind vanilla
/// would put there. Their minimum group distance is set to 0: the grid spaces camps now. A placed
/// camp is recorded as the game records one (a generated structure of group <c>trader</c> in the
/// map region, and its land claim).</item>
/// <item>Camp schematics hold an entity spawner with vanilla trader codes. The spawner asks the
/// <c>onattemptspawnerspawn</c> event bus before every spawn (vanilla's
/// ModSystemClimateSpecificTraderTypes turns <c>-temperate</c> into <c>-cold</c>/<c>-desert</c>
/// there); this class answers with the pack's trader of the cell's type, same gender, outfit set
/// by climate as vanilla's. The spawner keeps one trader, and spawns again when it is gone. A spawner
/// inside a story structure (vanilla's treasure hunter, or a mod's) is left alone: its trader is a
/// story NPC.</item>
/// </list>
/// Only in worlds that had the grid from their first start (<see cref="TradingSystem.GridActive"/>).
/// </summary>
public sealed class TraderCamps
{
    public const string RegistryKey = "seraphhorizons:tradercamps";
    public const string CampGroup = "trader";

    private static readonly FieldInfo? StructuresConfigField = AccessTools.Field(typeof(GenStructures), "scfg");
    private static readonly FieldInfo? SchematicsField = AccessTools.Field(typeof(WorldGenStructure), "schematicDatas");
    private static readonly FieldInfo? RockRemapsField = AccessTools.Field(typeof(WorldGenStructure), "resolvedRockTypeRemaps");
    private static readonly FieldInfo? LayerBlocksField = AccessTools.Field(typeof(WorldGenStructure), "replacewithblocklayersBlockids");

    private readonly ICoreServerAPI _api;
    private readonly TradingSystem _system;
    private readonly CampsConfig _config;
    private readonly Dictionary<string, bool> _traderCodes = new();
    private IWorldGenBlockAccessor? _blocks;
    private GenStructures? _gen;
    private GenStoryStructures? _story;
    private CampKind[] _camps = [];
    private double[] _weights = [];
    private int _regionChunkSize;

    public CampRegistry Registry { get; private set; } = new();

    /// <summary>The camp structures the grid places, after <see cref="CaptureStructures"/>.</summary>
    public IReadOnlyList<WorldGenStructure> Structures => _camps.Select(c => c.Structure).ToList();

    /// <summary>The ground tolerance in use (<see cref="CampsConfig.SlopeTolerance"/>).</summary>
    public int SlopeTolerance => Math.Max(0, _config.SlopeTolerance);

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
        if (StructuresConfigField?.GetValue(_gen) is not WorldGenStructuresConfig scfg || scfg.Structures is null
            || SchematicsField is null || RockRemapsField is null || LayerBlocksField is null)
        {
            _api.Logger.Warning("[seraphhorizons] Trading: the game's GenStructures looks different (no scfg, or no schematics on its structures); trader camps are left to the game");
            _system.DisableGrid("the game's structure generator looks different");
            return;
        }
        _regionChunkSize = _api.WorldManager.RegionSize / GlobalConstants.ChunkSize;
        var keep = new List<WorldGenStructure>();
        var camps = new List<CampKind>();
        int dropped = 0, settlement = 0;
        foreach (var s in scfg.Structures)
        {
            if (_config.DropGroups.Contains(s.Group)) dropped++;
            else if (!_config.CampGroups.Contains(s.Group)) keep.Add(s);
            else if (s.Schematics?.Any(loc => _config.SettlementSchematics.Any(p => WildcardUtil.Match(new AssetLocation(p), loc))) == true) settlement++;
            else if (CampKind.Of(s) is { } kind)
            {
                s.MinGroupDistance = 0;
                camps.Add(kind);
            }
            else
            {
                // A placement the grid doesn't place (underground, underwater, ruin): out of the
                // game's random placement like the rest of the group, and not placed.
                _api.Logger.Warning("[seraphhorizons] Trading: camp structure {0} is placed {1}, which the grid doesn't place; it is left out", s.Code, s.Placement);
                dropped++;
            }
        }
        scfg.Structures = keep.ToArray();
        _camps = camps.ToArray();
        _weights = _camps.Select(c => (double)Math.Max(0, c.Structure.Chance)).ToArray();
        _api.Logger.Notification("[seraphhorizons] Trading: {0} camp structures on the grid ({1}), {2} kept for settlements, {3} dropped; ground up to {4} uneven",
            _camps.Length, string.Join(", ", _camps.Select(c => c.Structure.Code).Distinct()), settlement, dropped, SlopeTolerance);
    }

    private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
    {
        if (!_system.GridActive || _camps.Length == 0 || _blocks is null || _gen is null || !TerraGenConfig.GenerateStructures) return;
        var grid = _system.Grid;
        if (grid is null) return;
        var cell = TraderGrid.CellOfChunk(request.ChunkX, request.ChunkZ);
        var spots = grid.Spots(cell);
        // Any spot of a cell without a camp tries as its chunk generates; the first to place wins.
        foreach (int spot in grid.AttemptsInChunk(cell, request.ChunkX, request.ChunkZ))
            if (Registry.OnSpot(cell, spot, spots.Count) == AttemptVerdict.Try)
                Attempt(request, grid, cell, spot, spot, spots[spot], spots.Count);
        // Every spot missed: some of the cell's later chunks get a second chance.
        if (grid.SecondChanceChunk(request.ChunkX, request.ChunkZ) && Registry.OnSecondChance(cell) == AttemptVerdict.Try)
        {
            int round = Registry.Get(cell)?.Retries ?? 1;
            Attempt(request, grid, cell, -1, TraderGrid.Attempts + round, TraderGrid.SecondChanceSpot(request.ChunkX, request.ChunkZ), spots.Count);
        }
    }

    /// <summary>One try in a chunk: <paramref name="spot"/> is the spot's index or -1 for a second
    /// chance, <paramref name="seedAttempt"/> what seeds its orders.</summary>
    private void Attempt(IChunkColumnGenerateRequest request, TraderGrid grid, CellKey cell, int spot, int seedAttempt, Spot at, int spotCount)
    {
        try
        {
            if (TryPlace(request, grid, cell, seedAttempt, at) is { } placed)
            {
                Record(placed.Region, placed.Kind.Structure);
                var location = placed.Kind.Structure.LastPlacedSchematicLocation;
                var centre = new BlockPos(location.CenterX, location.Y1, location.CenterZ);
                var where = RegionProbe.At(_blocks!, centre, _system.Classifier);
                string name = (placed.Kind.Structure.LastPlacedSchematic?.FromFile?.GetNameWithDomain() ?? "") + "/" + placed.Kind.Structure.Code;
                Registry.Placed(cell, spot, grid.TypeOf(cell), centre.X, location.Y1, centre.Z, where.ToString(), name, placed.Slope);
                return;
            }
        }
        catch (Exception e)
        {
            _api.Logger.Error("[seraphhorizons] Trading: placing the camp of cell {0} failed: {1}", cell, e);
        }
        Registry.Missed(cell, spotCount);
    }

    private sealed record PlacedCamp(CampKind Kind, IMapRegion Region, int Slope);

    private PlacedCamp? TryPlace(IChunkColumnGenerateRequest request, TraderGrid grid, CellKey cell, int attempt, Spot spot)
    {
        // Story locations (and their exclusion zones) keep other structures out, as in GenStructures.
        if (_gen!.GetIntersectingStructure(spot.X, spot.Z, ModStdWorldGen.StructuresHashCode) != null) return null;
        var mapChunk = request.Chunks[0].MapChunk;
        int top = _api.WorldManager.MapSizeY - 15;
        var region = mapChunk.MapRegion;
        var (climate, forest) = ClimateCorners(region, request.ChunkX, request.ChunkZ);
        var order = grid.StructureOrder(cell, attempt, _weights);
        var view = new HeightView(_blocks!, request.ChunkX, request.ChunkZ);
        _blocks!.BeginColumn();
        int tried = 0;
        foreach (var (lx, lz) in grid.PositionsInChunk(spot))
        {
            int height = view.Height(lx, lz);
            if (height <= 0 || height >= top) continue;
            // Only positions where some camp's ground passes count towards the budget.
            if (!AnyGround(view, lx, lz, order)) continue;
            if (++tried > MaxPositions) break;
            int forestAt = GameMath.BiLerpRgbColor(lx / 32f, lz / 32f, forest[0], forest[1], forest[2], forest[3]);
            foreach (int i in order)
            {
                var kind = _camps[i];
                if (!kind.ClimateAllows(view.BaseX + lx, height, view.BaseZ + lz, climate, forestAt, _api.World.SeaLevel)) continue;
                foreach (var (s, r) in grid.CandidateOrder(cell, attempt, i, kind.Schematics.Length))
                    if (TryCandidate(kind, s, r, view, lx, lz, climate) is { } slope)
                        return new PlacedCamp(kind, region, slope);
            }
        }
        return null;
    }

    /// <summary>Positions tried per spot, at most, of those whose ground suits some camp: each costs
    /// every structure's schematics and rotations.</summary>
    private const int MaxPositions = 48;

    /// <summary>Whether any camp's footprint at a position has ground its placement takes, by the
    /// five samples and the sea depth alone (the quick test before the budget counts it).</summary>
    private bool AnyGround(HeightView view, int lx, int lz, int[] order)
    {
        int tolerance = SlopeTolerance, sea = _api.World.SeaLevel;
        foreach (int i in order)
        {
            var kind = _camps[i];
            foreach (var (sx, sz) in kind.Footprints)
                if (Fit(kind, view, lx, lz, sx, sz, tolerance) is { } fit && !CampGround.TooDeep(fit.Centre, sea, kind.Structure.MaxBelowSealevel))
                    return true;
        }
        return false;
    }

    private static GroundFit? Fit(CampKind kind, HeightView view, int lx, int lz, int sizeX, int sizeZ, int tolerance)
    {
        if (!CampGround.InNeighbourhood(lx, lz, sizeX, sizeZ)) return null;
        var points = CampGround.SamplePoints(lx, lz, sizeX, sizeZ);
        var samples = new int[points.Length];
        for (int i = 0; i < points.Length; i++) samples[i] = view.Height(points[i].X, points[i].Z);
        return CampGround.Fit(kind.Placement, samples, tolerance);
    }

    /// <summary>One schematic in one rotation at a position: the game's checks, then levelling and
    /// placement. The ground's slope if placed, else null.</summary>
    private int? TryCandidate(CampKind kind, int s, int r, HeightView view, int lx, int lz, int[] climate)
    {
        var (sx, sz) = kind.Footprint(s, r);
        int tolerance = SlopeTolerance, sea = _api.World.SeaLevel;
        if (Fit(kind, view, lx, lz, sx, sz, tolerance) is null) return null;
        var schematic = kind.Schematics[s][r];
        schematic.Unpack(_api, r);
        if ((schematic.SizeX, schematic.SizeZ) != (sx, sz))
        {
            (sx, sz) = (schematic.SizeX, schematic.SizeZ);
            kind.Learn(s, r, sx, sz);
        }
        if (Fit(kind, view, lx, lz, sx, sz, tolerance) is not { } fit) return null;
        if (CampGround.TooDeep(fit.Centre, sea, kind.Structure.MaxBelowSealevel)) return null;
        bool surface = kind.Placement == CampPlacement.Surface;
        int x = view.BaseX + lx, z = view.BaseZ + lz;
        var pos = new BlockPos(0);

        var liquids = surface
            ? CampGround.SurfaceLiquidChecks(x, z, sx, sz, fit.Base)
            : CampGround.ShallowLiquidChecks(x, z, sx, sz, fit.Centre);
        foreach (var (px, py, pz) in liquids)
            if (_blocks!.GetBlock(pos.Set(px, py, pz), BlockLayersAccess.Fluid).IsLiquid()) return null;
        if (!surface)
            foreach (var (cx, cz) in CampGround.SamplePoints(lx, lz, sx, sz).Skip(1))
            {
                int depth = view.Rain(cx, cz) - view.Height(cx, cz);
                if (depth < 1 || depth > 2) return null;
            }

        var start = new BlockPos(x, fit.Base + 1 + schematic.OffsetY, z);
        int Levelled(int px, int pz)
        {
            int terrain = view.Height(px - view.BaseX, pz - view.BaseZ);
            return surface ? CampGround.LevelledHeight(terrain, CampGround.InFootprint(px, pz, x, z, sx, sz), fit.Base) : terrain;
        }
        foreach (var d in schematic.AbovegroundCheckPositions ?? [])
            if (start.Y + d.Y <= Levelled(start.X + d.X, start.Z + d.Z)) return null;
        foreach (var d in schematic.UndergroundCheckPositions ?? [])
        {
            int px = start.X + d.X, py = start.Y + d.Y, pz = start.Z + d.Z;
            bool? ground = surface
                ? CampGround.LevelledGround(py, view.Height(px - view.BaseX, pz - view.BaseZ), CampGround.InFootprint(px, pz, x, z, sx, sz), fit.Base)
                : null;
            if (!(ground ?? IsGround(_blocks!.GetBlock(pos.Set(px, py, pz))))) return null;
        }
        var location = new Cuboidi(start.X, start.Y, start.Z, start.X + schematic.SizeX, start.Y + schematic.SizeY, start.Z + schematic.SizeZ);
        List<SkirtColumn>? skirt = null;
        if (surface)
        {
            var heights = new List<int>(sx * sz);
            for (int dx = 0; dx < sx; dx++)
                for (int dz = 0; dz < sz; dz++)
                    heights.Add(view.Height(lx + dx, lz + dz));
            if (!CampGround.Levellable(heights, fit.Base)) return null;
            skirt = CampGround.Skirt(lx, lz, sx, sz, fit.Base, (px, pz) => view.Reaches(px, pz) ? view.Height(px, pz) : null);
            if (skirt is null || SkirtWet(view, skirt)) return null;
            // The overlap test covers the skirt too: it cuts and fills terrain outside the schematic.
            foreach (var c in skirt)
            {
                int px = view.BaseX + c.X, pz = view.BaseZ + c.Z;
                location.X1 = Math.Min(location.X1, px);
                location.X2 = Math.Max(location.X2, px + 1);
                location.Z1 = Math.Min(location.Z1, pz);
                location.Z2 = Math.Max(location.Z2, pz + 1);
                location.Y1 = Math.Min(location.Y1, Math.Min(c.Height, c.Target));
                location.Y2 = Math.Max(location.Y2, Math.Max(c.Height, c.Target) + 1);
            }
        }
        if (WouldOverlap(start, location)) return null;
        if (surface)
        {
            Level(view, lx, lz, sx, sz, fit.Base);
            Blend(view, skirt!);
        }

        kind.Structure.LastPlacedSchematicLocation.Set(start.X, start.Y, start.Z, start.X + sx, start.Y + schematic.SizeY, start.Z + sz);
        kind.Structure.LastPlacedSchematic = schematic;
        schematic.PlaceRespectingBlockLayers(_blocks, _api.World, start, climate[0], climate[1], climate[2], climate[3],
            kind.RockRemaps, kind.LayerBlocks, Vintagestory.ServerMods.NoObf.GlobalConfig.ReplaceMetaBlocks, replaceBlockEntities: false, suppressSoilIfAirBelow: false,
            displaceWater: surface);
        return fit.Slope;
    }

    /// <summary>The game's overlap test (<c>WorldGenStructure.WouldOverlapAt</c>) over
    /// <paramref name="location"/> (the schematic's, with a surface camp's skirt): no generated
    /// structure of the map regions around intersects it, and no mod's
    /// <c>GenStructures.OnPreventSchematicPlaceAt</c> objects.</summary>
    private bool WouldOverlap(BlockPos start, Cuboidi location)
    {
        var blocks = _blocks!;
        int regionSize = blocks.RegionSize;
        int maxX = blocks.MapSizeX / regionSize, maxZ = blocks.MapSizeZ / regionSize;
        for (int rx = GameMath.Clamp(location.X1 / regionSize, 0, maxX); rx <= GameMath.Clamp(location.X2 / regionSize, 0, maxX); rx++)
            for (int rz = GameMath.Clamp(location.Z1 / regionSize, 0, maxZ); rz <= GameMath.Clamp(location.Z2 / regionSize, 0, maxZ); rz++)
                if (blocks.GetMapRegion(rx, rz)?.GeneratedStructures is { } placed && placed.Any(g => g.Location.Intersects(location)))
                    return true;
        return _gen!.WouldSchematicOverlapAt(blocks, start, location, null);
    }

    /// <summary>Levels the terrain under a footprint to <paramref name="base"/> (#599): lower columns
    /// filled with their own soil (the block under their top, the top itself put back on top),
    /// higher ones cut down with their top block put back on the cut; the heightmaps follow, so the
    /// schematic's soil layers and later passes see the new ground.</summary>
    private void Level(HeightView view, int lx, int lz, int sizeX, int sizeZ, int @base)
    {
        var pos = new BlockPos(0);
        for (int dx = 0; dx < sizeX; dx++)
            for (int dz = 0; dz < sizeZ; dz++)
                LevelColumn(view, pos, lx + dx, lz + dz, @base);
    }

    /// <summary>Levels one column (view-local <paramref name="cx"/>, <paramref name="cz"/>) to
    /// <paramref name="base"/>, as <see cref="Level"/> does.</summary>
    private void LevelColumn(HeightView view, BlockPos pos, int cx, int cz, int @base)
    {
        var blocks = _blocks!;
        int x = view.BaseX + cx, z = view.BaseZ + cz;
        int height = view.Height(cx, cz);
        var work = CampGround.Level(height, @base);
        if (!work.Fills && !work.Cuts) return;
        var surface = blocks.GetBlock(pos.Set(x, height, z), BlockLayersAccess.Solid);
        if (work.Fills)
        {
            var under = blocks.GetBlock(pos.Set(x, height - 1, z), BlockLayersAccess.Solid);
            var fill = IsGround(under) ? under : IsGround(surface) ? surface : blocks.GetBlock(view.TopRock(cx, cz));
            var cover = IsGround(surface) ? surface : fill;
            // The old top is buried: it becomes fill, and the cover goes on the new top.
            for (int y = height; y <= @base; y++)
            {
                pos.Set(x, y, z);
                blocks.SetBlock(0, pos, BlockLayersAccess.Fluid);
                blocks.SetBlock(y == @base ? cover.Id : fill.Id, pos, BlockLayersAccess.Solid);
            }
        }
        else
        {
            for (int y = work.CutFrom; y <= work.CutTo; y++)
                blocks.SetBlock(0, pos.Set(x, y, z), BlockLayersAccess.Solid);
            if (surface.BlockMaterial is EnumBlockMaterial.Soil or EnumBlockMaterial.Sand or EnumBlockMaterial.Gravel
                && IsGround(blocks.GetBlock(pos.Set(x, @base, z), BlockLayersAccess.Solid)))
                blocks.SetBlock(surface.Id, pos, BlockLayersAccess.Solid);
        }
        view.SetHeight(cx, cz, @base, work.Fills);
    }

    /// <summary>Blends the terrain around a levelled footprint into it (<see cref="CampGround.Skirt"/>):
    /// each skirt column cut or filled to its target as a footprint column is. Plants and snow on a
    /// column are cleared off it first; a column with anything else standing on it (a tree, in a
    /// neighbour chunk further along) is left as it is.</summary>
    private void Blend(HeightView view, List<SkirtColumn> skirt)
    {
        var blocks = _blocks!;
        var pos = new BlockPos(0);
        foreach (var c in skirt)
        {
            int x = view.BaseX + c.X, z = view.BaseZ + c.Z;
            if (!Clearable(blocks.GetBlock(pos.Set(x, c.Height + 1, z), BlockLayersAccess.Solid))) continue;
            for (int y = c.Height + 1; y <= c.Height + 2; y++)
                if (blocks.GetBlock(pos.Set(x, y, z), BlockLayersAccess.Solid) is { Id: not 0 } above && Clearable(above))
                    blocks.SetBlock(0, pos, BlockLayersAccess.Solid);
            LevelColumn(view, pos, c.X, c.Z, c.Target);
        }
    }

    private static bool Clearable(Block block) => block.Id == 0 || block.BlockMaterial is EnumBlockMaterial.Plant or EnumBlockMaterial.Snow;

    /// <summary>Whether liquid is in a skirt column or beside it, from its lower height to one above
    /// its higher (a fill under water, a cut that would open a bank): the camp isn't placed.</summary>
    private bool SkirtWet(HeightView view, List<SkirtColumn> skirt)
    {
        var blocks = _blocks!;
        var pos = new BlockPos(0);
        foreach (var c in skirt)
        {
            int x = view.BaseX + c.X, z = view.BaseZ + c.Z;
            for (int y = Math.Min(c.Height, c.Target); y <= Math.Max(c.Height, c.Target) + 1; y++)
                foreach (var (dx, dz) in Beside)
                    if (blocks.GetBlock(pos.Set(x + dx, y, z + dz), BlockLayersAccess.Fluid).IsLiquid()) return true;
        }
        return false;
    }

    private static readonly (int X, int Z)[] Beside = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)];

    private static bool IsGround(Block block) =>
        block.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Soil or EnumBlockMaterial.Sand or EnumBlockMaterial.Gravel;

    /// <summary>A camp structure as the grid places it: the game's structure and what its placement
    /// reads from it (its schematics in four rotations each, rock remaps, block-layer blocks), and
    /// its climate range in the game's units.</summary>
    private sealed class CampKind
    {
        public required WorldGenStructure Structure { get; init; }
        public required CampPlacement Placement { get; init; }
        public required BlockSchematicStructure[][] Schematics { get; init; }
        public Dictionary<int, Dictionary<int, int>>? RockRemaps { get; init; }
        public required int[] LayerBlocks { get; init; }
        private (int X, int Z)[][] _sizes = [];
        public (int X, int Z)[] Footprints { get; private set; } = [];
        private int _minRain, _maxRain, _minTemp, _maxTemp, _minForest, _maxForest;

        public static CampKind? Of(WorldGenStructure s)
        {
            CampPlacement placement;
            if (s.Placement == EnumStructurePlacement.Surface) placement = CampPlacement.Surface;
            else if (s.Placement == EnumStructurePlacement.Shallowwater) placement = CampPlacement.ShallowWater;
            else return null;
            if (SchematicsField!.GetValue(s) is not BlockSchematicStructure[][] schematics || schematics.Length == 0) return null;
            var kind = new CampKind
            {
                Structure = s,
                Placement = placement,
                Schematics = schematics,
                RockRemaps = RockRemapsField!.GetValue(s) as Dictionary<int, Dictionary<int, int>>,
                LayerBlocks = LayerBlocksField!.GetValue(s) as int[] ?? [],
                _minRain = (int)(s.MinRain * 255f),
                _maxRain = (int)(s.MaxRain * 255f),
                _minTemp = Climate.DescaleTemperature(s.MinTemp),
                _maxTemp = Climate.DescaleTemperature(s.MaxTemp),
                _minForest = (int)(s.MinForest * 255f),
                _maxForest = (int)(s.MaxForest * 255f),
            };
            // A quarter turn swaps a footprint's sides; the rotated copies are only sized once
            // unpacked, which Learn corrects if it ever differs.
            kind._sizes = schematics.Select(r => Enumerable.Range(0, 4)
                .Select(n => n % 2 == 0 ? (r[0].SizeX, r[0].SizeZ) : (r[0].SizeZ, r[0].SizeX)).ToArray()).ToArray();
            kind.Footprints = kind._sizes.SelectMany(x => x).Distinct().ToArray();
            return kind;
        }

        public (int X, int Z) Footprint(int schematic, int rotation) => _sizes[schematic][rotation];

        public void Learn(int schematic, int rotation, int sizeX, int sizeZ)
        {
            _sizes[schematic][rotation] = (sizeX, sizeZ);
            Footprints = _sizes.SelectMany(x => x).Distinct().ToArray();
        }

        /// <summary>The game's climate gate (<c>WorldGenStructure.TryGenerate</c>): rain, temperature
        /// and forest in range at the position, and no cold camp high above the sea.</summary>
        public bool ClimateAllows(int x, int y, int z, int[] climate, int forest, int seaLevel)
        {
            int c = GameMath.BiLerpRgbColor(x % 32 / 32f, z % 32 / 32f, climate[0], climate[1], climate[2], climate[3]);
            int rain = Climate.GetRainFall((c >> 8) & 0xFF, y);
            int temp = Climate.DescaleTemperature(Climate.GetScaledAdjustedTemperature((c >> 16) & 0xFF, y - TerraGenConfig.seaLevel));
            if (rain < _minRain || rain > _maxRain || temp < _minTemp || temp > _maxTemp || forest < _minForest || forest > _maxForest) return false;
            return !(temp < 20 && y > seaLevel + 15);
        }
    }

    /// <summary>The terrain heights a chunk's placement reads, its own and its eight neighbours'
    /// (local coordinates -32..63: the footprint and its samples are in the chunk and its +X, +Z and
    /// diagonal neighbours, a skirt may reach any): the TerrainFeatures pass runs only once all eight
    /// neighbours have finished Terrain, so their <c>WorldGenTerrainHeightMap</c>s are there.</summary>
    private sealed class HeightView
    {
        private readonly IMapChunk?[] _chunks = new IMapChunk?[9];
        public int BaseX { get; }
        public int BaseZ { get; }

        public HeightView(IBlockAccessor blocks, int chunkX, int chunkZ)
        {
            BaseX = chunkX * GlobalConstants.ChunkSize;
            BaseZ = chunkZ * GlobalConstants.ChunkSize;
            for (int i = 0; i < 9; i++) _chunks[i] = blocks.GetMapChunk(chunkX + i % 3 - 1, chunkZ + i / 3 - 1);
        }

        /// <summary>Whether a column is in the view, its chunk there.</summary>
        public bool Reaches(int lx, int lz) => Chunk(lx, lz) != null;

        private IMapChunk? Chunk(int lx, int lz) =>
            lx < -32 || lz < -32 || lx >= 64 || lz >= 64 ? null : _chunks[((lx + 32) >> 5) + ((lz + 32) >> 5) * 3];

        /// <summary>The terrain height, or 0 outside the view or for a chunk not there (which no
        /// ground test passes with).</summary>
        public int Height(int lx, int lz) => Chunk(lx, lz)?.WorldGenTerrainHeightMap[(lz & 31) * 32 + (lx & 31)] ?? 0;

        public int Rain(int lx, int lz) => Chunk(lx, lz)?.RainHeightMap[(lz & 31) * 32 + (lx & 31)] ?? 0;

        public int TopRock(int lx, int lz) => Chunk(lx, lz)?.TopRockIdMap[(lz & 31) * 32 + (lx & 31)] ?? 0;

        /// <summary>A levelled column's new terrain height; its rain height follows (raised by a
        /// fill, lowered by a cut unless something stood above the old ground).</summary>
        public void SetHeight(int lx, int lz, int height, bool filled)
        {
            if (Chunk(lx, lz) is not { } chunk) return;
            int i = (lz & 31) * 32 + (lx & 31);
            int old = chunk.WorldGenTerrainHeightMap[i];
            chunk.WorldGenTerrainHeightMap[i] = (ushort)height;
            if (filled) chunk.RainHeightMap[i] = (ushort)Math.Max(chunk.RainHeightMap[i], height);
            else if (chunk.RainHeightMap[i] <= old) chunk.RainHeightMap[i] = (ushort)height;
        }
    }

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
        if (pos is null || InStoryStructure(pos)) return;
        string type = grid.TypeOf(TraderGrid.CellOf(pos.X, pos.Z));
        var climate = _api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
        string outfit = climate is null ? "temperate" : TraderTypes.OutfitClimate(climate.Temperature, climate.Rainfall);
        string gender = code.Contains("-female") ? "female" : code.Contains("-male") ? "male" : ((pos.X ^ pos.Z) & 1) == 0 ? "male" : "female";
        tree.SetString("type", $"{SeraphHorizonsSystem.HarmonyId}:{TraderTypes.EntityPath(gender, type, outfit)}");
    }

    /// <summary>Whether a position lies inside a story structure's schematic (vanilla's treasure
    /// hunter, or one a mod adds): its traders are story NPCs, with their own dialogue, and keep
    /// their code. The schematic's own area, not its landform radius, which reaches far past it
    /// (200 blocks for the treasure hunter) and over camps the grid placed.</summary>
    private bool InStoryStructure(BlockPos pos)
    {
        _story ??= _api.ModLoader.GetModSystem<GenStoryStructures>();
        if (_story?.Structures is not { } structures) return false;
        // Set at worldgen init and by /wgen story setpos, both on the main thread, as spawners spawn.
        foreach (var (_, location) in structures)
            if (location.Location is { } area && area.Contains(pos.X, pos.Z)) return true;
        return false;
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
            bool trader = props != null && HandbookTraders.IsOtherTrader(loc.Domain, loc.Path, props.Class);
            _traderCodes[code] = trader;
            return trader;
        }
    }
}
