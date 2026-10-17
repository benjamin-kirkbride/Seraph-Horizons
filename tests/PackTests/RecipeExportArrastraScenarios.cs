using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The arrastra (mods-src/seraphhorizons/Arrastra/, #732): its build is the shared ore machines'
/// <c>construction</c> record, and its grinding a hand-written <c>machine</c> record per crushed ore
/// (RecipeSection.Arrastra.cs): the rock, its 10 L of water and the arrastra as the station; one ground ore
/// of the same ore and grade, a rock's share of a charge's turns (95.5 × 0.6 × 7.5 / 2).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Arrastra_grinds_each_crushed_ore_and_is_built_in_four_stages()
    {
        var type = Doc["recipeTypes"]![RecipeSection.ArrastraType]!;
        Assert.Equal("Arrastra", (string)type["name"]!);
        Assert.Equal("machine", (string)type["shape"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.ArrastraType).ToList();
        Assert.Equal((int)type["count"]!, records.Count);
        Assert.True(records.Count > 20, $"{records.Count} arrastra records");

        var r = Recipe("arrastra|seraphhorizons:crushedore-hematite-poor|0");
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:crushedore-hematite-poor", "kind": "item", "quantity": 1 }""", ingredients[0]);
        Json("""{ "code": "game:waterportion", "kind": "item", "quantity": 1000, "litres": 10, "role": "liquid" }""", ingredients[1]);
        Json("""{ "code": "seraphhorizons:arrastra-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[2]);
        Json("""[{ "code": "seraphhorizons:groundore-hematite-poor", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        Json("""{ "power": "mechanical", "turns": 214.875 }""", r["machine"]!);
        Assert.Equal("Arrastra", (string?)r["switch"]);

        var build = Recipe("construction|seraphhorizons:arrastra-north|0");
        Assert.Equal("batch", (string)build["extra"]!["feed"]!);
        Assert.Equal(new[] { "the pit", "the post and gearing", "the sweep arms", "the drag stones" },
            build["extra"]!["stages"]!.Select(s => (string)s!));
        Assert.Equal(5, ((JArray)build["construction"]!["stages"]!).Count);
    }
}
