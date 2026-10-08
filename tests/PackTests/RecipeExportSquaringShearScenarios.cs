using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The squaring shear's process (mods-src/seraphhorizons/SquaringShear/): a hand-written
/// <c>machine</c> record per metal, from config/squaringshear-rig.json and the gameplay's
/// SquaringShearSettings defaults, written down here. A hand machine: power <c>hand</c>, the treadle's
/// strokes (work in strokes), no wear and no oil; two half plates of the metal a plate.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Squaring_shear_process_is_a_hand_machine_record_per_metal()
    {
        Json("""{ "name": "Squaring shear", "count": 2, "shape": "machine", "registry": "SquaringShearSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.SquaringShearType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.SquaringShearType).ToList();
        Assert.Equal(new[] { "squaringshear|game:metalplate-copper|0", "squaringshear|game:metalplate-lead|0" }, records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, strokes) in new[] { ("lead", 1.0), ("copper", 1.5) })
        {
            var r = Recipe($"squaringshear|game:metalplate-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            Assert.Equal("SquaringShear", (string?)r["switch"]);
            var ingredients = (JArray)r["ingredients"]!;
            Json($$"""{ "code": "game:metalplate-{{metal}}", "kind": "item", "quantity": 1 }""", ingredients[0]);
            Json("""{ "code": "game:metalplate-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[1]);
            Json("""{ "code": "game:rod-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[2]);
            Json("""{ "code": "seraphhorizons:squaringshear-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[3]);
            Assert.Equal(4, ingredients.Count);
            Json($$"""[{ "code": "seraphhorizons:halfplate-{{metal}}", "kind": "item", "quantity": 2 }]""", r["outputs"]!);
            Json($$"""{ "power": "hand", "turns": {{strokes}}, "work": { "amount": {{strokes}}, "unit": "strokes" }, "kept": [1, 2] }""", r["machine"]!);
            var variant = r["variants"]![0]!;
            Assert.Equal(new[] { "game:metalplate-iron", "game:metalplate-steel" }, Codes(variant["ingredients"]![1]!).Select(c => (string)c!));
            Assert.Equal(new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" }, Codes(variant["ingredients"]![2]!).Select(c => (string)c!));
            Assert.Equal(new[] { $"seraphhorizons:halfplate-{metal}" }, Codes(variant["outputs"]!).Select(c => (string)c!));
            // the half plate is the shear's own item, and the shear's switch owns it
            Assert.Equal("SquaringShear", (string?)Item($"seraphhorizons:halfplate-{metal}")["switch"]);
        }
        // the frame's grid recipe is the shear's too
        Assert.Equal("SquaringShear", (string?)Recipe("grid|seraphhorizons:recipes/grid/squaringshear.json|0")["switch"]);
    }
}
