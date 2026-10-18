using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The Parkes kettle (mods-src/seraphhorizons/ParkesKettle/, #927) in the export: its charge, a hand-written
/// <c>machine</c> record per silver-lead bullion it takes (plain galena's: a bar and its zinc in; rich lead, desilverized
/// lead and zinc back out, a bar's share of each), its build as a <c>construction</c> record from
/// config/builds/parkeskettle.json, and its stirrer on the power page, its load following the charge.
/// </summary>
public partial class RecipeExportScenarios
{
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Parkes_kettle_charge_is_a_record_per_silver_lead_it_takes()
    {
        var type = Doc["recipeTypes"]![RecipeSection.ParkesKettleType]!;
        Assert.Equal("machine", (string)type["shape"]!);
        Assert.Equal("ParkesKettleSettings", (string)type["registry"]!);
        Assert.Equal((int)type["count"]!, Doc["recipes"]!.Count(x => (string)x["type"]! == RecipeSection.ParkesKettleType));

        var r = Recipe("parkeskettle|seraphhorizons:bullion-galena-lead|0");
        var ingredients = r["ingredients"]!;
        Assert.Equal("seraphhorizons:bullion-galena-lead", (string)ingredients[0]!["code"]!);
        Assert.Equal("game:metalbit-zinc", (string)ingredients[1]!["code"]!);
        Near(0.2, ingredients[1]!["quantity"]);
        Assert.Equal("station", (string)ingredients[2]!["role"]!);
        var outputs = r["outputs"]!;
        // a bar's 3 silver whole, 10 to a rich bar: 0.3 rich lead, 0.7 lead, and 80 % of its zinc
        Assert.Equal("seraphhorizons:richlead", (string)outputs[0]!["code"]!);
        Near(0.3, outputs[0]!["quantity"]);
        Assert.Equal("game:ingot-lead", (string)outputs[1]!["code"]!);
        Near(0.7, outputs[1]!["quantity"]);
        Near(0.16, outputs[2]!["quantity"]);
        Assert.Equal("mechanical", (string)r["machine"]!["power"]!);
        Near(95.5 * 0.6 * 5 / 24, r["machine"]!["turns"]);
        Near(1, r["extra"]!["share"]);
        Assert.Equal(2400, (int)r["extra"]!["charge"]!);
        Assert.Equal(6, (int)r["extra"]!["coalPerCharge"]!);
        // argentiferous galena's bullion is cupelled directly
        Assert.DoesNotContain(Doc["recipes"]!, x => (string)x["type"]! == RecipeSection.ParkesKettleType
            && ((string)x["id"]!).Contains("nativesilver"));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Parkes_kettle_build_is_a_construction_record_and_its_stirrer_a_consumer()
    {
        var r = Recipe("construction|seraphhorizons:parkeskettle-north|0");
        Assert.Equal("parkeskettle", (string)r["extra"]!["machine"]!);
        // the frame as placed, then the brickwork, the lining, the kettle and the stirrer
        Assert.Equal(5, r["construction"]!["stages"]!.Count());
        var codes = r["ingredients"]!.Select(i => (string)i["code"]!).ToList();
        Assert.Contains("game:refractorybrick-fired-tier3", codes);
        Assert.Contains("game:rod-iron", codes);
        var consumer = Doc["power"]!["consumers"]!.First(c => (string)c["id"]! == "seraphhorizons:parkeskettle");
        Near(0.04, consumer["load"]);
        Near(0.2, consumer["loadMax"]);
    }
}
