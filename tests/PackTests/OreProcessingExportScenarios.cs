using Atlas.XUnit;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The recipe export with the <c>OreProcessing</c> switch on (fixtures/oreprocessing), on a server of
/// its own: a full export reads every recipe registry, which the other ore processing scenarios check
/// as the game left them.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/oreprocessing", TargetPath = "ModConfig")]
public class OreProcessingExportScenarios : AtlasScenarioBase
{
    // The recipe export with the switch on: a roasting record per sulfide, the firepit a station,
    // 0.85 of a roasted concentrate out of one concentrate; and the roasting guide page.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Roasting_is_exported()
    {
        var doc = ExportUnderTest.Get(World.Api);
        var type = doc["recipeTypes"]![RecipeSection.OreRoastingType]!;
        Assert.Equal("generic", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.OreRoastingType).ToList();
        Assert.Equal(OreProcessingSystem.Of(World.Api).Applied!.Roasting, records.Count);
        Assert.Equal(records.Count, (int)type["count"]!);
        var galena = records.Single(r => (string?)r["id"] == "oreroasting|seraphhorizons:concentrate-galena|0");
        Assert.Equal("OreProcessing", (string?)galena["switch"]);
        var ingredients = (JArray)galena["ingredients"]!;
        Assert.Equal("seraphhorizons:concentrate-galena", (string?)ingredients[0]["code"]);
        Assert.Equal(1, (double)ingredients[0]["quantity"]!);
        Assert.Equal("game:firepit-cold", (string?)ingredients[1]["code"]);
        Assert.Equal("station", (string?)ingredients[1]["role"]);
        var output = galena["outputs"]![0]!;
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", (string?)output["code"]);
        Assert.Equal(0.85, (double)output["quantity"]!, 9);
        Assert.Equal(OreRoasting.MeltingPoint, (int)galena["extra"]!["meltingPoint"]!);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.RoastingGuidePage);
    }

    // The items, guides and records ore processing adds keep the export valid.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Export_validates_against_the_schema()
    {
        var dir = Path.GetDirectoryName(typeof(OreProcessingExportScenarios).Assembly.Location)!;
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(dir, "recipe-export.schema.json")));
        using var instance = System.Text.Json.JsonDocument.Parse(ExportUnderTest.Get(World.Api).ToString(Formatting.None));
        var result = schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = (result.Details ?? new List<EvaluationResults>())
            .Where(d => d.Errors != null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Take(50).ToList();
        Assert.True(result.IsValid, "schema errors:\n" + string.Join("\n", errors));
    }
}
