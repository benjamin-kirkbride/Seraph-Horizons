using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.Gears;
using static SeraphHorizons.PackTests.GearConsumerUses;

namespace SeraphHorizons.PackTests;

/// <summary>
/// What the gear features' own scenarios (GearConsumers, GearBlank, GearReclamation, in
/// <see cref="SharedWorldScenarios"/>) check in the recipe export: here, so that only this class
/// builds an export and the shared world never does.
/// </summary>
public partial class RecipeExportScenarios
{
    private static readonly string[] GearMoldTypes = [GearBlanks.MoldType, GearBlanks.LargeMoldType];

    // ------------------------------------------------------ GearConsumers (GearConsumersScenarios.cs)

    [AtlasScenario(TimeoutMs = Timeout)]
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

    // ------------------------------------------------------ GearBlank (GearBlankScenarios.cs)

    // The export has both routes: the molds' clay-forming recipes and the raw mold's firing to the
    // fired one (the casting itself is the fired mold's drop, which the export does not model for any
    // mold; the blank's description says it), and the two smithing recipes.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Gear_blank_routes_are_in_the_export()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var recipes = ((JArray)doc["recipes"]!).Cast<JObject>().ToList();
        JObject Of(string type, string output) => Assert.Single(recipes, r => (string?)r["type"] == type
            && r["outputs"]!.Any(o => (string?)o["code"] == output));

        var smallSmith = Of("smithing", "seraphhorizons:gearblank-{metal}");
        var largeSmith = Of("smithing", "seraphhorizons:largegearblank-{metal}");
        foreach (var r in new[] { smallSmith, largeSmith })
        {
            Assert.Equal("seraphhorizons", (string?)r["mod"]);
            Assert.Equal("game:ingot-steel", (string?)Assert.Single(r["variants"]!)["ingredients"]![0]![0]!["code"]);
        }
        foreach (string type in GearMoldTypes)
        {
            var clay = Of("clayforming", $"seraphhorizons:toolmold-{{color}}-raw-{type}");
            Assert.Equal(3, clay["variants"]!.Count());
        }

        var items = (JObject)doc["items"]!;
        foreach (string code in new[] { GearBlanks.Blank, GearBlanks.LargeBlank })
            Assert.Contains("gear blank mold", (string?)items[code]?["description"]);
        foreach (string type in GearMoldTypes)
            Assert.Equal(GearBlankParts.Mold("blue", "fired", type),
                (string?)items[GearBlankParts.Mold("blue", "raw", type)]?["attributes"]?["smelting"]?["output"]?["code"]);
    }

    // ------------------------------------------------------ GearReclamation (GearReclamationScenarios.cs)

    [AtlasScenario(TimeoutMs = Timeout)]
    public void The_gear_recipes_export()
    {
        var doc = ExportUnderTest.Get(World.Api);
        JObject Recipe(string id) => doc["recipes"]!.Cast<JObject>().SingleOrDefault(r => (string)r["id"]! == id)
            ?? throw new Xunit.Sdk.XunitException($"no recipe {id}");
        var cook = Recipe("cooking|seraphhorizons:recipes/cooking/gear-degrease.json|1");
        Assert.Equal("cooking", (string)cook["type"]!);
        Assert.Contains(cook["outputs"]!, o => (string)o["code"]! == GearCodes.Degreased);
        var neutralize = Recipe("barrel|seraphhorizons:recipes/barrel/gear-neutralize.json|0");
        Assert.Contains(neutralize["outputs"]!, o => (string)o["code"]! == GearCodes.Neutralized);
        foreach (int i in new[] { 0, 1 })
            Assert.Contains(Recipe($"barrel|seraphhorizons:recipes/barrel/gear-oil.json|{i}")["outputs"]!,
                o => (string)o["code"]! == GearCodes.Oiled);
    }
}
