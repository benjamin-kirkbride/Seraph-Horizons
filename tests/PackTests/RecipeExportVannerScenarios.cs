using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The vanner (mods-src/seraphhorizons/Vanner/, #923) in the export: its washing, a hand-written <c>machine</c>
/// record per feed item (one in, its concentrate out, fractional), its build as a <c>construction</c> record from
/// config/builds/vanner.json with the belt its wearing part, and its draw on the power page from
/// config/ore-processing.json (machines.vanner), written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Vanner_washing_is_a_machine_record_per_feed()
    {
        var type = Doc["recipeTypes"]![RecipeSection.VannerType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal("Vanner", (string)type["registry"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.VannerType));
        var r = Recipe("vanner|seraphhorizons:crushedore-hematite-medium|0");
        Json("""[{ "code": "seraphhorizons:crushedore-hematite-medium", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:vanner-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            r["ingredients"]!);
        var output = Assert.Single(r["outputs"]!);
        Assert.Equal("seraphhorizons:concentrate-hematite", (string)output["code"]!);
        Assert.InRange((double)output["quantity"]!, 0.01, 1);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        // 95.5 turns an hour at speed 1, so 57.3 at 0.6, over 3 rock an hour
        Assert.Equal(19.1, (double)r["machine"]!["turns"]!, 3);
        Assert.Equal(15, (double)r["extra"]!["water"]!);
        Assert.Equal(0.8, (double)r["extra"]!["recovery"]!);
        Assert.Equal("seraphhorizons:crushedore-hematite-medium", (string)r["extra"]!["tailings"]!);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Vanner_build_is_a_construction_record_of_its_five_stages_and_its_belt()
    {
        var r = Recipe("construction|seraphhorizons:vanner-north|0");
        Assert.Equal("vanner", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the five fitted stages
        Assert.Equal(6, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("seraphhorizons:gear-stainless", codes);
        Assert.Contains("game:leather-normal-plain", codes);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Vanner_draws_its_steady_load_on_the_power_page()
    {
        var c = Consumer("seraphhorizons:vanner");
        Assert.Equal("seraphhorizons:vanner-north", (string)c["item"]!);
        Assert.Equal("machine", (string)c["category"]!);
        Near(0.08, c["load"]);
        Near(0.005, c["idleLoad"]);
        Assert.Null(c["loadMax"]);
    }
}
