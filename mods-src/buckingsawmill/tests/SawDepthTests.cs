using BuckingSawmill.Core;

namespace BuckingSawmill.Tests;

public class SawDepthTests
{
    private static readonly TrunkBed Bed = new(new Float3(3, 0.5f, 1.5f), Axis.X, 5);

    [Theory]
    [InlineData("xs", 1)]
    [InlineData("sm", 1)]
    [InlineData("md", 1)]
    [InlineData("lg", 1)]
    [InlineData("xl", 2)]
    [InlineData("xxl", 2)]
    [InlineData(null, 1)]
    public void Trunk_thickness_is_by_size(string? size, int blocks) =>
        Assert.Equal(blocks, SawDepth.TrunkThickness(size));

    [Fact]
    public void Touch_is_where_the_saws_meet_the_trunk_top()
    {
        // Default travel 3.0 to 0.5, bed at 0.5: a 1-high trunk's top is at 1.5, 1.5 / 2.5 down.
        Assert.Equal(0.6f, SawDepth.Touch(SawTravel.Default, Bed, "md"), 5);
        // A 2-high trunk's top is at 2.5, 0.5 / 2.5 down.
        Assert.Equal(0.2f, SawDepth.Touch(SawTravel.Default, Bed, "xl"), 5);
        Assert.Equal(0.2f, SawDepth.Touch(SawTravel.Default, Bed, "xxl"), 5);
    }

    [Fact]
    public void Touch_is_clamped_to_the_travel()
    {
        // A trunk taller than the latch height: the saws are on it from the start.
        Assert.Equal(0f, SawDepth.Touch(new SawTravel(2.0f, 0.5f), Bed, "xl"));
        // A bed below the saws' bottom: the trunk top is still under it, so the saws touch at the bottom.
        Assert.Equal(1f, SawDepth.Touch(new SawTravel(3.0f, 2.0f), new TrunkBed(new Float3(0, -1, 0), Axis.X, 1), "sm"));
        // No bed: the trunk lies at the saws' bottom.
        Assert.Equal(0.6f, SawDepth.Touch(SawTravel.Default, null, "sm"), 5);
    }

    [Fact]
    public void Cutting_runs_from_touch_to_the_bed()
    {
        Assert.Equal(0.6f, SawDepth.Cutting(0.6f, 0), 5);
        Assert.Equal(0.8f, SawDepth.Cutting(0.6f, 0.5f), 5);
        Assert.Equal(1f, SawDepth.Cutting(0.6f, 1), 5);
        Assert.Equal(1f, SawDepth.Cutting(0.6f, 1.3f), 5);
        Assert.Equal(0.25f, SawDepth.Cutting(0, 0.25f), 5);
    }

    [Fact]
    public void Raising_takes_the_configured_turns_from_the_bed()
    {
        float depth = 1;
        for (int i = 0; i < 5; i++)
            depth = SawDepth.Raise(depth, 2 * MathF.PI, 6);
        Assert.Equal(1f / 6, depth, 4);
        depth = SawDepth.Raise(depth, 2 * MathF.PI, 6);
        Assert.Equal(0f, depth, 4);
        // It stops at the latch.
        Assert.Equal(0f, SawDepth.Raise(depth, 10, 6));
        Assert.Equal(0f, SawDepth.Raise(0.1f, 4 * MathF.PI, 1));
    }

    [Fact]
    public void Raising_from_part_way_down_takes_proportionally_less()
    {
        // From 0.5 down, half of 4 turns.
        Assert.Equal(0f, SawDepth.Raise(0.5f, 2 * 2 * MathF.PI, 4), 4);
        Assert.Equal(0.25f, SawDepth.Raise(0.5f, 2 * MathF.PI, 4), 4);
    }

    [Fact]
    public void Raising_without_turning_does_nothing()
    {
        Assert.Equal(0.7f, SawDepth.Raise(0.7f, 0, 6));
        Assert.Equal(0.7f, SawDepth.Raise(0.7f, -1, 6));
    }

    [Theory]
    [InlineData(true, 0f, MillPhase.Cutting)]
    [InlineData(true, 0.5f, MillPhase.Cutting)]
    [InlineData(false, 0.5f, MillPhase.Raising)]
    [InlineData(false, 0.001f, MillPhase.Raising)]
    [InlineData(false, 0f, MillPhase.Idle)]
    public void Phase_follows_the_trunk_and_depth(bool trunk, float depth, MillPhase phase) =>
        Assert.Equal(phase, SawDepth.Phase(trunk, depth));

    [Fact]
    public void Easing_closes_most_of_a_drop_in_a_fraction_of_a_second()
    {
        float shown = 0;
        for (int i = 0; i < 5; i++)   // a quarter second of 50 ms client ticks
            shown = SawDepth.Ease(shown, 0.6f, 0.05f);
        Assert.InRange(shown, 0.55f, 0.6f);
        for (int i = 0; i < 10; i++)
            shown = SawDepth.Ease(shown, 0.6f, 0.05f);
        Assert.Equal(0.6f, shown);
        // Already there, or no time: it stays.
        Assert.Equal(0.6f, SawDepth.Ease(0.6f, 0.6f, 0.05f));
        Assert.Equal(0.3f, SawDepth.Ease(0.3f, 0.6f, 0));
        // Never past the target.
        Assert.InRange(SawDepth.Ease(1, 0, 10), 0, 0);
    }
}
