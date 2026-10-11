using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Liquation in the clay pan (#724) on the shipped <c>config/ore-processing.json</c>.</summary>
public class LiquationTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static LiquationCharge Ore(string ore, int items) => new(ore, items * 5);

    [Fact]
    public void ShippedFigures()
    {
        Assert.Equal(new LiquationSettings(100, 240, 327, 15), R.Liquation);
        Assert.Equal(0.85, R.Parting(PartingMethod.Liquation, Liquation.Tier));
        Assert.Empty(R.Problems);
    }

    [Theory]
    [InlineData("teallite", true)]
    [InlineData("franckeite", true)]
    [InlineData("cassiterite", false)] // tin with no lead
    [InlineData("galena", false)]      // its silver is cupelled
    [InlineData("tetrahedrite", false)]
    public void WhichOresThePanTakes(string ore, bool liquates) => Assert.Equal(liquates, Liquation.Liquates(R.Ore(ore)));

    // A full pan of teallite: all 100 units of tin run off, 40 % lead at 85 % stays.
    [Fact]
    public void Teallite()
    {
        LiquationCharge[] charge = [Ore("teallite", 20)];
        Assert.Equal(LiquationRefusal.None, Liquation.Check(R, charge));
        var y = Liquation.Yield(R, charge, overheated: false);
        Assert.Equal("tin", y.Metal);
        Assert.Equal(100, y.Units, 6);
        Assert.Equal(100 * 0.40 * 0.85, y.ResidueOf("lead"), 6);
        Assert.Equal(15, Liquation.Seconds(R.Liquation, 100), 6);
        Assert.Equal(7.5, Liquation.Seconds(R.Liquation, 50), 6);
    }

    // Franckeite and teallite mix: one pour of tin, each one's lead.
    [Fact]
    public void Mixed()
    {
        LiquationCharge[] charge = [Ore("teallite", 10), Ore("franckeite", 10)];
        Assert.Equal(LiquationRefusal.None, Liquation.Check(R, charge));
        var y = Liquation.Yield(R, charge, overheated: false);
        Assert.Equal(100, y.Units, 6);
        Assert.Equal(50 * 0.40 * 0.85 + 50 * 0.30 * 0.85, y.ResidueOf("lead"), 6);
    }

    // Over the lead point the lead runs with the tin: the tin as unparted smelting gives it, no lead.
    [Fact]
    public void Overheated()
    {
        Assert.False(Liquation.Overheated(R.Liquation, 327));
        Assert.True(Liquation.Overheated(R.Liquation, 327.5));
        var y = Liquation.Yield(R, [Ore("teallite", 20)], overheated: true);
        Assert.Equal(100, y.Units, 6);
        Assert.Empty(y.Residue);
    }

    [Fact]
    public void Refusals()
    {
        Assert.Equal(LiquationRefusal.Empty, Liquation.Check(R, []));
        Assert.Equal(LiquationRefusal.Foreign, Liquation.Check(R, [Ore("teallite", 4), LiquationCharge.Other]));
        Assert.Equal(LiquationRefusal.Foreign, Liquation.Check(R, [Ore("galena", 4)]));
        Assert.Equal(LiquationRefusal.TooMuch, Liquation.Check(R, [Ore("teallite", 21)]));
    }

    [Fact]
    public void BadFiguresAreReported()
    {
        var config = new OreProcessingConfig { Liquation = new() { TinPoint = 400, LeadPoint = 327, SecondsPerIngot = 0 } };
        var problems = new OreRecovery(config).Problems;
        Assert.Contains(problems, p => p.StartsWith("liquation.leadPoint", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("liquation.secondsPerIngot", StringComparison.Ordinal));
    }
}
