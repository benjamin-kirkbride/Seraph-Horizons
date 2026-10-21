using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Xunit;

namespace SeraphHorizons.Tests.Riddle;

/// <summary>The riddle's rules (#714, Riddle/Core): what goes on as a charge, the build, the riddling's pace, the
/// split into fines and oversize with nothing lost, the settings' bounds and the renderer's clock.</summary>
public class RiddleGameplayTests
{
    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "riddle.json")));

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
    public void Crushed_ore_in_hand_goes_round_again_once_the_charge_is_riddled()
    {
        // riddled, its oversize or its fines waiting, and a whole charge in hand: out, and the next on
        Assert.True(Riddling.CanReload(Crushed, false, 3, 3, true, false, true));
        Assert.True(Riddling.CanReload(Crushed, false, 16, 3, true, false, true));
        // nothing waiting is a plain load, not a round; a charge being riddled goes on being riddled
        Assert.False(Riddling.CanReload(Crushed, false, 3, 3, true, false, false));
        Assert.False(Riddling.CanReload(Crushed, false, 3, 3, true, true, true));
        // what would not go on as a charge leaves the riddle as it is
        Assert.False(Riddling.CanReload(Crushed, false, 2, 3, true, false, true));
        Assert.False(Riddling.CanReload(Crushed, true, 3, 3, true, false, true));
        Assert.False(Riddling.CanReload(Crushed, false, 3, 3, false, false, true));
        foreach (var other in new[] { "game:ore-medium-hematite-granite", "seraphhorizons:groundore-hematite-medium", null })
            Assert.False(Riddling.CanReload(other, false, 3, 3, true, false, true));
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
        var job = Riddling.Start();
        Assert.True(job.Untouched);
        double shake = 2 * Math.PI;
        bool finished = false;
        for (int i = 0; i < 11; i++)
        {
            (job, finished) = Riddling.Advance(job, shake, 12);
            Assert.False(finished);
        }
        Assert.False(job.Untouched);
        Assert.Equal(11.0 / 12, job.Work, 6);
        (job, finished) = Riddling.Advance(job, shake, 12);
        Assert.True(finished);
        Assert.True(job.Done);
        (job, finished) = Riddling.Advance(job, shake, 12);
        Assert.False(finished);
        Assert.Equal(1, job.Work);
        // the pace: twelve shakes at 0.75 s are 9 s of holding
        Assert.Equal(1.0 / 9, Riddling.ChargesFor(Riddling.ShakeRadiansPerSecond, 12), 6);
        Assert.Equal(1, Riddling.ShakesCrossed(0.08, 0.09, 12));
        Assert.Equal(0, Riddling.ShakesCrossed(0.09, 0.1, 12));
        Assert.Equal(MachineJob.None, Riddling.Restore(false, 0.5));
        Assert.Equal(0.5, Riddling.Restore(true, 0.5).Work);
        Assert.Equal(1, Riddling.Restore(true, 7).Work);
        Assert.Equal(0, Riddling.Restore(true, double.NaN).Work);
    }

    [Fact]
    public void The_riddle_is_fitted_with_two_nails_and_strips_and_kept()
    {
        var build = Build();
        Assert.Equal(["riddle"], build.Stages.Select(s => s.Id));
        Assert.Equal([RiddleRequires.Riddle], Assert.Single(build.Stages).Adds);
        var material = Assert.Single(Assert.Single(build.Stages).Materials);
        Assert.Equal(RiddleBuild.MeshCodes, material.Codes);
        Assert.Equal(RiddleBuild.Needed, material.Quantity);
        Assert.Null(build.Block);   // its records are its own in the export
        var parts = new MachineAssembly(build);
        Assert.False(parts.Built);
        Assert.True(parts.Shows(null));
        Assert.False(parts.Shows(RiddleRequires.Riddle));
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Check("game:metalnailsandstrips-copper", 2).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Check("game:plank-oak", 2).Verdict);
        Assert.Equal(AssemblyVerdict.TooFew, parts.Fit("game:metalnailsandstrips-iron", 1).Verdict);
        Assert.False(parts.Built);
        var fit = parts.Fit("metalnailsandstrips-steel", 5);   // two from the stack, all at once
        Assert.Equal((AssemblyVerdict.Fitted, 2), (fit.Verdict, fit.Take));
        Assert.True(parts.Built);
        Assert.True(parts.Shows(RiddleRequires.Riddle));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, parts.Check("game:metalnailsandstrips-iron", 2).Verdict);
        Assert.Equal([new StageDrop("game:metalnailsandstrips-steel", 2)], parts.Returns());
        var (snapshot, wearing) = parts.Snapshot();
        Assert.True(MachineAssembly.Restore(build, snapshot, wearing, out _).Built);
        Assert.False(MachineAssembly.Restore(build, new Dictionary<string, int> { ["0/0/game:plank-oak"] = 2 }, wearing, out _).Built);
        Assert.Empty(new MachineAssembly(build).Returns());
        // the creative shortcut fits iron
        var creative = new MachineAssembly(build);
        Assert.True(creative.FillNext());
        Assert.Equal([new StageDrop("game:metalnailsandstrips-iron", 2)], creative.Returns());
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

        Assert.True(RiddleClock.Shows(null, riddleFitted: false, chargeOn: true, boxHoldsFines: true));
        Assert.False(RiddleClock.Shows(RiddleRequires.Riddle, false, true, true));
        Assert.False(RiddleClock.Shows(RiddleRequires.Charge, false, true, true));
        Assert.True(RiddleClock.Shows(RiddleRequires.Fines, false, true, true));
        Assert.True(RiddleClock.Shows(RiddleRequires.Charge, true, true, true));
        Assert.False(RiddleClock.Shows(RiddleRequires.Fines, true, true, false));
    }

    [Fact]
    public void Holds_time_out()
    {
        // the shared hand holds (BEMachine.HandWorked), at the quern's 600 ms
        var holds = new HandHolds();
        holds.Hold("a", 0);
        Assert.True(holds.Any(HandHolds.DefaultTimeoutMs));
        Assert.False(holds.Any(HandHolds.DefaultTimeoutMs + 1));
        holds.Hold("a", 0);
        holds.Release("a");
        Assert.False(holds.Any(0));
    }
}
