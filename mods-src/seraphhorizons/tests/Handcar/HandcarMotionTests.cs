using SeraphHorizons.Mod.Handcar.Core;
using Xunit;

namespace SeraphHorizons.Tests.Handcar;

/// <summary>The client's timing of the pump (Handcar/Core/HandcarMotion.cs): the distance rolled, the
/// stroke's phase and frame, the effort's fade and the easing set ahead of the game's own step.</summary>
public class HandcarMotionTests
{
    [Fact]
    public void Travel_counts_forward_along_the_cars_yaw_and_back_against_it()
    {
        var t = new TravelTracker();
        double yaw = Math.PI / 2;                    // VS: the front is (sin yaw, cos yaw): +x
        t.Update(10, 5, 10, yaw);
        Assert.Equal(0, t.Distance);
        t.Update(10.5, 5, 10, yaw);
        Assert.Equal(0.5, t.Distance, 9);
        t.Update(10.2, 5, 10, yaw);
        Assert.Equal(0.2, t.Distance, 9);
        // a slope counts its whole length
        t.Update(10.5, 5.4, 10, yaw);
        Assert.Equal(0.7, t.Distance, 9);
        // a jump (a teleport, a reload) adds nothing
        t.Update(100, 5.4, 10, yaw);
        Assert.Equal(0.7, t.Distance, 9);
        // sideways adds nothing
        t.Update(100, 5.4, 10.3, yaw);
        Assert.Equal(0.7, t.Distance, 9);
    }

    [Fact]
    public void The_phase_wraps_every_stroke_and_the_frame_stays_short_of_the_end()
    {
        double d = 6 * Math.PI * 5 / 16;             // the shipped stroke: three turns of a 5-voxel wheel
        Assert.Equal(0, HandcarPhase.Of(0, d), 9);
        Assert.Equal(0.25, HandcarPhase.Of(d * 2.25, d), 9);
        Assert.Equal(0.75, HandcarPhase.Of(-d * 0.25, d), 9);
        double whole = HandcarPhase.Of(d * 3, d);
        Assert.True(Math.Min(whole, 1 - whole) < 1e-9, $"three strokes in, the phase is {whole}");
        Assert.Equal(15f, HandcarPhase.Frame(0.25, 60), 4);
        Assert.True(HandcarPhase.Frame(0.99999999, 60) < 60);
        Assert.Equal(0, HandcarPhase.Of(5, 0));
    }

    [Fact]
    public void The_effort_fades_in_and_out_over_its_time_eased_and_the_weights_add_up_to_the_mount()
    {
        var f = new EffortFade();
        Assert.Equal(0, f.Eased);
        for (int i = 0; i < 7; i++)
            f.Advance(true, 0.05f, 0.35);
        Assert.Equal(1, f.Value, 6);
        Assert.Equal(1, f.Eased, 6);
        f.Advance(false, 0.175f, 0.35);
        Assert.Equal(0.5, f.Value, 6);
        Assert.Equal(0.5, f.Eased, 6);
        for (double m = 0; m <= 1; m += 0.25)
        {
            var (grip, pump) = f.Weights(m);
            Assert.Equal(m, grip + pump, 5);
        }
        // eased: gentle at both ends
        var g = new EffortFade();
        g.Advance(true, 0.035f, 0.35);
        Assert.True(g.Eased < g.Value);
    }

    [Fact]
    public void Easing_set_ahead_of_the_games_step_lands_on_the_target()
    {
        foreach (bool active in new[] { true, false })
            foreach (double step in new[] { 0.0, 0.016, 0.05, 0.2 })
                foreach (double target in new[] { 0.2, 0.5, 0.8, 1.0 })
                {
                    if (active && target < step || !active && target > 1 - step)
                        continue;                    // out of reach: the game's step alone passes the target
                    double set = AnimationEasing.Before(target, active, step);
                    Assert.InRange(set, 0, 1);
                    Assert.Equal(target, AnimationEasing.GameStep(set, active, step), 5);
                }
        Assert.Equal(0f, AnimationEasing.Before(0.01, true, 0.1));
        Assert.Equal(1f, AnimationEasing.Before(1, false, 0.5));
    }
}
