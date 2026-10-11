using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Springs (mods-src/seraphhorizons/Springs/): a hand-written <c>generic</c> record per kind, the
/// spring (any rock, role <c>station</c>) giving a second's fresh well water from a pipe tapping it,
/// at the SpringsSettings defaults written down here (4 and 10 litres a second).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Springs_are_a_source_of_well_water_per_kind()
    {
        Json("""{ "name": "Spring", "count": 2, "shape": "generic", "registry": "SpringsSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.SpringType]!);
        foreach (var (kind, litres) in new[] { ("normal", 4), ("rapid", 10) })
        {
            var r = Recipe($"spring|seraphhorizons:waterspring-{kind}-*|0");
            Assert.Equal("seraphhorizons", (string)r["mod"]!);
            Assert.Equal("Springs", (string?)r["switch"]);
            Json($$"""[{ "code": "seraphhorizons:waterspring-{{kind}}-*", "kind": "block", "quantity": 1, "role": "station" }]""", r["ingredients"]!);
            Json($$"""[{ "code": "hydrateordiedrate:waterportion-fresh-well-clean", "kind": "item", "quantity": {{litres * 100}}, "litres": {{litres}} }]""", r["outputs"]!);
            Json($$"""{ "kind": "{{kind}}", "litresPerSecond": {{litres}}, "pressure": 1 }""", r["extra"]!);
            var rocks = Codes(r["variants"]![0]!["ingredients"]![0]!).Select(c => (string)c!).ToList();
            Assert.Contains($"seraphhorizons:waterspring-{kind}-granite", rocks);
            Assert.Contains($"seraphhorizons:waterspring-{kind}-dolostone", rocks);
            Assert.All(rocks, c => Assert.StartsWith($"seraphhorizons:waterspring-{kind}-", c));
        }
    }
}
