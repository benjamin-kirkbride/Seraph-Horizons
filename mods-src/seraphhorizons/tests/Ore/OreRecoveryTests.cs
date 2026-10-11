using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>
/// The recovery model (#685) on the shipped <c>config/ore-processing.json</c>, held to the design
/// doc's worked examples (epic #684): one pocket of 64 blocks at vanilla's 1.25 ore a block.
/// </summary>
public class OreRecoveryTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static OreProcessingConfig Shipped() =>
        JsonSerializer.Deserialize<OreProcessingConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")), Options)!;

    private static readonly OreRecovery R = new(Shipped());

    /// <summary>A pocket's units: 64 blocks × 1.25 ore × the grade's units.</summary>
    private static double Pocket(double unitsPerOre) => 64 * 1.25 * unitsPerOre;

    private static double Line(string ore, bool poor, OreTier tier, double units) =>
        units * R.Overall(R.Ore(ore), OreLine.AtTier(tier), poor);

    [Fact]
    public void ShippedConfigIsSound() => Assert.Empty(R.Problems);

    [Fact]
    public void ShippedOresAreKnownOres()
    {
        foreach (var ore in R.Ores)
            Assert.True(OreMetals.MetalOf(ore.Ore) != null, $"{ore.Ore} is not an ore OreMetals knows");
    }

    [Theory]
    [InlineData(OreTier.Hand, 352)]   // riddle, rocker: 55 % × 0.4 = 22 %
    [InlineData(OreTier.Tier2, 1280)] // stamps, arrastra, jig: 80 %
    [InlineData(OreTier.Tier4, 1584)] // 99 %
    public void PoorHematite(OreTier tier, double units) =>
        Assert.Equal(units, Line("hematite", true, tier, Pocket(20)), 6);

    [Theory]
    [InlineData(OreTier.Hand, 924)]   // 55 % × 1.05
    [InlineData(OreTier.Tier2, 1344)] // 80 % × 1.05
    [InlineData(OreTier.Tier4, 1600)] // 99 % × 1.05, capped
    public void MediumChromite(OreTier tier, double units) =>
        Assert.Equal(units, Line("chromite", false, tier, Pocket(20)), 6);

    [Theory]
    [InlineData(OreTier.Hand, 748)]   // rocker 55 %, firepit roast 85 %
    [InlineData(OreTier.Tier3, 1472)] // table 92 %, reverberatory 100 %
    [InlineData(OreTier.Tier4, 1584)]
    public void MediumGalena(OreTier tier, double units) =>
        Assert.Equal(units, Line("galena", false, tier, Pocket(20)), 6);

    [Fact]
    public void GalenaCarriesSilverWonOnlyByCupellation()
    {
        var galena = R.Ore("galena");
        double lead = Line("galena", false, OreTier.Tier4, Pocket(20));
        var parted = R.Smelted(galena, lead, OreTier.Tier4);
        Assert.Equal(new MetalUnits("lead", 1584), parted[0]);
        Assert.Equal("silver", parted[1].Metal);
        Assert.Equal(48, Math.Round(parted[1].Units)); // 3 % of 1584, cupelled at 100 %
        // Argentiferous galena (#690): a lead ore with 38 %; smelted unparted, the lead and no silver.
        var argentiferous = R.Ore("galena_nativesilver");
        Assert.Equal("lead", argentiferous.Metal);
        Assert.Equal(1584 * 0.38, R.Smelted(argentiferous, lead, OreTier.Tier4)[1].Units, 6);
        Assert.Equal([new MetalUnits("lead", 1584)], R.Smelted(argentiferous, lead, null));
        // By hand, the bone-ash cupel: 85 %.
        Assert.Equal(1584 * 0.03 * 0.85, R.Smelted(galena, lead, OreTier.Hand)[1].Units, 6);
        // Unparted, the lead and no silver.
        Assert.Equal([new MetalUnits("lead", 1584)], R.Smelted(galena, lead, null));
    }

    [Fact]
    public void UnroastedSulfideConcentrateDoesNotSmelt()
    {
        var galena = R.Ore("galena");
        Assert.Equal(0, R.Overall(galena, OreLine.AtTier(OreTier.Tier4) with { Roaster = null }, false));
        Assert.Equal(0, R.Roasting(galena, null));
        Assert.Equal(0, R.SmeltShare(galena, OreForm.Concentrate));
        Assert.Equal(1, R.SmeltShare(galena, OreForm.RoastedConcentrate));
        // Roasting is nothing to an oxide.
        Assert.Equal(1, R.Roasting(R.Ore("hematite"), null));
        Assert.Equal(1, R.SmeltShare(R.Ore("hematite"), OreForm.Concentrate));
    }

    [Fact]
    public void MediumGoldQuartz()
    {
        var gold = R.Ore("quartz_nativegold");
        double units = Pocket(10);
        var rockerOnly = OreLine.AtTier(OreTier.Hand) with { Amalgamated = false };
        Assert.Equal(308, units * R.Overall(gold, rockerOnly, false), 6);          // 55 % × 0.7
        Assert.Equal(440, Line("quartz_nativegold", false, OreTier.Hand, units), 6); // with the amalgam pan
        Assert.Equal(792, Line("quartz_nativegold", false, OreTier.Tier4, units), 6);
    }

    [Fact]
    public void GoldQuartzUnpartedGivesGoldAt85Percent()
    {
        var gold = R.Ore("quartz_nativegold");
        // No acid parting by hand: smelted as is, 85 % of the gold and the silver lost.
        Assert.Null(R.Parting(PartingMethod.AcidParting, OreTier.Hand));
        Assert.Equal([new MetalUnits("gold", 440 * 0.85)], R.Smelted(gold, 440, OreTier.Hand));
        // Parted at tier 4: all the gold plus 15 % silver.
        var parted = R.Smelted(gold, 792, OreTier.Tier4);
        Assert.Equal(new MetalUnits("gold", 792), parted[0]);
        Assert.Equal(792 * 0.15, parted[1].Units, 6);
    }

    [Theory]
    [InlineData(OreTier.Hand, 968)]   // 55 % × 1.1
    [InlineData(OreTier.Tier4, 1600)] // 99 % × 1.1, capped
    public void MediumNativeCopper(OreTier tier, double units) =>
        Assert.Equal(units, Line("nativecopper", false, tier, Pocket(20)), 6);

    [Fact]
    public void FeedFactors()
    {
        var hematite = R.Ore("hematite");
        Assert.Equal(0.45, R.Concentration(hematite, Concentrator.Pan, new OreFeed(false, true, false, true)), 9);
        // Not classified × 0.85; poor not ground × 0.4.
        Assert.Equal(0.55 * 0.85, R.Concentration(hematite, Concentrator.Rocker, new OreFeed(false, false, false, true)), 9);
        Assert.Equal(0.55 * 0.85 * 0.4, R.Concentration(hematite, Concentrator.Rocker, new OreFeed(true, false, false, true)), 9);
        // Ground, poor ore loses nothing for its grain; amalgamation is nothing to an ore without free metal.
        Assert.Equal(0.80, R.Concentration(hematite, Concentrator.Jig, new OreFeed(true, true, true, false)), 9);
        // Smithsonite is light.
        Assert.Equal(0.99 * 0.9, R.Concentration(R.Ore("smithsonite"), Concentrator.TableAndVanner, new OreFeed(false, true, true, true)), 9);
    }

    [Fact]
    public void TiersHaveTheirDevices()
    {
        Assert.Equal(new OreLine(Concentrator.Rocker, true, false, true, Roaster.Firepit), OreLine.AtTier(OreTier.Hand));
        Assert.Equal(new OreLine(Concentrator.Sluice, true, false, true, Roaster.Firepit), OreLine.AtTier(OreTier.Tier1));
        Assert.Equal(new OreLine(Concentrator.Jig, true, true, true, Roaster.Stall), OreLine.AtTier(OreTier.Tier2));
        Assert.Equal(new OreLine(Concentrator.Table, true, true, true, Roaster.Reverberatory), OreLine.AtTier(OreTier.Tier3));
        Assert.Equal(new OreLine(Concentrator.TableAndVanner, true, true, true, Roaster.Reverberatory), OreLine.AtTier(OreTier.Tier4));
    }

    [Fact]
    public void PartingByTier()
    {
        foreach (var m in new[] { PartingMethod.Cupellation, PartingMethod.Liquation })
        {
            Assert.Equal(0.85, R.Parting(m, OreTier.Hand));
            Assert.Equal(0.85, R.Parting(m, OreTier.Tier1));
            Assert.Equal(0.95, R.Parting(m, OreTier.Tier3));
            Assert.Equal(1.0, R.Parting(m, OreTier.Tier4));
        }
        Assert.Null(R.Parting(PartingMethod.AcidParting, OreTier.Tier1));
        Assert.Equal(0.95, R.Parting(PartingMethod.AcidParting, OreTier.Tier2));
        Assert.Equal(1.0, R.Parting(PartingMethod.AcidParting, OreTier.Tier4));
    }

    [Theory]
    [InlineData("tetrahedrite", "copper", "silver", 0.05)]
    [InlineData("freibergite", "silver", "copper", 0.30)]
    [InlineData("teallite", "tin", "lead", 0.40)]
    [InlineData("franckeite", "tin", "lead", 0.30)]
    public void ByProductShares(string ore, string main, string second, double share)
    {
        var spec = R.Ore(ore);
        var parted = R.Smelted(spec, 100, OreTier.Tier4);
        Assert.Equal(new MetalUnits(main, 100), parted[0]);
        Assert.Equal(second, parted[1].Metal);
        Assert.Equal(100 * share, parted[1].Units, 9);
        Assert.Equal([new MetalUnits(main, 100)], R.Smelted(spec, 100, null));
    }

    [Fact]
    public void UnlistedOreIsAPlainOxide()
    {
        var spec = R.Ore("alum");
        Assert.Equal(OreClass.Oxide, spec.Class);
        Assert.Equal("alum", spec.Metal);
        Assert.Equal(0.55, R.Overall(spec, OreLine.AtTier(OreTier.Hand), false), 9);
    }

    [Fact]
    public void SmeltShares()
    {
        var hematite = R.Ore("hematite");
        Assert.Equal(0, R.SmeltShare(hematite, OreForm.Raw));
        Assert.Equal(0.5, R.SmeltShare(hematite, OreForm.Chunk));
        Assert.Equal(0.5, R.SmeltShare(hematite, OreForm.Crushed));
        Assert.Equal(1, R.SmeltShare(hematite, OreForm.Concentrate));
        Assert.Equal(0.5, R.SmeltShare(R.Ore("galena"), OreForm.Chunk));
        // Ground ore goes on to the concentrator; it does not smelt (#686).
        Assert.Equal(0, R.SmeltShare(hematite, OreForm.Ground));
    }

    [Fact]
    public void ConcentrateCarriesItsFractionOver()
    {
        // The poor hematite pocket by hand, one raw ore (20 units) at a time: 352 units, exactly.
        var carry = new UnitCarry();
        double perOre = 20 * R.Overall(R.Ore("hematite"), OreLine.AtTier(OreTier.Hand), true);
        int items = 0;
        for (int i = 0; i < 80; i++) items += carry.Add("concentrate", perOre, R.ConcentrateUnits);
        Assert.Equal(70, items);
        Assert.Equal(2, carry.HeldFor("concentrate"), 6);
        Assert.Equal(352, items * R.ConcentrateUnits + carry.HeldFor("concentrate"), 6);
    }

    [Fact]
    public void CrushingKeepsEveryUnit()
    {
        var carry = new UnitCarry();
        Assert.Equal(5, carry.Add("crushed", 25, 5));
        Assert.Equal(0, carry.HeldFor("crushed"));
        Assert.Equal(1, carry.Add("crushed", 7, 5));
        Assert.Equal(2, carry.HeldFor("crushed"), 9);
        Assert.Equal(2, carry.Add("crushed", 8, 5));
        Assert.Empty(carry.Held);
    }

    [Fact]
    public void CarryIsPerOutputAndRestores()
    {
        var carry = new UnitCarry();
        Assert.Equal(0, carry.Add("a", 4.4, 5));
        Assert.Equal(0, carry.Add("b", 1, 5));
        var saved = carry.Held.ToDictionary();
        var again = new UnitCarry();
        again.Restore(saved);
        Assert.Equal(1, again.Add("a", 4.4, 5));
        Assert.Equal(3.8, again.HeldFor("a"), 9);
        Assert.Equal(1, again.HeldFor("b"), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => again.Add("a", 1, 0));
    }

    [Fact]
    public void ReportsABrokenConfig()
    {
        var config = Shipped();
        config.Concentrators.Remove("jig");
        config.Unclassified = 1.5;
        config.Ores["galena"].Class = "sulphide";
        config.Ores["galena"].ByProducts[0].PartedBy = "magic";
        config.Parting["cupellation"]["tier5"] = 1;
        var problems = new OreRecovery(config).Problems;
        Assert.Contains(problems, p => p.Contains("'jig'"));
        Assert.Contains(problems, p => p.StartsWith("unclassified"));
        Assert.Contains(problems, p => p.Contains("'sulphide'"));
        Assert.Contains(problems, p => p.Contains("'magic'"));
        Assert.Contains(problems, p => p.Contains("'tier5'"));
    }
}
