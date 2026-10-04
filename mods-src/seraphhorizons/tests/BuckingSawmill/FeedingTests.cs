using SeraphHorizons.Mod.BuckingSawmill.Core;

namespace SeraphHorizons.Mod.Tests;

public class FeedingTests
{
    [Fact]
    public void Winding_by_hand_takes_the_whole_travel_in_its_seconds()
    {
        Assert.Equal(0.5f, Feeding.Wind(1f, Feeding.HandWindSeconds / 2), 5);
        Assert.Equal(0f, Feeding.Wind(1f, Feeding.HandWindSeconds), 5);
        // in ticks, as the server winds
        float depth = 1f;
        int ticks = 0;
        while (depth > 0 && ticks < 1000)
        {
            depth = Feeding.Wind(depth, 0.05f);
            ticks++;
        }
        // 40 ticks of 50 ms, give or take one for the float steps
        Assert.InRange(ticks, (int)Math.Round(Feeding.HandWindSeconds / 0.05f), (int)Math.Round(Feeding.HandWindSeconds / 0.05f) + 1);
    }

    [Fact]
    public void Winding_stops_at_the_top_and_never_goes_down()
    {
        Assert.Equal(0f, Feeding.Wind(0.1f, 5f));
        Assert.Equal(0.7f, Feeding.Wind(0.7f, 0f));
        Assert.Equal(0.7f, Feeding.Wind(0.7f, -1f));
        Assert.Equal(0f, Feeding.Wind(0.7f, 0.1f, windSeconds: 0));
    }

    [Theory]
    [InlineData(false, 0.5f, true)]    // stopped with the saws down: an empty hand winds them up
    [InlineData(false, 1f, true)]
    [InlineData(false, 0.05f, false)]  // at the top already: an empty hand loads
    [InlineData(false, 0f, false)]
    [InlineData(true, 0.5f, false)]    // turning: an empty hand loads (and waits for the top)
    public void An_empty_hand_winds_up_only_a_stopped_mill_with_its_saws_down(bool running, float depth, bool winds) =>
        Assert.Equal(winds, Feeding.WindsUp(running, depth));

    // The load window is narrow; a fast shaft can step over it in one tick. A trunk waiting (in a
    // hand that is held, or on a rack) goes on on the tick the saws pass the top whatever the depth
    // after the tick.
    [Fact]
    public void A_trunk_can_go_on_when_the_saws_passed_the_top_this_tick()
    {
        Assert.True(Feeding.CanTakeTrunk(0f, false));
        Assert.True(Feeding.CanTakeTrunk(SawDepth.LoadWindow, false));
        Assert.False(Feeding.CanTakeTrunk(0.5f, false));
        Assert.True(Feeding.CanTakeTrunk(0.5f, true));

        // a tick of a turn and a half on a one-turn travel: from rising at 0.5 over the top and
        // down to 0.5 again, never in the window at the end of a tick
        var step = SawDepth.Advance(new SawCycle(0.5f, true), 1.0f * 2 * MathF.PI, null, 0, 8, 1);
        Assert.True(step.PassedTop);
        Assert.False(SawDepth.AtTop(step.Cycle.Depth));
        Assert.True(Feeding.CanTakeTrunk(step.Cycle.Depth, step.PassedTop));
    }

    [Fact]
    public void Winding_goes_on_to_the_very_top_once_begun()
    {
        Assert.True(Feeding.KeepsWinding(false, 0.05f));
        Assert.False(Feeding.WindsUp(false, 0.05f));
        Assert.False(Feeding.KeepsWinding(false, 0f));
        Assert.False(Feeding.KeepsWinding(true, 0.5f));
    }
}
