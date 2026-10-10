using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Cupellation in the bone-ash cupel (#722) on the shipped <c>config/ore-processing.json</c>.</summary>
public class CupellationTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static CupelCharge Ore(string ore, int items) => new(ore, items * 5);

    [Fact]
    public void ShippedFigures()
    {
        Assert.Equal(new CupelSettings(200, 1, 950, 60), R.Cupel);
        Assert.Equal(0.85, R.Parting(PartingMethod.Cupellation, Cupellation.Tier));
    }

    [Theory]
    [InlineData("galena", true)]
    [InlineData("galena_nativesilver", true)]
    [InlineData("tetrahedrite", true)]
    [InlineData("freibergite", true)]
    [InlineData("chalcopyrite", false)] // a sulfide with no silver
    [InlineData("teallite", false)]     // its lead is liquated, not cupelled
    [InlineData("quartz_nativesilver", false)] // free silver: amalgamated, not cupelled
    [InlineData("cerussite", false)]
    public void WhichOresTheCupelTakes(string ore, bool cupels) => Assert.Equal(cupels, Cupellation.Cupels(R.Ore(ore)));

    // A full cupel of argentiferous galena: 200 units of lead, 38 % silver at 85 %.
    [Fact]
    public void ArgentiferousGalena()
    {
        CupelCharge[] charge = [Ore("galena_nativesilver", 40)];
        Assert.Equal(CupelRefusal.None, Cupellation.Check(R, charge));
        var y = Cupellation.Yield(R, charge);
        Assert.Equal(200, y.LeadUnits, 6);
        Assert.Equal(200 * 0.38 * 0.85, y.UnitsOf("silver"), 6); // 64.6 units, about 13 bits
        Assert.Single(y.Metals);
        Assert.Equal(120, Cupellation.Seconds(R.Cupel, 200, 1)!.Value, 6);
        Assert.Equal(120 / 0.7, Cupellation.Seconds(R.Cupel, 200, 0.7)!.Value, 6);
        Assert.Null(Cupellation.Seconds(R.Cupel, 200, 0));
    }

    // Plain galena carries 3 %: a full cupel, about one bit.
    [Fact]
    public void PlainGalena() =>
        Assert.Equal(200 * 0.03 * 0.85, Cupellation.Yield(R, [Ore("galena", 40)]).UnitsOf("silver"), 6);

    // Tetrahedrite wants its own weight of lead: its copper and 5 % silver; the lead into litharge.
    [Fact]
    public void TetrahedriteWithLead()
    {
        Assert.Equal(CupelRefusal.TooLittleLead, Cupellation.Check(R, [Ore("tetrahedrite", 20), CupelCharge.Lead(95)]));
        // Roasted galena is lead too (and brings its own silver).
        CupelCharge[] charge = [Ore("tetrahedrite", 20), CupelCharge.Lead(50), Ore("galena", 10)];
        Assert.Equal(CupelRefusal.None, Cupellation.Check(R, charge));
        var y = Cupellation.Yield(R, charge);
        Assert.Equal(100, y.LeadUnits, 6);
        Assert.Equal(100, y.UnitsOf("copper"), 6);
        Assert.Equal(100 * 0.05 * 0.85 + 50 * 0.03 * 0.85, y.UnitsOf("silver"), 6);
    }

    // Freibergite: all its silver (its main metal) and 30 % copper at 85 %.
    [Fact]
    public void FreibergiteWithLead()
    {
        var y = Cupellation.Yield(R, [Ore("freibergite", 20), CupelCharge.Lead(100)]);
        Assert.Equal(100, y.UnitsOf("silver"), 6);
        Assert.Equal(100 * 0.30 * 0.85, y.UnitsOf("copper"), 6);
        Assert.Equal(100, y.LeadUnits, 6);
    }

    [Fact]
    public void Refusals()
    {
        Assert.Equal(CupelRefusal.Empty, Cupellation.Check(R, []));
        Assert.Equal(CupelRefusal.Foreign, Cupellation.Check(R, [Ore("galena", 4), CupelCharge.Other]));
        Assert.Equal(CupelRefusal.Foreign, Cupellation.Check(R, [Ore("chalcopyrite", 4)]));
        Assert.Equal(CupelRefusal.NothingToPart, Cupellation.Check(R, [CupelCharge.Lead(50)]));
        Assert.Equal(CupelRefusal.TooMuch, Cupellation.Check(R, [Ore("galena", 41)]));
        Assert.Equal(CupelRefusal.None, Cupellation.Check(R, [Ore("galena", 40)]));
    }

    [Theory]
    [InlineData(200, 0.99, 40)]   // exact: never one more
    [InlineData(64.6, 0.5, 13)]   // 12.92 bits: the 13th at 92 %
    [InlineData(64.6, 0.91, 13)]
    [InlineData(64.6, 0.93, 12)]
    [InlineData(5.1, 0.0, 2)]
    [InlineData(0, 0, 0)]
    public void WholeItemsCarryTheFractionAsAChance(double units, double draw, int items) =>
        Assert.Equal(items, Cupellation.Whole(units, 5, draw));
}
