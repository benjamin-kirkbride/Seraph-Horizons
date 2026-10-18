using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CylinderBorer;

/// <summary>
/// The cylinder borer's gameplay rules (CylinderBorer/Core): the four stages after the frame in
/// order, each taking its materials, the cutter head's durability, wear by the oil's fill and take-back, the bore's arithmetic,
/// the renderer's clock and the settings.
/// </summary>
public class CylinderBorerGameplayTests
{
    /// <summary>Each stage's materials in turn, as a player would put them in: full stacks of each.</summary>
    private static readonly (string Code, int Count)[][] Bills =
    [
        [("game:metal-parts", 1)],
        [("game:rod-iron", 3), ("game:metal-parts", 1), ("seraphhorizons:largegear-stainless", 1)],
        [("game:plank-oak", 2), ("game:rod-steel", 1), ("game:rope", 2), ("game:metalplate-meteoriciron", 1)],
    ];

    private static void FitStage(CylinderBorerParts parts, int stage)
    {
        foreach (var (code, count) in Bills[stage])
            Assert.Equal(new CylinderBorerFit(CylinderBorerFitVerdict.Fits, (CylinderBorerStage)stage, count), parts.Fit(code, 64, 64));
        Assert.True(parts.Has((CylinderBorerStage)stage));
    }

    private static CylinderBorerParts Built(int headLeft = 8, int headCapacity = 8)
    {
        var parts = new CylinderBorerParts();
        for (int s = 0; s < Bills.Length; s++)
            FitStage(parts, s);
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, headLeft: headLeft, headCapacity: headCapacity).Verdict);
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
        for (int i = 0; i < Bills.Length; i++)
        {
            Assert.Equal(CylinderBorerRequires.Stages[i], parts.Next);
            FitStage(parts, i);
        }
        Assert.Equal(CylinderBorerStage.CutterHead, parts.Next);
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 8, headCapacity: 8).Verdict);
        Assert.True(parts.Complete);
        Assert.Null(parts.Next);
        Assert.Empty(parts.NextNeeds());
    }

    [Fact]
    public void Each_stage_takes_its_old_items_bill_and_only_the_head_is_forged()
    {
        string Bill(CylinderBorerStage s) => string.Join("; ", CylinderBorerParts.MaterialsFor(s).Select(m => $"{m.Quantity} {string.Join("|", m.Codes)}"));
        Assert.Equal("1 game:metal-parts", Bill(CylinderBorerStage.Bushings));
        Assert.Equal("3 game:rod-iron|game:rod-meteoriciron|game:rod-steel; 1 game:metal-parts; 1 seraphhorizons:largegear-stainless",
            Bill(CylinderBorerStage.Bar));
        Assert.Equal("2 game:plank-oak; 1 game:rod-iron|game:rod-meteoriciron|game:rod-steel; 2 game:rope; "
                     + "1 game:metalplate-iron|game:metalplate-meteoriciron|game:metalplate-steel", Bill(CylinderBorerStage.Feed));
        Assert.Equal("1 seraphhorizons:cylinderborerhead-steel", Bill(CylinderBorerStage.CutterHead));
        // the old items are no part of it
        foreach (var code in new[] { CylinderBorerParts.LegacyBarCode, CylinderBorerParts.LegacyFeedCode, CylinderBorerParts.LegacyBushingsCode })
            Assert.False(CylinderBorerParts.IsPart(code), code);
        Assert.Equal([CylinderBorerStage.Bar, CylinderBorerStage.Feed], CylinderBorerParts.StagesOf("game:rod-meteoriciron"));
        Assert.Equal([CylinderBorerStage.Bushings, CylinderBorerStage.Bar], CylinderBorerParts.StagesOf("metal-parts"));
        Assert.Empty(CylinderBorerParts.StagesOf("game:rod-copper"));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new CylinderBorerParts();
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit("seraphhorizons:largegear-stainless").Verdict);
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit("game:rope", 2).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 8, headCapacity: 8).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit("game:rod-iron", 3).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit("game:rod-copper", 3).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit(CylinderBorerParts.LegacyBushingsCode).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.NotAPart, parts.Fit(null).Verdict);
        // a code with no domain is the game's; one lot of metal parts from a stack of two
        Assert.Equal(new CylinderBorerFit(CylinderBorerFitVerdict.Fits, CylinderBorerStage.Bushings, 1), parts.Fit("metal-parts", 2, 16));
        Assert.True(parts.Has(CylinderBorerStage.Bushings));
        // the bar's metal parts now, then already in
        Assert.Equal(new CylinderBorerFit(CylinderBorerFitVerdict.Fits, CylinderBorerStage.Bar, 1), parts.Fit("game:metal-parts", 2, 16));
        Assert.Equal(CylinderBorerFitVerdict.AlreadyFitted, parts.Fit("game:metal-parts", 2, 16).Verdict);
        Assert.Equal(CylinderBorerFitVerdict.OutOfOrder, parts.Fit("game:plank-oak", 2, 64).Verdict);
        Assert.False(parts.Has(CylinderBorerStage.Bar));
        Assert.False(parts.Has(CylinderBorerStage.Feed));
    }

    [Fact]
    public void A_stage_takes_a_material_a_click_and_keeps_what_it_holds_until_it_is_complete()
    {
        var parts = new CylinderBorerParts();
        FitStage(parts, 0);
        // two rods held of the three: too few, not a full stack
        Assert.Equal(CylinderBorerFitVerdict.TooFew, parts.Fit("game:rod-iron", 2, 16).Verdict);
        Assert.Equal(0, parts.Count(CylinderBorerStage.Bar, 0));
        // a full stack short of it goes in whole (a stack of one rod is full when rods stack to one)
        Assert.Equal(new CylinderBorerFit(CylinderBorerFitVerdict.Fits, CylinderBorerStage.Bar, 1), parts.Fit("game:rod-iron", 1, 1));
        Assert.Equal(new CylinderBorerFit(CylinderBorerFitVerdict.Fits, CylinderBorerStage.Bar, 2), parts.Fit("game:rod-steel", 5, 16));
        Assert.Equal(3, parts.Count(CylinderBorerStage.Bar, 0));
        Assert.Equal(new[] { ("seraphhorizons:largegear-stainless", 1), ("game:metal-parts", 1) },
            parts.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)).OrderByDescending(n => n.Item1));
        // drawn only once complete
        Assert.False(parts.Fitted("bar"));
        parts.Fit("seraphhorizons:largegear-stainless");
        Assert.False(parts.Fitted("bar"));
        Assert.Equal(CylinderBorerStage.Bar, parts.Next);
        parts.Fit("game:metal-parts", 4, 16);
        Assert.True(parts.Fitted("bar"));
        Assert.Equal(CylinderBorerStage.Feed, parts.Next);
        // what is in survives a save, partial stage and mixed metals included
        parts.Fit("game:plank-oak", 3, 64);
        var restored = CylinderBorerParts.Restore(parts.Snapshot(), 0, 0);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal(2, restored.Count(CylinderBorerStage.Feed, 0));
        Assert.False(restored.Has(CylinderBorerStage.Feed));
        Assert.Equal(
            [new("game:metal-parts"), new("game:rod-iron"), new("game:rod-steel", 2), new("game:metal-parts"),
             new("seraphhorizons:largegear-stainless"), new CylinderBorerDrop("game:plank-oak", 2)],
            restored.Returns());
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
        for (int s = 0; s < Bills.Length; s++)
            FitStage(parts, s);
        Assert.Equal(CylinderBorerFitVerdict.HeadSpent, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 0, headCapacity: 8).Verdict);
        Assert.False(parts.Complete);
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 5, headCapacity: 8).Verdict);
        Assert.Equal(5, parts.HeadLeft);
        Assert.Equal(8, parts.HeadCapacity);
        Assert.True(parts.Complete);
        Assert.Equal(CylinderBorerFitVerdict.AlreadyFitted, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 8, headCapacity: 8).Verdict);
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
        Assert.Equal(3, parts.Count(CylinderBorerStage.Bar, 0));
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
        Assert.True(parts.Has(CylinderBorerStage.Feed));
        Assert.False(parts.Complete);
        Assert.Equal(0, parts.HeadLeft);
        Assert.Equal(0, parts.HeadCapacity);
        Assert.False(parts.WearHead(1));
        Assert.Equal(CylinderBorerFitVerdict.Fits, parts.Fit(CylinderBorerParts.HeadCode, headLeft: 8, headCapacity: 8).Verdict);
    }

    [Fact]
    public void Breaking_returns_exactly_what_went_in_the_head_with_its_durability()
    {
        var parts = Built(3, 8);
        Assert.Equal(
            [new("game:metal-parts"), new("game:rod-iron", 3), new("game:metal-parts"), new("seraphhorizons:largegear-stainless"),
             new("game:plank-oak", 2), new("game:rod-steel"), new("game:rope", 2), new("game:metalplate-meteoriciron"),
             new CylinderBorerDrop(CylinderBorerParts.HeadCode, 1, 3)],
            parts.Returns());
        parts.RemoveHead();
        Assert.Equal(8, parts.Returns().Count);
        Assert.DoesNotContain(parts.Returns(), d => d.Code == CylinderBorerParts.HeadCode);
    }

    [Fact]
    public void The_creative_shortcut_fills_each_stage_in_turn()
    {
        var parts = new CylinderBorerParts();
        parts.Fit("game:rod-steel", 3, 16);   // nothing yet: the bushings go first
        FitStage(parts, 0);
        parts.Fit("game:rod-steel", 2, 2);    // part of the bar, the rest filled
        int n = 0;
        while (parts.FillNext(8) && n++ < 10) { }
        Assert.Equal(3, n);
        Assert.True(parts.Complete);
        Assert.Equal(8, parts.HeadLeft);
        Assert.Equal(8, parts.HeadCapacity);
        Assert.False(parts.FillNext(8));
        Assert.Equal(
            [new("game:metal-parts"), new("game:rod-steel", 2), new("game:rod-iron"), new("game:metal-parts"),
             new("seraphhorizons:largegear-stainless"), new("game:plank-oak", 2), new("game:rod-iron"), new("game:rope", 2),
             new("game:metalplate-iron"), new CylinderBorerDrop(CylinderBorerParts.HeadCode, 1, 8)],
            parts.Returns());
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_drops_what_could_not_have_been_fitted()
    {
        var parts = Built(4, 8);
        var restored = CylinderBorerParts.Restore(parts.Snapshot(), parts.HeadLeft, parts.HeadCapacity);
        Assert.True(restored.Complete);
        Assert.Equal(4, restored.HeadLeft);
        Assert.Equal(8, restored.HeadCapacity);
        Assert.Equal(parts.Returns(), restored.Returns());
        // a gap drops what follows it
        var gap = new Dictionary<string, int> { ["0/0/game:metal-parts"] = 1, ["2/0/game:plank-oak"] = 2 };
        restored = CylinderBorerParts.Restore(gap, 0, 0);
        Assert.True(restored.Has(CylinderBorerStage.Bushings));
        Assert.Equal(0, restored.Count(CylinderBorerStage.Feed, 0));
        // a spent head, and a code that is not its material's, are dropped
        restored = CylinderBorerParts.Restore(parts.Snapshot(), 0, 8);
        Assert.False(restored.Has(CylinderBorerStage.CutterHead));
        Assert.True(restored.Has(CylinderBorerStage.Feed));
        var wrong = new Dictionary<string, int> { ["0/0/game:rod-iron"] = 1 };
        Assert.False(CylinderBorerParts.Restore(wrong, 0, 0).Has(CylinderBorerStage.Bushings));
    }

    [Fact]
    public void A_save_from_when_each_stage_was_one_item_restores_those_stages_complete_in_their_materials()
    {
        var old = new Dictionary<string, string>
        {
            ["bushings"] = CylinderBorerParts.LegacyBushingsCode, ["bar"] = CylinderBorerParts.LegacyBarCode,
            ["feed"] = CylinderBorerParts.LegacyFeedCode, ["cutterhead"] = CylinderBorerParts.HeadCode,
        };
        var restored = CylinderBorerParts.RestoreLegacy(old, 5, 8);
        Assert.True(restored.Complete);
        Assert.Equal(5, restored.HeadLeft);
        Assert.Equal(8, restored.HeadCapacity);
        // breaking it gives back what the items were made of, the any-metal ones in iron
        Assert.Equal(
            [new("game:metal-parts"), new("game:rod-iron", 3), new("game:metal-parts"), new("seraphhorizons:largegear-stainless"),
             new("game:plank-oak", 2), new("game:rod-iron"), new("game:rope", 2), new("game:metalplate-iron"),
             new CylinderBorerDrop(CylinderBorerParts.HeadCode, 1, 5)],
            restored.Returns());
        // the bushings as metal parts, as they were saved before the old bushings item went
        restored = CylinderBorerParts.RestoreLegacy(new Dictionary<string, string> { ["bushings"] = "game:metal-parts", ["bar"] = CylinderBorerParts.LegacyBarCode }, 0, 0);
        Assert.True(restored.Has(CylinderBorerStage.Bar));
        Assert.Equal(CylinderBorerStage.Feed, restored.Next);
        // a gap, or a code that was not its stage's item, ends the run; a spent head is dropped
        restored = CylinderBorerParts.RestoreLegacy(new Dictionary<string, string> { ["bushings"] = "game:metal-parts", ["feed"] = CylinderBorerParts.LegacyFeedCode }, 0, 0);
        Assert.Equal(CylinderBorerStage.Bar, restored.Next);
        Assert.Single(restored.Returns());
        restored = CylinderBorerParts.RestoreLegacy(new Dictionary<string, string> { ["bushings"] = "game:metal-parts", ["bar"] = CylinderBorerParts.LegacyFeedCode }, 0, 0);
        Assert.Equal(CylinderBorerStage.Bar, restored.Next);
        restored = CylinderBorerParts.RestoreLegacy(old, 0, 8);
        Assert.Equal(CylinderBorerStage.CutterHead, restored.Next);
        Assert.Empty(CylinderBorerParts.RestoreLegacy(new Dictionary<string, string>(), 0, 0).Returns());
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
