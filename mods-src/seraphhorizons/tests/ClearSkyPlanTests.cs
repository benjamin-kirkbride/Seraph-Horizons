using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ClearSkyPlanTests
{
    // Exact in binary, so the sampled days land on the spells' edges.
    private const double Step = 0.125;

    [Theory]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(15.9, 0)]
    [InlineData(0, 12)]
    [InlineData(6.5, 5.5)]
    [InlineData(16, 20)]
    [InlineData(23, 13)]
    public void DaytimeIsLeftAloneAndOtherwiseTheClockGoesToTheNextNoon(double hour, double expected) =>
        Assert.Equal(expected, ClearSkyPlan.HoursToDay(hour, 24), 6);

    [Fact]
    public void DaytimeScalesWithTheLengthOfTheDay()
    {
        // A 48-hour day: daytime is 16 to 32, noon 24.
        Assert.Equal(0, ClearSkyPlan.HoursToDay(20, 48), 6);
        Assert.Equal(24, ClearSkyPlan.HoursToDay(0, 48), 6);
        Assert.Equal(40, ClearSkyPlan.HoursToDay(32, 48), 6);
    }

    [Fact]
    public void AlreadyDryNeedsNoForwarding()
    {
        var spell = ClearSkyPlan.FindDrySpell(_ => true, Step)!.Value;
        Assert.Equal(0, spell.StartDays);
        Assert.Equal(ClearSkyPlan.WantedDryDays, spell.LengthDays, 6);
    }

    [Fact]
    public void TheFirstLongEnoughDrySpellIsTaken()
    {
        // Rain for 2 days, dry for 6 hours, rain until day 5, then dry for good.
        bool Dry(double d) => d is >= 2 and < 2.25 || d >= 5;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(5, spell.StartDays, 6);
        Assert.True(spell.LengthDays >= ClearSkyPlan.WantedDryDays - 1e-9);
    }

    [Fact]
    public void FailingThatTheLongestShortSpellIsTaken()
    {
        bool Dry(double d) => d is >= 2 and < 2.25 || d is >= 7 and < 7.5 || d is >= 9 and < 9.1;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(7, spell.StartDays, 6);
        Assert.Equal(0.5, spell.LengthDays, 6);
    }

    [Fact]
    public void ADrySpellRunningToTheEndOfTheSearchCounts()
    {
        bool Dry(double d) => d >= ClearSkyPlan.SearchDays - 0.25;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(ClearSkyPlan.SearchDays - 0.25, spell.StartDays, 6);
    }

    [Fact]
    public void RainThroughoutFindsNothing() => Assert.Null(ClearSkyPlan.FindDrySpell(_ => false, Step));

    [Fact]
    public void TheLockRoundTrips()
    {
        var lk = new ClearLock
        {
            HadBaseline = true, Baseline = 60, OverridePrecipitation = 0.3f, AutoChangePatterns = false,
            StormDaysAhead = 4.25,
        };
        var back = ClearLock.FromBytes(lk.ToBytes())!;
        Assert.True(back.HadBaseline);
        Assert.Equal(60, back.Baseline);
        Assert.Equal(0.3f, back.OverridePrecipitation);
        Assert.False(back.AutoChangePatterns);
        Assert.Equal(4.25, back.StormDaysAhead);

        var none = ClearLock.FromBytes(new ClearLock().ToBytes())!;
        Assert.Null(none.OverridePrecipitation);
        Assert.Null(none.StormDaysAhead);
        Assert.Null(ClearLock.FromBytes(null));
    }
}
