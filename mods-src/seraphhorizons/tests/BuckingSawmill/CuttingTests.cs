using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

public class CuttingTests
{
    private const float Turn = 2 * MathF.PI;

    [Fact]
    public void A_trunk_takes_revolutions_per_log_times_its_logs()
    {
        // 12 logs at 8 revolutions each: 96 turns.
        float total = 0;
        for (int i = 0; i < 96; i++)
            total += Cutting.ProgressFor(Turn, 12, 8);
        Assert.Equal(1f, total, 3);
        Assert.Equal(0.5f, Cutting.ProgressFor(48 * Turn, 12, 8), 4);
    }

    [Fact]
    public void Progress_does_not_depend_on_how_the_turning_is_sliced()
    {
        float sliced = 0;
        for (int i = 0; i < 1000; i++)
            sliced += Cutting.ProgressFor(Turn / 1000, 3, 8);
        Assert.Equal(Cutting.ProgressFor(Turn, 3, 8), sliced, 5);
    }

    [Theory]
    [InlineData(0.1f, 0.3f, 0.2f)]
    [InlineData(0.3f, 0.1f, 0.2f)]
    // Across the 2π wrap, both ways.
    [InlineData(6.2f, 0.1f, 0.18318531f)]
    [InlineData(0.1f, 6.2f, 0.18318531f)]
    public void Angle_advance_is_the_short_way_round(float last, float now, float expected) =>
        Assert.Equal(expected, Cutting.AngleAdvance(last, now, speed: 0.1f, dt: 0.05f), 4);

    [Fact]
    public void Angle_advance_trusts_the_speed_past_half_a_turn()
    {
        // 5 rad/s per unit of speed: 1.0 for 1 s is 5 rad, more than π, so readings are ambiguous.
        Assert.Equal(5f, Cutting.AngleAdvance(0, 0.5f, speed: 1f, dt: 1f), 4);
        Assert.Equal(0.5f, Cutting.AngleAdvance(0, 0.5f, speed: 1f, dt: 0.05f), 4);
    }

    [Theory]
    [InlineData(12, 2f, 24)]
    [InlineData(1, 2f, 2)]
    [InlineData(5, 1.5f, 7)]
    [InlineData(3, 0.1f, 0)]
    [InlineData(10, 0.3f, 3)]
    [InlineData(48, 2f, 96)]
    [InlineData(7, 0f, 0)]
    public void Yield_is_rounded_down(int logs, float perLog, int expected) =>
        Assert.Equal(expected, Cutting.LogYield(logs, perLog));

    [Theory]
    [InlineData(12, 0.25f, 3)]
    [InlineData(1, 0.25f, 1)]
    [InlineData(5, 0.25f, 2)]
    [InlineData(8, 0.25f, 2)]
    [InlineData(10, 0.1f, 1)]
    [InlineData(4, 0f, 0)]
    public void Wear_is_rounded_up(int logs, float perLog, int expected) =>
        Assert.Equal(expected, Cutting.BladeWear(logs, perLog));

    // The default wear: a log's worth of durability per stored log.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(48, 48)]
    public void Default_wear_is_one_per_stored_log(int logs, int expected) =>
        Assert.Equal(expected, Cutting.BladeWear(logs, new MillConfig().BladeWearPerStoredLog));

    // The game's saw tiers: copper, gold and silver 2, bronzes 3, iron and meteoric iron 4, steel 5.
    [Theory]
    [InlineData(2, 1f)]
    [InlineData(3, 1.35f)]
    [InlineData(4, 1.7f)]
    [InlineData(5, 2.05f)]
    [InlineData(1, 1f)]
    [InlineData(0, 1f)]
    [InlineData(null, 1f)]
    public void Blade_speed_rises_with_the_tool_tier_above_copper(int? tier, float expected) =>
        Assert.Equal(expected, Cutting.BladeSpeed(tier, new MillConfig().BladeSpeedPerTier), 4);

    [Fact]
    public void Blade_speed_per_tier_is_the_setting()
    {
        Assert.Equal(1f, Cutting.BladeSpeed(5, 0f));
        Assert.Equal(2f, Cutting.BladeSpeed(5, 1f / 3), 4);
        Assert.Equal(1f, Cutting.BladeSpeed(5, float.NaN));
    }

    // A faster blade shortens the cut and nothing else: the windlass's travel is unchanged, and
    // the cut stays proportional to the stored logs.
    [Fact]
    public void A_faster_blade_cuts_in_fewer_turns()
    {
        float steel = Cutting.CutRevolutions(8, 2f);
        Assert.Equal(4f, steel);
        Assert.Equal(8f, Cutting.CutRevolutions(8, 1f));
        Assert.Equal(8f, Cutting.CutRevolutions(8, 0f));
        // 12 logs: 48 turns with steel, 96 with copper.
        Assert.Equal(1f, Cutting.ProgressFor(48 * Turn, 12, steel), 4);
        Assert.Equal(0.5f, Cutting.ProgressFor(48 * Turn, 12, 8), 4);
        Assert.Equal(1f, Cutting.ProgressFor(24 * Turn, 6, steel), 4);

        var empty = SawDepth.Advance(new SawCycle(0, false), 3 * Turn, null, 0, steel, 6);
        Assert.Equal(0.5f, empty.Cycle.Depth, 4);
        var raise = SawDepth.Advance(new SawCycle(1, true), 3 * Turn, null, 0, steel, 6);
        Assert.Equal(0.5f, raise.Cycle.Depth, 4);
        var cut = SawDepth.Advance(new SawCycle(0.5f, false), 24 * Turn, 0.5f, 12, steel, 6);
        Assert.Equal(0.5f, cut.Cycle.Progress, 4);
    }

    [Theory]
    [InlineData(0, 64, new int[0])]
    [InlineData(24, 64, new[] { 24 })]
    [InlineData(96, 64, new[] { 64, 32 })]
    [InlineData(128, 64, new[] { 64, 64 })]
    [InlineData(3, 1, new[] { 1, 1, 1 })]
    [InlineData(2, 0, new[] { 1, 1 })]
    public void Logs_are_split_by_stack_size(int total, int max, int[] expected) =>
        Assert.Equal(expected, Cutting.SplitStacks(total, max));

    [Fact]
    public void A_trunk_is_recoverable_below_a_quarter()
    {
        Assert.True(Cutting.Recoverable(0));
        Assert.True(Cutting.Recoverable(0.2499f));
        Assert.False(Cutting.Recoverable(0.25f));
        Assert.False(Cutting.Recoverable(0.9f));
    }

    // As Logging Expanded's TreeTrunkInventory.WoodType reads it, grown- removed as its callers do.
    [Theory]
    [InlineData("log-placed-oak-ud", "oak")]
    [InlineData("log-placed-grown-oak-ud", "oak")]
    [InlineData("log-placed-baldcypress-ud", "baldcypress")]
    [InlineData("log-placed-red-mangrove-ud", "red-mangrove")]
    [InlineData("log-grown-pine-ud", "pine")]
    [InlineData("log-oak-ud", "oak")]
    [InlineData("firewood", null)]
    [InlineData("log-placed-", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Wood_is_read_from_the_stored_log(string? path, string? wood) =>
        Assert.Equal(wood, Cutting.WoodOfStoredLog(path));
}
