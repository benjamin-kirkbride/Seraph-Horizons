using System.Text;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
        await World.JoinPlayer("surveyor");
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
        var player = await World.JoinPlayer("prospector");
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
        // Native copper's chimney, scaled by 400 / 3567 on its tendrils.
        var chimney = IogVeins("nativecopper").Single();
        var shape = new VeinShape("chimney",
            new SizeRange(Attribute(chimney, "radius"), chimney.Attributes["radius"]["var"].AsFloat()),
            new SizeRange(Attribute(chimney, "branchCount"), chimney.Attributes["branchCount"]["var"].AsFloat()),
            new SizeRange(Attribute(chimney, "branchLength"), chimney.Attributes["branchLength"]["var"].AsFloat()));
        var expected = VeinScaling.Scale(shape, 400.0 / 3567);
        output.WriteLine($"native copper chimney {shape} -> {expected}");
        Assert.NotEqual(shape, expected);
        Assert.Equal(expected.BranchCount.Avg, Field(chimney, "BranchCount").avg, 3);
        Assert.Equal(expected.BranchLength.Avg, Field(chimney, "BranchLength").avg, 3);

        // Hematite's seam, by √(400 / 4806) on the radius.
        var seam = IogVeins("hematite").Single();
        Assert.Equal(Attribute(seam, "radius") * Math.Sqrt(400.0 / 4806), Field(seam, "Radius").avg, 3);

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

    [AtlasScenario]
    public void Hydrothermal_districts_tile_the_world_in_10_km_squares()
    {
        var system = World.Api.ModLoader.Systems.First(s => s.GetType().FullName == "InterestingOreGen.Generators.HydrothermalDistrictSystem");
        var configs = (System.Collections.IEnumerable)AccessTools.Field(system.GetType(), "_configs").GetValue(system)!;
        var sizes = configs.Cast<object>()
            .Select(c => (int)AccessTools.Field(c.GetType(), "MinDistanceBetweenDistricts").GetValue(c)!)
            .ToList();
        Assert.NotEmpty(sizes);
        Assert.All(sizes, size => Assert.Equal(10000, size));
    }
}
