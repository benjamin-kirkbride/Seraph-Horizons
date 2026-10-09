using SeraphHorizons.Mod.CrucibleFurnace.Core;
using static SeraphHorizons.Mod.CrucibleFurnace.Core.BessemerHeat;

namespace SeraphHorizons.Mod.Tests;

public class BessemerHeatTests
{
    private const float Melts = 1530f;

    [Theory]
    [InlineData(2400, 480, 1700f, Outcome.Stainless)] // 4 to 1, a full converter
    [InlineData(500, 90, 1700f, Outcome.Stainless)]   // 18 %
    [InlineData(500, 110, 1700f, Outcome.Stainless)]  // 22 %
    [InlineData(500, 85, 1700f, Outcome.OffRatio)]    // 17 %
    [InlineData(500, 115, 1700f, Outcome.OffRatio)]   // 23 %
    [InlineData(100, 100, 1700f, Outcome.OffRatio)]   // ferrochrome alone
    [InlineData(2400, 480, 1529f, Outcome.TooCold)]
    [InlineData(2400, 480, 1530f, Outcome.Stainless)]
    [InlineData(2400, 0, 1700f, Outcome.None)]
    public void A_heat_pours_stainless_only_on_the_pots_ratio_and_hot_enough(int heat, int ferrochrome, float temperature, Outcome expected)
    {
        Assert.Equal(expected, Judge(heat, ferrochrome, temperature, Melts));
    }

    [Fact]
    public void Before_the_blow_only_the_ratio_is_judged()
    {
        Assert.Equal(Outcome.Stainless, Judge(1000, 200, null, Melts));
        Assert.Equal(Outcome.OffRatio, Judge(1000, 300, null, Melts));
    }

    [Fact]
    public void The_ratio_is_the_pots()
    {
        for (int ferrochrome = 5; ferrochrome < 1000; ferrochrome += 5)
            Assert.Equal(Stainless.RatioMet(1000 - ferrochrome, ferrochrome), Judge(1000, ferrochrome, 1600f, Melts) == Outcome.Stainless);
    }

    [Theory]
    [InlineData(Outcome.Stainless, 600)]
    [InlineData(Outcome.OffRatio, 480)]
    [InlineData(Outcome.TooCold, 480)]
    [InlineData(Outcome.None, 600)]
    public void Off_the_ratio_the_ferrochrome_goes_to_the_slag(Outcome outcome, int poured)
    {
        Assert.Equal(poured, UnitsPoured(600, 120, outcome));
    }

    [Fact]
    public void The_share_is_a_whole_percent()
    {
        Assert.Equal(20, SharePercent(2400, 480));
        Assert.Equal(0, SharePercent(0, 0));
    }
}
