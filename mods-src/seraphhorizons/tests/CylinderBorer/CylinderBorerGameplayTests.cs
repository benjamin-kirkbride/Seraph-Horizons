using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CylinderBorer;

/// <summary>
/// The cylinder borer's gameplay rules: its build (config/builds/cylinderborer.json on the shared staged build,
/// the cutter head its durable wearing part): the four stages after the frame in order, each taking its materials,
/// the cutter head's durability, wear and take-back; the bore's arithmetic and wear by the oil's fill (CylinderBorer/Core),
/// the renderer's clock and the settings.
/// </summary>
public class CylinderBorerGameplayTests
{
    private const string Head = CylinderBorerBuild.HeadCode;

    /// <summary>Each stage's materials in turn, as a player would put them in: full stacks of each.</summary>
    private static readonly (string Code, int Count)[][] Bills =
    [
        [("game:metal-parts", 1)],
        [("game:rod-iron", 3), ("game:metal-parts", 1), ("seraphhorizons:largegear-stainless", 1)],
        [("game:plank-oak", 2), ("game:rod-steel", 1), ("game:rope", 2), ("game:metalplate-meteoriciron", 1)],
    ];

    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "cylinderborer.json")));

    private static AssemblyClick Click(MachineAssembly parts, string? code, int held = 1, int maxStack = 64, double? uses = null) =>
        parts.Fit(code, held, maxStack, uses);

    private static void FitStage(MachineAssembly parts, int stage)
    {
        foreach (var (code, count) in Bills[stage])
            Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, count, stage), Click(parts, code, 64));
        Assert.True(parts.Build.StageComplete(stage));
    }

    private static MachineAssembly Built(int headLeft = 8)
    {
        var parts = new MachineAssembly(Build());
        for (int s = 0; s < Bills.Length; s++)
            FitStage(parts, s);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, Head, uses: headLeft).Verdict);
        return parts;
    }

    private static double? HeadLeft(MachineAssembly parts) => parts.Wearing.Fitted(CylinderBorerBuild.HeadPart)?.Left;

    [Fact]
    public void The_stages_are_frame_then_bushings_bar_feed_and_cutter_head()
    {
        var build = Build();
        Assert.Equal(["bushings", "bar", "feed", "cutterhead"], build.Stages.Select(s => s.Id));
        Assert.Equal(CylinderBorerRequires.Stages, build.Stages.Select(s => Assert.Single(s.Adds)));
        Assert.Null(build.Block);   // its records are its own in the export
        Assert.Equal(new HashSet<string> { "bushings", "bar", "feed", "cutterhead", "blankiron", "blanksteel" }, CylinderBorerRequires.KnownRequires);
        var parts = new MachineAssembly(build);
        for (int i = 0; i < Bills.Length; i++)
        {
            Assert.Equal(i, parts.Build.Next);
            FitStage(parts, i);
        }
        Assert.Equal(3, parts.Build.Next);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, Head, uses: 8).Verdict);
        Assert.True(parts.Ready);
        Assert.Null(parts.Build.Next);
        Assert.Empty(parts.Build.NextNeeds());
    }

    [Fact]
    public void Each_stage_takes_its_old_items_bill_and_only_the_head_is_forged()
    {
        var build = Build();
        string Bill(int s) => string.Join("; ", build.Stages[s].Materials.Select(m => $"{m.Quantity} {string.Join("|", m.Codes)}"));
        Assert.Equal("1 game:metal-parts", Bill(0));
        Assert.Equal("3 game:rod-iron|game:rod-meteoriciron|game:rod-steel; 1 game:metal-parts; 1 seraphhorizons:largegear-stainless", Bill(1));
        Assert.Equal("2 game:plank-oak; 1 game:rod-iron|game:rod-meteoriciron|game:rod-steel; 2 game:rope; "
                     + "1 game:metalplate-iron|game:metalplate-meteoriciron|game:metalplate-steel", Bill(2));
        Assert.Equal("1 seraphhorizons:cylinderborerhead-steel", Bill(3));
        Assert.Equal(CylinderBorerBuild.HeadPart, build.Stages[3].Materials.Single().Wearing);
        var head = Assert.Single(build.Wearing);
        Assert.True(head.Durable);
        Assert.Equal("cutterhead", head.Requires);
        // the old items are no part of it
        foreach (var code in new[] { "seraphhorizons:cylinderborerbar", "seraphhorizons:cylinderborerfeed", "seraphhorizons:cylinderborerbushings" })
            Assert.Equal(AssemblyVerdict.NotAPart, Click(new MachineAssembly(build), code).Verdict);
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "seraphhorizons:largegear-stainless").Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:rope", 2).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, Head, uses: 8).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:rod-iron", 3).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, "game:rod-copper", 3).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, null).Verdict);
        // a code with no domain is the game's; one lot of metal parts from a stack of two
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 1, 0), Click(parts, "metal-parts", 2, 16));
        Assert.True(parts.Build.StageComplete(0));
        // the bar's metal parts now, then already in
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 1, 1), Click(parts, "game:metal-parts", 2, 16));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, "game:metal-parts", 2, 16).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:plank-oak", 2).Verdict);
        Assert.False(parts.Build.StageComplete(1));
        Assert.False(parts.Build.StageComplete(2));
    }

    [Fact]
    public void A_stage_takes_a_material_a_click_and_keeps_what_it_holds_until_it_is_complete()
    {
        var parts = new MachineAssembly(Build());
        FitStage(parts, 0);
        // two rods held of the three: too few, not a full stack
        Assert.Equal(AssemblyVerdict.TooFew, Click(parts, "game:rod-iron", 2, 16).Verdict);
        Assert.Equal(0, parts.Build.Count(1, 0));
        // a full stack short of it goes in whole (a stack of one rod is full when rods stack to one)
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 1, 1), Click(parts, "game:rod-iron", 1, 1));
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 2, 1), Click(parts, "game:rod-steel", 5, 16));
        Assert.Equal(3, parts.Build.Count(1, 0));
        Assert.Equal(new[] { ("seraphhorizons:largegear-stainless", 1), ("game:metal-parts", 1) },
            parts.Build.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)).OrderByDescending(n => n.Item1));
        // drawn only once complete
        Assert.False(parts.Shows("bar"));
        Click(parts, "seraphhorizons:largegear-stainless");
        Assert.False(parts.Shows("bar"));
        Assert.Equal(1, parts.Build.Next);
        Click(parts, "game:metal-parts", 4, 16);
        Assert.True(parts.Shows("bar"));
        Assert.Equal(2, parts.Build.Next);
        // what is in survives a save, partial stage and mixed metals included
        Click(parts, "game:plank-oak", 3);
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out _);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal(2, restored.Build.Count(2, 0));
        Assert.False(restored.Build.StageComplete(2));
        Assert.Equal(
            [new("game:metal-parts", 2), new("game:rod-iron", 1), new("game:rod-steel", 2),
             new("seraphhorizons:largegear-stainless", 1), new StageDrop("game:plank-oak", 2)],
            restored.Returns());
    }

    [Fact]
    public void Each_stage_is_drawn_once_it_is_in_and_never_the_castings()
    {
        var parts = new MachineAssembly(Build());
        Assert.True(parts.Shows(null));
        Assert.False(parts.Shows("bushings"));
        Click(parts, CylinderBorerBuild.BushingsCode);
        Assert.True(parts.Shows("bushings"));
        Assert.False(parts.Shows("bar"));
        parts = Built();
        foreach (var stage in CylinderBorerRequires.Stages)
            Assert.True(parts.Shows(stage));
        // the castings are the work's: no stage adds them (BECylinderBorer.ShowsPart draws them by its clock)
        Assert.DoesNotContain(Build().Stages, s => s.Adds.Contains(CylinderBorerRequires.BlankIron) || s.Adds.Contains(CylinderBorerRequires.BlankSteel));
        parts.Take(CylinderBorerBuild.HeadPart);
        Assert.False(parts.Shows("cutterhead"));
    }

    [Fact]
    public void The_cutter_head_carries_its_durability_and_a_spent_one_is_refused()
    {
        var parts = new MachineAssembly(Build());
        for (int s = 0; s < Bills.Length; s++)
            FitStage(parts, s);
        Assert.Equal(AssemblyVerdict.Spent, Click(parts, Head, uses: 0).Verdict);
        Assert.False(parts.Built);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, Head, uses: 5).Verdict);
        Assert.Equal(5, HeadLeft(parts));
        Assert.Equal(0.625, parts.Wearing.ShareLeft(CylinderBorerBuild.HeadPart), 9);
        Assert.True(parts.Ready);
        Assert.Equal(AssemblyVerdict.NotWorn, Click(parts, Head, uses: 8).Verdict);
    }

    [Fact]
    public void Ctrl_takes_the_head_back_unworn_then_the_casting()
    {
        var parts = Built(6);
        Assert.Equal(CylinderBorerTakeBack.Head, CylinderBorerBuild.TakeBack(parts, blankOn: true));
        Assert.Equal((Head, 6.0), parts.Take(CylinderBorerBuild.HeadPart));
        Assert.False(parts.Ready);
        Assert.Equal(CylinderBorerTakeBack.Blank, CylinderBorerBuild.TakeBack(parts, blankOn: true));
        Assert.Equal(CylinderBorerTakeBack.Nothing, CylinderBorerBuild.TakeBack(parts, blankOn: false));
        Assert.Null(parts.Take(CylinderBorerBuild.HeadPart));
        // the rest only by breaking the frame, and a head goes back in
        Assert.True(parts.Build.StageComplete(2));
        Assert.Equal(3, parts.Build.Count(1, 0));
        Assert.Equal(AssemblyVerdict.Renewed, Click(parts, Head, uses: 6).Verdict);
        Assert.True(parts.Ready);
    }

    [Fact]
    public void A_spent_head_is_gone_and_the_borer_needs_another()
    {
        var parts = Built(2);
        Assert.False(parts.Wearing.Wear(CylinderBorerBuild.HeadPart, 1));
        Assert.Equal(1, HeadLeft(parts));
        Assert.False(parts.Wearing.Wear(CylinderBorerBuild.HeadPart, 0));
        Assert.True(parts.Wearing.Wear(CylinderBorerBuild.HeadPart, 1));
        Assert.Null(HeadLeft(parts));
        Assert.True(parts.Built);
        Assert.False(parts.Ready);
        Assert.False(parts.Wearing.Wear(CylinderBorerBuild.HeadPart, 1));
        Assert.DoesNotContain(parts.Returns(), d => d.Code == Head);
        Assert.Equal(AssemblyVerdict.Renewed, Click(parts, Head, uses: 8).Verdict);
    }

    [Fact]
    public void Breaking_returns_exactly_what_went_in_the_head_with_its_durability()
    {
        var parts = Built(3);
        Assert.Equal(
            [new("game:metal-parts", 2), new("game:rod-iron", 3), new("seraphhorizons:largegear-stainless", 1),
             new("game:plank-oak", 2), new("game:rod-steel", 1), new("game:rope", 2), new("game:metalplate-meteoriciron", 1),
             new StageDrop(Head, 1, 3)],
            parts.Returns());
        parts.Take(CylinderBorerBuild.HeadPart);
        Assert.Equal(7, parts.Returns().Count);
        Assert.DoesNotContain(parts.Returns(), d => d.Code == Head);
    }

    [Fact]
    public void The_creative_shortcut_fills_each_stage_in_turn()
    {
        var parts = new MachineAssembly(Build());
        Click(parts, "game:rod-steel", 3, 16);   // nothing yet: the bushings go first
        FitStage(parts, 0);
        Click(parts, "game:rod-steel", 2, 2);    // part of the bar, the rest filled
        int n = 0;
        while (!parts.Ready && parts.FillNext() && n++ < 10) { }
        Assert.Equal(3, n);
        Assert.Equal(8, HeadLeft(parts));
        Assert.False(parts.FillNext());
        Assert.Equal(
            [new("game:metal-parts", 2), new("game:rod-steel", 2), new("game:rod-iron", 2),
             new("seraphhorizons:largegear-stainless", 1), new("game:plank-oak", 2), new("game:rope", 2),
             new("game:metalplate-iron", 1), new StageDrop(Head, 1, 8)],
            parts.Returns());
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_drops_what_could_not_have_been_fitted()
    {
        var parts = Built(4);
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.True(restored.Ready);
        Assert.Equal(4, HeadLeft(restored));
        Assert.Equal(parts.Returns(), restored.Returns());
        // a gap drops what follows it
        var gap = new Dictionary<string, int> { ["0/0/game:metal-parts"] = 1, ["2/0/game:plank-oak"] = 2 };
        restored = MachineAssembly.Restore(Build(), gap, new Dictionary<string, (string, double)>(), out dropped);
        Assert.True(restored.Build.StageComplete(0));
        Assert.Equal(0, restored.Build.Count(2, 0));
        Assert.Equal([new StageDrop("game:plank-oak", 2)], dropped);
        // a spent head, and a code that is not its material's, are dropped
        restored = MachineAssembly.Restore(Build(), build, new Dictionary<string, (string, double)> { ["head"] = (Head, 0) }, out _);
        Assert.False(restored.Ready);
        Assert.True(restored.Built);
        var wrong = new Dictionary<string, int> { ["0/0/game:rod-iron"] = 1 };
        Assert.False(MachineAssembly.Restore(Build(), wrong, new Dictionary<string, (string, double)>(), out _).Build.StageComplete(0));
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
        var parts = Built(8);
        int bored = 0;
        while (parts.Ready)
        {
            bored++;
            parts.Wearing.Wear(CylinderBorerBuild.HeadPart, CutterHeadWear.WearFor(CylinderBorerConfig.Defaults.HeadWearPerCylinder, 1, (int)HeadLeft(parts)!));
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
        // the shared machine base's rule (BlockMachine.TryPlaceBlock)
        var side = OreMachineRig.PlacedSide(look);
        // the drive end (native z 3) is three blocks along the look from the bench end
        var far = Footprint.ToWorld(new Int3(0, 0, 3), side);
        var n = look.Normal();
        Assert.Equal(new Int3(n.X * 3, 0, n.Z * 3), far);
    }
}
