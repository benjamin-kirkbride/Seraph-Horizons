using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// <c>GearConsumers</c> (#473), on as it is by default: every recipe that took a rusty gear (or one of
/// ppex's anvil gears) takes the steel gear, ppex's anvil gears are gone, and smex's Bessemer converter
/// is raised with the steel large gear. Read from the game's registries and from the recipe export.
/// The off check is in <see cref="SwitchesOffScenarios"/>; <c>tools/tests/test_gear_consumers.py</c>
/// holds the patch files to every locked mod's recipe files.
/// <para>Its own world, with <c>fixtures/gear-steel-placeholder</c> as an extra mod: the steel gear
/// and steel large gear items are not in seraphhorizons yet (#474, #480), and without them the
/// patched recipes would not resolve. Once they are, drop the fixture and the <c>Mods</c> argument
/// (two items of one code fail the boot), and this class can join <see cref="SharedWorldScenarios"/>.</para>
/// </summary>
[AtlasWorld(Mods = new[] { "fixtures/gear-steel-placeholder" })]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public class GearConsumersScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int ExportTimeout = 900_000;

    private IWorldAccessor W => World.Api.World;

    /// <summary>What still takes a rusty gear, by output: the uses test_gear_consumers.py exempts.</summary>
    internal static readonly string[] ExemptOutputs =
    [
        "game:clothes-neck-gear-amulet-rusty", // the gear on a string; uncrafts back into the gear
        "game:dye-gray", "game:dye-black", // rust as pigment
        "betterloot:gearpart", // change: four parts craft back into the gear
        "cartwrightscaravan:cartsign-single-oak-wood-rustygear", // a sign showing the gear
    ];

    /// <summary>The recipe files those exempt uses are in, as the export names them.</summary>
    private static readonly string[] ExemptSources =
    [
        "game:recipes/grid/clothes/neck.json", "game:recipes/barrel/dye/gray.json", "game:recipes/barrel/dye/black.json",
        "betterloot:recipes/grid/rustygearpart.json", "cartwrightscaravan:recipes/grid/signs.json",
    ];

    /// <summary>Recipes that take the steel gear, by the start of their output's code, and how many a
    /// slot takes (patches/gearconsumers-*.json).</summary>
    internal static readonly (string Output, int PerSlot)[] SteelGearRecipes =
    [
        ("ppex:enginecornish-north", 4), ("ppex:enginewatt-north", 2), ("ppex:enginefluidpump-north", 1),
        ("ppex:manualfluidpump-north", 2), ("ppex:enginempgenerator-north", 2),
        ("ppex:pipe-valve-sn-", 2), ("ppex:pipe-pressurevalve-sn-", 2),
        ("smex:hopperbell", 4), ("smex:engineairblower-north", 2), ("smex:convertertransmission-north", 16),
        ("game:glider", 1),
        ("game:jonasframes-gearbox01", 3), ("game:jonasframes-gearbox02", 5), ("game:jonasframes-oscillator01", 3),
        ("game:jonasframes-spring01", 1), ("game:jonasframes-gears01", 1), ("game:jonasframes-gears02", 5),
        ("game:jonas-lamp", 1),
        ("immersivewoodworking:sawmillcarriage", 1), ("flyingmachine:chaindrive", 1),
        ("playercorpseforked:corpsecompass", 1), ("spinningwheel:hpmoaitem-", 1),
        ("sprinklersmod:t_one_sprinkler-", 2), ("sprinklersmod:t_two_sprinkler-", 4),
    ];

    /// <summary>A rusty gear or one of ppex's gears or large gears, wildcards included.</summary>
    internal static bool IsOldGear(string? code) =>
        code != null && (code == "game:gear-rusty" || code == "game:gear-*" || code.StartsWith("ppex:gear-") || code.StartsWith("ppex:largegear-"));

    private static IEnumerable<string?> Codes(GridRecipe r) =>
        (r.Ingredients?.Values.Select(i => i.Code?.ToString()) ?? [])
        .Concat(r.ResolvedIngredients?.Where(i => i != null).Select(i => i.Code?.ToString()) ?? []);

    [AtlasScenario, ReadsBootLog]
    public void Every_gear_patch_applies_and_resolves()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("gearconsumers", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("gear-steel", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("Steelmaking Expanded changed", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.NotNull(W.GetItem(new AssetLocation(GearConsumers.SteelGear)));
        Assert.NotNull(W.GetItem(new AssetLocation(GearConsumers.SteelLargeGear)));
    }

    [AtlasScenario]
    public void No_registered_recipe_takes_a_rusty_or_ppex_gear()
    {
        var grid = W.GridRecipes
            .Where(r => !ExemptOutputs.Contains(r.Output?.Code?.ToString()) && Codes(r).Any(IsOldGear))
            .Select(r => $"grid {r.Name}: {r.Output?.Code} takes {string.Join(", ", Codes(r).Where(IsOldGear).Distinct())}");
        var barrel = World.Api.GetBarrelRecipes()
            .Where(r => !ExemptOutputs.Contains(r.Output?.Code?.ToString()) && r.Ingredients.Any(i => IsOldGear(i.Code?.ToString())))
            .Select(r => $"barrel {r.Code}: {r.Output?.Code}");
        var found = grid.Concat(barrel).ToList();
        Assert.True(found.Count == 0, string.Join("\n", found));
        // The exemptions are still there, so their list is not stale.
        foreach (var exempt in ExemptOutputs.Where(o => !o.StartsWith("game:dye-")))
            Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == exempt && Codes(r).Contains("game:gear-rusty"));
    }

    [AtlasScenario]
    public void Every_patched_recipe_takes_the_steel_gear_and_resolves()
    {
        foreach (var (prefix, perSlot) in SteelGearRecipes)
        {
            var recipes = W.GridRecipes.Where(r => r.Output?.Code?.ToString().StartsWith(prefix) == true).ToList();
            var steel = recipes
                .SelectMany(r => r.ResolvedIngredients?.Where(i => i?.Code?.ToString() == GearConsumers.SteelGear) ?? [])
                .ToList();
            output.WriteLine($"{prefix}: {recipes.Count} recipes, {steel.Count} steel gear slots");
            Assert.True(steel.Count > 0, $"no registered recipe for {prefix} takes {GearConsumers.SteelGear}");
            Assert.All(steel, i => Assert.Equal(perSlot, i.Quantity));
            Assert.All(steel, i => Assert.Equal(GearConsumers.SteelGear, i.ResolvedItemStack?.Collectible?.Code?.ToString()));
        }
        // Each ppex and smex machine is left with the recipe that took the rusty gear (one per metal
        // where it names one): its twin taking ppex's gears is switched off.
        foreach (var machine in new[] { "ppex:enginecornish-north", "ppex:enginewatt-north", "ppex:enginefluidpump-north",
                                        "ppex:manualfluidpump-north", "ppex:enginempgenerator-north", "smex:hopperbell",
                                        "smex:engineairblower-north", "smex:convertertransmission-north" })
        {
            var found = W.GridRecipes.Where(r => r.Output?.Code?.ToString() == machine).ToList();
            Assert.NotEmpty(found);
            Assert.All(found, r => Assert.Equal(GearConsumers.SteelGear, r.Ingredients!["G"].Code.ToString()));
        }
    }

    [AtlasScenario]
    public void Ppex_anvil_gears_are_not_made_and_are_hidden()
    {
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => IsOldGear(r.Output?.Code?.ToString()));
        foreach (var code in new[] { "ppex:gear-iron", "ppex:gear-steel", "ppex:largegear-iron", "ppex:largegear-steel" })
        {
            var item = W.GetItem(new AssetLocation(code));
            Assert.NotNull(item);
            Assert.False(item!.CreativeInventoryTabs is { Length: > 0 } || item.CreativeInventoryStacks is { Length: > 0 }, $"{code} is in creative");
            Assert.True(item.Attributes?["handbook"]?["exclude"].AsBool() == true, $"{code} is in the handbook");
            Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.ToString() == code);
        }
    }

    [AtlasScenario]
    public void Bessemer_converter_is_raised_with_the_steel_large_gear()
    {
        Assert.True(Harmony.HasAnyPatches(GearConsumers.HarmonyId));
        var isSpawnGear = AccessTools.Method(AccessTools.TypeByName(GearConsumers.ControlType), GearConsumers.SpawnGearMethod);
        bool Raises(string code) => (bool)isSpawnGear.Invoke(null, [new ItemStack(W.GetItem(new AssetLocation(code)))])!;
        Assert.True(Raises(GearConsumers.SteelLargeGear));
        Assert.False(Raises("ppex:largegear-steel"));
        Assert.False(Raises("ppex:largegear-iron"));

        var converter = W.GetBlock(new AssetLocation("smex:converterbessemer-north"));
        Assert.NotNull(converter);
        var drops = converter!.GetDrops(W, World.Spawn, null, 1f);
        Assert.Contains(drops, s => s.Collectible.Code.ToString() == GearConsumers.SteelLargeGear && s.StackSize == 1);
        Assert.DoesNotContain(drops, s => IsOldGear(s.Collectible.Code.ToString()));

        Assert.Contains("steel large gear", Lang.GetL("en", "smex:bessemer-err-materials"));
        Assert.Contains("one steel large gear", Lang.GetL("en", "smex:handbook-bessemer-text"));
    }

    // ------------------------------------------------------ the recipe export (what the browser shows)

    private static IEnumerable<string> ExportedCodes(JObject record) =>
        record["ingredients"]!.Select(i => (string?)i["code"])
            .Concat(record["variants"]!.SelectMany(v => v["ingredients"]!).SelectMany(slot => slot).Select(s => (string?)s["code"]))
            .OfType<string>();

    [AtlasScenario(TimeoutMs = ExportTimeout)]
    public void Export_lists_the_steel_gear_and_no_recipe_takes_a_rusty_gear()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (bool?)r["enabled"] != false).ToList();

        var old = records
            .Where(r => !ExemptSources.Contains((string?)r["source"]))
            .Where(r => !r["outputs"]!.Any(o => ExemptOutputs.Contains((string?)o["code"])))
            .Where(r => ExportedCodes(r).Any(IsOldGear))
            .Select(r => $"{r["id"]}: {string.Join(", ", ExportedCodes(r).Where(IsOldGear).Distinct())}")
            .ToList();
        Assert.True(old.Count == 0, "Still take an old gear:\n" + string.Join("\n", old));

        JObject Record(string id) => records.SingleOrDefault(r => (string)r["id"]! == id)
                                     ?? throw new Xunit.Sdk.XunitException($"no enabled recipe {id}");
        void TakesSteel(string id, int perSlot)
        {
            var r = Record(id);
            Assert.Contains(r["ingredients"]!, i => (string?)i["code"] == GearConsumers.SteelGear && (int)i["quantity"]! == perSlot);
            Assert.NotEqual(false, (bool?)r["extra"]?["resolved"]);
            Assert.All(r["variants"]!, v => Assert.Contains(GearConsumers.SteelGear,
                v["ingredients"]!.SelectMany(slot => slot).Select(s => (string?)s["code"])));
        }
        TakesSteel("grid|ppex:recipes/grid/machines.json|4", 4); // Cornish engine
        TakesSteel("grid|ppex:recipes/grid/machines.json|8", 2); // mechanical power generator
        TakesSteel("grid|ppex:recipes/grid/pipes.json|8", 2); // pressure valve
        TakesSteel("grid|smex:recipes/grid/bessemerconverter.json|1", 16); // converter transmission
        TakesSteel("grid|game:recipes/grid/glider.json|0", 1);
        TakesSteel("grid|betterruins:recipes/grid/schematic-jonasassembly/assembly.json|6", 5); // Jonas gears
        TakesSteel("grid|sprinklersmod:recipes/grid/ttwosprinklerrecipe.json|0", 4); // asset paths are lower case

        var all = doc["recipes"]!.Cast<JObject>().ToDictionary(r => (string)r["id"]!);
        foreach (var off in new[] { "grid|ppex:recipes/grid/machines.json|9", "grid|ppex:recipes/grid/pipes.json|10",
                                    "grid|smex:recipes/grid/bessemerconverter.json|3", "smithing|ppex:recipes/smithing/gear.json|0",
                                    "smithing|ppex:recipes/smithing/largegear.json|0" })
            Assert.False((bool?)all[off]["enabled"] ?? true, $"{off} is not switched off");

        // Immersive Woodworking registers the carriage itself, once per wood.
        var carriages = records.Where(r => r["outputs"]!.Any(o => (string?)o["code"] == "immersivewoodworking:sawmillcarriage")).ToList();
        Assert.NotEmpty(carriages);
        Assert.All(carriages, r => Assert.Contains(GearConsumers.SteelGear, ExportedCodes(r)));
    }
}
