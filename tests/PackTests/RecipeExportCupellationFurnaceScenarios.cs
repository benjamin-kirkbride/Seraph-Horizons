using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The cupellation furnace (mods-src/seraphhorizons/CupellationFurnace/, #740) in the export: its parting, a
/// hand-written <c>generic</c> record per bar it parts (each cupellation ore's base bullion, a lead ingot added for a
/// bar whose metal is not lead, and the Parkes kettle's rich lead; litharge and silver and copper bits out), and its
/// build as a <c>construction</c> record from config/builds/cupellationfurnace.json. It draws no shaft power, so it is
/// not on the power page.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cupellation_furnace_parting_is_a_record_per_bar()
    {
        var type = Doc["recipeTypes"]![RecipeSection.CupellationFurnaceType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal("CupellationFurnaceSettings", (string)type["registry"]!);
        var mine = Doc["recipes"]!.Where(x => (string)x["type"]! == RecipeSection.CupellationFurnaceType).ToList();
        Assert.Equal((int)type["count"]!, mine.Count);
        // galena's and argentiferous galena's silver-lead, argentiferous copper, cupriferous silver and rich lead
        Assert.Equal(5, mine.Count);

        var galena = Recipe("cupellationfurnace|seraphhorizons:bullion-galena-lead|0");
        Json("""[{ "code": "seraphhorizons:bullion-galena-lead", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:cupellationfurnace-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            galena["ingredients"]!);
        var outputs = galena["outputs"]!;
        Assert.Equal("seraphhorizons:litharge", (string)outputs[0]!["code"]!);
        Near(20, outputs[0]!["quantity"]);
        Assert.Equal("game:metalbit-silver", (string)outputs[1]!["code"]!);
        Near(3 * 0.95 / 5, outputs[1]!["quantity"]);
        Near(0.95, galena["extra"]!["share"]);
        Near(55, galena["extra"]!["perHour"]);

        var copper = Recipe("cupellationfurnace|seraphhorizons:bullion-tetrahedrite-copper|0");
        Assert.Contains(copper["ingredients"]!, i => (string)i["code"]! == "game:ingot-lead");
        Assert.True((bool)copper["extra"]!["leadAdded"]!);
        Assert.Contains(copper["outputs"]!, o => (string)o["code"]! == "game:metalbit-copper" && Math.Abs((double)o["quantity"]! - 20) < 1e-6);
        Assert.Contains(copper["outputs"]!, o => (string)o["code"]! == "seraphhorizons:litharge" && Math.Abs((double)o["quantity"]! - 40) < 1e-6);

        var rich = Recipe("cupellationfurnace|seraphhorizons:richlead|0");
        Assert.True((bool)rich["extra"]!["richLead"]!);
        Assert.Contains(rich["outputs"]!, o => (string)o["code"]! == "game:metalbit-silver" && Math.Abs((double)o["quantity"]! - 2) < 1e-6);
        // tin-lead and doré are not cupelled
        Assert.DoesNotContain(mine, x => ((string)x["id"]!).Contains("teallite") || ((string)x["id"]!).Contains("quartz_nativegold"));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cupellation_furnace_build_is_a_construction_record_of_its_three_stages_and_test()
    {
        var r = Recipe("construction|seraphhorizons:cupellationfurnace-north|0");
        Assert.Equal("cupellationfurnace", (string)r["extra"]!["machine"]!);
        // the frame as placed, the brickwork, the lining, the fittings, then the test (its wearing part, fitted by itself)
        Assert.Equal(5, r["construction"]!["stages"]!.Count());
        Assert.Equal("The test", (string)r["extra"]!["stages"]![3]!);
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:refractorybrick-fired-tier3", codes);
        Assert.Contains("game:metalplate-copper", codes);
        Assert.Contains("game:bonemeal", codes);
        Assert.Null(Doc["power"]!["consumers"]!.FirstOrDefault(c => (string)c["id"]! == "seraphhorizons:cupellationfurnace"));
    }
}
