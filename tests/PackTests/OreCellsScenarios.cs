using System.Text;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Ore/ (epic #435): ore cells (#438), no surface copper (#440), smaller
/// deposits (#439) and rarer hydrothermal districts (#441), all on (the defaults) in a new standard
/// world with a fixed seed. Generating enough ore to measure deposits is far too slow here: sizes
/// and spacing are the survey tool's job (#458). This checks that each change is bound to the
/// loaded pack: the rule replaces Interesting Ore Gen's spacing filter, answers by the seed and
/// places a deposit at a generated anchor, the patches and the scaling reached the deposits the
/// game loaded, and the world recorded its settings.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class OreCellsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 424242;

    private OreSystem Ore => World.Api.ModLoader.GetModSystem<OreSystem>();

    private OreCellPlacement Placement => Ore.Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");

    private IEnumerable<DepositVariant> Deposits =>
        World.Api.ModLoader.Systems.OfType<GenDeposits>().SelectMany(g => g.Deposits ?? []);

    /// <summary>The Interesting Ore Gen vein variants with tries of an ore.</summary>
    private IEnumerable<DepositVariant> IogVeins(string code) =>
        Deposits.Where(v => v.Code == code && v.TriesPerChunk > 0
                            && OreCellPlacement.GeneratorType!.IsInstanceOfType(v.GeneratorInst));

    private static float Attribute(DepositVariant variant, string name) =>
        variant.Attributes[name]["avg"].AsFloat(float.NaN);

    private static NatFloat Field(DepositVariant variant, string name) =>
        (NatFloat)AccessTools.Field(variant.GeneratorInst.GetType(), name).GetValue(variant.GeneratorInst)!;

    private async Task<string> Run(string command)
    {
        var result = await World.ExecuteCommand(command);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}:\n{result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        return result.Message ?? "";
    }

    [AtlasScenario]
    public void The_switches_are_read_and_recorded_for_the_new_world()
    {
        var config = World.Api.LoadModConfig(SeraphHorizonsSystem.ConfigFile);
        foreach (var key in new[] { "OreCells", "NoSurfaceCopper", "SmallerDeposits", "RarerDistricts" })
            Assert.True(config[key].AsBool(false), $"{key} is not on in the config");
        Assert.Equal(5000, config["OreCellSizeMetres"].AsInt(0));

        Assert.True(Ore.NewWorld);
        var world = Ore.World;
        Assert.True(world.OreCells && world.NoSurfaceCopper && world.SmallerDeposits && world.RarerDistricts);
        Assert.Equal(5000, world.CellSize);

        var saved = ((ICoreServerAPI)World.Api).WorldManager.SaveGame.GetData(OreSystem.WorldRecordKey);
        Assert.NotNull(saved);
        Assert.Equal(world, OreWorldRecord.Parse(Encoding.UTF8.GetString(saved)));
    }

    [AtlasScenario]
    public void The_rule_replaces_Interesting_Ore_Gens_spacing_filter()
    {
        Assert.Null(OreCellPlacement.Unsupported(World.Api));
        var approve = OreCellPlacement.ApproveMethod;
        Assert.NotNull(approve);
        var patches = Harmony.GetPatchInfo(approve);
        Assert.NotNull(patches);
        Assert.Contains(patches.Prefixes, p => p.owner == OreSystem.HarmonyId);
        Assert.Equal(Seed, Placement.Cells.Seed);
        Assert.Contains("copper", Placement.Managed);
        Assert.Contains("iron", Placement.Managed);
        Assert.Contains("coal", Placement.Managed);
        // Gems, quartz and olivine stay with IOG's own rule.
        Assert.DoesNotContain(Placement.Managed, m => m is "quartz" or "olivine");
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Ore cells", StringComparison.Ordinal)
                        || e.Message.Contains("Smaller deposits", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary>
    /// #1012: the game's deposit depth noise is built at the scale a fresh process has, not at the
    /// quarter scale an earlier server in the process left behind. Atlas boots every class in one
    /// process, so this checks the fix wherever this class is not a test host's first.
    /// </summary>
    [AtlasScenario]
    public void Deposit_depths_are_drawn_as_in_a_fresh_process()
    {
        var gen = World.Api.ModLoader.Systems.OfType<GenDeposits>().First(g => g.GetType() == typeof(GenDeposits));
        int seed = ((ICoreServerAPI)World.Api).WorldManager.Seed;
        foreach (var (field, offset) in new[] { ("verticalDistortTop", 28), ("verticalDistortBottom", 12) })
        {
            var layer = (MapLayerBase)AccessTools.Field(typeof(GenDeposits), field).GetValue(gen)!;
            // GenMaps.GetDepositVerticalDistort(seed + offset), at the fresh scale.
            var fresh = new MapLayerPerlin(seed + offset + 1, 4, 0.8f, 25 * OreSystem.FreshDepositVerticalDistortScale, 40, [0.1, 0.1, 0.1, 0.1]);
            Assert.Equal(fresh.GenLayer(7000, 7000, 64, 64), layer.GenLayer(7000, 7000, 64, 64));
        }
    }

    [AtlasScenario]
    public void Only_the_first_try_from_the_active_spots_chunk_is_approved()
    {
        // A cell far from the spawn, which nothing in this world has generated.
        var cell = new CellPos(150, 150);
        var spot = Placement.SpotsOf("copper", cell)[0];
        Assert.Equal(SpotStatus.Waiting, Placement.StatusOf("copper", cell, 0));
        var chimney = IogVeins("nativecopper").First().GeneratorInst; // taken as it comes
        var tube = IogVeins("malachite").First().GeneratorInst; // needs its centre's rock, unreadable here
        bool Approves(DepositGeneratorBase gen, int x, int z) =>
            (bool)OreCellPlacement.ApproveMethod!.Invoke(gen, [new BlockPos(x, 0, z)])!;

        int cx = spot.Chunk.X * 32, cz = spot.Chunk.Z * 32;
        if (Placement.Veins != null)
        {
            // Stable veins (#946, on in this world): no managed try is approved in TerrainFeatures.
            // The anchor's own pass records the vein (OreCellOrderScenarios checks that), which is
            // laid at PreDone; IOG never places it itself.
            Assert.False(Approves(chimney, cx + 3, cz + 7));
            Assert.False(Approves(tube, cx + 3, cz + 7));
            Assert.False(Approves(chimney, cx - 1, cz + 5));
            return;
        }
        Assert.False(Approves(chimney, cx - 1, cz + 5)); // the next chunk west
        Assert.True(Approves(chimney, cx + 3, cz + 7)); // the first copper try from the anchor
        Assert.False(Approves(chimney, cx + 20, cz + 20)); // a second one
        // The same tries seen from another chunk being generated: the first is approved again.
        Assert.False(Approves(chimney, cx + 40, cz)); // a try from elsewhere starts a new pass
        Assert.True(Approves(chimney, cx + 3, cz + 7));
        // Iron is a cell of its own.
        Assert.False(Approves(IogVeins("hematite").First().GeneratorInst, cx + 3, cz + 7));
        // When a tube's centre can't be read (no chunk there), no try of the metal is approved in
        // that pass, not a later one in its place.
        Assert.False(Approves(chimney, cx + 40, cz));
        Assert.False(Approves(tube, cx + 3, cz + 7));
        Assert.False(Approves(chimney, cx + 3, cz + 7));
    }

    /// <summary>The anchors of the spawn cell's copper, iron and tin generated in turn: each
    /// resolves (a deposit, or the next spot), and one that holds a deposit has the metal's ore in
    /// its column. With this seed and pack all three are placed, but a new pack version may move
    /// rock, so only one is required.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Generated_anchors_hold_their_deposit()
    {
        var sapi = (ICoreServerAPI)World.Api;
        await World.JoinAtSpawn("surveyor");
        var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
        int placed = 0;
        foreach (var metal in new[] { "copper", "iron", "tin" })
        {
            var cell = Placement.Cells.CellOf(metal, spawn.X, spawn.Z);
            for (int tries = 0; tries < 3; tries++)
            {
                var before = Placement.StateOf(metal, cell);
                if (before.None || before.Placed) break;
                var spot = Placement.SpotsOf(metal, cell)[before.Active];
                bool loaded = false;
                sapi.WorldManager.LoadChunkColumnPriority(spot.Chunk.X, spot.Chunk.Z,
                    new ChunkLoadOptions { OnLoaded = () => loaded = true });
                await World.Until(() => loaded, 3000);
                await World.Until(() => !Placement.StateOf(metal, cell).Equals(before)
                                        && Placement.StateOf(metal, cell) is var s && (s.Placed || s.Active != before.Active), 200);
                var after = Placement.StateOf(metal, cell);
                output.WriteLine($"{metal} cell {cell} spot {spot.Index} at {spot.X}, {spot.Z}: placed {after.Placed}, active {after.Active}");
                Assert.True(after.Placed || after.Active > before.Active, $"{metal} spot {spot.Index} did not resolve");
                if (!after.Placed) continue;
                placed++;
                Assert.True(ColumnHasOre(sapi, spot.Chunk, metal), $"{metal} placed but its ore is not in chunk {spot.Chunk}");
                break;
            }
        }
        Assert.True(placed > 0, "no deposit placed for copper, iron or tin");
        Assert.Contains("deposit placed", await Run($"/sh ore cell {spawn.X} {spawn.Z} copper")
                                          + await Run($"/sh ore cell {spawn.X} {spawn.Z} iron")
                                          + await Run($"/sh ore cell {spawn.X} {spawn.Z} tin"));
    }

    private static bool ColumnHasOre(ICoreServerAPI sapi, ChunkPos chunk, string metal)
    {
        for (int cy = 0; cy < sapi.WorldManager.MapSizeY / 32; cy++)
        {
            if (sapi.WorldManager.GetChunk(chunk.X, cy, chunk.Z) is not { } c) continue;
            c.Unpack();
            for (int i = 0; i < 32 * 32 * 32; i++)
            {
                int id = c.Data.GetBlockIdUnsafe(i);
                if (id > 0 && sapi.World.Blocks[id]?.Code is { } code
                    && OreMetals.MetalOf(OreMetals.OreOfBlockPath(code.Path)) == metal)
                    return true;
            }
        }
        return false;
    }

    [AtlasScenario]
    public async Task Ore_cell_command_answers_from_the_seed()
    {
        int x = 750_123, z = 760_456;
        var first = await Run($"/sh ore cell {x} {z} copper");
        var again = await Run($"/sh ore cell {x} {z} copper");
        Assert.Equal(first, again);

        var cells = new OreCells(Seed);
        var cell = cells.CellOf("copper", x, z);
        Assert.Contains($"cell {cell.X}, {cell.Z}, 5000 m square, world seed {Seed}", first);
        foreach (var spot in cells.Spots("copper", cell))
            Assert.Contains($"Spot {spot.Index}: {spot.X}, {spot.Z} (chunk {spot.Chunk.X}, {spot.Chunk.Z};", first);
        Assert.Contains("Spot 0: ", first);
        Assert.Contains("active, its chunk not generated yet", first);

        var iron = await Run($"/sh ore cell {x} {z} iron");
        Assert.NotEqual(first, iron);
        var error = await World.ExecuteCommand($"/sh ore cell {x} {z} mithril");
        Assert.False(error.Ok);
    }

    [AtlasScenario]
    public async Task Ore_here_lists_every_managed_metal()
    {
        var player = await World.JoinAtSpawn("prospector");
        var result = await player.ExecuteCommand("/sh ore here");
        output.WriteLine(result.Message);
        Assert.True(result.Ok, result.Message);
        foreach (var metal in Placement.Managed)
            Assert.Contains($"\n{metal}: ", result.Message);
    }

    [AtlasScenario]
    public void Surface_copper_and_cassiterite_are_gone()
    {
        foreach (var code in new[] { "surfacecopper", "surfacecassiterite" })
        {
            var variants = Deposits.Where(v => v.Code == code).ToList();
            Assert.NotEmpty(variants);
            Assert.All(variants, v => Assert.Equal(0, v.TriesPerChunk));
        }
        // IOG's own copper veins, which carry its surface signs, are still tried.
        Assert.Contains(Deposits, v => v.Code == "nativecopper" && v.TriesPerChunk > 0);
    }

    [AtlasScenario]
    public void Veins_are_scaled_by_their_metals_factor()
    {
        // The variant's attributes keep the deposit file's values; its generator has the scaled ones.
        // Native copper's chimney, scaled on its tendrils by copper's factor in config/ore-sizes.json
        // (retuned on the census of #945), four times over with ore processing on (#863:
        // an ore item holds about a quarter of vanilla's units, so a deposit is four times the blocks).
        var chimney = IogVeins("nativecopper").Single();
        var shape = new VeinShape("chimney",
            new SizeRange(Attribute(chimney, "radius"), chimney.Attributes["radius"]["var"].AsFloat()),
            new SizeRange(Attribute(chimney, "branchCount"), chimney.Attributes["branchCount"]["var"].AsFloat()),
            new SizeRange(Attribute(chimney, "branchLength"), chimney.Attributes["branchLength"]["var"].AsFloat()));
        Assert.Equal(OreProducts.DepositBlocks, OreSystem.GradedOreBlocks(World.Api));
        var expected = VeinScaling.Scale(shape, 0.136 * OreProducts.DepositBlocks);
        output.WriteLine($"native copper chimney {shape} -> {expected}");
        Assert.NotEqual(shape, expected);
        Assert.Equal(expected.BranchCount.Avg, Field(chimney, "BranchCount").avg, 3);
        Assert.Equal(expected.BranchLength.Avg, Field(chimney, "BranchLength").avg, 3);

        // Hematite's seam, by the square root of iron's factor (#945) on the radius, four times over.
        var seam = IogVeins("hematite").Single();
        Assert.Equal(Attribute(seam, "radius") * Math.Sqrt(0.0527 * 4), Field(seam, "Radius").avg, 3);

        // Coal is not graded ore: its quarter cut only.
        var coal = IogVeins("bituminouscoal").First();
        Assert.Equal(Attribute(coal, "radius") * Math.Sqrt(0.75), Field(coal, "Radius").avg, 3);

        // Gems are left alone.
        var garnet = IogVeins("garnetpyrope").Single();
        Assert.Equal(Attribute(garnet, "radius"), Field(garnet, "Radius").avg);
    }

    /// <summary>Copper and tin ore come only from Interesting Ore Gen's veins (which the cells
    /// place) and its hydrothermal districts: no other deposit of theirs is tried.</summary>
    [AtlasScenario]
    public void No_other_generator_tries_a_managed_metal()
    {
        var others = Deposits
            .Where(v => v.TriesPerChunk > 0 && OreMetals.MetalOf(v.Code) is "copper" or "tin" or "iron" or "zinc" or "lead" or "bismuth"
                        && !OreCellPlacement.GeneratorType!.IsInstanceOfType(v.GeneratorInst))
            .Select(v => $"{v.Code} ({v.fromFile}, {v.GeneratorInst?.GetType().Name}, {v.TriesPerChunk})")
            .ToList();
        Assert.True(others.Count == 0, "Also tried:\n" + string.Join("\n", others));
    }

    /// <summary>The prospecting pick builds a second set of deposit generators on the thread pool
    /// (ProPickWorkSpace.OnLoaded). That second initAssets has to finish, scaled like worldgen's
    /// (the pick reports what generates), and the task that runs it is guarded, so a server
    /// disposed while it still runs doesn't take the process down: a test host crashed that way
    /// with an NRE in Interesting Ore Gen's IOGDepositBlock.Resolve logging to a nulled logger.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task The_prospecting_picks_deposits_initialise_and_are_scaled_too()
    {
        Assert.Null(ProPickShutdownGuard.Unsupported());
        var patches = Harmony.GetPatchInfo(ProPickShutdownGuard.Task);
        Assert.NotNull(patches);
        Assert.Contains(patches.Finalizers, p => p.owner == OreSystem.HarmonyId);

        var workspace = ObjectCacheUtil.TryGet<ProPickWorkSpace>(World.Api, "propickworkspace");
        Assert.NotNull(workspace);
        var genField = AccessTools.Field(typeof(ProPickWorkSpace), "depositGen");
        await World.Until(() => genField.GetValue(workspace) != null, 6000);
        var pickGen = (GenDeposits)genField.GetValue(workspace)!;
        Assert.Equal(Deposits.Count(), pickGen.Deposits.Length);

        var worldgen = IogVeins("nativecopper").Single();
        var pick = pickGen.Deposits.Single(v => v.Code == "nativecopper" && v.TriesPerChunk > 0
                                                && OreCellPlacement.GeneratorType!.IsInstanceOfType(v.GeneratorInst));
        Assert.NotSame(worldgen.GeneratorInst, pick.GeneratorInst);
        Assert.NotEqual(Attribute(pick, "branchCount"), Field(pick, "BranchCount").avg);
        Assert.Equal(Field(worldgen, "BranchCount").avg, Field(pick, "BranchCount").avg);
        Assert.Equal(Field(worldgen, "BranchLength").avg, Field(pick, "BranchLength").avg);
    }

    [AtlasScenario]
    public void Hydrothermal_districts_tile_the_world_in_7_km_squares()
    {
        var system = World.Api.ModLoader.Systems.First(s => s.GetType().FullName == "InterestingOreGen.Generators.HydrothermalDistrictSystem");
        var configs = (System.Collections.IEnumerable)AccessTools.Field(system.GetType(), "_configs").GetValue(system)!;
        var sizes = configs.Cast<object>()
            .Select(c => (int)AccessTools.Field(c.GetType(), "MinDistanceBetweenDistricts").GetValue(c)!)
            .ToList();
        Assert.NotEmpty(sizes);
        Assert.All(sizes, size => Assert.Equal(7000, size));
    }

    /// <summary>The rock strata predicted for a column before it generates (#946,
    /// <see cref="RockStrataPrediction"/>, a replay of <c>GenRockStrataNew.genBlockColumn</c> with the
    /// live generator's noise) are the strata the game lays there, given the column's surface height:
    /// four chunk columns generated and compared block for block (deposits and structures replace a
    /// few later). Seeds 1101, 1103 and 1105 matched 99.6–99.9 %.</summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task District_country_rock_is_predicted_from_the_strata()
    {
        var sapi = (ICoreServerAPI)World.Api;
        var strata = RockStrataPrediction.TryCreate(sapi, out var why);
        Assert.True(strata != null, why);
        var rocks = strata!.RockIds.ToHashSet();
        var rock = new int[sapi.World.BlockAccessor.MapSizeY];
        int cx0 = sapi.World.BlockAccessor.MapSizeX / 64 + 40, cz0 = sapi.World.BlockAccessor.MapSizeZ / 64 - 40;
        long same = 0, total = 0;
        foreach (var (cx, cz) in new[] { (cx0, cz0), (cx0 + 60, cz0), (cx0, cz0 + 60), (cx0 + 60, cz0 + 60) })
        {
            bool loaded = false;
            sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { OnLoaded = () => loaded = true });
            await World.Until(() => loaded, 6000);
            var map = sapi.WorldManager.GetMapChunk(cx, cz);
            Assert.NotNull(map);
            for (int lx = 0; lx < 32; lx += 4)
                for (int lz = 0; lz < 32; lz += 4)
                {
                    int x = cx * 32 + lx, z = cz * 32 + lz, h = map.WorldGenTerrainHeightMap[lz * 32 + lx];
                    strata.Column(x, z, h, rock);
                    for (int y = 1; y <= h; y++)
                    {
                        int actual = sapi.World.BlockAccessor.GetBlockId(new BlockPos(x, y, z));
                        if (!rocks.Contains(actual)) continue;
                        total++;
                        if (actual == rock[y]) same++;
                    }
                }
        }
        output.WriteLine($"strata predicted for {total} rock blocks: {(double)same / total:P2} as generated");
        Assert.True(total > 10_000, $"only {total} rock blocks");
        Assert.True(same >= 0.98 * total, $"{same} of {total} rock blocks as predicted");
    }

    /// <summary>District veins (under SmallerDeposits): a district built from the granite config,
    /// as IOG builds one when a chunk near it generates, comes out with at most the configured
    /// number of veins of each base metal, every one cut to a band of heights under sea level (one
    /// too tall for that room is the room, #943), and its gold untouched in number. With ore
    /// processing's quarter units (#863) every kept vein of a sized metal is widened four times (a
    /// shoot four times as long and no wider, #943; a ladder's slabs closer). A horsetail lens
    /// holding a sized metal (gold, silver) has its lenses twice IOG's horizontal radius and, where
    /// the predicted strata there are mostly not granite, twice its vertical one too (#946), its box
    /// around them. A felsic district (tin, bismuth, lead, silver) the same. Each band's height is
    /// grown by its fault's richness (IOG's <c>RichnessAt</c>, still there to read). No zone keeps an
    /// ore whose code names no block in granite (the geology add-on ores name basalt, #946), so
    /// neither district holds copper or iron.</summary>
    [AtlasScenario]
    public void Hydrothermal_district_veins_are_counted_and_sized()
    {
        Assert.Null(DistrictVeinSizes.Unsupported(World.Api));
        var districtType = AccessTools.TypeByName(DistrictVeinSizes.DistrictTypeName)!;
        var patches = Harmony.GetPatchInfo(AccessTools.Method(districtType, "DetectOreZones"));
        Assert.NotNull(patches);
        Assert.Contains(patches.Postfixes, p => p.owner == OreSystem.HarmonyId);
        Assert.Equal(OreProducts.DepositBlocks, OreSystem.GradedOreBlocks(World.Api));
        // A band's grades are reckoned from its fault's rich bands (#943).
        Assert.Equal(typeof(float), AccessTools.Method(AccessTools.TypeByName("InterestingOreGen.Generators.HydrothermalFaultSpline"), "RichnessAt", [typeof(int)])?.ReturnType);
        // The country rock is predicted from the strata (#946): the predictor binds, and a deep
        // column's rock is a stratum's.
        var strata = RockStrataPrediction.TryCreate(World.Api as ICoreServerAPI ?? throw new Xunit.Sdk.XunitException("no server api"), out var why);
        Assert.True(strata != null, why);
        var column = new int[World.Api.World.BlockAccessor.MapSizeY];
        strata!.Column(100_000, 100_000, World.Api.World.SeaLevel + 25, column);
        Assert.Contains(column[30], strata.RockIds);

        var system = World.Api.ModLoader.Systems.First(s => s.GetType().FullName == OreDistricts.SystemTypeName);
        var configs = ((System.Collections.IEnumerable)AccessTools.Field(system.GetType(), "_configs").GetValue(system)!).Cast<object>().ToList();
        object Config(string code) => configs.Single(c => (string)AccessTools.Field(c.GetType(), "Code").GetValue(c)! == code);
        int top = World.Api.World.BlockAccessor.MapSizeY - 1;
        int seaLevel = World.Api.World.SeaLevel;
        var oreSizes = new OreSizeTable(World.Api.Assets.Get(new AssetLocation("seraphhorizons", "config/ore-sizes.json"))
            .ToObject<Dictionary<string, OreSizeTable.Entry>>());

        foreach (var (code, sized) in new[] { ("magmatic-granitic-deep", new[] { "tin", "bismuth" }), ("magmatic-felsic-shallow", new[] { "tin", "bismuth", "lead", "silver" }) })
        {
            var config = Config(code);
            float shootRadius = (float)AccessTools.Field(config.GetType(), "OreShootRadius").GetValue(config)!;
            float radiusVariance = (float)AccessTools.Field(config.GetType(), "OreShootRadiusVariance").GetValue(config)!;
            int maxSpacing = (int)AccessTools.Field(config.GetType(), "LadderMaxSlabSpacing").GetValue(config)!;
            string host = (string)AccessTools.Field(config.GetType(), "HostMaterialCode").GetValue(config)!;
            Assert.Equal("rock-granite", host);
            float Cfg(string name) => (float)AccessTools.Field(config.GetType(), name).GetValue(config)!;
            int done = DistrictVeinSizes.DistrictsDone;
            var district = Activator.CreateInstance(districtType, new Vec2i(100_000, 100_000), Seed, 12345, 1, top, config)!;
            Assert.Equal(done + 1, DistrictVeinSizes.DistrictsDone);

            var veins = new Dictionary<string, int>();
            int gold = 0, zones = 0, banded = 0, whole = 0, shoots = 0, ladders = 0, lenses = 0;
            foreach (var zone in (System.Collections.IEnumerable)Traverse.Create(district).Property("OreZones").GetValue())
            {
                zones++;
                var t = Traverse.Create(zone);
                string kind = t.Field("ZoneType").GetValue()!.ToString()!;
                var box = t.Field("BoundingBox").GetValue<Cuboidd>();
                // The ores it places: IOG places nothing of one whose code names no block in the district's rock.
                var metals = ((System.Collections.IEnumerable)t.Field("EligibleOres").GetValue())
                    .Cast<object>()
                    .Select(Traverse.Create)
                    .Where(o => o.Field("Density").GetValue<float>() > 0
                                && DistrictVeinSizes.IngotsPerBlock(o.Field("OreCode").GetValue<string>(), host,
                                    o.Field("AllowedVariantsByInBlock").GetValue<Dictionary<string, List<string>>>()).Resolved)
                    .Select(o => OreMetals.MetalOf(DistrictVeins.OreOfCode(o.Field("OreCode").GetValue<string>())))
                    .OfType<string>().Distinct().ToList();
                if (metals.Contains("gold")) gold++;
                // IOG places nothing of an ore whose code names no block in granite: none is left (#946).
                Assert.All(((System.Collections.IEnumerable)t.Field("EligibleOres").GetValue()).Cast<object>().Select(Traverse.Create)
                        .Where(o => o.Field("Density").GetValue<float>() > 0),
                    o => Assert.True(DistrictVeinSizes.IngotsPerBlock(o.Field("OreCode").GetValue<string>(), host,
                        o.Field("AllowedVariantsByInBlock").GetValue<Dictionary<string, List<string>>>()).Resolved, o.Field("OreCode").GetValue<string>()));
                Assert.DoesNotContain("copper", metals);
                Assert.DoesNotContain("iron", metals);
                if (kind == "HorsetailLens")
                {
                    Assert.DoesNotContain("tin", metals);
                    if (!metals.Any(m => oreSizes.TargetsFor(m) is not null)) continue;
                    // Widened four times in ore (#863): the horizontal radius doubled, and the
                    // vertical one too where the lens is not mostly in granite (#943, #946).
                    foreach (var lens in (System.Collections.IEnumerable)t.Field("Sampler").Field("_lenses").GetValue())
                    {
                        var l = Traverse.Create(lens);
                        float rh = l.Field("RadiusH").GetValue<float>(), rv = l.Field("RadiusV").GetValue<float>();
                        Assert.InRange(rh, 2 * Cfg("MinHorsetailLensRadius") - 0.01, 2 * Cfg("MaxHorsetailLensRadius") + 0.01);
                        Assert.InRange(rv, Cfg("MinHorsetailLensVerticalRadius") - 0.01, 2 * Cfg("MaxHorsetailLensVerticalRadius") + 0.01);
                        int cy = l.Field("CentreY").GetValue<int>();
                        Assert.True(box.Y1 <= cy - (int)rv && box.Y2 >= cy + (int)rv, $"lens box y {box.Y1}..{box.Y2} under its lens {cy} ± {rv}");
                        Assert.True(box.X2 - box.X1 >= 2 * rh && box.Z2 - box.Z1 >= 2 * rh, $"lens box {box.X1}..{box.X2}, {box.Z1}..{box.Z2} under its radius {rh}");
                    }
                    lenses++;
                    continue;
                }
                foreach (var m in metals) veins[m] = veins.GetValueOrDefault(m) + 1;
                if (!metals.Intersect(sized).Any()) continue;
                // Widened four times (#863).
                var sampler = t.Field("Sampler");
                if (kind == "OreShoot")
                {
                    float r = sampler.Field("_halfStrike").GetValue<float>();
                    float across = sampler.Field("_halfAcross").GetValue<float>();
                    output.WriteLine($"{code} {kind} {string.Join("+", metals)}: half strike {r}, across {across}");
                    // Along the strike (#943): four times IOG's radius, and IOG's half across, half its
                    // radius; or, for a shoot too thin for the room, a wider shape of 8 to 64 times
                    // IOG's area, across at most 8 times (#946).
                    Assert.InRange(r, shootRadius - radiusVariance - 0.01, 64 * (shootRadius + radiusVariance) + 0.01);
                    Assert.InRange(across, (shootRadius - radiusVariance) / 2 - 0.01, 8 * (shootRadius + radiusVariance) / 2 + 0.01);
                    Assert.InRange(r * across, 4 * (shootRadius - radiusVariance) * (shootRadius - radiusVariance) / 2 - 1,
                        64 * (shootRadius + radiusVariance) * (shootRadius + radiusVariance) / 2 + 1);
                    Assert.True(box.X2 - box.X1 >= 2 * r && box.Z2 - box.Z1 >= 2 * r, $"{kind} box {box.X1}..{box.X2}, {box.Z1}..{box.Z2} under its strike {r}");
                    shoots++;
                }
                else
                {
                    var spacings = sampler.Field("_region").Field("SlabSpacings").GetValue<int[]>();
                    Assert.All(spacings, sp => Assert.InRange(sp, 1, (int)Math.Round(maxSpacing / 4.0, MidpointRounding.AwayFromZero)));
                    ladders++;
                }
                output.WriteLine($"{code} {kind} {string.Join("+", metals)}: y {box.Y1}..{box.Y2}");
                if (box.Y1 <= 1 && box.Y2 >= top) whole++;
                else
                {
                    Assert.True(box.Y1 >= 4 && box.Y2 <= seaLevel, $"{kind} {string.Join("+", metals)}: {box.Y1}..{box.Y2}");
                    banded++;
                }
            }
            output.WriteLine($"{code}: {zones} zones; veins {string.Join(", ", veins.Select(kv => $"{kv.Key} {kv.Value}"))}; gold zones {gold}; "
                             + $"{string.Join(" and ", sized)} veins banded {banded}, whole {whole} ({shoots} shoots, {ladders} ladders, {lenses} lenses widened)");
            Assert.Equal(0, whole);
            Assert.True(banded > 0, $"{code}: no vein banded");
            foreach (var m in sized.Where(m => m is "tin" or "bismuth" or "lead"))
                Assert.InRange(veins.GetValueOrDefault(m), code == "magmatic-granitic-deep" ? 1 : 0, 8);
            if (code == "magmatic-granitic-deep")
            {
                Assert.True(gold > 8, $"gold zones {gold}: gold keeps every lens IOG makes");
                Assert.True(lenses > 0, "no gold lens widened");
            }
        }
    }
}
