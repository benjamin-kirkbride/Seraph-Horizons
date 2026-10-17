using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The amalgam pan's process (mods-src/seraphhorizons/AmalgamPan/, #718): a hand-written <c>machine</c>
/// record per kind of crushed gold or silver quartz (ore and grade), a full charge of 16 with its
/// mercury and water in, the amalgam and the same quartz (amalgamated) out; power <c>hand</c>, the
/// muller's turns; the muller and shoes kept.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Amalgam_pan_process_is_a_hand_machine_record_per_quartz()
    {
        Json("""{ "name": "Amalgam pan", "count": 8, "shape": "machine", "registry": "AmalgamPanSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.AmalgamPanType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.AmalgamPanType).ToList();
        Assert.Equal(8, records.Count);
        var r = Recipe("amalgampan|seraphhorizons:crushedore-quartz_nativegold-medium|0");
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:crushedore-quartz_nativegold-medium", "kind": "item", "quantity": 16 }""", ingredients[0]);
        Assert.Equal("em:mercuryportion", (string)ingredients[1]["code"]!);
        Assert.True((double)ingredients[1]["quantity"]! > 0);
        Json("""{ "code": "game:waterportion", "kind": "item", "quantity": 400, "litres": 4 }""", ingredients[2]);
        Json("""{ "code": "game:rod-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[3]);
        Json("""{ "code": "game:metalplate-iron", "kind": "item", "quantity": 1, "role": "kept" }""", ingredients[4]);
        Json("""{ "code": "seraphhorizons:amalgampan-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[5]);
        var outputs = (JArray)r["outputs"]!;
        Assert.Equal("seraphhorizons:amalgam-quartz_nativegold", (string)outputs[0]["code"]!);
        // a tenth of a litre of mercury for each amalgam
        Assert.Equal((double)outputs[0]["quantity"]! * 10, (double)ingredients[1]["quantity"]!, 4);
        Json("""{ "code": "seraphhorizons:crushedore-quartz_nativegold-medium", "kind": "item", "quantity": 16 }""", outputs[1]);
        Json("""{ "power": "hand", "turns": 16, "work": { "amount": 16, "unit": "muller turns" }, "kept": [3, 4] }""", r["machine"]!);
        var variant = r["variants"]![0]!;
        Assert.Equal(new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" }, Codes(variant["ingredients"]![3]!).Select(c => (string)c!));
        Assert.Equal(new[] { "game:metalplate-iron", "game:metalplate-steel" }, Codes(variant["ingredients"]![4]!).Select(c => (string)c!));
    }
}
