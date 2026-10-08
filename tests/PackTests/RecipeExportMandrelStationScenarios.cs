using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The mandrel forging station's process (mods-src/seraphhorizons/MandrelStation/): a hand-written
/// <c>machine</c> record per metal, from config/mandrelstation-rig.json and the gameplay's
/// MandrelStationSettings defaults, written down here. A hand station: power <c>hand</c>, its turns the
/// base (copper) hammer's blows, the definition's hammer, the mandrel kept, the hammer worn a point a blow, no oil; two pipe sections of the
/// metal a hollow.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Mandrel_station_process_is_a_hand_machine_record_per_metal()
    {
        Json("""{ "name": "Mandrel forging station", "count": 2, "shape": "machine", "registry": "MandrelStationSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.MandrelStationType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.MandrelStationType).ToList();
        Assert.Equal(new[] { "mandrelstation|game:chutesection-copper|0", "mandrelstation|game:chutesection-lead|0" }, records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, blows) in new[] { ("lead", 9), ("copper", 14) })
        {
            var r = Recipe($"mandrelstation|game:chutesection-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            var ingredients = (JArray)r["ingredients"]!;
            Json($$"""{ "code": "game:chutesection-{{metal}}", "kind": "item", "quantity": 1 }""", ingredients[0]);
            Json("""{ "code": "game:rod-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[1]);
            Json($$"""{ "code": "game:hammer-copper", "kind": "item", "quantity": 1, "role": "tool", "isTool": true, "toolDurabilityCost": {{blows}} }""", ingredients[2]);
            Json("""{ "code": "seraphhorizons:mandrelstation-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[3]);
            Assert.Equal(4, ingredients.Count);
            Json($$"""[{ "code": "seraphhorizons:pipesection-{{metal}}", "kind": "item", "quantity": 2 }]""", r["outputs"]!);
            Json($$"""{ "power": "hand", "turns": {{blows}}, "work": { "amount": {{blows}}, "unit": "blows" }, "kept": [1], "wear": { "ingredient": 2, "rule": "fixed" } }""", r["machine"]!);
            var variant = r["variants"]![0]!;
            Assert.Equal(new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" }, Codes(variant["ingredients"]![1]!).Select(c => (string)c!));
            var hammers = Codes(variant["ingredients"]![2]!).Select(c => (string)c!).ToList();
            Assert.Contains("game:hammer-iron", hammers);
            Assert.Contains("game:hammer-copper", hammers);
            Assert.All(hammers, h => Assert.StartsWith("game:hammer-", h));
            Assert.Equal(new[] { $"seraphhorizons:pipesection-{metal}" }, Codes(variant["outputs"]!).Select(c => (string)c!));
        }
    }
}
