using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

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
        // A trunk taller than the top of the travel: the saws are on it from the start.
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
        // It stops at the top.
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
    [InlineData(false, true, true, MillPhase.Stopped)]
    [InlineData(false, false, false, MillPhase.Stopped)]
    [InlineData(true, false, false, MillPhase.Sinking)]
    [InlineData(true, true, false, MillPhase.Cutting)]
    [InlineData(true, false, true, MillPhase.Raising)]
    [InlineData(true, true, true, MillPhase.Raising)]
    public void Phase_is_what_the_mill_is_doing(bool running, bool trunk, bool rising, MillPhase phase) =>
        Assert.Equal(phase, SawDepth.Phase(running, trunk, rising));

    private const float Turn = 2 * MathF.PI;

    private static SawCycle Run(SawCycle c, float turns, float? touch = null, int logs = 0, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
            c = SawDepth.Advance(c, turns * Turn / steps, touch, logs, 8, 6).Cycle;
        return c;
    }

    [Fact]
    public void Empty_the_saws_sink_and_rise_in_the_travel_turns_each_way()
    {
        var c = new SawCycle(0, false);
        c = Run(c, 3);
        Assert.Equal(0.5f, c.Depth, 4);
        Assert.False(c.Rising);
        c = Run(c, 3);                                  // at the bed: the trip throws the lift in
        Assert.Equal(1f, c.Depth, 4);
        c = Run(c, 1.5f);
        Assert.True(c.Rising);
        Assert.Equal(0.75f, c.Depth, 4);
        c = Run(c, 4.5f);                               // back at the top after 12 turns in all
        Assert.Equal(0f, c.Depth, 3);
        c = Run(c, 0.6f);                               // and straight on down
        Assert.False(c.Rising);
        Assert.Equal(0.1f, c.Depth, 3);
    }

    [Fact]
    public void One_step_can_turn_the_cycle_at_both_ends()
    {
        // from near the bottom going down, 7 turns: down 0.6 turns, up 6, down 0.4
        var step = SawDepth.Advance(new SawCycle(0.9f, false), 7 * Turn, null, 0, 8, 6);
        Assert.False(step.Cycle.Rising);
        Assert.Equal(0.4f / 6, step.Cycle.Depth, 3);
        Assert.True(step.PassedTop);
        Assert.False(step.CutFinished);
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.05f, true)]
    [InlineData(SawDepth.LoadWindow, true)]
    [InlineData(0.09f, false)]
    [InlineData(0.5f, false)]
    [InlineData(1f, false)]
    public void A_trunk_goes_on_only_at_the_top(float depth, bool accepted) =>
        Assert.Equal(accepted, SawDepth.AtTop(depth));

    [Fact]
    public void The_load_window_is_about_half_a_turn_each_side_of_the_top()
    {
        // at the default 6 turns for the travel, the window lasts this many turns on the way up and again on the way down
        Assert.InRange(SawDepth.LoadWindow * 6, 0.4f, 0.6f);
    }

    [Fact]
    public void A_trunk_loaded_at_the_top_is_cut_and_the_saws_then_rise()
    {
        // going down at the top: the saws drop onto the trunk, cut it in logs × 8 turns, then rise from the bed
        var c = SawDepth.Load(new SawCycle(0.02f, false), 0.5f);
        Assert.Equal(new SawCycle(0.5f, false, 0), c);
        var step = SawDepth.Advance(c, 16 * Turn, 0.5f, 4, 8, 6);   // half of 32 turns
        Assert.Equal(0.5f, step.Cycle.Progress, 4);
        Assert.Equal(0.75f, step.Cycle.Depth, 4);
        Assert.False(step.CutFinished);
        step = SawDepth.Advance(step.Cycle, 17 * Turn, 0.5f, 4, 8, 6);
        Assert.True(step.CutFinished);
        Assert.True(step.Cycle.Rising);
        Assert.Equal(1f, step.Cycle.Depth);
        Assert.Equal(0f, step.Cycle.Progress);
    }

    [Fact]
    public void A_trunk_loaded_on_the_last_of_the_rise_waits_for_the_top_then_drops()
    {
        var c = SawDepth.Load(new SawCycle(0.05f, true), 0.5f);
        Assert.True(c.Rising);
        Assert.Equal(0.05f, c.Depth);
        var step = SawDepth.Advance(c, 0.3f * Turn + 0.001f, 0.5f, 4, 8, 6);   // to the top, then onto the trunk
        Assert.True(step.PassedTop);
        Assert.False(step.Cycle.Rising);
        Assert.Equal(0.5f, step.Cycle.Depth, 3);
    }

    [Fact]
    public void Near_the_top_a_tall_trunk_takes_the_saws_back_onto_its_top()
    {
        // a two-block trunk's top is just under the top of the travel: loaded at 0.06, the cut starts at its top
        var c = SawDepth.Load(new SawCycle(0.06f, false), 0.03f);
        Assert.Equal(0.03f, c.Depth);
        Assert.Equal(0f, c.Progress);
    }

    [Fact]
    public void Taking_the_trunk_out_early_lets_the_saws_carry_on_down_empty()
    {
        var c = SawDepth.Advance(SawDepth.Load(new SawCycle(0, false), 0.5f), 3.2f * Turn, 0.5f, 4, 8, 6).Cycle;
        float depth = c.Depth;
        Assert.InRange(c.Progress, 0.09f, 0.11f);
        // the trunk is gone: the same state goes on at the empty rate (1/6 of the travel a turn)
        var on = SawDepth.Advance(c with { Progress = 0 }, Turn, null, 0, 8, 6).Cycle;
        Assert.Equal(depth + 1f / 6, on.Depth, 4);
        Assert.False(on.Rising);
    }

    [Fact]
    public void Stopping_and_resuming_gives_the_same_cycle_as_running_straight_through()
    {
        var start = new SawCycle(0.3f, false);
        var straight = Run(start, 20);
        var parts = Run(start, 20, steps: 37);
        Assert.Equal(straight.Rising, parts.Rising);
        Assert.Equal(straight.Depth, parts.Depth, 3);
        // no turning: nothing moves
        Assert.Equal(start, SawDepth.Advance(start, 0, null, 0, 8, 6).Cycle);
        Assert.Equal(start, SawDepth.Advance(start, -1, null, 0, 8, 6).Cycle);
    }

    [Fact]
    public void Sinking_without_turning_does_nothing_and_stops_at_the_bed()
    {
        Assert.Equal(0.3f, SawDepth.Sink(0.3f, 0, 6));
        Assert.Equal(1f, SawDepth.Sink(0.9f, 10, 6));
        Assert.Equal(0.25f, SawDepth.Sink(0, Turn, 4), 4);
    }

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
