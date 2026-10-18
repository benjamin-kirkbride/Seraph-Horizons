using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The reverberatory roaster (mods-src/seraphhorizons/ReverberatoryRoaster/, #736) in the export: its process, a
/// hand-written <c>machine</c> record per sulfide's concentrate (its roasted concentrate one for one) and one for native
/// sulfur rock (a fraction of sulfur powder), its build as a <c>construction</c> record from
/// config/builds/reverberatory.json, and its draw on the power page, following the bed.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Reverberatory_process_is_a_record_per_sulfide_concentrate_and_native_sulfur()
    {
        var type = Doc["recipeTypes"]![RecipeSection.ReverberatoryType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.ReverberatoryType));

        var r = Recipe("reverberatory|seraphhorizons:concentrate-chalcopyrite|0");
        Json("""[{ "code": "seraphhorizons:concentrate-chalcopyrite", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:reverberatory-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            r["ingredients"]!);
        Assert.Equal("seraphhorizons:roastedconcentrate-chalcopyrite", (string)r["outputs"]![0]!["code"]!);
        Assert.Equal(1, (double)r["outputs"]![0]!["quantity"]!);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        // 57.3 shaft turns an hour at speed 0.6 over 76 concentrate
        Near(95.5 * 0.6 / 76, r["machine"]!["turns"]);
        Near(2, r["extra"]!["water"]);
        Near(1.0 / 6, r["extra"]!["coal"]);

        // native sulfur: 13 units x 0.65 / 5 a powder; 2.6 concentrates' turns, water and coal
        var s = Recipe("reverberatory|game:ore-sulfur|0");
        Assert.Equal("game:powder-sulfur", (string)s["outputs"]![0]!["code"]!);
        Near(13 * 0.65 / 5, s["outputs"]![0]!["quantity"]);
        Near(95.5 * 0.6 / 76 * 2.6, s["machine"]!["turns"]);
        Near(5.2, s["extra"]!["water"]);
        Assert.Equal("nativesulfur", (string)s["extra"]!["form"]!);

        // no oxide's concentrate
        Assert.DoesNotContain(Doc["recipes"]!, x => (string)x["type"]! == RecipeSection.ReverberatoryType
            && ((string)x["id"]!).Contains("hematite"));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Reverberatory_build_is_a_construction_record_and_its_draw_follows_the_bed()
    {
        var r = Recipe("construction|seraphhorizons:reverberatory-north|0");
        Assert.Equal("reverberatory", (string)r["extra"]!["machine"]!);
        // the frame as placed, then its five stages
        Assert.Equal(6, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:refractorybrick-fired-tier3", codes);
        Assert.Contains("seraphhorizons:gear-stainless", codes);
        var power = Doc["power"]!["consumers"]!.Single(c => (string)c["id"]! == "seraphhorizons:reverberatory");
        Near(0.1, power["load"]);
        Near(0.5, power["loadMax"]);
    }
}
