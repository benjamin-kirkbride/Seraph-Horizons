using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The riddle's process (mods-src/seraphhorizons/Riddle/, #714): a hand-written <c>machine</c> record per crushed
/// ore, from the gameplay's RiddleSettings defaults, written down here (three crushed ore a charge, twelve shakes,
/// one oversize). A hand station: power <c>hand</c>, the shakes (work in shakes), the mesh kept, no wear and no
/// oil; out, two classified crushed ore and one raw ore, the oversize, back to spalling.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Riddle_process_is_a_hand_station_record_per_crushed_ore()
    {
        var type = (JObject)Doc["recipeTypes"]![RecipeSection.RiddleType]!;
        Assert.Equal("Riddle", (string)type["name"]!);
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal("RiddleSettings", (string)type["registry"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.RiddleType).ToList();
        Assert.Equal((int)type["count"]!, records.Count);
        // every ore and grade of crushed ore
        Assert.True(records.Count >= 32 * 4, $"only {records.Count} riddle records");

        var r = Recipe("riddle|seraphhorizons:crushedore-hematite-medium|0");
        Assert.Equal("seraphhorizons", (string)r["mod"]!);
        Assert.Equal("Riddle", (string?)r["switch"]);
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:crushedore-hematite-medium", "kind": "item", "quantity": 3 }""", ingredients[0]);
        Json("""{ "code": "game:metalnailsandstrips-iron", "kind": "item", "quantity": 2, "role": "kept" }""", ingredients[1]);
        Json("""{ "code": "seraphhorizons:riddle-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[2]);
        Assert.Equal(3, ingredients.Count);
        Json("""[{ "code": "seraphhorizons:crushedore-hematite-medium", "kind": "item", "quantity": 2 }, { "code": "game:ore-medium-hematite-granite", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        Json("""{ "power": "hand", "turns": 12, "work": { "amount": 12, "unit": "shakes" }, "kept": [1] }""", r["machine"]!);
        var variant = r["variants"]![0]!;
        Assert.Equal(new[] { "game:metalnailsandstrips-iron", "game:metalnailsandstrips-meteoriciron", "game:metalnailsandstrips-steel" },
            Codes(variant["ingredients"]![1]!).Select(c => (string)c!));
        // the fines are the same crushed ore, classified
        Assert.Equal(1, (int)variant["outputs"]![0]!["attributes"]!["oreState"]!);
        // the frame's grid recipe is the riddle's too
        Assert.Equal("Riddle", (string?)Recipe("grid|seraphhorizons:recipes/grid/riddle.json|0")["switch"]);
    }
}
