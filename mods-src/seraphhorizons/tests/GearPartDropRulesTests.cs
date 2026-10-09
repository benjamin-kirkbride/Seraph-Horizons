using SeraphHorizons.Mod.Core;
using Drop = SeraphHorizons.Mod.Core.GearPartDropRules.Drop;

namespace SeraphHorizons.Mod.Tests;

public class GearPartDropRulesTests
{
    [Theory]
    [InlineData("betterloot:gearpart")]
    [InlineData(" BetterLoot:GearPart ")]
    public void A_gear_part_is_one(string code) => Assert.True(GearPartDropRules.IsGearPart(code));

    // The rest of BetterLoot+'s drops, the rusty gear among them, are not parts.
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

    [Theory]
    [InlineData("gear-rusty", true)]
    [InlineData(" Game:Gear-Rusty ", true)]
    [InlineData("betterloot:gear-rusty", false)]
    [InlineData("gear-temporal", false)]
    [InlineData(null, false)]
    public void The_rusty_gear(string? code, bool rusty) => Assert.Equal(rusty, GearPartDropRules.IsRustyGear(code));

    // BetterLoot+ 1.0.0's drifters: the parts go into the rusty gear drop at a quarter, and the part
    // drop goes. Averages 0.25 (surface), 1 (deep), 1.5 (tainted), 2 (corrupt), 3 (nightmare).
    [Theory]
    [InlineData(0.01, 0.25, 0.0725)]
    [InlineData(0.08, 1.0, 0.33)]
    [InlineData(0.09, 1.5, 0.465)]
    [InlineData(0.25, 2.0, 0.75)]
    [InlineData(0.4, 3.0, 1.15)]
    public void Parts_are_added_to_the_rusty_gear_drop(double gears, double parts, double total)
    {
        Drop[] drops = [new("gear-temporal", 0.01, 0), new("gear-rusty", gears, 0), new("flaxfibers", 0.2, 0),
            new("betterloot:gearpart", parts, 0.4), new("game:stick", 0.15, 0)];
        var (edits, removed) = GearPartDropRules.Plan(drops);
        var edit = Assert.Single(edits);
        Assert.Equal(1, edit.Index);
        Assert.Equal("gear-rusty", edit.Code);
        Assert.Equal(total, edit.Avg, 12);
        Assert.Equal(0.1, edit.Var, 12);
        Assert.Equal([3], removed);
    }

    [Fact]
    public void Without_a_rusty_gear_drop_the_first_part_drop_becomes_it()
    {
        Drop[] drops = [new("flint", 0.1, 0), new("betterloot:gearpart", 1, 0), new("game:stick", 0.15, 0),
            new("betterloot:gearpart", 3, 0)];
        var (edits, removed) = GearPartDropRules.Plan(drops);
        Assert.Equal([new GearPartDropRules.Edit(1, "game:gear-rusty", 1.0, 0)], edits);
        Assert.Equal([3], removed);
    }

    [Fact]
    public void Removed_indices_come_highest_first()
    {
        Drop[] drops = [new("betterloot:gearpart", 1, 0), new("gear-rusty", 0, 0), new("betterloot:gearpart", 1, 0),
            new(null, 0, 0), new("betterloot:gearpart", 2, 0)];
        var (edits, removed) = GearPartDropRules.Plan(drops);
        Assert.Equal(1, Assert.Single(edits).Index);
        Assert.Equal(1.0, edits[0].Avg, 12);
        Assert.Equal([4, 2, 0], removed);
    }

    [Fact]
    public void A_creature_without_parts_is_left_alone()
    {
        var (edits, removed) = GearPartDropRules.Plan([new("gear-rusty", 7, 2.5), new("gear-temporal", 2, 0.4)]);
        Assert.Empty(edits);
        Assert.Empty(removed);
    }
}
