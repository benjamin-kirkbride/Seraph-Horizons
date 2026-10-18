using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>Core/SpanningTake.cs: a dialogue's stack taken across slots (the elk's 100 rusty gears
/// in stacks of 64), exactly the quantity asked, only when no one slot holds it.</summary>
public class SpanningTakeTests
{
    [Fact]
    public void HundredGearsInTwoStacksAreTakenExactly()
    {
        var plan = SpanningTake.Plan([64, 36], 100);
        Assert.Equal([64, 36], plan);
        Assert.Equal(100, plan!.Sum());
    }

    [Fact]
    public void TakesInSlotOrderAndLeavesTheRest()
    {
        Assert.Equal([64, 11, 0], SpanningTake.Plan([64, 64, 20], 75));
        Assert.Equal(75, SpanningTake.Plan([64, 64, 20], 75)!.Sum());
        Assert.Equal([30, 0, 40, 30], SpanningTake.Plan([30, 0, 40, 64], 100));
    }

    [Theory]
    [InlineData(new[] { 50 }, 100)]          // too few
    [InlineData(new[] { 64, 35 }, 100)]      // 99
    [InlineData(new[] { 64, 64 }, 50)]       // one slot holds it: the game's own case
    [InlineData(new int[0], 1)]
    public void NothingWhenTooFewOrOneSlotSuffices(int[] sizes, int want) =>
        Assert.Null(SpanningTake.Plan(sizes, want));
}
