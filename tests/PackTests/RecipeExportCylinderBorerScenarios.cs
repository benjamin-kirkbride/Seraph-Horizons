using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The cylinder borer's process (mods-src/seraphhorizons/CylinderBorer/; #639): a hand-written
/// <c>machine</c> record per metal (a cast cylinder in, the bored cylinder out), from
/// config/cylinderborer-rig.json and the gameplay's CylinderBorerSettings and
/// MachineOilSettings.CylinderBorer defaults, written down here; the cylinder mold's casting, which the
/// exporter finds as it finds every tool mold's; and ppex's engines taking the bored cylinders.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cylinder_borer_process_is_a_machine_record_per_metal()
    {
        Json("""{ "name": "Cylinder borer", "count": 2, "shape": "machine", "registry": "CylinderBorerSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.CylinderBorerType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.CylinderBorerType).ToList();
        Assert.Equal(new[] { "cylinderborer|seraphhorizons:cylinderblank-iron|0", "cylinderborer|seraphhorizons:cylinderblank-steel|0" },
            records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, turnsPerBlock) in new[] { ("iron", 64.0), ("steel", 96.0) })
        {
            var r = Recipe($"cylinderborer|seraphhorizons:cylinderblank-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            Assert.Equal("CylinderBorer", (string?)r["switch"]);
            var ingredients = (JArray)r["ingredients"]!;
            Assert.Equal(new[] { $"seraphhorizons:cylinderblank-{metal}", "game:metal-parts", "seraphhorizons:cylinderborerbar",
                                 "seraphhorizons:cylinderborerfeed" },
                ingredients.Take(4).Select(i => (string)i["code"]!));
            Assert.All(ingredients.Skip(1).Take(3), i => Assert.Equal("kept", (string)i["role"]!));
            Assert.Equal(new[] { "block", "item", "item" }, ingredients.Skip(1).Take(3).Select(i => (string)i["kind"]!));
            Json("""{ "code": "seraphhorizons:cylinderborerhead-steel", "kind": "item", "quantity": 1, "role": "tool", "isTool": true, "toolDurabilityCost": 1 }""",
                ingredients[4]);
            Assert.Equal("oil", (string)ingredients[5]["role"]!);
            Assert.Equal(0.1, (double)ingredients[5]["litres"]!, 6);   // 10 points a cylinder
            Json("""{ "code": "seraphhorizons:cylinderborer-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[6]);
            Json($$"""[{ "code": "seraphhorizons:cylinder-{{metal}}", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
            var machine = r["machine"]!;
            Assert.Equal("mechanical", (string)machine["power"]!);
            Assert.Equal(18.0 / 16 * turnsPerBlock, (double)machine["turns"]!, 2);
            Assert.Equal(18.0 / 16, (double)machine["work"]!["amount"]!, 4);
            Assert.Equal("blocks", (string)machine["work"]!["unit"]!);
            Assert.Equal(turnsPerBlock, (double)machine["work"]!["turnsPerUnit"]!, 2);
            Json("[1, 2, 3]", machine["kept"]!);
            Json("""{ "ingredient": 4, "rule": "dividedByOilFill" }""", machine["wear"]!);
            Json("""{ "ingredient": 5, "points": 10, "tank": 1000 }""", machine["oil"]!);
            Assert.Equal(metal, (string)r["extra"]!["metal"]!);
            var variant = r["variants"]![0]!;
            Assert.Equal(new[] { $"seraphhorizons:cylinder-{metal}" }, Codes(variant["outputs"]!).Select(c => (string)c!));
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cylinder_mold_casts_iron_and_steel_blanks_from_six_ingots()
    {
        var casting = Doc["recipes"]!.Cast<JObject>()
            .Where(r => (string)r["type"]! == RecipeSection.CastingType)
            .Where(r => r["ingredients"]!.Any(i => ((string?)i["code"])?.StartsWith("seraphhorizons:cylindermold-") == true))
            .ToList();
        var r = Assert.Single(casting);
        Assert.Equal("CylinderBorer", (string?)r["switch"]);
        var metal = r["ingredients"]!.Single(i => (string?)i["role"] == "metal");
        Assert.Equal(6, (double)metal["quantity"]!, 6);
        Assert.Equal(600, (int)metal["extra"]!["units"]!);
        var cast = r["variants"]!.Select(v => (string)v["bindings"]!["metal"]!).ToList();
        Assert.Equal(new[] { "iron", "steel" }, cast);
        Assert.Equal(new[] { "seraphhorizons:cylinderblank-iron", "seraphhorizons:cylinderblank-steel" },
            r["variants"]!.Select(v => (string)Codes(v["outputs"]!).Single()!));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Ppex_engines_take_a_bored_cylinder_for_their_plates_and_pipes()
    {
        foreach (var (engine, cylinder) in new[] { ("ppex:enginewatt-north", "seraphhorizons:cylinder-iron"), ("ppex:enginecornish-north", "seraphhorizons:cylinder-steel") })
        {
            var grids = Doc["recipes"]!.Cast<JObject>()
                .Where(r => ((string)r["id"]!).StartsWith("grid|") && r["outputs"]!.Any(o => (string?)o["code"] == engine))
                .ToList();
            Assert.NotEmpty(grids);
            foreach (var g in grids)
            {
                var codes = g["ingredients"]!.Select(i => (string?)i["code"]).ToList();
                Assert.Contains(cylinder, codes);
                Assert.DoesNotContain(codes, c => c?.StartsWith("ppex:pipe-") == true);
            }
        }
    }
}
