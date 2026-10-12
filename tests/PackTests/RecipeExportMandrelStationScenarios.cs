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
        Json("""{ "name": "Mandrel forging station", "count": 3, "shape": "machine", "registry": "MandrelStationSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.MandrelStationType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.MandrelStationType).ToList();
        Assert.Equal(new[] { "mandrelstation|game:chutesection-copper|0", "mandrelstation|game:chutesection-lead|0", "mandrelstation|game:rod-steel|0" },
            records.Select(r => (string)r["id"]!).Order());
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

    // The spring job: one hot steel rod in, the mandrel kept, the tongs worn a point a spring, the
    // station; one spring out; by hand, eight seconds of winding; the heat said in requirements.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Mandrel_station_winds_a_steel_rod_into_a_spring_record()
    {
        var r = Recipe("mandrelstation|game:rod-steel|0");
        Assert.Equal("seraphhorizons", (string)r["mod"]!);
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "game:rod-steel", "kind": "item", "quantity": 1 }""", ingredients[0]);
        Json("""{ "code": "game:rod-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[1]);
        Json("""{ "code": "game:tongs", "kind": "item", "quantity": 1, "role": "tool", "isTool": true, "toolDurabilityCost": 1 }""", ingredients[2]);
        Json("""{ "code": "seraphhorizons:mandrelstation-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[3]);
        Assert.Equal(4, ingredients.Count);
        Json("""[{ "code": "seraphhorizons:spring-steel", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        Json("""{ "power": "hand", "turns": 8, "work": { "amount": 8, "unit": "seconds" }, "kept": [1], "wear": { "ingredient": 2, "rule": "fixed" } }""", r["machine"]!);
        Assert.Contains("751", (string)r["requirements"]![0]!);
        var variant = r["variants"]![0]!;
        Assert.Equal(new[] { "game:rod-steel" }, Codes(variant["ingredients"]![0]!).Select(c => (string)c!));
        var tongs = Codes(variant["ingredients"]![2]!).Select(c => (string)c!).ToList();
        Assert.Contains("game:tongs", tongs);
        Assert.Contains("game:tongsmetal-standard-iron", tongs);
        Assert.Equal(new[] { "seraphhorizons:spring-steel" }, Codes(variant["outputs"]!).Select(c => (string)c!));
    }
}
