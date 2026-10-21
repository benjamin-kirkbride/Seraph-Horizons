using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The eidolon's two builds (mods-src/seraphhorizons/EidolonGantry/): the gantry's winch and the
/// body on its spine in layers of material, as <c>construction</c> records read from its build file,
/// BodyBill and EidolonFittings, written down here; the torso's grid recipe; and its handbook guide in
/// the export's guides.
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
    public void Eidolon_body_is_a_construction_record_on_a_gantry_in_layers_of_material()
    {
        var r = Recipe("construction|seraphhorizons:creature-eidolon|0");
        Assert.Equal("Eidolon", (string?)r["switch"]);
        Json("""[{ "code": "seraphhorizons:creature-eidolon", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:eidolongantry-*-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[0]);
        var stages = (JArray)r["construction"]!["stages"]!;
        // the gantry, then the torso, the five layers and the mind
        Assert.Equal(8, stages.Count);
        Json("[0]", stages[0]["ingredients"]!);
        Assert.Equal(new[] { "torso", "bones", "joints", "plates", "sash", "skirt", "mind" },
            ((JArray)r["extra"]!["stages"]!).Select(s => (string)s!));
        List<string> CodesOf(int stage) => ((JArray)stages[stage]["ingredients"]!).Select(i => (string)ingredients[(int)i]["code"]!).ToList();
        Assert.Equal(new[] { "seraphhorizons:eidolontorso" }, CodesOf(1));
        Assert.Equal("Clamp the torso onto the spine", (string?)stages[1]["action"]);
        Assert.Equal(new[] { "game:gear-temporal" }, CodesOf(7));
        Assert.Contains("crank", (string?)r["extra"]!["wake"]);
        // each layer's material is a wildcard over what it takes, then its Jonas parts
        Assert.Equal("game:supportbeam-*", CodesOf(2)[0]);
        Assert.Contains("game:rustypart-eidolon2tr", CodesOf(2));
        Assert.Equal(new[] { "game:rod-*", "game:metalnailsandstrips-*" }, CodesOf(3).Take(2));
        Assert.Equal(new[] { "game:metalplate-*" }, CodesOf(4));
        Assert.Equal(new[] { "game:*" }, CodesOf(5));
        Assert.Equal(new[] { "game:metalchain-*" }, CodesOf(6));
        JToken Def(int stage, int k = 0) => ingredients[(int)stages[stage]["ingredients"]![k]!];
        Json("""["steel", "iron", "stainlesssteel", "brass"]""", Def(4)["allowedVariants"]!);
        // the counts are the mod's (FittingCounts), not written down here
        var counts = new[] { FittingLayer.Bones, FittingLayer.Joints, FittingLayer.Plates, FittingLayer.Sash, FittingLayer.Skirt }
            .SelectMany(FittingCounts.Of).Select(c => (double)c);
        Assert.Equal(counts, new[] { Def(2), Def(3), Def(3, 1), Def(4), Def(5), Def(6) }.Select(d => (double)d["quantity"]!));
        Assert.Contains("cloth-blue", ((JArray)Def(5)["allowedVariants"]!).Select(v => (string)v!));
        Assert.Contains("leather-normal-plain", ((JArray)Def(5)["allowedVariants"]!).Select(v => (string)v!));
        Assert.DoesNotContain("rotten", ((JArray)Def(2)["allowedVariants"]!).Select(v => (string)v!));
        // the Jonas parts over the layers: 9 stainless gears and 12 metal parts (the torso's own are its recipe's)
        double Total(string code) => ingredients.Where(i => (string)i["code"]! == code).Sum(i => (double)i["quantity"]!);
        Assert.Equal(9, Total("seraphhorizons:gear-stainless"));
        Assert.Equal(12, Total("game:metal-parts"));
        // one layer, one material: extra.layers says which ingredients share it
        var layers = ((JArray)r["extra"]!["layers"]!).Cast<JObject>().ToList();
        Assert.Equal(new[] { "bones", "joints", "plates", "sash", "skirt" }, layers.Select(l => (string)l["layer"]!));
        var joints = layers[1];
        Assert.Equal("steel", (string?)joints["default"]);
        Json("""["steel", "iron", "stainlesssteel"]""", joints["materials"]!);
        Assert.Equal(((JArray)stages[3]["ingredients"]!).Take(2).Select(i => (int)i), ((JArray)joints["ingredients"]!).Select(i => (int)i));
        Assert.Equal("leather-plain", (string?)layers[3]["default"]);
        // the one variant lists every material's stack for a piece, the default's first
        var variant = Assert.Single((JArray)r["variants"]!);
        Assert.Equal(12, Codes(variant["ingredients"]![0]!).Count());   // a gantry of any wood
        int plates = (int)stages[4]["ingredients"]![0]!;
        Assert.Equal(new[] { "game:metalplate-steel", "game:metalplate-iron", "game:metalplate-stainlesssteel", "game:metalplate-brass" },
            Codes(variant["ingredients"]![plates]!).Select(c => (string)c!));
        int rods = (int)stages[3]["ingredients"]![0]!;
        Assert.Contains("game:rod-stainlesssteel", Codes(variant["ingredients"]![rods]!).Select(c => (string)c!));
        int sash = (int)stages[5]["ingredients"]![0]!;
        var sashes = Codes(variant["ingredients"]![sash]!).Select(c => (string)c!).ToList();
        Assert.Equal("game:leather-normal-plain", sashes[0]);
        Assert.Contains("game:cloth-blue", sashes);
        int chain = (int)stages[6]["ingredients"]![0]!;
        Assert.Contains("game:metalchain-stainlesssteel", Codes(variant["ingredients"]![chain]!).Select(c => (string)c!));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_torso_is_a_grid_recipe_behind_the_schematic()
    {
        // the schematic's gate registers the recipe again with the schematic (|r0), the asset's own is left unresolved
        var r = Recipe("grid|seraphhorizons:recipes/grid/eidolontorso.json|r0");
        Assert.Equal("Eidolon", (string?)r["switch"]);
        Json("""[{ "code": "seraphhorizons:eidolontorso", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        var codes = ((JArray)r["ingredients"]!).Select(i => ((string)i["code"]!, (double)i["quantity"]!)).ToList();
        Assert.Contains(("game:jonasparts-pumphead", 1.0), codes);
        Assert.Contains(("game:jonasparts-tank01", 1.0), codes);
        Assert.Contains(("game:jonasparts-tank02", 1.0), codes);
        Assert.Contains(("game:metal-parts", 4.0), codes);
        Assert.Contains(("seraphhorizons:gear-stainless", 3.0), codes);
        Assert.Contains(("seraphhorizons:schematic-eidolon", 1.0), codes);
        // every slot resolves to a stack
        var variant = Assert.Single((JArray)r["variants"]!);
        Assert.All((JArray)variant["ingredients"]!, slot => Assert.NotEmpty(slot));
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
