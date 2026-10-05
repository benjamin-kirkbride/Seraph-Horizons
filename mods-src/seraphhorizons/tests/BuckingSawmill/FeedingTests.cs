using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;

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

    [Fact]
    public void A_rack_offers_its_top_trunk_unless_branched_or_empty_of_logs()
    {
        Assert.Equal(RackState.Empty, Feeding.RackOffer(false, false, 0));
        Assert.Equal(RackState.Empty, Feeding.RackOffer(false, true, 5));
        Assert.Equal(RackState.Branched, Feeding.RackOffer(true, true, 5));
        Assert.Equal(RackState.Branched, Feeding.RackOffer(true, true, 0));
        Assert.Equal(RackState.NoLogs, Feeding.RackOffer(true, false, 0));
        Assert.Equal(RackState.Ready, Feeding.RackOffer(true, false, 1));
    }

    [Fact]
    public void A_feeder_offers_its_finished_trunk_by_the_racks_rules_or_says_whether_one_is_coming()
    {
        Assert.Equal(RackState.FeederEmpty, Feeding.FeederOffer(false, false, false, 0));
        Assert.Equal(RackState.FeederBusy, Feeding.FeederOffer(false, true, false, 0));
        // A finished trunk wins over a busy flag; the rules for it are the rack's.
        Assert.Equal(RackState.FeederReady, Feeding.FeederOffer(true, true, false, 3));
        Assert.Equal(RackState.FeederReady, Feeding.FeederOffer(true, false, false, 3));
        Assert.Equal(RackState.Branched, Feeding.FeederOffer(true, false, true, 3));
        Assert.Equal(RackState.NoLogs, Feeding.FeederOffer(true, false, false, 0));
    }

    [Fact]
    public void The_mill_pulls_only_from_a_ready_rack_or_feeder()
    {
        foreach (var state in Enum.GetValues<RackState>())
            Assert.Equal(state is RackState.Ready or RackState.FeederReady, Feeding.Pulls(state));
    }

    [Fact]
    public void The_most_telling_offer_is_named()
    {
        RackState[] order = [RackState.None, RackState.Empty, RackState.FeederEmpty, RackState.FeederBusy, RackState.Branched, RackState.NoLogs];
        for (int i = 0; i < order.Length; i++)
            for (int j = 0; j < order.Length; j++)
            {
                var expected = order[Math.Max(i, j)];
                Assert.Equal(expected, Feeding.MoreTelling(order[i], order[j]));
            }
        // The racks' own order is as it always was (by the enum's number).
        RackState[] racks = [RackState.None, RackState.Empty, RackState.Branched, RackState.NoLogs];
        foreach (var a in racks)
            foreach (var b in racks)
                Assert.Equal(b > a ? b : a, Feeding.MoreTelling(a, b));
    }

    [Fact]
    public void Saved_rack_states_keep_their_numbers()
    {
        // rackState is saved and synced as a number: the rack's values never move.
        Assert.Equal(0, (int)RackState.Unknown);
        Assert.Equal(7, (int)RackState.Ready);
        Assert.Equal(8, (int)RackState.FeederEmpty);
        Assert.Equal(9, (int)RackState.FeederBusy);
        Assert.Equal(10, (int)RackState.FeederReady);
    }

    [Fact]
    public void Every_rack_state_the_info_names_has_its_line_in_the_English_lang_file()
    {
        string en = File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "lang", "en.json"));
        foreach (var state in Enum.GetValues<RackState>().Where(s => s != RackState.Unknown))
            Assert.Contains($"\"buckingmill-info-rack-{state.ToString().ToLowerInvariant()}\":", en);
    }

    private static string ModDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
