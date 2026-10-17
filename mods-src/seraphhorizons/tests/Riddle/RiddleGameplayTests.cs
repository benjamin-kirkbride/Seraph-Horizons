using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Xunit;

namespace SeraphHorizons.Tests.Riddle;

/// <summary>The riddle's rules (#714, Riddle/Core): what goes on as a charge, the build, the riddling's pace, the
/// split into fines and oversize with nothing lost, the settings' bounds and the renderer's clock.</summary>
public class RiddleGameplayTests
{
    private const string Crushed = "seraphhorizons:crushedore-hematite-medium";

    [Fact]
    public void A_charge_is_three_unclassified_crushed_ore_on_a_complete_empty_riddle()
    {
        Assert.Equal(RiddleLoadVerdict.Loads, Riddling.CanLoad(Crushed, false, 3, 3, true, false, false));
        Assert.Equal(RiddleLoadVerdict.Loads, Riddling.CanLoad(Crushed, false, 16, 3, true, false, false));
        Assert.Equal(RiddleLoadVerdict.TooFew, Riddling.CanLoad(Crushed, false, 2, 3, true, false, false));
        Assert.Equal(RiddleLoadVerdict.AlreadyClassified, Riddling.CanLoad(Crushed, true, 3, 3, true, false, false));
        Assert.Equal(RiddleLoadVerdict.Incomplete, Riddling.CanLoad(Crushed, false, 3, 3, false, false, false));
        Assert.Equal(RiddleLoadVerdict.Occupied, Riddling.CanLoad(Crushed, false, 3, 3, true, true, false));
        // the owner's ruling: the box is emptied before the next charge
        Assert.Equal(RiddleLoadVerdict.BoxFull, Riddling.CanLoad(Crushed, false, 3, 3, true, false, true));
        foreach (var other in new[] { "game:ore-medium-hematite-granite", "seraphhorizons:groundore-hematite-medium", "game:crushed-iron", null })
            Assert.Equal(RiddleLoadVerdict.NotCrushedOre, Riddling.CanLoad(other, false, 3, 3, true, false, false));
    }

    [Fact]
    public void Every_item_of_a_charge_comes_back_as_fines_or_oversize()
    {
        Assert.Equal((2, 1), Riddling.Split(3, 1));
        Assert.Equal((3, 0), Riddling.Split(3, 0));
        Assert.Equal((0, 3), Riddling.Split(3, 9));
        Assert.Equal((3, 0), Riddling.Split(3, -1));
        for (int lumps = 1; lumps <= 6; lumps++)
            for (int over = 0; over <= lumps; over++)
            {
                var (f, o) = Riddling.Split(lumps, over);
                Assert.Equal(lumps, f + o);
            }
    }

    [Fact]
    public void A_charge_is_riddled_in_its_shakes_and_no_further()
    {
        var job = RiddleJob.Start;
        Assert.True(job.Untouched);
        double shake = 2 * Math.PI;
        bool finished = false;
        for (int i = 0; i < 11; i++)
        {
            (job, finished) = job.Advance(shake, 12);
            Assert.False(finished);
        }
        Assert.False(job.Untouched);
        Assert.Equal(11.0 / 12, job.Work, 6);
        (job, finished) = job.Advance(shake, 12);
        Assert.True(finished);
        Assert.True(job.Done);
        (job, finished) = job.Advance(shake, 12);
        Assert.False(finished);
        Assert.Equal(1, job.Work);
        // the pace: twelve shakes at 0.75 s are 9 s of holding
        Assert.Equal(1.0 / 9, Riddling.ChargesFor(Riddling.ShakeRadiansPerSecond, 12), 6);
        Assert.Equal(1, RiddleJob.ShakesCrossed(0.08, 0.09, 12));
        Assert.Equal(0, RiddleJob.ShakesCrossed(0.09, 0.1, 12));
        Assert.Equal(RiddleJob.None, RiddleJob.Restore(false, 0.5));
        Assert.Equal(0.5, RiddleJob.Restore(true, 0.5).Work);
        Assert.Equal(1, RiddleJob.Restore(true, 7).Work);
        Assert.Equal(0, RiddleJob.Restore(true, double.NaN).Work);
    }

    [Fact]
    public void The_riddle_is_fitted_with_two_nails_and_strips_and_kept()
    {
        var parts = new RiddleParts();
        Assert.False(parts.Complete);
        Assert.True(parts.Draws(null));
        Assert.False(parts.Draws(RiddleRequires.Riddle));
        Assert.Equal(RiddleFitVerdict.NotAPart, parts.CanFit("game:metalnailsandstrips-copper"));
        Assert.Equal(RiddleFitVerdict.NotAPart, parts.CanFit("game:plank-oak"));
        Assert.Equal(RiddleFitVerdict.TooFew, parts.Fit("game:metalnailsandstrips-iron", 1));
        Assert.False(parts.Complete);
        Assert.Equal("game:metalnailsandstrips-iron", parts.NextPart);
        Assert.Equal(RiddleFitVerdict.Fits, parts.Fit("metalnailsandstrips-steel", 2));
        Assert.True(parts.Complete);
        Assert.True(parts.Draws(RiddleRequires.Riddle));
        Assert.False(parts.Draws(RiddleRequires.Charge));
        Assert.Equal(RiddleFitVerdict.AlreadyFitted, parts.CanFit("game:metalnailsandstrips-iron"));
        Assert.Equal(("game:metalnailsandstrips-steel", 2), parts.Returns());
        Assert.True(RiddleParts.Restore(parts.Fitted).Complete);
        Assert.False(RiddleParts.Restore("game:plank-oak").Complete);
        Assert.Null(new RiddleParts().Returns());
    }

    [Fact]
    public void Settings_out_of_range_fall_back()
    {
        var config = new RiddleConfig { LumpsPerCharge = 0, ShakesPerCharge = float.NaN, OversizePerCharge = 5 };
        Assert.Equal(3, config.Sanitise().Count);
        Assert.Equal(3, config.LumpsPerCharge);
        Assert.Equal(12f, config.ShakesPerCharge);
        Assert.Equal(1, config.OversizePerCharge);
        Assert.Empty(new RiddleConfig().Sanitise());
        // every charge puts some fines through
        var all = new RiddleConfig { LumpsPerCharge = 2, OversizePerCharge = 2 };
        Assert.Single(all.Sanitise());
        Assert.Equal(1, all.OversizePerCharge);
    }

    private sealed class View : IRiddleView
    {
        public Side Side => Side.North;
        public bool RiddleFitted { get; set; } = true;
        public bool ChargeOn { get; set; }
        public double RiddleWork { get; set; }
        public bool BoxHoldsFines { get; set; }
        public bool Held { get; set; }
        public double ShakesPerCharge => 12;
    }

    [Fact]
    public void The_clock_turns_only_while_a_charge_is_riddled_and_the_charge_is_drawn_whole()
    {
        var clock = new RiddleClock();
        clock.Advance(0.1f, false, 0, true, 12);
        Assert.Equal(0, clock.Theta);
        Assert.Equal(0, clock.Class);
        Assert.Equal(0, clock.Presence);
        // a charge goes on at p 1 at once
        clock.Advance(0.1f, true, 0, false, 12);
        Assert.Equal(1, clock.Class);
        Assert.Equal(1, clock.Presence);
        Assert.Equal(0, clock.Theta);
        clock.Advance(0.75f, true, 0, true, 12);
        Assert.Equal(2 * Math.PI, clock.Theta, 6);
        Assert.True(clock.Work > 0);
        // riddled: W 1, and the clock stops where it is
        double theta = clock.Theta;
        clock.Advance(0.5f, true, 1, true, 12);
        Assert.Equal(1, clock.Work);
        Assert.Equal(theta, clock.Theta);
        // taken off: not drawn
        clock.Advance(0.1f, false, 1, false, 12);
        Assert.Equal(0, clock.Class);
        Assert.Equal(0, clock.Input().Presence);

        var view = new View { RiddleFitted = false, ChargeOn = true, BoxHoldsFines = true };
        Assert.True(RiddleClock.Shows(view, null));
        Assert.False(RiddleClock.Shows(view, RiddleRequires.Riddle));
        Assert.False(RiddleClock.Shows(view, RiddleRequires.Charge));
        Assert.True(RiddleClock.Shows(view, RiddleRequires.Fines));
        view.RiddleFitted = true;
        Assert.True(RiddleClock.Shows(view, RiddleRequires.Charge));
        view.BoxHoldsFines = false;
        Assert.False(RiddleClock.Shows(view, RiddleRequires.Fines));
    }

    [Fact]
    public void Holds_time_out()
    {
        var holds = new RiddleHolds();
        holds.Hold("a", 0);
        Assert.True(holds.Any(Riddling.HoldTimeoutMs));
        Assert.False(holds.Any(Riddling.HoldTimeoutMs + 1));
        holds.Hold("a", 0);
        holds.Release("a");
        Assert.False(holds.Any(0));
    }
}
