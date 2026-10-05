using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserVisualsTests
{
    [Fact]
    public void Presence_eases_in_and_out_and_lands()
    {
        float p = 0;
        p = RosserVisuals.EasePresence(p, true, 0.05f);
        Assert.InRange(p, 0.4f, 0.5f);              // 1 − e^−0.6
        for (int i = 0; i < 20; i++)
            p = RosserVisuals.EasePresence(p, true, 0.05f);
        Assert.Equal(1f, p);
        p = RosserVisuals.EasePresence(p, false, 0.05f);
        Assert.InRange(p, 0.5f, 0.6f);
        for (int i = 0; i < 20; i++)
            p = RosserVisuals.EasePresence(p, false, 0.05f);
        Assert.Equal(0f, p);
        Assert.Equal(0.3f, RosserVisuals.EasePresence(0.3f, true, -1));
    }

    [Fact]
    public void The_shown_class_is_held_while_the_trunk_eases_out()
    {
        Assert.Equal(2, RosserVisuals.ShownClass(2, 1, 0));
        Assert.Equal(1, RosserVisuals.ShownClass(0, 1, 0.3f));
        Assert.Equal(0, RosserVisuals.ShownClass(0, 1, 0));
        Assert.Equal(0, RosserVisuals.ShownClass(0, 0, 0.5f));
    }

    [Fact]
    public void The_client_estimate_follows_the_shaft_and_resyncs_on_a_gap()
    {
        Assert.Equal(0.5, RosserVisuals.EstimateTravel(0.3, 0.1, 2, 9), 9);
        Assert.Equal(9, RosserVisuals.EstimateTravel(8.95, 0.1, 2, 9), 9);
        Assert.Equal(0.3, RosserVisuals.EstimateTravel(0.3, 0.1, -2, 9), 9);
        Assert.Equal(1.0, RosserVisuals.Reconcile(1.0, 1.05));          // within a sixteenth: keep
        Assert.Equal(1.1, RosserVisuals.Reconcile(1.0, 1.1));
        Assert.Equal(0, RosserVisuals.Reconcile(5, 0));                 // a new trunk
        Assert.Equal(1.25, RosserVisuals.Interpolate(1, 2, 0.25f), 9);
        Assert.Equal(2, RosserVisuals.Interpolate(1, 2, 3), 9);
    }

    [Fact]
    public void Feed_travel_follows_the_trunk_and_never_goes_back()
    {
        var rig = RosserFixture.Rig();
        double b = rig.Feed.BlocksPerRadian;
        Assert.Equal(1 / b, RosserVisuals.FeedAdvance(2, 3, b), 6);
        Assert.Equal(0, RosserVisuals.FeedAdvance(9, 0, b));            // next trunk
        Assert.Equal(0, RosserVisuals.FeedAdvance(3, 2.9, b));          // a sync behind the estimate
        Assert.Equal(0, RosserVisuals.FeedAdvance(1, 2, 0));
    }

    [Theory]
    [InlineData(TrunkClass.Thin, 3)]
    [InlineData(TrunkClass.Thick, 5)]
    public void Over_a_whole_trip_phi_turns_the_rolls_exactly_as_far_as_the_trunk_goes(TrunkClass k, int? tier)
    {
        // The renderer accumulates φ from the shown T; through the test rig's feed driver (a top roll
        // of 0.25-block radius at 4/14 of φ) the roll's surface moves exactly T.
        var rig = RosserFixture.Rig();
        var pace = new RosserPace(rig, new RosserConfig());
        var trip = RosserTrip.Load(pace, k, 30, 20, tier);
        double phi = 0, last = 0, psi = 0;
        while (trip.State(pace) != RosserState.Delivered)
        {
            trip = trip.Advance(pace, 0.3).Trip;
            psi += 0.3;
            phi += RosserVisuals.FeedAdvance(last, trip.Travel, rig.Feed.BlocksPerRadian);
            last = trip.Travel;
        }
        Assert.Equal(trip.End(pace), phi * rig.Feed.BlocksPerRadian, 6);
        Assert.Equal(0.25 * 4 / 14, rig.Feed.BlocksPerRadian, 6);
        // and φ/ψ is the class's feed ratio (the end step is cut short, so compare the rate)
        Assert.Equal(pace.FeedRatio((int)k, tier, rig.Feed.BlocksPerRadian), trip.Rate / rig.Feed.BlocksPerRadian, 9);
        Assert.True(phi <= psi * pace.FeedRatio((int)k, tier, rig.Feed.BlocksPerRadian) + 1e-9);
    }
}
