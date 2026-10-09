using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The press brake's process (mods-src/seraphhorizons/PressBrake/): a hand-written <c>machine</c>
/// record per metal, from config/pressbrake-rig.json and the gameplay's PressBrakeSettings defaults,
/// written down here. A hand machine: power <c>hand</c>, the lever's turns, no wear and no oil; one
/// angle of the metal a half plate (the squaring shear's item; the game's plate never goes on).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Press_brake_process_is_a_hand_machine_record_per_metal()
    {
        Json("""{ "name": "Press brake", "count": 2, "shape": "machine", "registry": "PressBrakeSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.PressBrakeType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.PressBrakeType).ToList();
        Assert.Equal(new[] { "pressbrake|seraphhorizons:halfplate-copper|0", "pressbrake|seraphhorizons:halfplate-lead|0" }, records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, turns) in new[] { ("lead", 1.5), ("copper", 2.25) })
        {
            var r = Recipe($"pressbrake|seraphhorizons:halfplate-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            var ingredients = (JArray)r["ingredients"]!;
            Json($$"""{ "code": "seraphhorizons:halfplate-{{metal}}", "kind": "item", "quantity": 1 }""", ingredients[0]);
            Json("""{ "code": "game:metal-parts", "kind": "block", "quantity": 1, "role": "kept" }""", ingredients[1]);
            Json("""{ "code": "game:metalplate-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[2]);
            Json("""{ "code": "seraphhorizons:pressbrake-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[3]);
            Assert.Equal(4, ingredients.Count);
            Json($$"""[{ "code": "seraphhorizons:angle-{{metal}}", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
            Json($$"""{ "power": "hand", "turns": {{turns}}, "kept": [1, 2] }""", r["machine"]!);
            var variant = r["variants"]![0]!;
            Assert.Equal(new[] { "game:metal-parts" }, Codes(variant["ingredients"]![1]!).Select(c => (string)c!));
            Assert.Equal(new[] { "game:metalplate-iron", "game:metalplate-steel" }, Codes(variant["ingredients"]![2]!).Select(c => (string)c!));
            Assert.Equal(new[] { $"seraphhorizons:angle-{metal}" }, Codes(variant["outputs"]!).Select(c => (string)c!));
        }
    }
}
