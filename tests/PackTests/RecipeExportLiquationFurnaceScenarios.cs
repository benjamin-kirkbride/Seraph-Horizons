using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The liquation furnace (mods-src/seraphhorizons/LiquationFurnace/, #929) in the export: its sweating, a hand-written
/// <c>generic</c> record per tin-lead bar (teallite's and franckeite's; a tin ingot and a fraction of lead bits out), and
/// its build as a <c>construction</c> record from config/builds/liquationfurnace.json. It draws no power, so it is not
/// on the power page.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Liquation_furnace_sweating_is_a_record_per_tin_lead_bar()
    {
        var type = Doc["recipeTypes"]![RecipeSection.LiquationFurnaceType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal("LiquationFurnaceSettings", (string)type["registry"]!);
        Assert.Equal(2, (int)type["count"]!);
        Assert.Equal(2, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.LiquationFurnaceType));

        var teallite = Recipe("liquationfurnace|seraphhorizons:bullion-teallite-tin|0");
        Json("""[{ "code": "seraphhorizons:bullion-teallite-tin", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:liquationfurnace-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            teallite["ingredients"]!);
        var outputs = teallite["outputs"]!;
        Assert.Equal("game:ingot-tin", (string)outputs[0]!["code"]!);
        Near(1, outputs[0]!["quantity"]);
        // 100 units of tin carry 40 of lead, 95 % of it left on the plate, 5 a bit
        Assert.Equal("game:metalbit-lead", (string)outputs[1]!["code"]!);
        Near(100 * 0.40 * 0.95 / 5, outputs[1]!["quantity"]);
        Near(0.95, teallite["extra"]!["share"]);
        Near(1, teallite["extra"]!["hours"]);
        Assert.Equal(4, (int)teallite["extra"]!["chargeBars"]!);
        Near(0.36, teallite["extra"]!["fuelPerHour"]);

        var franckeite = Recipe("liquationfurnace|seraphhorizons:bullion-franckeite-tin|0");
        Near(100 * 0.30 * 0.95 / 5, franckeite["outputs"]![1]!["quantity"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Liquation_furnace_build_is_a_construction_record_of_its_three_stages()
    {
        var r = Recipe("construction|seraphhorizons:liquationfurnace-north|0");
        Assert.Equal("liquationfurnace", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the brickwork, the lining and the fittings
        Assert.Equal(4, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:refractorybrick-fired-tier3", codes);
        Assert.Contains("game:metalnailsandstrips-iron", codes);
        Assert.Null(Doc["power"]!["consumers"]!.FirstOrDefault(c => (string)c["id"]! == "seraphhorizons:liquationfurnace"));
    }
}
