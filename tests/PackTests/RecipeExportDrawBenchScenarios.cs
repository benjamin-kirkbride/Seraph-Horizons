using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The draw bench's process (mods-src/seraphhorizons/DrawBench/): a hand-written <c>machine</c>
/// record per metal, from config/drawbench-rig.json and the gameplay's DrawBenchSettings and
/// MachineOilSettings.DrawBench defaults, written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Draw_bench_process_is_a_machine_record_per_metal()
    {
        Json("""{ "name": "Draw bench", "count": 2, "shape": "machine", "registry": "DrawBenchSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.DrawBenchType]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.DrawBenchType).ToList();
        Assert.Equal(new[] { "drawbench|game:ingot-copper|0", "drawbench|game:ingot-lead|0" }, records.Select(r => (string)r["id"]!).Order());
        foreach (var (metal, dies, turns) in new[]
                 {
                     ("lead", new[] { "seraphhorizons:drawdie-iron", "seraphhorizons:drawdie-steel" }, 7.72),
                     ("copper", new[] { "seraphhorizons:drawdie-steel" }, 15.45),
                 })
        {
            var r = Recipe($"drawbench|game:ingot-{metal}|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            var ingredients = (JArray)r["ingredients"]!;
            Assert.Equal(new[] { $"game:ingot-{metal}", "game:jonasframes-gearbox01", "game:metalchain-iron", "game:bracket-heavy-iron", "game:rod-iron" },
                ingredients.Take(5).Select(i => (string)i["code"]!));
            Assert.All(ingredients.Skip(1).Take(4), i => Assert.Equal("kept", (string)i["role"]!));
            Json($$"""{ "code": "{{dies[0]}}", "kind": "item", "quantity": 1, "role": "tool", "isTool": true, "toolDurabilityCost": 1 }""", ingredients[5]);
            Assert.Equal("oil", (string)ingredients[6]["role"]!);
            Assert.Equal(0.06, (double)ingredients[6]["litres"]!, 6);
            Json("""{ "code": "seraphhorizons:drawbench-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[7]);
            Json($$"""[{ "code": "game:chutesection-{{metal}}", "kind": "item", "quantity": 3 }]""", r["outputs"]!);
            var machine = r["machine"]!;
            Assert.Equal("mechanical", (string)machine["power"]!);
            Assert.Equal(turns * 3, (double)machine["turns"]!, 2);
            Assert.Equal(3, (double)machine["work"]!["amount"]!);
            Assert.Equal("sections", (string)machine["work"]!["unit"]!);
            Json("[1, 2, 3, 4]", machine["kept"]!);
            Json("""{ "ingredient": 5, "rule": "fixed" }""", machine["wear"]!);
            Json("""{ "ingredient": 6, "points": 6, "tank": 1000 }""", machine["oil"]!);
            var variant = r["variants"]![0]!;
            Assert.Equal(dies, Codes(variant["ingredients"]![5]!).Select(c => (string)c!));
            Assert.Equal(new[] { "game:metalchain-iron", "game:metalchain-meteoriciron", "game:metalchain-steel" },
                Codes(variant["ingredients"]![2]!).Select(c => (string)c!));
            Assert.Equal(new[] { $"game:chutesection-{metal}" }, Codes(variant["outputs"]!).Select(c => (string)c!));
        }
    }
}
