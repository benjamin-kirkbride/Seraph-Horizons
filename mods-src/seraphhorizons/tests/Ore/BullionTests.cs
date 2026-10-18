using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Base bullion and rich lead (#740) on the shipped <c>config/ore-processing.json</c>.</summary>
public class BullionTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static readonly string ItemTypes = Path.Combine(AppContext.BaseDirectory, "oreprocessing");

    [Fact]
    public void ShippedFigures()
    {
        Assert.Empty(R.Problems);
        Assert.Equal(0.1, R.RichLeadSilver);
        Assert.Equal(10, Bullion.RichLeadSilverUnits(R), 9);
    }

    [Theory]
    [InlineData("galena", "seraphhorizons:bullion-galena-lead")]
    [InlineData("galena_nativesilver", "seraphhorizons:bullion-galena_nativesilver-lead")]
    [InlineData("tetrahedrite", "seraphhorizons:bullion-tetrahedrite-copper")]
    [InlineData("freibergite", "seraphhorizons:bullion-freibergite-silver")]
    [InlineData("teallite", "seraphhorizons:bullion-teallite-tin")]
    [InlineData("franckeite", "seraphhorizons:bullion-franckeite-tin")]
    [InlineData("quartz_nativegold", "seraphhorizons:bullion-quartz_nativegold-gold")]
    public void EveryByProductOreHasItsBullion(string ore, string code)
    {
        var spec = R.Ore(ore);
        Assert.True(Bullion.Makes(spec));
        Assert.Equal(code, Bullion.Code(spec));
        Assert.Equal(ore, Bullion.OreOf(code));
    }

    [Theory]
    [InlineData("chalcopyrite")]
    [InlineData("quartz_nativesilver")]
    [InlineData("cassiterite")]
    public void OresWithoutABy_productSmeltPlainly(string ore) => Assert.False(Bullion.Makes(R.Ore(ore)));

    [Theory]
    [InlineData("seraphhorizons:richlead")]
    [InlineData("game:ingot-lead")]
    [InlineData("seraphhorizons:bullion-")]
    [InlineData("seraphhorizons:bullion-galena")]
    public void NotBullion(string code) => Assert.Null(Bullion.OreOf(code));

    /// <summary>The item type lists exactly the by-product ores of the config, each with its main metal.</summary>
    [Fact]
    public void TheItemTypeMatchesTheConfig()
    {
        string text = File.ReadAllText(Path.Combine(ItemTypes, "bullion.json"));
        var expected = R.Ores.Where(Bullion.Makes).Select(o => Bullion.Code(o).Split(':')[1]).Order().ToList();
        Assert.Equal(7, expected.Count);
        foreach (string code in expected)
            Assert.Contains($"\"{code}\"", text);
        int allowed = System.Text.RegularExpressions.Regex.Matches(text, "\"bullion-[a-z_]+-[a-z]+\"").Count;
        Assert.Equal(expected.Count, allowed);
    }

    [Theory]
    // Silver-lead and the rest go to the cupellation furnace.
    [InlineData("galena", PartingStation.CupellationFurnace, true)]
    [InlineData("galena_nativesilver", PartingStation.CupellationFurnace, true)]
    [InlineData("tetrahedrite", PartingStation.CupellationFurnace, true)]
    [InlineData("freibergite", PartingStation.CupellationFurnace, true)]
    [InlineData("teallite", PartingStation.CupellationFurnace, false)]
    // The kettle takes silver-lead leaner than its rich lead: plain galena (3 %), not argentiferous (38 %).
    [InlineData("galena", PartingStation.ParkesKettle, true)]
    [InlineData("galena_nativesilver", PartingStation.ParkesKettle, false)]
    [InlineData("tetrahedrite", PartingStation.ParkesKettle, false)]
    [InlineData("freibergite", PartingStation.ParkesKettle, false)]
    [InlineData("teallite", PartingStation.LiquationFurnace, true)]
    [InlineData("franckeite", PartingStation.LiquationFurnace, true)]
    [InlineData("galena", PartingStation.LiquationFurnace, false)]
    [InlineData("quartz_nativegold", PartingStation.AcidParting, true)]
    [InlineData("galena", PartingStation.AcidParting, false)]
    // The hand stations take roasted concentrate, not bullion.
    [InlineData("galena", PartingStation.Cupel, false)]
    [InlineData("teallite", PartingStation.LiquationPan, false)]
    public void WhichMachineTakesWhichBullion(string ore, PartingStation s, bool taken) =>
        Assert.Equal(taken, Bullion.TakenBy(R, R.Ore(ore), s));

    // A bar of silver-lead cupelled: its 100 lead, and 3 silver at the furnace's 95 %.
    [Fact]
    public void ABarOfSilverLeadCupelled()
    {
        var y = R.Smelted(R.Ore("galena"), Bullion.UnitsPerItem, PartingStation.CupellationFurnace);
        Assert.Equal(100, y.Single(m => m.Metal == "lead").Units, 9);
        Assert.Equal(3 * 0.95, y.Single(m => m.Metal == "silver").Units, 9);
    }

    // Doré: the gold whole once parted, 15 silver at 95 %; smelted plainly, the gold at 85 %.
    [Fact]
    public void Dore()
    {
        var spec = R.Ore("quartz_nativegold");
        var y = R.Smelted(spec, Bullion.UnitsPerItem, PartingStation.AcidParting);
        Assert.Equal(100, y.Single(m => m.Metal == "gold").Units, 9);
        Assert.Equal(15 * 0.95, y.Single(m => m.Metal == "silver").Units, 9);
        Assert.Equal(0.85, Bullion.PlainShare(spec));
        Assert.Equal(1, Bullion.PlainShare(R.Ore("galena")));
    }

    // The kettle's charge of 24 bars of galena's silver-lead: 72 silver, 7.2 rich lead bars.
    [Fact]
    public void RichLeadBars()
    {
        double silver = R.Smelted(R.Ore("galena"), 24 * Bullion.UnitsPerItem, PartingStation.ParkesKettle)
            .Single(m => m.Metal == "silver").Units;
        Assert.Equal(72, silver, 9);
        Assert.Equal(7.2, Bullion.RichLeadBars(silver, R.RichLeadSilver), 9);
        Assert.Equal(0, Bullion.RichLeadBars(0, R.RichLeadSilver));
        Assert.Equal(0, Bullion.RichLeadBars(10, 0));
    }

    [Fact]
    public void ABadRichLeadShareIsAProblem()
    {
        var config = new OreProcessingConfig { Bullion = new OreProcessingConfig.BullionEntry { RichLeadSilver = 0 } };
        Assert.Contains(new OreRecovery(config).Problems, p => p.StartsWith("bullion.richLeadSilver"));
    }
}
