using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CylinderBorer;

/// <summary>
/// The cylinder borer's gameplay rules (CylinderBorer/Core): the four stages after the frame in
/// order, the cutter head's durability, wear by the oil's fill and take-back, the bore's arithmetic,
/// the renderer's clock and the settings.
/// </summary>
public class CylinderBorerGameplayTests
{
    private static readonly string[] Order =
        [CylinderBorerParts.BushingsCode, CylinderBorerParts.BarCode, CylinderBorerParts.FeedCode, CylinderBorerParts.HeadCode];

    private static CylinderBorerParts Built(int headLeft = 8, int headCapacity = 8)
    {
        var parts = new CylinderBorerParts();
        foreach (var code in Order)
            Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(code, headLeft, headCapacity));
        return parts;
    }

    [Fact]
    public void The_stages_are_frame_then_bushings_bar_feed_and_cutter_head()
    {
        Assert.Equal([CylinderBorerStage.Bushings, CylinderBorerStage.Bar, CylinderBorerStage.Feed, CylinderBorerStage.CutterHead],
            CylinderBorerRequires.Stages);
        Assert.Equal(["bushings", "bar", "feed", "cutterhead"], CylinderBorerRequires.Stages.Select(CylinderBorerRequires.Name));
        Assert.Equal(new HashSet<string> { "bushings", "bar", "feed", "cutterhead", "blankiron", "blanksteel" }, CylinderBorerRequires.KnownRequires);
        var parts = new CylinderBorerParts();
        for (int i = 0; i < Order.Length; i++)
        {
            Assert.Equal(CylinderBorerRequires.Stages[i], parts.Next);
            Assert.Equal(Order[i], parts.NextPart);
            Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(Order[i], 8, 8));
        }
        Assert.True(parts.Complete);
        Assert.Null(parts.Next);
        Assert.Null(parts.NextPart);
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new CylinderBorerParts();
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit(CylinderBorerParts.BarCode));
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit(CylinderBorerParts.HeadCode, 8, 8));
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit("game:rod-iron"));
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit("game:metal-parts"));
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit(null));
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.BushingsCode));
        Assert.Equal(CylinderBorerFitVerdict.AlreadyFitted, parts.Fit(CylinderBorerParts.BushingsCode));
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit(CylinderBorerParts.FeedCode));
        Assert.True(parts.Has(CylinderBorerStage.Bushings));
        Assert.False(parts.Has(CylinderBorerStage.Feed));
    }

    [Fact]
    public void Fitted_draws_each_stage_once_it_is_in_and_never_the_castings()
    {
        var parts = new CylinderBorerParts();
        Assert.True(parts.Fitted(null));
        Assert.False(parts.Fitted("bushings"));
        parts.Fit(CylinderBorerParts.BushingsCode);
        Assert.True(parts.Fitted("bushings"));
        Assert.False(parts.Fitted("bar"));
        parts = Built();
        foreach (var stage in CylinderBorerRequires.Stages)
            Assert.True(parts.Fitted(CylinderBorerRequires.Name(stage)));
        Assert.False(parts.Fitted(CylinderBorerRequires.BlankIron));
        Assert.False(parts.Fitted(CylinderBorerRequires.BlankSteel));
        Assert.False(parts.Fitted("chain"));
    }

    [Fact]
    public void The_cutter_head_carries_its_durability_and_a_spent_one_is_refused()
    {
        var parts = new CylinderBorerParts();
        foreach (var code in Order.Take(3))
            parts.Fit(code);
        Assert.Equal(CylinderBorerFitVerdict.HeadSpent, parts.Fit(CylinderBorerParts.HeadCode, 0, 8));
        Assert.False(parts.Complete);
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, 5, 8));
        Assert.Equal(5, parts.HeadLeft);
        Assert.Equal(8, parts.HeadCapacity);
        Assert.True(parts.Complete);
    }

    [Fact]
    public void Ctrl_takes_the_head_back_unworn_then_the_casting()
    {
        var parts = Built(6, 8);
        Assert.Equal(CylinderBorerTakeBack.Head, parts.TakeBack(blankOn: true));
        Assert.Equal((6, 8), parts.RemoveHead());
        Assert.False(parts.Complete);
        Assert.Equal(CylinderBorerStage.CutterHead, parts.Next);
        Assert.Equal(CylinderBorerTakeBack.Blank, parts.TakeBack(blankOn: true));
        Assert.Equal(CylinderBorerTakeBack.Nothing, parts.TakeBack(blankOn: false));
        Assert.Null(parts.RemoveHead());
        // the rest only by breaking the frame
        Assert.True(parts.Has(CylinderBorerStage.Feed));
    }

    [Fact]
    public void A_spent_head_is_gone_and_the_borer_needs_another()
    {
        var parts = Built(2, 8);
        Assert.False(parts.WearHead(1));
        Assert.Equal(1, parts.HeadLeft);
        Assert.False(parts.WearHead(0));
        Assert.True(parts.WearHead(1));
        Assert.False(parts.Has(CylinderBorerStage.CutterHead));
        Assert.False(parts.Complete);
        Assert.Equal(0, parts.HeadLeft);
        Assert.Equal(0, parts.HeadCapacity);
        Assert.False(parts.WearHead(1));
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, 8, 8));
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_the_head_with_its_durability()
    {
        var parts = Built(3, 8);
        Assert.Equal(
            [new(CylinderBorerParts.BushingsCode), new(CylinderBorerParts.BarCode), new(CylinderBorerParts.FeedCode), new CylinderBorerDrop(CylinderBorerParts.HeadCode, 3)],
            parts.Returns());
        parts.RemoveHead();
        Assert.Equal(3, parts.Returns().Count);
    }

    [Fact]
    public void The_creative_shortcut_fits_each_stage_in_turn()
    {
        var parts = new CylinderBorerParts();
        int n = 0;
        while (parts.NextPart is { } next && n++ < 10)
            Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(next, 8, 8));
        Assert.Equal(4, n);
        Assert.True(parts.Complete);
        Assert.Equal(CylinderBorerParts.HeadCode, parts.FittedIn(CylinderBorerStage.CutterHead));
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_drops_what_could_not_have_been_fitted()
    {
        var parts = Built(4, 8);
        var restored = CylinderBorerParts.Restore(parts.Snapshot(), parts.HeadLeft, parts.HeadCapacity);
        Assert.True(restored.Complete);
        Assert.Equal(4, restored.HeadLeft);
        Assert.Equal(8, restored.HeadCapacity);
        // a gap drops what follows it
        var gap = new Dictionary<string, string> { ["bushings"] = CylinderBorerParts.BushingsCode, ["feed"] = CylinderBorerParts.FeedCode };
        restored = CylinderBorerParts.Restore(gap, 0, 0);
        Assert.True(restored.Has(CylinderBorerStage.Bushings));
        Assert.False(restored.Has(CylinderBorerStage.Feed));
        // a spent head, and a code that is not its stage's, are dropped
        restored = CylinderBorerParts.Restore(parts.Snapshot(), 0, 8);
        Assert.False(restored.Has(CylinderBorerStage.CutterHead));
        Assert.True(restored.Has(CylinderBorerStage.Feed));
        var wrong = new Dictionary<string, string> { ["bushings"] = "game:metal-parts" };
        Assert.False(CylinderBorerParts.Restore(wrong, 0, 0).Has(CylinderBorerStage.Bushings));
    }

    [Fact]
    public void Castings_and_cylinders_by_metal()
    {
        Assert.Equal(1, Boring.ClassOfBlank("seraphhorizons:cylinderblank-iron"));
        Assert.Equal(2, Boring.ClassOfBlank("seraphhorizons:cylinderblank-steel"));
        Assert.Equal(0, Boring.ClassOfBlank("seraphhorizons:cylinder-iron"));
        Assert.Equal(0, Boring.ClassOfBlank("game:ingot-iron"));
        Assert.Equal(0, Boring.ClassOfBlank(null));
        Assert.Equal("seraphhorizons:cylinder-iron", Boring.CylinderFor(1));
        Assert.Equal("seraphhorizons:cylinder-steel", Boring.CylinderFor(2));
        Assert.Null(Boring.CylinderFor(0));
        Assert.Equal(Boring.BlankIron, Boring.BlankFor(1));
        Assert.Equal(Boring.BlankSteel, Boring.BlankFor(2));
        Assert.Equal("iron", Boring.MetalOf(1));
        Assert.Equal("steel", Boring.MetalOf(2));
        Assert.Equal(CylinderBorerRequires.BlankIron, CylinderBorerRequires.Blank(1));
        Assert.Equal(CylinderBorerRequires.BlankSteel, CylinderBorerRequires.Blank(2));
        Assert.Null(CylinderBorerRequires.Blank(0));
    }

    [Fact]
    public void A_casting_goes_in_only_on_a_complete_borer_with_the_cradle_empty()
    {
        Assert.Equal(BoreLoadVerdict.Loads, Boring.CanLoad(Boring.BlankIron, true, false));
        Assert.Equal(BoreLoadVerdict.Loads, Boring.CanLoad(Boring.BlankSteel, true, false));
        Assert.Equal(BoreLoadVerdict.Incomplete, Boring.CanLoad(Boring.BlankIron, false, false));
        Assert.Equal(BoreLoadVerdict.Occupied, Boring.CanLoad(Boring.BlankIron, true, true));
        Assert.Equal(BoreLoadVerdict.NotABlank, Boring.CanLoad(Boring.CylinderIron, true, false));
        Assert.Equal(BoreLoadVerdict.NotABlank, Boring.CanLoad("game:ingot-steel", true, false));
    }

    [Theory]
    [InlineData(1, 64.0)]
    [InlineData(2, 96.0)]
    public void One_traverse_bores_a_cylinder_at_its_metals_pace(int k, double turnsPerBlock)
    {
        var job = new BoreJob(k, 0);
        Assert.True(job.On);
        Assert.Equal(18.0 / 16, job.End, 9);
        Assert.Equal(18.0 / 16 * turnsPerBlock, Boring.TurnsPerCylinder(turnsPerBlock), 9);
        // a block of travel is turnsPerBlock axle turns
        (job, bool finished) = job.Advance(2 * Math.PI * turnsPerBlock, turnsPerBlock);
        Assert.Equal(1.0, job.Work, 9);
        Assert.False(finished);
        // backwards turns count as turns; the rest finishes it, and W stops at the end
        (job, finished) = job.Advance(2 * Math.PI * turnsPerBlock, turnsPerBlock);
        Assert.True(finished);
        Assert.True(job.Done);
        Assert.Equal(Boring.Travel, job.Work, 9);
        (var after, bool again) = job.Advance(1000, turnsPerBlock);
        Assert.False(again);
        Assert.Equal(job, after);
        // nothing for no turn, a negative one or no pace
        Assert.Equal(0, Boring.BlocksFor(-3, turnsPerBlock));
        Assert.Equal(0, Boring.BlocksFor(3, 0));
        Assert.False(BoreJob.None.Advance(100, turnsPerBlock).Finished);
    }

    [Fact]
    public void A_saved_bore_is_clamped_and_none_without_a_casting()
    {
        Assert.Equal(new BoreJob(1, 0.5), BoreJob.Restore(1, 0.5));
        Assert.Equal(Boring.Travel, BoreJob.Restore(2, 99).Work, 9);
        Assert.Equal(0, BoreJob.Restore(2, double.NaN).Work);
        Assert.Equal(0, BoreJob.Restore(1, -1).Work);
        Assert.Equal(BoreJob.None, BoreJob.Restore(3, 0.5));
        Assert.Equal(BoreJob.None, BoreJob.Restore(0, 0.5));
    }

    [Fact]
    public void It_runs_only_complete_with_a_casting_at_speed()
    {
        Assert.True(Boring.Running(true, true, 0.1f, 0.05f));
        Assert.True(Boring.Running(true, true, -0.1f, 0.05f));
        Assert.False(Boring.Running(false, true, 0.1f, 0.05f));
        Assert.False(Boring.Running(true, false, 0.1f, 0.05f));
        Assert.False(Boring.Running(true, true, 0.01f, 0.05f));
    }

    [Fact]
    public void The_head_bores_eight_cylinders_oiled_fewer_as_the_tank_runs_down_and_breaks_dry()
    {
        Assert.Equal(1, CutterHeadWear.WearFor(1, 1, 8));
        Assert.Equal(2, CutterHeadWear.WearFor(1, 0.5, 8));
        Assert.Equal(4, CutterHeadWear.WearFor(1, 0.25, 8));
        Assert.Equal(3, CutterHeadWear.WearFor(1, 0.4, 8));      // 2.5, rounded up
        Assert.Equal(8, CutterHeadWear.WearFor(1, 0.1, 8));      // 10, at most what is left
        Assert.Equal(5, CutterHeadWear.WearFor(1, 0, 5));        // dry: all of it
        Assert.Equal(0, CutterHeadWear.WearFor(1, 1, 0));
        Assert.Equal(0, CutterHeadWear.WearFor(0, 1, 8));
        Assert.Equal(8, CutterHeadWear.CylindersLeft(1, 1, 8));
        Assert.Equal(4, CutterHeadWear.CylindersLeft(1, 0.5, 8));
        Assert.Equal(1, CutterHeadWear.CylindersLeft(1, 0, 8));
        Assert.Equal(0, CutterHeadWear.CylindersLeft(1, 1, 0));
        Assert.Equal(int.MaxValue, CutterHeadWear.CylindersLeft(0, 1, 8));
        Assert.Equal(2, CutterHeadWear.Multiplier(0.5), 9);
        Assert.Equal(double.PositiveInfinity, CutterHeadWear.Multiplier(0));
        // a full tank throughout: eight cylinders, the eighth spending the head
        var parts = Built(8, 8);
        int bored = 0;
        while (parts.Complete)
        {
            bored++;
            parts.WearHead(CutterHeadWear.WearFor(CylinderBorerConfig.Defaults.HeadWearPerCylinder, 1, parts.HeadLeft));
        }
        Assert.Equal(8, bored);
    }

    [Fact]
    public void The_clock_follows_the_shaft_but_never_strays_from_the_server()
    {
        var clock = new CylinderBorerClock();
        double turns = 64;
        clock.Advance(0.1f, 0, 1, 0, true, turns);
        Assert.Equal(1, clock.Class);
        Assert.Equal(0.25f, clock.Presence, 5);
        Assert.True(clock.ShowsBlank(CylinderBorerRequires.BlankIron));
        Assert.False(clock.ShowsBlank(CylinderBorerRequires.BlankSteel));
        // ahead of the server by the shaft's own turning, up to the snap
        clock.Advance(0.05f, 2 * Math.PI * turns * 0.03, 1, 0, true, turns);
        Assert.Equal(0.03, clock.Work, 6);
        clock.Advance(0.05f, 2 * Math.PI * turns * 0.1, 1, 0, true, turns);
        Assert.Equal(0, clock.Work, 6);          // too far ahead: back to the server's
        // never behind it
        clock.Advance(0.05f, 0, 1, 0.5, true, turns);
        Assert.Equal(0.5, clock.Work, 6);
        // stopped, it does not move
        clock.Advance(0.05f, 2 * Math.PI * turns * 0.03, 1, 0.5, false, turns);
        Assert.Equal(0.5, clock.Work, 6);
        var input = clock.Input(1.5, 2.5, 0.75);
        Assert.Equal(1.5, input.Theta);
        Assert.Equal(2.5, input.Travel);
        Assert.Equal(0.5, input.Work, 6);
        Assert.Equal(1, input.Class);
        Assert.Equal(0.75, input.Oil);
    }

    [Fact]
    public void The_casting_goes_with_the_job_while_the_head_eases_back_then_a_new_one_starts_from_0()
    {
        var clock = new CylinderBorerClock();
        for (int i = 0; i < 10; i++)
            clock.Advance(0.1f, 0, 2, Boring.Travel, false, 96);
        Assert.Equal(1f, clock.Presence);
        Assert.Equal(Boring.Travel, clock.Work, 6);
        // bored and handed out: the casting is gone at once, W and k held while p eases out
        clock.Advance(0.2f, 0, 0, 0, false, 0);
        Assert.False(clock.ShowsBlank(CylinderBorerRequires.BlankSteel));
        Assert.Equal(2, clock.Class);
        Assert.Equal(Boring.Travel, clock.Work, 6);
        Assert.Equal(0.5f, clock.Presence, 5);
        clock.Advance(0.2f, 0, 0, 0, false, 0);
        Assert.Equal(0f, clock.Presence);
        Assert.Equal(0, clock.Class);
        Assert.Equal(0, clock.Work);
        // the next casting starts from the server's W
        clock.Advance(0.1f, 0, 1, 0, false, 64);
        Assert.Equal(1, clock.Class);
        Assert.Equal(0, clock.Work);
        Assert.True(clock.ShowsBlank(CylinderBorerRequires.BlankIron));
    }

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new CylinderBorerConfig();
        Assert.Equal(64f, c.TurnsPerBlockIron);
        Assert.Equal(96f, c.TurnsPerBlockSteel);
        Assert.Equal(1, c.HeadWearPerCylinder);
        Assert.Equal(0.3f, c.Resistance);
        Assert.Equal(0.05f, c.MinSpeed);
        Assert.Equal(64, c.TurnsPerBlock(1));
        Assert.Equal(96, c.TurnsPerBlock(2));
        Assert.Equal(0, c.TurnsPerBlock(0));
        Assert.Empty(c.Sanitise());
        var bad = new CylinderBorerConfig
        {
            TurnsPerBlockIron = 0, TurnsPerBlockSteel = float.NaN, HeadWearPerCylinder = -1, Resistance = 11, MinSpeed = -1,
        };
        Assert.Equal(5, bad.Sanitise().Count);
        Assert.Equal(64f, bad.TurnsPerBlockIron);
        Assert.Equal(96f, bad.TurnsPerBlockSteel);
        Assert.Equal(1, bad.HeadWearPerCylinder);
        Assert.Equal(0.3f, bad.Resistance);
        Assert.Equal(0.05f, bad.MinSpeed);
    }

    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_borer_runs_away_from_the_player(Side look)
    {
        var side = CylinderBorerRig.PlacedSide(look);
        // the drive end (native z 3) is three blocks along the look from the bench end
        var far = Footprint.ToWorld(new Int3(0, 0, 3), side);
        var n = look.Normal();
        Assert.Equal(new Int3(n.X * 3, 0, n.Z * 3), far);
    }
}
