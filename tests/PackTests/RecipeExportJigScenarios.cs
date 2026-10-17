using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The jig (mods-src/seraphhorizons/Jig/, #919) in the export: its jigging, a hand-written record per feed item
/// (crushed ore its concentrate at r 0.60, fractional, the rock going on as tailings), mechanical; its build as a
/// <c>construction</c> record from config/builds/jig.json; and its steady draw on the power page from
/// config/ore-processing.json (machines.jig), written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Jig_jigging_is_a_record_per_feed_with_its_concentrate()
    {
        var type = Doc["recipeTypes"]![RecipeSection.JigType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.JigType));
        var r = Recipe("jig|seraphhorizons:crushedore-hematite-rich|0");
        Json("""[{ "code": "seraphhorizons:crushedore-hematite-rich", "kind": "item", "quantity": 1 }, { "code": "seraphhorizons:jig-north", "kind": "block", "quantity": 1, "role": "station" }]""",
            r["ingredients"]!);
        var output = Assert.Single(r["outputs"]!);
        Assert.Equal("seraphhorizons:concentrate-hematite", (string)output["code"]!);
        Assert.True((double)output["quantity"]! is > 0 and < 1);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        // 95.5 turns an hour at speed 1, so 57.3 at 0.6, over 8 rock an hour
        Assert.Equal(7.1625, (double)r["machine"]!["turns"]!, 3);
        Assert.Equal(0.6, (double)r["extra"]!["recovery"]!, 6);
        Assert.Equal(70, (double)r["extra"]!["waterPerItem"]!);
        Assert.Equal("seraphhorizons:crushedore-hematite-rich", (string)r["extra"]!["tailings"]!);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Jig_build_is_a_construction_record_of_its_four_stages()
    {
        var r = Recipe("construction|seraphhorizons:jig-north|0");
        Assert.Equal("jig", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the four fitted stages
        Assert.Equal(5, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:metalchain-iron", codes);
        Assert.Contains("game:rod-steel", codes);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Jig_draws_its_steady_load_on_the_power_page()
    {
        var c = Consumer("seraphhorizons:jig");
        Assert.Equal("seraphhorizons:jig-north", (string)c["item"]!);
        Assert.Equal("machine", (string)c["category"]!);
        Near(0.1, c["load"]);
        Near(0.005, c["idleLoad"]);
        Assert.Null(c["loadMax"]);
    }
}
