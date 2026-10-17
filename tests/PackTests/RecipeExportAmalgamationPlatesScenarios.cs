using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The amalgamation plates (mods-src/seraphhorizons/AmalgamationPlates/, #917) in the export: a hand-written record
/// per crushed and ground free gold or silver quartz (the rock and its catch's mercury in, the amalgam and the rock
/// amalgamated out), its build as a <c>construction</c> record from config/builds/amalgamationplates.json, and no
/// draw on the power page (it takes no power).
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Amalgamation_plates_process_is_a_record_per_free_quartz()
    {
        var type = Doc["recipeTypes"]![RecipeSection.AmalgamationPlatesType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.AmalgamationPlatesType));
        // gold and silver quartz, four grades, crushed and ground
        Assert.Equal(16, (int)type["count"]!);
        var r = Recipe("amalgamationplates|seraphhorizons:crushedore-quartz_nativegold-rich|0");
        // 4 units, unclassified (× 0.85), 0.65 caught: 2.21 units, 0.442 of an amalgam, 4.42 portions of mercury
        Json("""
            [{ "code": "seraphhorizons:crushedore-quartz_nativegold-rich", "kind": "item", "quantity": 1 },
             { "code": "em:mercuryportion", "kind": "item", "quantity": 4.42, "litres": 0.0442 },
             { "code": "seraphhorizons:amalgamationplates-north", "kind": "block", "quantity": 1, "role": "station" }]
            """, r["ingredients"]!);
        Json("""
            [{ "code": "seraphhorizons:amalgam-quartz_nativegold", "kind": "item", "quantity": 0.442 },
             { "code": "seraphhorizons:crushedore-quartz_nativegold-rich", "kind": "item", "quantity": 1 }]
            """, r["outputs"]!);
        Assert.Equal(4, (int)r["variants"]![0]!["outputs"]![1]!["attributes"]!["oreState"]!);
        Assert.Null(r["machine"]);
        Assert.Equal(8, (double)r["extra"]!["perHour"]!);
        Assert.Equal(5, (double)r["extra"]!["waterPerItem"]!);
        Assert.Equal(0.65, (double)r["extra"]!["recovery"]!);
        // ores without free metal run over unchanged: no record
        Assert.DoesNotContain(Doc["recipes"]!, x => (string)x["id"]! == "amalgamationplates|seraphhorizons:crushedore-galena-rich|0");
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Amalgamation_plates_build_is_a_construction_record_of_its_three_stages()
    {
        var r = Recipe("construction|seraphhorizons:amalgamationplates-north|0");
        Assert.Equal("amalgamationplates", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the three fitted stages
        Assert.Equal(4, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("seraphhorizons:halfplate-copper", codes);
        Assert.Contains("game:metalplate-iron", codes);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Amalgamation_plates_take_no_power()
    {
        var consumers = (JArray)Doc["power"]!["consumers"]!;
        Assert.DoesNotContain(consumers, c => (string)c["id"]! == "seraphhorizons:amalgamationplates");
    }
}
