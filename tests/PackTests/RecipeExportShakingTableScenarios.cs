using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The shaking table (mods-src/seraphhorizons/ShakingTable/, #921): a hand-written <c>machine</c> record per crushed
/// and ground ore (in, its concentrate out, fractional, and for an unscreened feed one middlings item, the same
/// item; 8.19 axle turns a rock from ore-processing.json's 7 an hour at 0.6), and its build as a
/// <c>construction</c> record from config/builds/shakingtable.json (the shared ore machine records, #711).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Shaking_table_washes_each_crushed_and_ground_ore_and_its_build_is_a_construction_record()
    {
        var type = Doc["recipeTypes"]![RecipeSection.ShakingTableType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.ShakingTableType).ToList();
        Assert.NotEmpty(records);
        Assert.Equal((int)type["count"]!, records.Count);

        // unscreened crushed ore: its concentrate, and the same item out as middlings
        var crushed = Recipe("shakingtable|seraphhorizons:crushedore-hematite-rich|0");
        Json("""{ "code": "seraphhorizons:shakingtable-north", "kind": "block", "quantity": 1, "role": "station" }""", crushed["ingredients"]![1]!);
        var outputs = (JArray)crushed["outputs"]!;
        Assert.Contains(outputs, o => (string)o["code"]! == "seraphhorizons:concentrate-hematite" && (double)o["quantity"]! > 0);
        Assert.Contains(outputs, o => (string)o["code"]! == "seraphhorizons:crushedore-hematite-rich" && (double)o["quantity"]! == 1);
        Assert.Equal("seraphhorizons:crushedore-hematite-rich", (string)crushed["extra"]!["middlings"]!);
        Assert.Equal("mechanical", (string)crushed["machine"]!["power"]!);
        Assert.Equal(95.5 * 0.6 / 7, (double)crushed["machine"]!["turns"]!, 4);
        Assert.Equal(0.7, (double)crushed["extra"]!["recovery"]!, 6);
        Assert.Equal(20, (double)crushed["extra"]!["water"]!, 6);

        // its build: the frame, then the four stages
        var build = Recipe("construction|seraphhorizons:shakingtable-north|0");
        Assert.Equal("seraphhorizons.OreMachine", (string)build["extra"]!["behavior"]!);
        Assert.Equal(5, ((JArray)build["construction"]!["stages"]!).Count);
    }
}
