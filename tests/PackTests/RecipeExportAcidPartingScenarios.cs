using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The acid parting vessel (mods-src/seraphhorizons/AcidParting/, #931) in the export: its parting, a hand-written
/// <c>generic</c> record per bullion it parts (gold quartz's dore: a bar and the nitric acid it spends in, 20 gold bits
/// and 2.85 silver bits out), its build as a <c>construction</c> record from config/builds/acidparting.json, and the
/// pack's aqua fortis cooking recipe. It draws no power, so it is not on the power page.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Acid_parting_is_a_record_per_dore()
    {
        var type = Doc["recipeTypes"]![RecipeSection.AcidPartingType]!;
        Assert.Equal("generic", (string)type["shape"]!);
        Assert.Equal("AcidPartingSettings", (string)type["registry"]!);
        Assert.Equal(1, (int)type["count"]!);

        var r = Recipe("acidparting|seraphhorizons:bullion-quartz_nativegold-gold|0");
        var ingredients = r["ingredients"]!;
        Assert.Equal("seraphhorizons:bullion-quartz_nativegold-gold", (string)ingredients[0]!["code"]!);
        Assert.Equal("game:acid-full-nitric", (string)ingredients[1]!["code"]!);
        Near(0.8, ingredients[1]!["litres"]);
        Assert.Equal("station", (string)ingredients[2]!["role"]!);
        Assert.Equal("seraphhorizons:acidparting-north", (string)ingredients[2]!["code"]!);
        var outputs = r["outputs"]!;
        Assert.Equal("game:metalbit-gold", (string)outputs[0]!["code"]!);
        Near(20, outputs[0]!["quantity"]);
        Assert.Equal("game:metalbit-silver", (string)outputs[1]!["code"]!);
        Near(100 * 0.15 * 0.95 / 5, outputs[1]!["quantity"]);
        Near(0.95, r["extra"]!["share"]);
        Near(10, r["extra"]!["hours"]);
        Near(10, r["extra"]!["waterLitres"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Acid_parting_build_is_a_construction_record_of_its_five_stages()
    {
        var r = Recipe("construction|seraphhorizons:acidparting-north|0");
        Assert.Equal("acidparting", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the brickwork, the lining, the sand bath, the vessel and tub and the siphon, and
        // last the tub's copper plate, the wearing part fitted by itself
        Assert.Equal(7, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("seraphhorizons:pipesection-lead", codes);
        Assert.Contains("game:plank-oak", codes);
        Assert.Contains("game:metalplate-copper", codes);
        Assert.Null(Doc["power"]!["consumers"]!.FirstOrDefault(c => (string)c["id"]! == "seraphhorizons:acidparting"));
    }
}
