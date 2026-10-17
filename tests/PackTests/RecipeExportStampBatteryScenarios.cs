using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The stamp battery's process (mods-src/seraphhorizons/StampBattery/, #728): a hand-written <c>machine</c>
/// record per kind of rock it takes, raw and crystallised ore by grade and ore, every host rock an
/// alternative, and crushed ore; the figures are ore-core's (<c>machines.stampbattery</c>: 6 rock an hour,
/// 40 L a rock) and the build file's shoes (5 a set, 1,000 rocks), written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Stamp_battery_process_is_a_machine_record_per_kind_of_rock()
    {
        var type = Doc["recipeTypes"]![RecipeSection.StampBatteryType]!;
        Assert.Equal("Stamp battery", (string)type["name"]!);
        Assert.Equal("machine", (string)type["shape"]!);
        var records = Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == RecipeSection.StampBatteryType).ToList();
        Assert.Equal(records.Count, (int)type["count"]!);

        var raw = Recipe("stampbattery|game:ore-rich-galena-*|0");
        var ingredients = (JArray)raw["ingredients"]!;
        Json("""{ "code": "game:ore-rich-galena-*", "kind": "item", "quantity": 1 }""", ingredients[0]);
        Assert.Equal("game:waterportion", (string)ingredients[1]["code"]!);
        Assert.Equal(40, (double)ingredients[1]["litres"]!, 6);
        Assert.Equal("game:poundercap-steel", (string)ingredients[2]["code"]!);
        Assert.Equal(0.005, (double)ingredients[2]["quantity"]!, 9);
        Json("""{ "code": "seraphhorizons:stampbattery-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[3]);
        Json("""[{ "code": "seraphhorizons:crushedore-galena-rich", "kind": "item", "quantity": 1 }]""", raw["outputs"]!);
        var machine = raw["machine"]!;
        Assert.Equal("mechanical", (string)machine["power"]!);
        // 95.5 turns an hour at speed 1, so 57.3 at the reference 0.6, over 6 rock an hour
        Assert.Equal(9.55, (double)machine["turns"]!, 6);
        Assert.Equal("rocks", (string)machine["work"]!["unit"]!);
        Assert.True((bool)raw["extra"]!["classified"]!);
        Assert.Equal("raw", (string)raw["extra"]!["form"]!);
        Assert.Contains("game:ore-rich-galena-granite", Codes(raw["variants"]![0]!["ingredients"]![0]!).Select(c => (string)c!));

        var crystal = Recipe("stampbattery|game:crystalizedore-rich-galena-*|0");
        Json("""[{ "code": "seraphhorizons:crushedore-galena-rich", "kind": "item", "quantity": 2 }]""", crystal["outputs"]!);

        var crushed = Recipe("stampbattery|seraphhorizons:crushedore-galena-rich|0");
        Assert.Equal("crushed", (string)crushed["extra"]!["form"]!);
        Json("""[{ "code": "seraphhorizons:crushedore-galena-rich", "kind": "item", "quantity": 1 }]""", crushed["outputs"]!);
    }
}
