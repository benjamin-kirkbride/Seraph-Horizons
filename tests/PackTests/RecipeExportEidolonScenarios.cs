using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The eidolon's two builds (mods-src/seraphhorizons/EidolonGantry/): the gantry's winch and the
/// body on its spine, as <c>construction</c> records read from GantryParts and BodyBill, written down
/// here; and its handbook guide in the export's guides.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_gantry_winch_is_a_construction_record_per_wood()
    {
        var r = Recipe("construction|seraphhorizons:eidolongantry-acacia-north|0");
        Assert.Equal("seraphhorizons", (string)r["mod"]!);
        Assert.Equal("Eidolon", (string?)r["switch"]);
        Json("""[{ "code": "seraphhorizons:eidolongantry-{wood}-north", "kind": "block", "quantity": 1 }]""", r["outputs"]!);
        var ingredients = (JArray)r["ingredients"]!;
        Assert.Equal(new[] { "game:woodenaxle-ud", "game:rod-*", "game:spurgear-s", "game:plank-{wood}", "game:metalnailsandstrips-*",
                             "game:metalplate-*", "game:rod-*", "game:metalchain-*", "game:supportbeam-{wood}" },
            ingredients.Select(i => (string)i["code"]!));
        Assert.Equal(new[] { 8.0, 1, 4, 10, 10, 1, 1, 4, 3 }, ingredients.Select(i => (double)i["quantity"]!));
        Json("""["iron", "meteoriciron", "steel"]""", ingredients[1]["allowedVariants"]!);
        // the placed frame, then one stage a click, in order
        var stages = (JArray)r["construction"]!["stages"]!;
        Assert.Equal(10, stages.Count);
        Json("[]", stages[0]["ingredients"]!);
        for (int s = 1; s < 10; s++)
        {
            Json($"[{s - 1}]", stages[s]["ingredients"]!);
            Assert.Equal("Fit the next stage", (string?)stages[s]["action"]);
        }
        var variants = ((JArray)r["variants"]!).Cast<JObject>().ToList();
        Assert.Equal(12, variants.Count);
        var oak = Assert.Single(variants, v => (string?)v["bindings"]?["wood"] == "oak");
        Assert.Equal(new[] { "game:plank-oak" }, Codes(oak["ingredients"]![3]!).Select(c => (string)c!));
        Assert.Equal(new[] { "game:supportbeam-oak" }, Codes(oak["ingredients"]![8]!).Select(c => (string)c!));
        Assert.Equal(new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" }, Codes(oak["ingredients"]![6]!).Select(c => (string)c!));
        Assert.Equal(new[] { "seraphhorizons:eidolongantry-oak-north" }, Codes(oak["outputs"]!).Select(c => (string)c!));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_body_is_a_construction_record_on_a_gantry()
    {
        var r = Recipe("construction|seraphhorizons:creature-eidolon|0");
        Assert.Equal("Eidolon", (string?)r["switch"]);
        Json("""[{ "code": "seraphhorizons:creature-eidolon", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:eidolongantry-*-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[0]);
        var stages = (JArray)r["construction"]!["stages"]!;
        Assert.Equal(7, stages.Count);   // the gantry, then torso, pelvis, legs, arms, head and mind
        Json("[0]", stages[0]["ingredients"]!);
        Assert.Equal("Fit the mind and wake it", (string?)stages[6]["action"]);
        var mind = Assert.Single((JArray)stages[6]["ingredients"]!);
        Json("""{ "code": "game:gear-temporal", "kind": "item", "quantity": 1 }""", ingredients[(int)mind]);
        // the vessel goes in with the head
        var head = ((JArray)stages[5]["ingredients"]!).Select(i => (string)ingredients[(int)i]["code"]!).ToList();
        Assert.Contains("game:rustypart-eidolon2tr", head);
        // the bill's totals (the epic's, #668): 12 stainless gears, 16 metal parts, 13 steel plates
        double Total(string code) => ingredients.Where(i => (string)i["code"]! == code).Sum(i => (double)i["quantity"]!);
        Assert.Equal(12, Total("seraphhorizons:gear-stainless"));
        Assert.Equal(16, Total("game:metal-parts"));
        Assert.Equal(13, Total("game:metalplate-steel"));
        var variant = Assert.Single((JArray)r["variants"]!);
        Assert.Equal(12, Codes(variant["ingredients"]![0]!).Count());   // a gantry of any wood
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_guide_is_in_the_exported_guides()
    {
        var guide = Assert.Single(((JArray)Doc["guides"]!).OfType<JObject>(),
            g => (string?)g["code"] == SeraphHorizons.Mod.Eidolon.EidolonGuideSystem.GuidePageCode);
        Assert.Equal("seraphhorizons", (string?)guide["mod"]);
        Assert.Equal("Building and commanding an eidolon", (string?)guide["title"]);
        Assert.Contains("rustypart-eidolon2tr", (string?)guide["text"]);
    }
}
