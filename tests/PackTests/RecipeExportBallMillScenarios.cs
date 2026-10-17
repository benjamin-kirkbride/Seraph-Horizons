using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The ball mill (mods-src/seraphhorizons/BallMill/, #915) in the export: its process, a hand-written <c>machine</c>
/// record per crushed ore (one in, one ground ore out, classified), its build as a <c>construction</c> record from
/// config/builds/ballmill.json, and its draw on the power page from config/ore-processing.json (machines.ballmill),
/// written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Ball_mill_process_is_a_machine_record_per_crushed_ore()
    {
        var type = Doc["recipeTypes"]![RecipeSection.BallMillType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal("BallMill", (string)type["registry"]!);
        var r = Recipe("ballmill|seraphhorizons:crushedore-hematite-poor|0");
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.BallMillType));
        Json("""[{ "code": "seraphhorizons:crushedore-hematite-poor", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:ballmill-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            r["ingredients"]!);
        Json("""[{ "code": "seraphhorizons:groundore-hematite-poor", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        Assert.Equal(1, (int)r["variants"]![0]!["outputs"]![0]!["attributes"]!["oreState"]!);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        // 95.5 turns an hour at speed 1, so 57.3 at 0.6, over 6 rock an hour
        Assert.Equal(9.55, (double)r["machine"]!["turns"]!, 3);
        Assert.Equal(25, (double)r["extra"]!["water"]!);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Ball_mill_build_is_a_construction_record_of_its_five_stages()
    {
        var r = Recipe("construction|seraphhorizons:ballmill-north|0");
        Assert.Equal("ballmill", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the five fitted stages
        Assert.Equal(6, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:ingot-iron", codes);
        Assert.Contains("seraphhorizons:gear-stainless", codes);
        Assert.Contains("game:spurgear-s", codes);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Ball_mill_draws_its_steady_load_on_the_power_page()
    {
        var c = Consumer("seraphhorizons:ballmill");
        Assert.Equal("seraphhorizons:ballmill-north", (string)c["item"]!);
        Assert.Equal("machine", (string)c["category"]!);
        Near(1.1, c["load"]);
        Near(0.005, c["idleLoad"]);
        Assert.Null(c["loadMax"]);
    }
}
