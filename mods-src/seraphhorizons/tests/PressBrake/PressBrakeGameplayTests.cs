using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using Xunit;

namespace SeraphHorizons.Tests.PressBrake;

/// <summary>PressBrake/Core: the build order, what each stage takes and take-back; the half plates by
/// metal; the fold's arithmetic (W only while the lever is worked, one angle at W = 1); who holds
/// the lever; the renderer's clock; and the settings.</summary>
public class PressBrakeGameplayTests
{
    private static PressBrakeParts Complete(string screws = "game:rod-iron", string edge = "game:metalplate-iron")
    {
        var parts = new PressBrakeParts();
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit(screws));
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit(edge));
        return parts;
    }

    // ---- Parts ----

    [Fact]
    public void The_stages_go_in_in_the_contracts_order()
    {
        Assert.Equal(["screws", "edge"], PressBrakeRequires.Stages.Select(PressBrakeRequires.Name));
        var parts = new PressBrakeParts();
        Assert.Equal(PressBrakeStage.Screws, parts.Next);
        Assert.Equal("game:rod-iron", parts.NextPart);
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit("game:rod-steel"));
        Assert.Equal(PressBrakeStage.Edge, parts.Next);
        Assert.False(parts.Complete);
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit("game:metalplate-steel"));
        Assert.True(parts.Complete);
        Assert.Null(parts.NextPart);
        Assert.Equal(("steel", "steel"), (PressBrakeParts.MetalOf(parts.FittedIn(PressBrakeStage.Screws)), PressBrakeParts.MetalOf(parts.FittedIn(PressBrakeStage.Edge))));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new PressBrakeParts();
        Assert.Equal(PressBrakeFitVerdict.OutOfOrder, parts.Fit("game:metalplate-iron"));
        Assert.Equal(PressBrakeFitVerdict.NotAPart, parts.Fit("game:rod-copper"));
        Assert.Equal(PressBrakeFitVerdict.NotAPart, parts.Fit("game:metalplate-meteoriciron"));
        Assert.Equal(PressBrakeFitVerdict.NotAPart, parts.Fit("game:metalplate-lead"));   // the work, not a part
        Assert.Equal(PressBrakeFitVerdict.NotAPart, parts.Fit(null));
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit("rod-meteoriciron"));   // a code with no domain is the game's
        Assert.Equal(PressBrakeFitVerdict.AlreadyFitted, parts.Fit("game:rod-iron"));
        Assert.Equal(PressBrakeStage.Edge, parts.Next);
    }

    [Theory]
    [InlineData("game:rod-iron", "game:metalplate-iron")]
    [InlineData("game:rod-meteoriciron", "game:metalplate-steel")]
    [InlineData("game:rod-steel", "game:metalplate-iron")]
    public void Each_rod_and_edge_plate_fits(string rod, string plate) => Assert.True(Complete(rod, plate).Complete);

    [Fact]
    public void Fitted_draws_each_stage_once_it_is_in_and_never_the_plates()
    {
        var parts = new PressBrakeParts();
        Assert.True(parts.Fitted(null));
        Assert.False(parts.Fitted("screws"));
        parts.Fit("game:rod-iron");
        Assert.True(parts.Fitted("screws"));
        Assert.False(parts.Fitted("edge"));
        Assert.True(Complete().Fitted("edge"));
        Assert.False(Complete().Fitted("platelead"));
        Assert.False(Complete().Fitted("platecopper"));
        Assert.True(PressBrakeRequires.KnownRequires.SetEquals(["screws", "edge", "platelead", "platecopper"]));
        Assert.Equal("platelead", PressBrakeRequires.Plate(1));
        Assert.Equal("platecopper", PressBrakeRequires.Plate(2));
        Assert.Null(PressBrakeRequires.Plate(0));
    }

    [Fact]
    public void Take_back_gives_the_last_part_first_and_never_with_a_plate_on()
    {
        var parts = Complete("game:rod-steel", "game:metalplate-iron");
        Assert.False(parts.CanTakeBack(plateOn: true));
        Assert.True(parts.CanTakeBack(plateOn: false));
        Assert.Equal("game:metalplate-iron", parts.RemoveLast());
        Assert.Equal(PressBrakeStage.Edge, parts.Next);
        Assert.Equal("game:rod-steel", parts.RemoveLast());
        Assert.Equal(PressBrakeStage.Screws, parts.Next);
        Assert.False(parts.CanTakeBack(false));
        Assert.Null(parts.RemoveLast());
        Assert.Equal(PressBrakeFitVerdict.Fits, parts.Fit("game:rod-iron"));
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_and_a_save_keeps_them()
    {
        var parts = Complete("game:rod-meteoriciron", "game:metalplate-steel");
        Assert.Equal(["game:rod-meteoriciron", "game:metalplate-steel"], parts.Returns());
        var restored = PressBrakeParts.Restore(parts.Snapshot());
        Assert.True(restored.Complete);
        Assert.Equal(parts.Returns(), restored.Returns());
        // a gap or a code that is not its stage's part ends the run
        Assert.False(PressBrakeParts.Restore(new Dictionary<string, string> { ["edge"] = "game:metalplate-iron" }).Has(PressBrakeStage.Edge));
        var wrong = PressBrakeParts.Restore(new Dictionary<string, string> { ["screws"] = "game:rod-copper", ["edge"] = "game:metalplate-iron" });
        Assert.Empty(wrong.Returns());
    }

    // ---- Half plates ----

    [Fact]
    public void Half_plates_and_angles_by_metal()
    {
        Assert.Equal(1, Folding.ClassOfPlate("seraphhorizons:halfplate-lead"));
        Assert.Equal(2, Folding.ClassOfPlate("seraphhorizons:halfplate-copper"));
        // a whole plate is the squaring shear's work, never the brake's
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-lead"));
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-copper"));
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-iron"));
        Assert.Equal(0, Folding.ClassOfPlate("game:ingot-lead"));
        Assert.Equal(0, Folding.ClassOfPlate("seraphhorizons:angle-lead"));   // what comes off never goes back on
        Assert.Equal(0, Folding.ClassOfPlate("game:chutesection-copper"));
        Assert.Equal(0, Folding.ClassOfPlate("seraphhorizons:pipesection-copper"));
        Assert.Equal("seraphhorizons:angle-lead", Folding.AngleFor(1));
        Assert.Equal("seraphhorizons:angle-copper", Folding.AngleFor(2));
        Assert.Null(Folding.AngleFor(0));
        Assert.Equal("seraphhorizons:halfplate-lead", Folding.PlateFor(1));
        Assert.Equal("seraphhorizons:halfplate-copper", Folding.PlateFor(2));
        Assert.Null(Folding.PlateFor(0));
        Assert.Equal(("lead", "copper"), (Folding.MetalOf(1), Folding.MetalOf(2)));
        Assert.Equal(1, Folding.AnglesPerPlate);
    }

    [Fact]
    public void A_half_plate_goes_on_a_complete_empty_brake_only()
    {
        Assert.Equal(FoldLoadVerdict.Loads, Folding.CanLoad(Folding.LeadHalfPlate, true, false));
        Assert.Equal(FoldLoadVerdict.Loads, Folding.CanLoad(Folding.CopperHalfPlate, true, false));
        Assert.Equal(FoldLoadVerdict.Incomplete, Folding.CanLoad(Folding.LeadHalfPlate, false, false));
        Assert.Equal(FoldLoadVerdict.Occupied, Folding.CanLoad(Folding.CopperHalfPlate, true, true));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:metalplate-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:ingot-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("seraphhorizons:angle-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:chutesection-lead", true, false));
    }

    // ---- The fold ----

    [Theory]
    [InlineData(1, 1.5)]
    [InlineData(2, 2.25)]
    public void W_advances_with_the_lever_and_the_plate_is_done_at_1(int k, double turns)
    {
        var job = new FoldJob(k, 0);
        Assert.True(job.Untouched);
        (job, bool finished) = job.Advance(2 * Math.PI * turns / 2, turns);
        Assert.Equal(0.5, job.Work, 9);
        Assert.False(finished);
        Assert.False(job.Untouched);
        (job, finished) = job.Advance(2 * Math.PI * turns * 0.49, turns);
        Assert.False(finished);
        (job, finished) = job.Advance(2 * Math.PI * turns, turns);   // more than enough: held at 1
        Assert.True(finished);
        Assert.Equal(1.0, job.Work);
        // done, it goes no further and does not finish twice
        (job, finished) = job.Advance(100, turns);
        Assert.False(finished);
        Assert.Equal(FoldJob.None, FoldJob.None.Advance(100, turns).Job);
    }

    [Fact]
    public void W_advances_only_while_the_lever_is_held()
    {
        Assert.True(Folding.Running(complete: true, plateOn: true, held: true));
        Assert.False(Folding.Running(true, true, false));
        Assert.False(Folding.Running(true, false, true));
        Assert.False(Folding.Running(false, true, true));
        // one second of holding is one lever turn: a lead half plate takes 1.5, copper 2.25
        Assert.Equal(2 * Math.PI, Folding.LeverRadiansPerSecond, 9);
        var c = new PressBrakeConfig();
        Assert.Equal(1 / 1.5, Folding.PlatesFor(Folding.LeverRadiansPerSecond, c.LeverTurnsPerPlate(1)), 9);
        Assert.Equal(1 / 2.25, Folding.PlatesFor(Folding.LeverRadiansPerSecond, c.LeverTurnsPerPlate(2)), 9);
        Assert.Equal(0, Folding.PlatesFor(-3, 6));
        Assert.Equal(0, Folding.PlatesFor(3, 0));
    }

    [Fact]
    public void The_folds_crossed_in_a_step_are_counted()
    {
        double[] moments = [0.25, 0.75];
        Assert.Equal(0, FoldJob.Crossed(moments, 0, 0.2));
        Assert.Equal(1, FoldJob.Crossed(moments, 0.2, 0.25));
        Assert.Equal(0, FoldJob.Crossed(moments, 0.25, 0.3));
        Assert.Equal(2, FoldJob.Crossed(moments, 0, 1));
    }

    [Fact]
    public void A_saved_fold_is_clamped_and_a_bad_class_is_none()
    {
        Assert.Equal(new FoldJob(2, 1), FoldJob.Restore(2, 7));
        Assert.Equal(new FoldJob(1, 0), FoldJob.Restore(1, double.NaN));
        Assert.Equal(FoldJob.None, FoldJob.Restore(3, 0.5));
    }

    [Fact]
    public void A_player_holds_the_lever_until_they_let_go_or_go_quiet()
    {
        var holds = new LeverHolds();
        Assert.False(holds.Any(0));
        holds.Hold("a", 1000);
        Assert.True(holds.Any(1000 + Folding.HoldTimeoutMs));
        holds.Hold("b", 1500);
        Assert.True(holds.Any(1000 + Folding.HoldTimeoutMs + 1));   // a went quiet, b holds
        holds.Release("b");
        Assert.False(holds.Any(1600));
        holds.Hold("c", 2000);
        holds.Clear();
        Assert.False(holds.Any(2000));
    }

    // ---- The renderer's clock ----

    [Fact]
    public void The_clock_turns_theta_and_W_only_while_held_and_never_strays_from_the_server()
    {
        var clock = new PressBrakeClock();
        clock.Advance(0.1f, 1, 0, held: false, 6);
        Assert.Equal((0.0, 0.0, 1), (clock.Theta, clock.Work, clock.Class));
        Assert.Equal(0.25f, clock.Presence, 5);
        Assert.True(clock.ShowsPlate("platelead"));
        Assert.False(clock.ShowsPlate("platecopper"));
        // held for 0.1 s: a tenth of a lever turn, a sixtieth of a lead plate
        clock.Advance(0.1f, 1, 0, held: true, 6);
        Assert.Equal(0.2 * Math.PI, clock.Theta, 6);
        Assert.Equal(1 / 60.0, clock.Work, 6);
        // never more than Snap ahead of the server
        clock.Advance(0.5f, 1, 0, true, 6);
        Assert.Equal(0, clock.Work, 9);
        // never behind it
        clock.Advance(0.01f, 1, 0.4, false, 6);
        Assert.Equal(0.4, clock.Work, 9);
        var input = clock.Input();
        Assert.Equal((clock.Theta, Math.Abs(clock.Theta), 0.4, 1), (input.Theta, input.Psi, input.Work, input.Class));
    }

    [Fact]
    public void The_clock_holds_W_at_1_and_k_while_p_eases_out_and_hides_the_sheet_once_delivered()
    {
        var clock = new PressBrakeClock();
        clock.Advance(1f, 2, 0.98, false, 9);
        Assert.Equal(1f, clock.Presence);
        clock.Advance(0.2f, 0, 0, false, 9);   // delivered and cleared
        Assert.Equal(1.0, clock.Work);
        Assert.Equal(2, clock.Class);
        Assert.Equal(0.5f, clock.Presence, 5);
        Assert.False(clock.ShowsPlate("platecopper"));
        clock.Advance(0.3f, 0, 0, false, 9);
        Assert.Equal((0f, 0), (clock.Presence, clock.Class));
        // the next plate starts from the server's 0
        clock.Advance(0.1f, 1, 0, false, 6);
        Assert.Equal((0.0, 1), (clock.Work, clock.Class));
        // and so does a second plate of the same metal after the first is done
        clock.Advance(0.1f, 1, 0.99, false, 6);
        clock.Advance(0.1f, 1, 0, false, 6);
        Assert.Equal(0.0, clock.Work);
    }

    // ---- Settings ----

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new PressBrakeConfig();
        Assert.Equal((1.5f, 2.25f), (c.LeverTurnsPerPlateLead, c.LeverTurnsPerPlateCopper));
        Assert.Equal(c.LeverTurnsPerPlateLead * 1.5, c.LeverTurnsPerPlateCopper, 3);   // copper half as much again
        Assert.Equal(0, c.LeverTurnsPerPlate(0));
        Assert.Empty(c.Sanitise());
        var bad = new PressBrakeConfig { LeverTurnsPerPlateLead = float.NaN, LeverTurnsPerPlateCopper = -1 };
        Assert.Equal(2, bad.Sanitise().Count);
        Assert.Equal((1.5f, 2.25f), (bad.LeverTurnsPerPlateLead, bad.LeverTurnsPerPlateCopper));
    }

    // The player looks along the brake: it runs away from them, the leaf end nearest.
    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_brake_runs_away_from_the_player(Side look)
    {
        var side = PressBrakeRig.PlacedSide(look);
        Assert.Equal(look.Normal(), Footprint.ToWorld(new Int3(0, 0, 1), side));
    }
}
