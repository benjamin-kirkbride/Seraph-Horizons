using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear.Core;
using Xunit;

namespace SeraphHorizons.Tests.SquaringShear;

/// <summary>SquaringShear/Core: the build order, what each stage takes and take-back; the plates by
/// metal; the cut's arithmetic (W only while the treadle is worked, two half plates at W = 1); who
/// holds the treadle; the renderer's clock; and the settings.</summary>
public class SquaringShearGameplayTests
{
    private static SquaringShearParts Complete(string blade = "game:metalplate-iron", string gauge = "game:rod-iron")
    {
        var parts = new SquaringShearParts();
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit(blade));
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit(gauge));
        return parts;
    }

    // ---- Parts ----

    [Fact]
    public void The_stages_go_in_in_the_contracts_order()
    {
        Assert.Equal(["blade", "gauge"], SquaringShearRequires.Stages.Select(SquaringShearRequires.Name));
        var parts = new SquaringShearParts();
        Assert.Equal(SquaringShearStage.Blade, parts.Next);
        Assert.Equal("game:metalplate-iron", parts.NextPart);
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit("game:metalplate-steel"));
        Assert.Equal(SquaringShearStage.Gauge, parts.Next);
        Assert.False(parts.Complete);
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit("game:rod-meteoriciron"));
        Assert.True(parts.Complete);
        Assert.Null(parts.NextPart);
        Assert.Equal(("steel", "meteoriciron"),
            (SquaringShearParts.MetalOf(parts.FittedIn(SquaringShearStage.Blade)), SquaringShearParts.MetalOf(parts.FittedIn(SquaringShearStage.Gauge))));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new SquaringShearParts();
        Assert.Equal(SquaringShearFitVerdict.OutOfOrder, parts.Fit("game:rod-iron"));
        Assert.Equal(SquaringShearFitVerdict.NotAPart, parts.Fit("game:rod-copper"));
        Assert.Equal(SquaringShearFitVerdict.NotAPart, parts.Fit("game:metalplate-meteoriciron"));
        Assert.Equal(SquaringShearFitVerdict.NotAPart, parts.Fit("game:metalplate-lead"));   // the work, not a part
        Assert.Equal(SquaringShearFitVerdict.NotAPart, parts.Fit("seraphhorizons:halfplate-copper"));
        Assert.Equal(SquaringShearFitVerdict.NotAPart, parts.Fit(null));
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit("metalplate-iron"));   // a code with no domain is the game's
        Assert.Equal(SquaringShearFitVerdict.AlreadyFitted, parts.Fit("game:metalplate-steel"));
        Assert.Equal(SquaringShearStage.Gauge, parts.Next);
    }

    [Theory]
    [InlineData("game:metalplate-iron", "game:rod-iron")]
    [InlineData("game:metalplate-steel", "game:rod-meteoriciron")]
    [InlineData("game:metalplate-iron", "game:rod-steel")]
    public void Each_blade_plate_and_gauge_rod_fits(string plate, string rod) => Assert.True(Complete(plate, rod).Complete);

    [Fact]
    public void Fitted_draws_each_stage_once_it_is_in_and_never_the_plates()
    {
        var parts = new SquaringShearParts();
        Assert.True(parts.Fitted(null));
        Assert.False(parts.Fitted("blade"));
        parts.Fit("game:metalplate-iron");
        Assert.True(parts.Fitted("blade"));
        Assert.False(parts.Fitted("gauge"));
        Assert.True(Complete().Fitted("gauge"));
        Assert.False(Complete().Fitted("platelead"));
        Assert.False(Complete().Fitted("platecopper"));
        Assert.True(SquaringShearRequires.KnownRequires.SetEquals(["blade", "gauge", "platelead", "platecopper"]));
        Assert.Equal("platelead", SquaringShearRequires.Plate(1));
        Assert.Equal("platecopper", SquaringShearRequires.Plate(2));
        Assert.Null(SquaringShearRequires.Plate(0));
    }

    [Fact]
    public void Take_back_gives_the_last_part_first_and_never_with_a_plate_on()
    {
        var parts = Complete("game:metalplate-steel", "game:rod-iron");
        Assert.False(parts.CanTakeBack(plateOn: true));
        Assert.True(parts.CanTakeBack(plateOn: false));
        Assert.Equal("game:rod-iron", parts.RemoveLast());
        Assert.Equal(SquaringShearStage.Gauge, parts.Next);
        Assert.Equal("game:metalplate-steel", parts.RemoveLast());
        Assert.Equal(SquaringShearStage.Blade, parts.Next);
        Assert.False(parts.CanTakeBack(false));
        Assert.Null(parts.RemoveLast());
        Assert.Equal(SquaringShearFitVerdict.Fits, parts.Fit("game:metalplate-iron"));
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_and_a_save_keeps_them()
    {
        var parts = Complete("game:metalplate-steel", "game:rod-meteoriciron");
        Assert.Equal(["game:metalplate-steel", "game:rod-meteoriciron"], parts.Returns());
        var restored = SquaringShearParts.Restore(parts.Snapshot());
        Assert.True(restored.Complete);
        Assert.Equal(parts.Returns(), restored.Returns());
        // a gap or a code that is not its stage's part ends the run
        Assert.False(SquaringShearParts.Restore(new Dictionary<string, string> { ["gauge"] = "game:rod-iron" }).Has(SquaringShearStage.Gauge));
        var wrong = SquaringShearParts.Restore(new Dictionary<string, string> { ["blade"] = "game:metalplate-copper", ["gauge"] = "game:rod-iron" });
        Assert.Empty(wrong.Returns());
    }

    // ---- Plates ----

    [Fact]
    public void Plates_and_half_plates_by_metal()
    {
        Assert.Equal(1, Cutting.ClassOfPlate("game:metalplate-lead"));
        Assert.Equal(2, Cutting.ClassOfPlate("game:metalplate-copper"));
        Assert.Equal(0, Cutting.ClassOfPlate("game:metalplate-iron"));
        Assert.Equal(0, Cutting.ClassOfPlate("game:ingot-lead"));
        Assert.Equal(0, Cutting.ClassOfPlate("seraphhorizons:halfplate-lead"));   // what comes off is never cut again
        Assert.Equal(0, Cutting.ClassOfPlate("seraphhorizons:angle-copper"));
        Assert.Equal("seraphhorizons:halfplate-lead", Cutting.HalfPlateFor(1));
        Assert.Equal("seraphhorizons:halfplate-copper", Cutting.HalfPlateFor(2));
        Assert.Null(Cutting.HalfPlateFor(0));
        Assert.Equal("game:metalplate-copper", Cutting.PlateFor(2));
        Assert.Equal(("lead", "copper"), (Cutting.MetalOf(1), Cutting.MetalOf(2)));
        Assert.Equal(2, Cutting.HalfPlatesPerPlate);
    }

    [Fact]
    public void A_plate_goes_on_a_complete_empty_shear_only()
    {
        Assert.Equal(CutLoadVerdict.Loads, Cutting.CanLoad(Cutting.LeadPlate, true, false));
        Assert.Equal(CutLoadVerdict.Loads, Cutting.CanLoad(Cutting.CopperPlate, true, false));
        Assert.Equal(CutLoadVerdict.Incomplete, Cutting.CanLoad(Cutting.LeadPlate, false, false));
        Assert.Equal(CutLoadVerdict.Occupied, Cutting.CanLoad(Cutting.CopperPlate, true, true));
        Assert.Equal(CutLoadVerdict.NotAPlate, Cutting.CanLoad("game:ingot-copper", true, false));
        Assert.Equal(CutLoadVerdict.NotAPlate, Cutting.CanLoad("seraphhorizons:halfplate-copper", true, false));
    }

    // ---- The cut ----

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.5)]
    public void W_advances_with_the_treadle_and_the_plate_is_done_at_1(int k, double strokes)
    {
        var job = new CutJob(k, 0);
        Assert.True(job.Untouched);
        (job, bool finished) = job.Advance(2 * Math.PI * strokes / 2, strokes);
        Assert.Equal(0.5, job.Work, 9);
        Assert.False(finished);
        Assert.False(job.Untouched);
        (job, finished) = job.Advance(2 * Math.PI * strokes * 0.49, strokes);
        Assert.False(finished);
        (job, finished) = job.Advance(2 * Math.PI * strokes, strokes);   // more than enough: held at 1
        Assert.True(finished);
        Assert.Equal(1.0, job.Work);
        // done, it goes no further and does not finish twice
        (job, finished) = job.Advance(100, strokes);
        Assert.False(finished);
        Assert.Equal(CutJob.None, CutJob.None.Advance(100, strokes).Job);
    }

    [Fact]
    public void W_advances_only_while_the_treadle_is_held()
    {
        Assert.True(Cutting.Running(complete: true, plateOn: true, held: true));
        Assert.False(Cutting.Running(true, true, false));
        Assert.False(Cutting.Running(true, false, true));
        Assert.False(Cutting.Running(false, true, true));
        // one second of holding is one treadle stroke: a lead plate takes 1, copper 1.5
        Assert.Equal(2 * Math.PI, Cutting.StrokeRadiansPerSecond, 9);
        var c = new SquaringShearConfig();
        Assert.Equal(1.0, Cutting.PlatesFor(Cutting.StrokeRadiansPerSecond, c.StrokesPerPlate(1)), 9);
        Assert.Equal(1 / 1.5, Cutting.PlatesFor(Cutting.StrokeRadiansPerSecond, c.StrokesPerPlate(2)), 9);
        Assert.Equal(0, Cutting.PlatesFor(-3, 6));
        Assert.Equal(0, Cutting.PlatesFor(3, 0));
    }

    [Fact]
    public void The_cuts_crossed_in_a_step_are_counted()
    {
        double[] moments = [0.4];
        Assert.Equal(0, CutJob.Crossed(moments, 0, 0.3));
        Assert.Equal(1, CutJob.Crossed(moments, 0.3, 0.4));
        Assert.Equal(0, CutJob.Crossed(moments, 0.4, 0.5));
        Assert.Equal(1, CutJob.Crossed(moments, 0, 1));
    }

    [Fact]
    public void A_saved_cut_is_clamped_and_a_bad_class_is_none()
    {
        Assert.Equal(new CutJob(2, 1), CutJob.Restore(2, 7));
        Assert.Equal(new CutJob(1, 0), CutJob.Restore(1, double.NaN));
        Assert.Equal(CutJob.None, CutJob.Restore(3, 0.5));
    }

    [Fact]
    public void A_player_holds_the_treadle_until_they_let_go_or_go_quiet()
    {
        var holds = new TreadleHolds();
        Assert.False(holds.Any(0));
        holds.Hold("a", 1000);
        Assert.True(holds.Any(1000 + Cutting.HoldTimeoutMs));
        holds.Hold("b", 1500);
        Assert.True(holds.Any(1000 + Cutting.HoldTimeoutMs + 1));   // a went quiet, b holds
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
        var clock = new SquaringShearClock();
        clock.Advance(0.1f, 1, 0, held: false, 6);
        Assert.Equal((0.0, 0.0, 1), (clock.Theta, clock.Work, clock.Class));
        Assert.Equal(0.25f, clock.Presence, 5);
        Assert.True(clock.ShowsPlate("platelead"));
        Assert.False(clock.ShowsPlate("platecopper"));
        // held for 0.1 s: a tenth of a stroke, a sixtieth of a plate at six strokes a plate
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
        var clock = new SquaringShearClock();
        clock.Advance(1f, 2, 0.98, false, 1.5);
        Assert.Equal(1f, clock.Presence);
        clock.Advance(0.2f, 0, 0, false, 1.5);   // delivered and cleared
        Assert.Equal(1.0, clock.Work);
        Assert.Equal(2, clock.Class);
        Assert.Equal(0.5f, clock.Presence, 5);
        Assert.False(clock.ShowsPlate("platecopper"));
        clock.Advance(0.3f, 0, 0, false, 1.5);
        Assert.Equal((0f, 0), (clock.Presence, clock.Class));
        // the next plate starts from the server's 0
        clock.Advance(0.1f, 1, 0, false, 1);
        Assert.Equal((0.0, 1), (clock.Work, clock.Class));
        // and so does a second plate of the same metal after the first is done
        clock.Advance(0.1f, 1, 0.99, false, 1);
        clock.Advance(0.1f, 1, 0, false, 1);
        Assert.Equal(0.0, clock.Work);
    }

    // ---- Settings ----

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new SquaringShearConfig();
        Assert.Equal((1f, 1.5f), (c.StrokesPerPlateLead, c.StrokesPerPlateCopper));
        Assert.Equal(c.StrokesPerPlateLead * 1.5, c.StrokesPerPlateCopper, 3);   // copper half as much again
        Assert.Equal(0, c.StrokesPerPlate(0));
        Assert.Empty(c.Sanitise());
        var bad = new SquaringShearConfig { StrokesPerPlateLead = float.NaN, StrokesPerPlateCopper = -1 };
        Assert.Equal(2, bad.Sanitise().Count);
        Assert.Equal((1f, 1.5f), (bad.StrokesPerPlateLead, bad.StrokesPerPlateCopper));
    }

    // The player looks along the shear: it runs away from them, the table end nearest.
    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_shear_runs_away_from_the_player(Side look)
    {
        var side = SquaringShearRig.PlacedSide(look);
        Assert.Equal(look.Normal(), Footprint.ToWorld(new Int3(0, 0, 1), side));
    }
}
