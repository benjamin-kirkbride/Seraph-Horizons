using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The stall roaster (mods-src/seraphhorizons/StallRoaster/, #925) in the export: its burn, a hand-written
/// <c>generic</c> record per sulfide rock item kind (raw ore by grade with every host rock an alternative, and crushed
/// ore; one stall-roasted rock and a fraction of a sulfur powder out), and its build as a <c>construction</c> record
/// from config/builds/stallroaster.json. It draws no power, so it is not on the power page.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Stall_roaster_burn_is_a_record_per_sulfide_rock()
    {
        var type = Doc["recipeTypes"]![RecipeSection.StallRoasterType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal("StallRoasterSettings", (string)type["registry"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.StallRoasterType));

        var raw = Recipe("stallroasting|game:ore-rich-galena-*|0");
        Json("""[{ "code": "game:ore-rich-galena-*", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:stallroaster-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            raw["ingredients"]!);
        Assert.True(raw["variants"]![0]!["ingredients"]![0]!.Count() > 1, "raw galena's host rocks are alternatives");
        var outputs = raw["outputs"]!;
        Assert.Equal("seraphhorizons:stallroastedore-galena-rich", (string)outputs[0]!["code"]!);
        Assert.Equal(1, (double)outputs[0]!["quantity"]!);
        // a rich galena's 7 units of lead, 0.155 sulfur a unit, a third of it, 5 a powder
        Assert.Equal("game:powder-sulfur", (string)outputs[1]!["code"]!);
        Near(7 * 0.155 / 3 / 5, outputs[1]!["quantity"]);
        Near(0.92, raw["extra"]!["share"]);
        Assert.Equal(32, (int)raw["extra"]!["charge"]!);
        Near(40, raw["extra"]!["hours"]);

        var crushed = Recipe("stallroasting|seraphhorizons:crushedore-chalcopyrite-medium|0");
        Assert.Equal("crushed", (string)crushed["extra"]!["form"]!);
        Assert.Equal("seraphhorizons:stallroastedore-chalcopyrite-medium", (string)crushed["outputs"]![0]!["code"]!);
        // no oxide, and nothing of a sulfide's ground ore
        Assert.DoesNotContain(Doc["recipes"]!, x => (string)x["type"]! == RecipeSection.StallRoasterType
            && ((string)x["id"]!).Contains("hematite"));
        Assert.DoesNotContain(Doc["recipes"]!, x => (string)x["type"]! == RecipeSection.StallRoasterType
            && ((string)x["id"]!).Contains("groundore"));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Stall_roaster_build_is_a_construction_record_of_its_two_stages()
    {
        var r = Recipe("construction|seraphhorizons:stallroaster-north|0");
        Assert.Equal("stallroaster", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the brickwork and the lining
        Assert.Equal(3, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:refractorybrick-fired-tier3", codes);
        Assert.Contains("game:clay-fire", codes);
        Assert.Null(Doc["power"]!["consumers"]!.FirstOrDefault(c => (string)c["id"]! == "seraphhorizons:stallroaster"));
    }
}
