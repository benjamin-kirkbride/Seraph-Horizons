using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The rocker's washes (mods-src/seraphhorizons/Rocker/, #716): a hand-written <c>machine</c> record per
/// feed item, the rocker as station, power <c>hand</c> at its rocks an item, the water a requirement, and
/// the concentrate at the rocker's figure of the item's units (unriddled: × 0.85).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Rocker_washes_are_a_hand_machine_record_per_feed()
    {
        var type = Doc["recipeTypes"]![RecipeSection.RockerType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal("RockerSettings", (string)type["registry"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.RockerType).ToList();
        Assert.Equal((int)type["count"]!, records.Count);
        Assert.Contains(records, r => ((string)r["id"]!).StartsWith("rocker|game:richgravel-", StringComparison.Ordinal));
        Assert.Contains(records, r => ((string)r["id"]!).StartsWith("rocker|seraphhorizons:groundore-", StringComparison.Ordinal));

        var magnetite = Recipe("rocker|seraphhorizons:crushedore-magnetite-medium|0");
        var ingredients = (JArray)magnetite["ingredients"]!;
        Json("""{ "code": "seraphhorizons:crushedore-magnetite-medium", "kind": "item", "quantity": 1 }""", ingredients[0]);
        Json("""{ "code": "seraphhorizons:rocker-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[1]);
        Assert.Equal(2, ingredients.Count);
        var output = Assert.Single((JArray)magnetite["outputs"]!);
        Assert.Equal("seraphhorizons:concentrate-magnetite", (string)output["code"]!);
        // 5 units × 0.85 × 0.55 / 5 units a concentrate
        Assert.Equal(0.4675, (double)output["quantity"]!, 6);
        Json("""{ "power": "hand", "turns": 4, "work": { "amount": 4, "unit": "rocks" } }""", magnetite["machine"]!);
        Assert.Contains("2 L", (string)magnetite["requirements"]![0]!);
    }
}
