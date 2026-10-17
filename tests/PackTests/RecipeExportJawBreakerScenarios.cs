using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The jaw breaker (mods-src/seraphhorizons/JawBreaker/, #912): a hand-written <c>machine</c> record per ore
/// item kind (raw ore in, its crushed ore out, as spalling's, the breaker the station, 2.3875 axle turns a
/// rock from ore-processing.json's 24 an hour at 0.6), and its build as a <c>construction</c> record from
/// config/builds/jawbreaker.json (the shared ore machine records, #711).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Jaw_breaker_breaks_each_raw_ore_kind_and_its_build_is_a_construction_record()
    {
        var type = Doc["recipeTypes"]![RecipeSection.JawBreakerType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.JawBreakerType).ToList();
        var spalled = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.SpallingType).ToList();
        Assert.NotEmpty(records);
        Assert.Equal((int)type["count"]!, records.Count);
        // one for every ore kind spalling breaks, into the same crushed ore
        Assert.Equal(spalled.Select(r => ((string)r["id"]!)["spalling|".Length..]).Order(), records.Select(r => ((string)r["id"]!)["jawbreaker|".Length..]).Order());
        var r = records[0];
        var match = spalled.Single(s => ((string)s["id"]!)["spalling|".Length..] == ((string)r["id"]!)["jawbreaker|".Length..]);
        Assert.Equal(match["outputs"]!.ToString(), r["outputs"]!.ToString());
        var ingredients = (JArray)r["ingredients"]!;
        Json("""{ "code": "seraphhorizons:jawbreaker-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[1]);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        Assert.Equal(95.5 * 0.6 / 24, (double)r["machine"]!["turns"]!, 4);
        Assert.Equal(0.8, (double)r["extra"]!["load"]!, 6);
        Assert.Equal(0.16, (double)r["extra"]!["emptyLoad"]!, 6);

        // its build: the frame, then the five stages, the jaw faces with the jaws
        var build = Recipe("construction|seraphhorizons:jawbreaker-north|0");
        Assert.Equal("seraphhorizons.OreMachine", (string)build["extra"]!["behavior"]!);
        Assert.Equal(6, ((JArray)build["construction"]!["stages"]!).Count);
        Assert.Equal("jawfaces", (string)build["extra"]!["wearing"]![0]!["id"]!);
    }
}
