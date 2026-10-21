using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The slitting mill's process (mods-src/seraphhorizons/SlittingMill/): a hand-written <c>machine</c> record per
/// plate metal (a plate in, 20 nails and strips out), from config/slittingmill-rig.json and the gameplay's
/// SlittingMillSettings and MachineOilSettings.SlittingMill defaults, written down here; and the cutters' anvil
/// recipe, which the exporter reads as it reads every smithing recipe.
/// </summary>
public partial class RecipeExportScenarios
{
    private static readonly (string Metal, int KnifeWear)[] SlitMetals =
    [
        ("copper", 6), ("tinbronze", 10), ("bismuthbronze", 10), ("blackbronze", 12), ("brass", 8), ("silver", 4), ("gold", 4),
        ("iron", 20), ("meteoriciron", 24), ("steel", 40), ("molybdochalkos", 8), ("electrum", 6),
    ];

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Slitting_mill_process_is_a_machine_record_per_plate_metal()
    {
        Json("""{ "name": "Slitting mill", "count": 12, "shape": "machine", "registry": "SlittingMillSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.SlittingMillType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.SlittingMillType).ToList();
        Assert.Equal(SlitMetals.Select(m => $"slittingmill|game:metalplate-{m.Metal}|0").Order(), records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, wear) in SlitMetals)
        {
            var r = Recipe($"slittingmill|game:metalplate-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            Assert.Equal("SlittingMill", (string?)r["switch"]);
            var ingredients = (JArray)r["ingredients"]!;
            // kept: what the five stages are built of, summed by item
            Assert.Equal(new[] { $"game:metalplate-{metal}", "seraphhorizons:slittercutter-steel", "game:rod-iron", "game:jonasframes-gears01",
                                 "game:metal-parts", "game:jonasframes-gearbox01", "game:bracket-heavy-iron", "game:metalplate-iron", "game:plank-oak" },
                ingredients.Take(9).Select(i => (string)i["code"]!));
            Assert.All(ingredients.Skip(1).Take(8), i => Assert.Equal("kept", (string)i["role"]!));
            Assert.Equal(new[] { "item", "item", "item", "block", "item", "item", "item", "item" }, ingredients.Skip(1).Take(8).Select(i => (string)i["kind"]!));
            Assert.Equal(new[] { 10.0, 6, 1, 1, 1, 1, 1, 1 }, ingredients.Skip(1).Take(8).Select(i => (double)i["quantity"]!));
            var stacks = r["variants"]![0]!["ingredients"]!;
            Assert.Equal(new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" }, stacks[2]!.Select(st => (string)st["code"]!));
            Assert.Equal(new[] { "game:jonasframes-gearbox01", "game:jonasframes-gearbox02" }, stacks[5]!.Select(st => (string)st["code"]!));
            // the knife: a cleaver, worn by the plate's metal
            Json($$"""{ "code": "game:cleaver-steel", "kind": "item", "quantity": 1, "role": "tool", "isTool": true, "toolDurabilityCost": {{wear}} }""",
                ingredients[9]);
            Assert.Equal(new[] { "game:cleaver-steel", "game:cleaver-meteoriciron", "game:cleaver-iron" }, stacks[9]!.Select(st => (string)st["code"]!));
            Assert.Equal("oil", (string)ingredients[10]["role"]!);
            Assert.Equal(0.05, (double)ingredients[10]["litres"]!, 6);   // 5 points a plate
            Json("""{ "code": "seraphhorizons:slittingmill-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[11]);
            Json($$"""[{ "code": "game:metalnailsandstrips-{{metal}}", "kind": "item", "quantity": 20 }]""", r["outputs"]!);
            var machine = r["machine"]!;
            Assert.Equal("mechanical", (string)machine["power"]!);
            Assert.Equal(5, (double)machine["turns"]!, 6);
            Json("""{ "amount": 5, "unit": "strokes", "turnsPerUnit": 1 }""", machine["work"]!);
            Json("[1, 2, 3, 4, 5, 6, 7, 8]", machine["kept"]!);
            Json("""{ "ingredient": 9, "rule": "fixed" }""", machine["wear"]!);
            Json("""{ "ingredient": 10, "points": 5, "tank": 1000 }""", machine["oil"]!);
            Assert.Equal(metal, (string)r["extra"]!["metal"]!);
            Assert.Equal(new[] { $"game:metalnailsandstrips-{metal}" }, Codes(r["variants"]![0]!["outputs"]!).Select(c => (string)c!));
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Slitter_cutters_are_forged_two_from_a_steel_ingot()
    {
        var r = Assert.Single(Doc["recipes"]!.Cast<JObject>(), r => (string?)r["type"] == "smithing"
            && r["outputs"]!.Any(o => (string?)o["code"] == "seraphhorizons:slittercutter-{metal}"));
        Assert.Equal("SlittingMill", (string?)r["switch"]);
        Assert.Equal(2, (double)r["outputs"]![0]!["quantity"]!, 6);
        var variant = Assert.Single(r["variants"]!);
        Assert.Equal("game:ingot-steel", (string?)variant["ingredients"]![0]![0]!["code"]);
        Assert.Equal(new[] { "seraphhorizons:slittercutter-steel" }, Codes(variant["outputs"]!).Select(c => (string)c!));
    }
}
