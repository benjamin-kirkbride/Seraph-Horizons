using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class CrateSlotRulesTests
{
    [Theory]
    [InlineData(16, 21)] // the game's aged crate, Material Needs' dark aged crate
    [InlineData(20, 26)] // the game's other woods, Material Needs' other dark aged types
    [InlineData(25, 32)] // ebony and purpleheart: the roomiest crate holds 32
    [InlineData(1, 2)]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    public void Slots_scale_by_32_over_25_rounded_up(int before, int after) =>
        Assert.Equal(after, CrateSlotRules.Slots(before));

    [Fact]
    public void The_roomiest_crate_holds_32() =>
        Assert.Equal(CrateSlotRules.BestSlots, CrateSlotRules.Slots(CrateSlotRules.BestBefore));
}
