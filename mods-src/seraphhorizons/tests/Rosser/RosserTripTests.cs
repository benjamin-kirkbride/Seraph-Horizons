using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserTripTests
{
    // The test rig: nose0 −9.875, breaker −8.875, ring −7.5, tailStop −4.25; L thin 4, thick 5.
    private static readonly RosserPace Pace = RosserFixture.Pace();

    private static RosserTrip Load(TrunkClass k = TrunkClass.Thin, int logs = 20, int branches = 15, int? tier = null) =>
        RosserTrip.Load(Pace, k, logs, branches, tier);

    /// <summary>Runs a trip to the end in steps of <paramref name="radians"/>, adding up what dropped.</summary>
    private static (RosserTrip Trip, int Sticks, int Bark, int Deliveries) Run(RosserTrip trip, double radians, int maxSteps = 100000)
    {
        int sticks = 0, bark = 0, delivered = 0;
        for (int i = 0; i < maxSteps && trip.State(Pace) != RosserState.Delivered; i++)
        {
            var (next, step) = trip.Advance(Pace, radians);
            Assert.True(next.Travel >= trip.Travel);
            sticks += step.Sticks;
            bark += step.BarkLogs;
            delivered += step.Delivered ? 1 : 0;
            trip = next;
        }
        return (trip, sticks, bark, delivered);
    }

    [Fact]
    public void A_loaded_trunk_waits_at_zero_with_its_rate()
    {
        var trip = Load(tier: 4);
        Assert.Equal(RosserState.Waiting, trip.State(Pace));
        Assert.Equal(0, trip.Travel);
        Assert.Equal(Pace.Rate(1, 4), trip.Rate);
        Assert.Equal(9.625, trip.End(Pace), 6);
        Assert.Equal(-9.875, trip.Nose(Pace), 6);
        Assert.Equal(-13.875, trip.Tail(Pace), 6);
        Assert.Equal(RosserState.Empty, RosserTrip.None.State(Pace));
        Assert.Equal(RosserTrip.None, RosserTrip.Load(Pace, TrunkClass.None, 5, 5, null));
    }

    [Theory]
    [InlineData(TrunkClass.Thin, 20, 15, 7)]
    [InlineData(TrunkClass.Thick, 40, 30, 15)]
    [InlineData(TrunkClass.Thin, 3, 0, 0)]
    [InlineData(TrunkClass.Thin, 1, 1, 0)]
    [InlineData(TrunkClass.Thick, 48, 3, 1)]
    public void A_whole_trip_drops_every_stick_and_bark_once_and_delivers_once(TrunkClass k, int logs, int branches, int sticks)
    {
        var trip = Load(k, logs, branches);
        Assert.Equal(sticks, trip.Sticks(Pace));
        foreach (double step in new[] { 0.05, 0.7, 3.0, 1000.0 })
        {
            var (end, s, b, d) = Run(trip, step);
            Assert.Equal(RosserState.Delivered, end.State(Pace));
            Assert.Equal(end.End(Pace), end.Travel);
            Assert.Equal(sticks, s);
            Assert.Equal(logs, b);
            Assert.Equal(1, d);
            Assert.Equal(sticks, end.SticksDone);
            Assert.Equal(logs, end.BarkDone);
        }
    }

    [Fact]
    public void A_typical_trip_takes_the_typical_turns()
    {
        int turns = 0;
        var trip = Load(TrunkClass.Thick, 25, 61);
        while (trip.State(Pace) != RosserState.Delivered)
        {
            trip = trip.Advance(Pace, 2 * Math.PI).Trip;
            turns++;
        }
        // whole turns: the first one that reaches the end
        Assert.Equal((int)Math.Ceiling(Pace.Config.TypicalTurns(2)), turns);
    }

    [Fact]
    public void Sticks_drop_evenly_as_the_trunk_passes_the_breaker()
    {
        // 10 branches → 5 sticks on a 4-block thin trunk: one per 0.8 blocks of nose past the breaker (1 block from nose0).
        var trip = Load(TrunkClass.Thin, 20, 10);
        Assert.Equal(0, trip.SticksDue(Pace));
        Assert.Equal(0, (trip with { Travel = 1.0 }).SticksDue(Pace));       // nose at the breaker
        Assert.Equal(0, (trip with { Travel = 1.79 }).SticksDue(Pace));
        Assert.Equal(1, (trip with { Travel = 1.8 }).SticksDue(Pace));
        Assert.Equal(2, (trip with { Travel = 2.6 }).SticksDue(Pace));
        Assert.Equal(4, (trip with { Travel = 4.99 }).SticksDue(Pace));
        Assert.Equal(5, (trip with { Travel = 5.0 }).SticksDue(Pace));       // tail at the breaker
        Assert.Equal(5, (trip with { Travel = 9.0 }).SticksDue(Pace));
    }

    [Fact]
    public void Bark_drops_per_log_as_the_trunk_passes_the_ring()
    {
        // 8 logs on a 5-block thick trunk: one per 0.625 blocks of nose past the ring (2.375 from nose0).
        var trip = Load(TrunkClass.Thick, 8, 0);
        Assert.Equal(0, (trip with { Travel = 2.375 }).BarkDue(Pace));
        Assert.Equal(0, (trip with { Travel = 2.99 }).BarkDue(Pace));
        Assert.Equal(1, (trip with { Travel = 3.0 }).BarkDue(Pace));
        Assert.Equal(4, (trip with { Travel = 4.875 }).BarkDue(Pace));
        Assert.Equal(8, (trip with { Travel = 7.375 }).BarkDue(Pace));
        // the sticks come before the bark: nothing is scraped before the ring
        var early = trip with { Travel = 2.3 };
        Assert.Equal(0, early.BarkDue(Pace));
    }

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(1.0, 0, 0)]
    [InlineData(1.0001, 0, 0)]
    [InlineData(1.8, 1, 0)]
    [InlineData(3.0, 2, 3)]
    [InlineData(4.4, 4, 10)]
    [InlineData(9.0, 5, 20)]
    public void Nothing_drops_twice_across_a_save_and_reload(double travel, int sticksBefore, int barkBefore)
    {
        // Run to T, "save" (T, rate, counters), reload, and finish: the totals are exact.
        var trip = Load(TrunkClass.Thin, 20, 10);
        int sticks = 0, bark = 0;
        while (trip.Travel < travel)
        {
            var (next, step) = trip.Advance(Pace, Math.Min(0.37, (travel - trip.Travel) / trip.Rate));
            sticks += step.Sticks;
            bark += step.BarkLogs;
            trip = next;
        }
        Assert.Equal(sticks, trip.SticksDone);
        Assert.Equal(bark, trip.BarkDone);
        Assert.Equal(sticksBefore, sticks);
        Assert.Equal(barkBefore, bark);
        var reloaded = RosserTrip.Restore(Pace, TrunkClass.Thin, 20, 10, trip.Travel, trip.Rate, trip.SticksDone, trip.BarkDone, null);
        Assert.Equal(trip, reloaded);
        Assert.Equal(TripStep.Nothing, reloaded.Advance(Pace, 0).Step);
        var (end, s, b, d) = Run(reloaded, 0.5);
        Assert.Equal(5, sticks + s);
        Assert.Equal(20, bark + b);
        Assert.Equal(1, d);
        Assert.Equal(RosserState.Delivered, end.State(Pace));
    }

    [Fact]
    public void A_save_without_counters_counts_what_was_due_as_dropped()
    {
        var restored = RosserTrip.Restore(Pace, TrunkClass.Thin, 20, 10, 4.4, 0, null, null, null);
        Assert.Equal(4, restored.SticksDone);
        Assert.Equal(10, restored.BarkDone);
        Assert.Equal(Pace.Rate(1, null), restored.Rate);            // a missing rate is recomputed
        Assert.Equal(TripStep.Nothing, restored.Advance(Pace, 0).Step);
    }

    [Fact]
    public void Restore_clamps()
    {
        var r = RosserTrip.Restore(Pace, TrunkClass.Thin, 20, 10, 99, double.NaN, 99, 99, 5);
        Assert.Equal(9.625, r.Travel, 9);
        Assert.Equal(RosserState.Delivered, r.State(Pace));
        Assert.Equal(5, r.SticksDone);
        Assert.Equal(20, r.BarkDone);
        Assert.Equal(Pace.Rate(1, 5), r.Rate);
        Assert.Equal(0, RosserTrip.Restore(Pace, TrunkClass.Thin, 20, 10, double.NaN, 1, 0, 0, null).Travel);
        Assert.Equal(0, RosserTrip.Restore(Pace, TrunkClass.Thin, 20, 10, -3, 1, 0, 0, null).Travel);
        Assert.Equal(RosserTrip.None, RosserTrip.Restore(Pace, TrunkClass.None, 20, 10, 3, 1, 0, 0, null));
    }

    [Fact]
    public void Counters_ahead_of_travel_never_drop_again()
    {
        // e.g. the stick fraction was lowered, or a sync wobble: drops never go negative and counters never fall
        var trip = Load(TrunkClass.Thin, 20, 10) with { Travel = 2, SticksDone = 4, BarkDone = 0 };
        var (next, step) = trip.Advance(Pace, 1);
        Assert.Equal(0, step.Sticks);
        Assert.Equal(4, next.SticksDone);
    }

    [Fact]
    public void Advancing_empty_or_delivered_does_nothing()
    {
        Assert.Equal((RosserTrip.None, TripStep.Nothing), RosserTrip.None.Advance(Pace, 100));
        var done = Run(Load(), 1000).Trip;
        Assert.Equal((done, TripStep.Nothing), done.Advance(Pace, 100));
        var trip = Load();
        Assert.Equal(trip, trip.Advance(Pace, -5).Trip);
        Assert.Equal(trip, trip.Advance(Pace, double.NaN).Trip);
    }

    [Fact]
    public void States_along_the_trip()
    {
        var trip = Load();
        Assert.Equal(RosserState.Waiting, trip.State(Pace));
        trip = trip.Advance(Pace, 0.01).Trip;
        Assert.Equal(RosserState.Feeding, trip.State(Pace));
        var (end, step) = (trip with { Travel = 9.6 }).Advance(Pace, 10);
        Assert.True(step.Delivered);
        Assert.Equal(RosserState.Delivered, end.State(Pace));
        Assert.Equal(RosserState.Empty, RosserTrip.Taken.State(Pace));
    }

    [Theory]
    [InlineData(0.0, RosserBrokenTrunk.AsLoaded)]
    [InlineData(1.0, RosserBrokenTrunk.AsLoaded)]      // nose at the breaker
    [InlineData(1.01, RosserBrokenTrunk.Debranched)]
    [InlineData(2.375, RosserBrokenTrunk.Debranched)]  // nose at the ring
    [InlineData(2.4, RosserBrokenTrunk.Debarked)]
    [InlineData(9.0, RosserBrokenTrunk.Debarked)]
    [InlineData(9.625, RosserBrokenTrunk.Debarked)]    // delivered
    public void Breaking_per_travel(double travel, RosserBrokenTrunk expected)
    {
        Assert.Equal(expected, (Load() with { Travel = travel }).Broken(Pace));
        Assert.Equal(RosserBrokenTrunk.None, RosserTrip.None.Broken(Pace));
    }

    [Fact]
    public void Breaking_never_lets_a_trunk_give_sticks_or_bark_twice()
    {
        // Wherever the trip is broken, the trunk that comes out gives (if run again) only what had not dropped.
        for (double t = 0; t <= 9.625; t += 0.125)
        {
            var trip = Load(TrunkClass.Thin, 20, 10) with { Travel = t };
            trip = trip with { SticksDone = trip.SticksDue(Pace), BarkDone = trip.BarkDue(Pace) };
            switch (trip.Broken(Pace))
            {
                case RosserBrokenTrunk.AsLoaded:
                    Assert.Equal(0, trip.SticksDone);
                    Assert.Equal(0, trip.BarkDone);
                    break;
                case RosserBrokenTrunk.Debranched:
                    Assert.Equal(0, trip.BarkDone);   // its bark is still to come, its branches are gone
                    break;
                case RosserBrokenTrunk.Debarked:
                    break;                             // nothing more can come off a debarked trunk
                default:
                    Assert.Fail("no trunk");
                    break;
            }
        }
    }

    [Fact]
    public void Over_a_station()
    {
        var trip = Load() with { Travel = 3 };   // nose −6.875, tail −10.875
        Assert.True(trip.Over(Pace, Pace.Ring));
        Assert.True(trip.Over(Pace, -10.875));
        Assert.False(trip.Over(Pace, -6.8));
        Assert.False(RosserTrip.None.Over(Pace, 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.0624, 0)]
    [InlineData(0.0625, 0.0625)]
    [InlineData(1.99, 1.9375)]
    [InlineData(2, 2)]
    public void Boxes_follow_in_sixteenths(double travel, double expected) =>
        Assert.Equal(expected, RosserTrip.BoxTravel(travel), 9);

    [Fact]
    public void Running_needs_complete_and_turning()
    {
        Assert.True(RosserTrip.Running(true, 0.05f, 0.05f));
        Assert.True(RosserTrip.Running(true, -0.3f, 0.05f));
        Assert.False(RosserTrip.Running(true, 0.04f, 0.05f));
        Assert.False(RosserTrip.Running(false, 1, 0.05f));
    }

    [Theory]
    [InlineData(false, RosserState.Empty, false, 5, RosserLoadVerdict.Incomplete)]
    [InlineData(true, RosserState.Waiting, false, 5, RosserLoadVerdict.Occupied)]
    [InlineData(true, RosserState.Delivered, false, 5, RosserLoadVerdict.Occupied)]
    [InlineData(true, RosserState.Empty, true, 5, RosserLoadVerdict.AlreadyDebarked)]
    [InlineData(true, RosserState.Empty, false, 0, RosserLoadVerdict.NoLogs)]
    [InlineData(true, RosserState.Empty, false, 1, RosserLoadVerdict.Loads)]
    public void Loading(bool complete, RosserState state, bool debarked, int logs, RosserLoadVerdict expected) =>
        Assert.Equal(expected, RosserTrip.CanLoad(complete, state, debarked, logs));

    [Fact]
    public void Rack_offers_and_room()
    {
        Assert.Equal(RosserRackState.Empty, RosserTrip.RackOffer(false, false, 3));
        Assert.Equal(RosserRackState.Debarked, RosserTrip.RackOffer(true, true, 3));
        Assert.Equal(RosserRackState.NoLogs, RosserTrip.RackOffer(true, false, 0));
        Assert.Equal(RosserRackState.Ready, RosserTrip.RackOffer(true, false, 3));
        Assert.True(RosserTrip.RackHasRoom(3));
        Assert.False(RosserTrip.RackHasRoom(4));
    }

    [Theory]
    [InlineData(RosserState.Waiting, true, true, RosserTakeBack.Trunk)]
    [InlineData(RosserState.Delivered, true, false, RosserTakeBack.Trunk)]
    [InlineData(RosserState.Feeding, true, true, RosserTakeBack.InRolls)]
    [InlineData(RosserState.Empty, true, true, RosserTakeBack.Heads)]
    [InlineData(RosserState.Empty, true, false, RosserTakeBack.HeadsWorn)]
    [InlineData(RosserState.Empty, false, false, RosserTakeBack.Nothing)]
    public void Taking_back(RosserState state, bool heads, bool unworn, RosserTakeBack expected) =>
        Assert.Equal(expected, RosserTrip.TakeBack(state, heads, unworn));

    [Theory]
    [InlineData(10, 0.5f, 5)]
    [InlineData(15, 0.5f, 7)]
    [InlineData(3, 0.1f, 0)]
    [InlineData(10, 0.3f, 3)]
    [InlineData(10, 1f, 10)]
    [InlineData(10, 0f, 0)]
    public void Sticks_are_rounded_down_over_the_trunk(int branches, float fraction, int sticks) =>
        Assert.Equal(sticks, RosserTrip.Load(RosserFixture.Pace(new RosserConfig { StickFraction = fraction }), TrunkClass.Thin, 5, branches, null)
            .Sticks(RosserFixture.Pace(new RosserConfig { StickFraction = fraction })));

    [Theory]
    [InlineData(0, 1.0, 4.0, 0)]
    [InlineData(5, 0.0, 4.0, 0)]
    [InlineData(5, -1.0, 4.0, 0)]
    [InlineData(5, 4.0, 4.0, 5)]
    [InlineData(5, 9.0, 4.0, 5)]
    [InlineData(3, 1.2, 4.0, 0)]
    [InlineData(3, 1.3333334, 4.0, 1)]
    [InlineData(10, 1.2, 4.0, 3)]
    public void Due_counts(int total, double past, double length, int expected) =>
        Assert.Equal(expected, RosserTrip.Due(total, past, length));

    [Fact]
    public void The_outfeed_states_keep_their_saved_numbers()
    {
        // synced as numbers: a new state goes at the end
        Assert.Equal([0, 1, 2, 3, 4, 5], Enum.GetValues<RosserOutfeedState>().Select(s => (int)s));
        Assert.Equal(5, (int)RosserOutfeedState.Off);
    }

    [Fact]
    public void Every_rack_and_outfeed_state_the_info_names_has_its_line_in_the_English_lang_file()
    {
        string en = File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "lang", "en.json"));
        foreach (var state in Enum.GetValues<RosserRackState>().Where(s => s != RosserRackState.Unknown))
            Assert.Contains($"\"rosser-info-rack-{state.ToString().ToLowerInvariant()}\":", en);
        foreach (var state in Enum.GetValues<RosserOutfeedState>().Where(s => s != RosserOutfeedState.Unknown))
            Assert.Contains($"\"rosser-info-outfeed-{state.ToString().ToLowerInvariant()}\":", en);
    }

    private static string ModDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
