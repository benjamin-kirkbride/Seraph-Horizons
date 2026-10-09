using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class GearPartDropRulesTests
{
    [Theory]
    [InlineData("betterloot:gearpart")]
    [InlineData(" BetterLoot:GearPart ")]
    public void A_gear_part_is_one(string code) => Assert.True(GearPartDropRules.IsGearPart(code));

    // The rest of BetterLoot+'s drops, the rusty gear among them, stay as they are.
    [Theory]
    [InlineData("gear-rusty")]
    [InlineData("game:gear-rusty")]
    [InlineData("gearpart")]
    [InlineData("game:gearpart")]
    [InlineData("betterloot:gearpart-x")]
    [InlineData("gear-temporal")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string? code) => Assert.False(GearPartDropRules.IsGearPart(code));

    // BetterLoot+ 1.0.0's averages: 0.25 (surface), 1 (deep), 1.5 (tainted), 2 (corrupt), 3 (nightmare).
    [Theory]
    [InlineData(0.25, 0.0625)]
    [InlineData(1.0, 0.25)]
    [InlineData(1.5, 0.375)]
    [InlineData(2.0, 0.5)]
    [InlineData(3.0, 0.75)]
    public void A_part_drop_becomes_rusty_gears_at_a_quarter(double avg, double gears)
    {
        var (code, newAvg, newVar) = GearPartDropRules.Replacement(avg, 0.4);
        Assert.Equal("game:gear-rusty", code);
        Assert.Equal(gears, newAvg, 12);
        Assert.Equal(0.1, newVar, 12);
    }
}
