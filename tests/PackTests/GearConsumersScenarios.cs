using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using Xunit.Abstractions;
using static SeraphHorizons.PackTests.GearConsumerUses;

namespace SeraphHorizons.PackTests;

/// <summary>What the <c>GearConsumers</c> scenarios expect: the uses still exempt, the patched recipes
/// and how to read a gear code.</summary>
internal static class GearConsumerUses
{
    /// <summary>What still takes a rusty gear, by output: the uses test_gear_consumers.py exempts.</summary>
    internal static readonly string[] ExemptOutputs =
    [
        "game:clothes-neck-gear-amulet-rusty", // the gear on a string; uncrafts back into the gear
        "game:dye-gray", "game:dye-black", // rust as pigment
        "cartwrightscaravan:cartsign-single-oak-wood-rustygear", // a sign showing the gear
    ];

    /// <summary>The recipe files those exempt uses are in, as the export names them.</summary>
    internal static readonly string[] ExemptSources =
    [
        "game:recipes/grid/clothes/neck.json", "game:recipes/barrel/dye/gray.json", "game:recipes/barrel/dye/black.json",
        "cartwrightscaravan:recipes/grid/signs.json",
        // Gear reclamation's first step boils the rusty gears themselves: salvage, not a machine part.
        "seraphhorizons:recipes/cooking/gear-degrease.json",
    ];

    /// <summary>Recipes that take the stainless gear, by the start of their output's code, and how many a
    /// slot takes (patches/gearconsumers-*.json).</summary>
    internal static readonly (string Output, int PerSlot)[] SteelGearRecipes =
    [
        ("ppex:enginecornish-north", 4), ("ppex:enginewatt-north", 2), ("ppex:enginefluidpump-north", 1),
        ("ppex:manualfluidpump-north", 2), ("ppex:enginempgenerator-north", 2),
        // ppex's valve and pressure valve (pipes.json /7, /8) take the stainless gear too, but UnifiedPipes,
        // on here, switches both off: its bronze valves take none (UnifiedPipesScenarios.cs).
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

    internal static IEnumerable<string?> Codes(GridRecipe r) =>
        (r.Ingredients?.Values.Select(i => i.Code?.ToString()) ?? [])
        .Concat(r.ResolvedIngredients?.Where(i => i != null).Select(i => i.Code?.ToString()) ?? []);

    internal static IEnumerable<string> ExportedCodes(JObject record) =>
        record["ingredients"]!.Select(i => (string?)i["code"])
            .Concat(record["variants"]!.SelectMany(v => v["ingredients"]!).SelectMany(slot => slot).Select(s => (string?)s["code"]))
            .OfType<string>();
}

/// <summary>
/// <c>GearConsumers</c> (#473), on as it is by default: every recipe that took a rusty gear (or one of
/// ppex's anvil gears) takes the stainless gear, ppex's anvil gears are gone, and smex's Bessemer converter
/// is raised with the stainless large gear. Read from the game's registries here, and from the recipe export in
/// <see cref="RecipeExportScenarios"/> (RecipeExportGearChainScenarios.cs).
/// The off check is in <see cref="SwitchesOffScenarios"/>; <c>tools/tests/test_gear_consumers.py</c>
/// holds the patch files to every locked mod's recipe files.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario, ReadsBootLog]
    public void Every_gear_patch_applies_and_resolves()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("gearconsumers", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("gear-stainless", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("Steelmaking Expanded changed", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.NotNull(W.GetItem(new AssetLocation(GearConsumers.StainlessGear)));
        Assert.NotNull(W.GetItem(new AssetLocation(GearConsumers.StainlessLargeGear)));
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
                .SelectMany(r => r.ResolvedIngredients?.Where(i => i?.Code?.ToString() == GearConsumers.StainlessGear) ?? [])
                .ToList();
            output.WriteLine($"{prefix}: {recipes.Count} recipes, {steel.Count} stainless gear slots");
            Assert.True(steel.Count > 0, $"no registered recipe for {prefix} takes {GearConsumers.StainlessGear}");
            Assert.All(steel, i => Assert.Equal(perSlot, i.Quantity));
            Assert.All(steel, i => Assert.Equal(GearConsumers.StainlessGear, i.ResolvedItemStack?.Collectible?.Code?.ToString()));
        }
        // Each ppex and smex machine is left with the recipe that took the rusty gear (one per metal
        // where it names one): its twin taking ppex's gears is switched off.
        foreach (var machine in new[] { "ppex:enginecornish-north", "ppex:enginewatt-north", "ppex:enginefluidpump-north",
                                        "ppex:manualfluidpump-north", "ppex:enginempgenerator-north", "smex:hopperbell",
                                        "smex:engineairblower-north", "smex:convertertransmission-north" })
        {
            var found = W.GridRecipes.Where(r => r.Output?.Code?.ToString() == machine).ToList();
            Assert.NotEmpty(found);
            // The resolved ingredients, as the loop above reads: the server drops a grid recipe's keyed
            // ones once recipes are sent to a client (SchematicRecipes.cs), so after a scenario joins a
            // player there are none.
            Assert.All(found, r => Assert.Contains(r.ResolvedIngredients ?? [], i => i?.Code?.ToString() == GearConsumers.StainlessGear));
            Assert.All(found, r => Assert.DoesNotContain(r.ResolvedIngredients ?? [], i => IsOldGear(i?.Code?.ToString())));
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
        Assert.True(Raises(GearConsumers.StainlessLargeGear));
        Assert.False(Raises("ppex:largegear-steel"));
        Assert.False(Raises("ppex:largegear-iron"));

        var converter = W.GetBlock(new AssetLocation("smex:converterbessemer-north"));
        Assert.NotNull(converter);
        var drops = converter!.GetDrops(W, World.Spawn, null, 1f);
        Assert.Contains(drops, s => s.Collectible.Code.ToString() == GearConsumers.StainlessLargeGear && s.StackSize == 1);
        Assert.DoesNotContain(drops, s => IsOldGear(s.Collectible.Code.ToString()));

        Assert.Contains("stainless large gear", Lang.GetL("en", "smex:bessemer-err-materials"));
        Assert.Contains("one stainless large gear", Lang.GetL("en", "smex:handbook-bessemer-text"));
    }
}
