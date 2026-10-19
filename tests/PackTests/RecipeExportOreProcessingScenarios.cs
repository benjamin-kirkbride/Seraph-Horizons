using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.RecipeExport;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Ore processing's records in the recipe export (#686-#726): spalling, cupellation, liquation,
/// roasting and retorting, with their guide pages. They read the export this class builds anyway, on
/// the same plain world with the default config (the <c>OreProcessing</c> switch on), so
/// <see cref="OreProcessingScenarios"/>' server does not build a second one (over a minute in CI).
/// </summary>
public partial class RecipeExportScenarios
{
    private OreProcessingSystem OreProcessing => OreProcessingSystem.Of(World.Api);

    // The recipe browser's record per ore the cupel takes.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cupellation_is_exported()
    {
        var doc = Doc;
        Assert.Equal("generic", (string)doc["recipeTypes"]!["cupellation"]!["shape"]!);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == "cupellation").ToList();
        Assert.Equal(["cupellation|seraphhorizons:roastedconcentrate-freibergite|0", "cupellation|seraphhorizons:roastedconcentrate-galena_nativesilver|0",
                      "cupellation|seraphhorizons:roastedconcentrate-galena|0", "cupellation|seraphhorizons:roastedconcentrate-tetrahedrite|0"],
            records.Select(r => (string)r["id"]!).Order(StringComparer.Ordinal));
        var argentiferous = records.Single(r => (string)r["extra"]!["ore"]! == "galena_nativesilver");
        Assert.Equal("OreProcessing", (string?)argentiferous["switch"]);
        Assert.Equal(64.6, (double)argentiferous["extra"]!["metalUnits"]!["silver"]!, 6);
        Assert.Equal(40, (double)argentiferous["ingredients"]![1]!["quantity"]!);
        var tetrahedrite = records.Single(r => (string)r["extra"]!["ore"]! == "tetrahedrite");
        Assert.Contains(tetrahedrite["ingredients"]!, i => (string)i["code"]! == "game:metalbit-lead" && (double)i["quantity"]! == 20);
    }

    // The ore routes page's figures carry rich lead's silver share, which keeps argentiferous galena (38 % silver,
    // richer than rich lead) off the Parkes kettle, as the mod's final step does.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Ore_processing_section_carries_rich_leads_silver()
    {
        var section = (JObject)Doc["oreProcessing"]!;
        Assert.Equal(0.1, (double)section["richLeadSilver"]!, 9);
        var argentiferous = section["ores"]!.Single(o => (string)o["ore"]! == "galena_nativesilver");
        Assert.Equal(0.38, (double)argentiferous["byProducts"]![0]!["share"]!, 9);
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);
        Assert.Equal(OreMachine.CupellationFurnace, recovery.FinalStep(recovery.Ore("galena_nativesilver")));
        Assert.Equal(OreMachine.ParkesKettle, recovery.FinalStep(recovery.Ore("galena")));
    }

    // The recipe browser's record per ore the pan takes, and the guide page.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Liquation_is_exported()
    {
        var doc = Doc;
        Assert.Equal("generic", (string)doc["recipeTypes"]!["liquation"]!["shape"]!);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == "liquation").ToList();
        Assert.Equal(["liquation|seraphhorizons:roastedconcentrate-franckeite|0", "liquation|seraphhorizons:roastedconcentrate-teallite|0"],
            records.Select(r => (string)r["id"]!).Order(StringComparer.Ordinal));
        var teallite = records.Single(r => (string)r["extra"]!["ore"]! == "teallite");
        Assert.Equal("OreProcessing", (string?)teallite["switch"]);
        Assert.Equal(34, (double)teallite["extra"]!["residueUnits"]!["lead"]!, 6);
        Assert.Contains(teallite["outputs"]!, o => (string)o["code"]! == "game:ingot-tin" && (double)o["quantity"]! == 1);
        Assert.Contains(teallite["outputs"]!, o => (string)o["code"]! == "game:metalbit-lead" && Math.Abs((double)o["quantity"]! - 6.8) < 1e-6);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.LiquationGuidePage);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.BullionGuidePage);
    }

    // The recipe export has a spalling record per ore kind: by hand, its turns the blows, the hammer
    // worn a point a blow, no station; one crushed ore of its grade.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Spalling_is_exported_as_a_hand_job_per_ore_kind()
    {
        var doc = Doc;
        var type = doc["recipeTypes"]![RecipeSection.SpallingType]!;
        Assert.Equal("machine", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.SpallingType).ToList();
        Assert.Equal((int)type["count"]!, records.Count);
        Assert.Equal(2 * 4 * OreProducts.Ores.Count, records.Count);
        var medium = records.Single(r => (string?)r["id"] == "spalling|game:ore-medium-galena-*|0");
        Assert.Equal("game:ore-medium-galena-*", (string?)medium["ingredients"]![0]!["code"]);
        Assert.Equal(2, (int)medium["ingredients"]![1]!["toolDurabilityCost"]!);
        Assert.Equal("seraphhorizons:crushedore-galena-medium", (string?)medium["outputs"]![0]!["code"]);
        Assert.Equal(1, (int)medium["outputs"]![0]!["quantity"]!);
        Assert.Equal("hand", (string?)medium["machine"]!["power"]);
        Assert.Equal(2, (int)medium["machine"]!["turns"]!);
        Assert.Equal("strikes", (string?)medium["machine"]!["work"]!["unit"]);
        Assert.DoesNotContain(medium["ingredients"]!, i => (string?)i["role"] == "station");
        Assert.Equal(RecipeSection.SpallingRequirement, (string?)medium["requirements"]![0]);
        Assert.Contains(medium["variants"]![0]!["ingredients"]![0]!, s => (string?)s["code"] == "game:ore-medium-galena-shale");
        var poor = records.Single(r => (string?)r["id"] == "spalling|game:ore-poor-galena-*|0");
        Assert.Equal("seraphhorizons:crushedore-galena-poor", (string?)poor["outputs"]![0]!["code"]);
        Assert.Equal(1, (int)poor["outputs"]![0]!["quantity"]!);
        var bountiful = records.Single(r => (string?)r["id"] == "spalling|game:ore-bountiful-galena-*|0");
        Assert.Equal(2, (int)bountiful["machine"]!["turns"]!);
        Assert.Equal(1, (int)bountiful["outputs"]![0]!["quantity"]!);
        var crystal = records.Single(r => (string?)r["id"] == "spalling|game:crystalizedore-medium-galena-*|0");
        Assert.Equal(2, (int)crystal["outputs"]![0]!["quantity"]!);
        // The guide page is in the export with the switch on.
        Assert.Contains(((JArray)doc["guides"]!).OfType<JObject>(), g => (string?)g["code"] == OreProcessingSystem.SpallingGuidePage);
        var dump = Environment.GetEnvironmentVariable("SPALLING_EXPORT_DUMP");
        if (!string.IsNullOrEmpty(dump))
            File.WriteAllText(dump, doc.ToString());
    }

    // Roasting (#720): a record per sulfide, the firepit a station, 0.85 of a roasted concentrate
    // out of one concentrate; and the roasting guide page.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Roasting_is_exported()
    {
        var doc = Doc;
        var type = doc["recipeTypes"]![RecipeSection.OreRoastingType]!;
        Assert.Equal("generic", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.OreRoastingType).ToList();
        // One per sulfide's concentrate; rock's roast is its own smelting attribute, one for one.
        Assert.Equal(OreProducts.Ores.Count(o => OreProcessing.Recovery!.Ore(o).IsSulfide), records.Count);
        Assert.Equal(records.Count, (int)type["count"]!);
        var galena = records.Single(r => (string?)r["id"] == "oreroasting|seraphhorizons:concentrate-galena|0");
        Assert.Equal("OreProcessing", (string?)galena["switch"]);
        var ingredients = (JArray)galena["ingredients"]!;
        Assert.Equal("seraphhorizons:concentrate-galena", (string?)ingredients[0]["code"]);
        Assert.Equal(1, (double)ingredients[0]["quantity"]!);
        Assert.Equal("game:firepit-cold", (string?)ingredients[1]["code"]);
        Assert.Equal("station", (string?)ingredients[1]["role"]);
        var output = galena["outputs"]![0]!;
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", (string?)output["code"]);
        Assert.Equal(0.85, (double)output["quantity"]!, 9);
        Assert.Equal(OreRoasting.MeltingPoint, (int)galena["extra"]!["meltingPoint"]!);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.RoastingGuidePage);
        // and the guide to the whole chain, in English
        var chain = Assert.Single(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == OreProcessingSystem.ProcessingGuidePage);
        Assert.Equal("Ore processing", (string?)chain["title"]);
        Assert.Contains("Bountiful", (string?)chain["text"]);
    }

    // Retorting in the still (#726): a record per input, the boiler and condenser
    // stations, the mercury in portions and litres, an amalgam's sponge; and the guide page.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Retorting_is_exported()
    {
        var doc = Doc;
        var type = doc["recipeTypes"]![RecipeSection.OreRetortingType]!;
        Assert.Equal("generic", (string?)type["shape"]);
        var records = doc["recipes"]!.Cast<JObject>().Where(r => (string?)r["type"] == RecipeSection.OreRetortingType).ToList();
        Assert.Equal(MercuryStillSystem.Of(World.Api).Marked.Count, records.Count);
        var gold = records.Single(r => (string?)r["id"] == "oreretorting|seraphhorizons:amalgam-quartz_nativegold|0");
        Assert.Equal("OreProcessing", (string?)gold["switch"]);
        var ingredients = (JArray)gold["ingredients"]!;
        Assert.Equal("seraphhorizons:amalgam-quartz_nativegold", (string?)ingredients[0]["code"]);
        Assert.Equal("game:verticalboiler-west", (string?)ingredients[1]["code"]);
        Assert.Equal("station", (string?)ingredients[2]["role"]);
        var outputs = (JArray)gold["outputs"]!;
        Assert.Equal("em:mercuryportion", (string?)outputs[0]["code"]);
        Assert.Equal(9, (double)outputs[0]["quantity"]!, 9);
        Assert.Equal(0.09, (double)outputs[0]["litres"]!, 9);
        Assert.Equal("seraphhorizons:sponge-quartz_nativegold", (string?)outputs[1]["code"]);
        var cinnabar = records.Single(r => (string?)r["id"] == "oreretorting|game:powder-cinnabar|0");
        Assert.Single((JArray)cinnabar["outputs"]!);
        Assert.Contains(doc["guides"]!.Cast<JObject>(), g => (string?)g["code"] == MercuryStillSystem.GuidePage);
    }
}
