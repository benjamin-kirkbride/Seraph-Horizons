using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.GearCutter;

/// <summary>GearCutter: its build (config/builds/gearcutter.json on the shared staged build, the kit its durable
/// wearing part and the master its last stage): the order, what each stage takes (the spindle and the index fed
/// their materials), the master's class, take-back and drops (#480); the cut's arithmetic; the cutter kit's wear by
/// oil (#481); the renderer's clock; the settings and the shipped rig.</summary>
public class GearCutterGameplayTests
{
    /// <summary>A material stage's bill: each material and how many.</summary>
    private static readonly (string Code, int Count)[] Bill =
        [(GearCutterBuild.RodCode, 2), (GearCutterBuild.PlateCode, 2), (GearCutterBuild.MetalPartsCode, 1)];

    /// <summary>The one-item stages up to the head, in order; null where a material stage's bill goes.</summary>
    private static readonly string?[] Order =
    [
        null, GearCutterBuild.FeedScrewCode, GearCutterBuild.GearboxCode, GearCutterBuild.GearboxCode,
        GearCutterBuild.LiftCamCode, null, GearCutterBuild.ValveCode, GearCutterBuild.HeadCode,
    ];

    /// <summary>The build, the kit's uses its item's durability.</summary>
    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "gearcutter.json")));

    private static AssemblyClick Click(MachineAssembly parts, string? code, int held = 1, int maxStack = 64, double? uses = null) =>
        parts.Fit(code, held, maxStack, uses);

    private static void Fits(MachineAssembly parts, string code, int held = 1, double? uses = null) =>
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, code, held, uses: uses).Verdict);

    /// <summary>Feeds the next stage its whole bill, a material a click.</summary>
    private static void FeedBill(MachineAssembly parts)
    {
        foreach (var (code, n) in Bill)
        {
            int next = parts.Build.Next!.Value;
            Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, n, next), Click(parts, code, 16));
        }
    }

    private static void FitStep(MachineAssembly parts, string? code)
    {
        if (code == null)
            FeedBill(parts);
        else
            Fits(parts, code);
    }

    /// <summary>Every stage up to the head fitted.</summary>
    private static MachineAssembly UpToHead(int steps = 8)
    {
        var parts = new MachineAssembly(Build());
        foreach (var code in Order.Take(steps))
            FitStep(parts, code);
        return parts;
    }

    private static MachineAssembly Complete(string master = GearCutterBuild.MasterCode, int kit = 500)
    {
        var parts = UpToHead();
        Fits(parts, GearCutterBuild.KitCode, uses: kit);
        Fits(parts, master);
        return parts;
    }

    private static int Stage(string id) => Build().Stages.Select(s => s.Id).ToList().IndexOf(id);

    private static int Count(MachineAssembly parts, string stage, int material) => parts.Build.Count(Stage(stage), material);

    // ---- The build ----

    [Fact]
    public void The_stages_go_in_in_the_decided_order()
    {
        Assert.Equal(["spindle", "feedscrew", "camfeed", "camindex", "liftcam", "index", "oiler", "head", "cutter", "master"],
            Build().Stages.Select(s => s.Id));
        Assert.Equal(GearCutterRequires.Stages, Build().Stages.Select(s => s.Adds[0]));
        Assert.Null(Build().Block);   // its records are its own in the export
        var parts = new MachineAssembly(Build());
        var fitted = new List<int>();
        foreach (var code in Order)
        {
            int next = parts.Build.Next!.Value;
            FitStep(parts, code);
            Assert.True(parts.Build.StageComplete(next));
            fitted.Add(next);
        }
        Assert.Equal(Enumerable.Range(0, 8), fitted);
        Assert.Equal(Stage("cutter"), parts.Build.Next);
        Assert.False(parts.Built);
    }

    [Fact]
    public void Only_the_spindle_and_the_index_take_materials_their_old_recipes_bill()
    {
        var build = Build();
        Assert.Equal(["spindle", "index"], build.Stages.Where(s => s.Materials.Count > 1).Select(s => s.Id));
        foreach (var stage in new[] { "spindle", "index" })
            Assert.Equal(Bill, build.Stages[Stage(stage)].Materials.Select(m => (Assert.Single(m.Codes), m.Quantity)));
        // every other stage takes one item (the cutter its kit, the wearing part)
        Assert.All(build.Stages.Where(s => s.Materials.Count == 1), s => Assert.Equal(1, s.Materials[0].Quantity));
        Assert.Equal("kit", build.Stages[Stage("cutter")].Materials.Single().Wearing);
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, GearCutterBuild.FeedScrewCode).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, GearCutterBuild.MasterCode).Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, GearCutterBuild.KitCode, uses: 500).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, "game:gear-rusty").Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, null).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, "seraphhorizons:gearcutterspindle").Verdict);
        FeedBill(parts);
        Assert.Equal(Stage("feedscrew"), parts.Build.Next);
        // the index takes the same materials, but not before its turn: a stage still to come takes them, so not yet
        // (StagedBuild.CanFit asks the later stages first)
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, GearCutterBuild.RodCode, 2).Verdict);
        Fits(parts, GearCutterBuild.FeedScrewCode);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, GearCutterBuild.FeedScrewCode).Verdict);
        // once both are in, the materials are fitted
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(UpToHead(), GearCutterBuild.MetalPartsCode).Verdict);
    }

    [Fact]
    public void A_material_stage_takes_a_material_a_click_and_is_drawn_once_complete()
    {
        var parts = new MachineAssembly(Build());
        // fewer rods than the stage still needs, and not a full stack: refused
        Assert.Equal(AssemblyVerdict.TooFew, Click(parts, GearCutterBuild.RodCode, 1, 16).Verdict);
        // all it needs of one material from a bigger stack, no more
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 2, 0), Click(parts, GearCutterBuild.RodCode, 10));
        Assert.Equal(2, Count(parts, "spindle", 0));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, GearCutterBuild.RodCode, 10).Verdict);
        Assert.False(parts.Build.StageComplete(0));
        Assert.False(parts.Shows("spindle"));
        Assert.Equal(0, parts.Build.Next);
        Assert.Equal([(GearCutterBuild.PlateCode, 2), (GearCutterBuild.MetalPartsCode, 1)],
            parts.Build.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
        // a full stack short of the need goes in whole (a plate that stacks to one, say)
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 1, 0), Click(parts, GearCutterBuild.PlateCode, 1, 1));
        Assert.Equal(1, Count(parts, "spindle", 1));
        Assert.Equal([(GearCutterBuild.PlateCode, 1), (GearCutterBuild.MetalPartsCode, 1)],
            parts.Build.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
        Fits(parts, GearCutterBuild.PlateCode);
        Fits(parts, "metal-parts");   // a code with no domain is the game's
        Assert.True(parts.Build.StageComplete(0));
        Assert.True(parts.Shows("spindle"));
        Assert.Equal(Stage("feedscrew"), parts.Build.Next);
    }

    [Fact]
    public void The_two_cam_drums_take_one_eccentric_gearbox_each()
    {
        var parts = UpToHead(2);
        Fits(parts, GearCutterBuild.GearboxCode);
        Assert.True(parts.Build.StageComplete(Stage("camfeed")));
        Assert.False(parts.Build.StageComplete(Stage("camindex")));
        Fits(parts, "jonasframes-gearbox02");   // a code with no domain is the game's
        Assert.True(parts.Build.StageComplete(Stage("camindex")));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, GearCutterBuild.GearboxCode).Verdict);
    }

    [Theory]
    [InlineData(GearCutterBuild.HeadCode)]
    [InlineData(GearCutterBuild.HeadAltCode)]
    public void Either_Jonas_gear_assembly_is_the_dividing_head(string head)
    {
        var parts = UpToHead(7);
        Fits(parts, head);
        Assert.Equal(head, parts.Build.CodeIn(Stage("head"), 0));
    }

    [Fact]
    public void The_master_decides_the_class_and_what_is_drawn()
    {
        var small = Complete();
        Assert.True(small.Ready);
        Assert.Equal(1, GearCutterBuild.Master(small));
        Assert.True(small.Shows("master"));
        Assert.False(small.Shows("masterlarge"));
        var large = Complete(GearCutterBuild.LargeMasterCode);
        Assert.Equal(2, GearCutterBuild.Master(large));
        Assert.True(large.Shows("masterlarge"));
        Assert.False(large.Shows("master"));
        Assert.True(large.Shows(null));
        Assert.True(large.Shows("cutter"));
        Assert.False(new MachineAssembly(Build()).Shows("spindle"));
        Assert.Equal(0, GearCutterBuild.Master(UpToHead()));
        Assert.Equal((1, 2, 0), (GearCutterBuild.MasterClass("gear-temporal"), GearCutterBuild.MasterClass(GearCutterBuild.LargeMasterCode),
            GearCutterBuild.MasterClass("game:gear-rusty")));
        // the blanks are the work's and the cover always drawn: no stage adds them (BEGearCutter.ShowsPart)
        Assert.DoesNotContain(Build().Stages, s => s.Adds.Intersect(["blanksmall", "blanklarge", "cover"]).Any());
    }

    [Fact]
    public void A_spent_kit_is_refused()
    {
        var parts = UpToHead();
        Assert.Equal(AssemblyVerdict.Spent, Click(parts, GearCutterBuild.KitCode, uses: 0).Verdict);
        Assert.False(parts.Build.StageComplete(Stage("cutter")));
    }

    [Fact]
    public void Take_back_is_the_kit_then_the_blank_then_the_master()
    {
        var parts = Complete(kit: 321);
        Assert.Equal(GearCutterTakeBack.Kit, GearCutterBuild.TakeBack(parts, blankOn: true));
        Assert.Null(GearCutterBuild.TakeMaster(parts));   // not while the kit is in
        Assert.Equal((GearCutterBuild.KitCode, 321.0), parts.Take(GearCutterBuild.KitPart));
        Assert.Equal(GearCutterTakeBack.Blank, GearCutterBuild.TakeBack(parts, blankOn: true));
        Assert.Equal(GearCutterTakeBack.Master, GearCutterBuild.TakeBack(parts, blankOn: false));
        Assert.Equal(GearCutterBuild.MasterCode, GearCutterBuild.TakeMaster(parts));
        Assert.Equal(GearCutterTakeBack.Nothing, GearCutterBuild.TakeBack(parts, blankOn: false));
        Assert.Equal(Stage("master"), parts.Build.Next);
        Assert.False(parts.Shows("master"));
        // the stages before stay
        Assert.True(parts.Build.StageComplete(Stage("head")));
        Assert.True(parts.Build.StageComplete(Stage("index")));
        // and the other master then a new kit go back in
        Fits(parts, GearCutterBuild.LargeMasterCode);
        Assert.Equal(2, GearCutterBuild.Master(parts));
        Assert.False(parts.Ready);
        Assert.Equal(AssemblyVerdict.Renewed, Click(parts, GearCutterBuild.KitCode, uses: 500).Verdict);
        Assert.True(parts.Ready);
    }

    [Fact]
    public void A_spent_kit_leaves_the_master_and_needs_a_new_kit()
    {
        var parts = Complete(kit: 15);
        Assert.False(parts.Wearing.Wear(GearCutterBuild.KitPart, 10));
        Assert.Equal(5, parts.Wearing.Fitted(GearCutterBuild.KitPart)!.Value.Left);
        Assert.True(parts.Wearing.Wear(GearCutterBuild.KitPart, 10));
        Assert.False(parts.Ready);
        Assert.True(parts.Built);
        Assert.False(parts.Shows("cutter"));
        Assert.Equal(1, GearCutterBuild.Master(parts));
        Assert.Equal(GearCutterTakeBack.Master, GearCutterBuild.TakeBack(parts, false));
        Assert.Equal(AssemblyVerdict.Renewed, Click(parts, GearCutterBuild.KitCode, uses: 500).Verdict);
        Assert.True(parts.Ready);
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_the_kit_with_its_durability()
    {
        var drops = Complete(kit: 480).Returns();
        // the bill's three materials (both stages', together), the five other one-item stages' items (the two
        // gearboxes together) and the master, and the kit
        Assert.Equal(10, drops.Count);
        Assert.Equal(2, drops.Single(d => d.Code == GearCutterBuild.GearboxCode).Count);
        Assert.Equal(new StageDrop(GearCutterBuild.KitCode, 1, 480), drops[^1]);
        Assert.All(drops.SkipLast(1), d => Assert.Null(d.Durability));
        Assert.Contains(new StageDrop(GearCutterBuild.MasterCode, 1), drops);
        foreach (var (code, n) in Bill)
            Assert.Equal(2 * n, drops.Single(d => d.Code == code).Count);
        Assert.Empty(new MachineAssembly(Build()).Returns());
    }

    [Fact]
    public void Breaking_a_stage_part_way_in_returns_exactly_what_went_in()
    {
        var parts = new MachineAssembly(Build());
        Fits(parts, GearCutterBuild.RodCode, held: 2);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, GearCutterBuild.PlateCode, 1, 1).Verdict);
        Assert.Equal([new StageDrop(GearCutterBuild.RodCode, 2), new StageDrop(GearCutterBuild.PlateCode, 1)], parts.Returns());
    }

    [Fact]
    public void The_creative_shortcut_fits_each_stage_in_turn()
    {
        var parts = new MachineAssembly(Build());
        int stages = 0;
        while (!parts.Ready)
        {
            Assert.True(parts.FillNext());
            stages++;
        }
        Assert.Equal(10, stages);
        // a new kit, and the small master last
        Assert.Equal((GearCutterBuild.KitCode, 500.0), parts.Wearing.Fitted(GearCutterBuild.KitPart));
        Assert.Equal(1, GearCutterBuild.Master(parts));
        Assert.Equal(Complete().Returns(), parts.Returns());
        Assert.False(parts.FillNext());
    }

    [Fact]
    public void A_save_keeps_every_fitted_code()
    {
        var parts = Complete(GearCutterBuild.LargeMasterCode, kit: 77);
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.True(restored.Ready);
        Assert.Equal(2, GearCutterBuild.Master(restored));
        Assert.Equal((GearCutterBuild.KitCode, 77.0), restored.Wearing.Fitted(GearCutterBuild.KitPart));
    }

    [Fact]
    public void A_save_keeps_a_material_stage_part_way_in()
    {
        var parts = UpToHead(5);
        Fits(parts, GearCutterBuild.RodCode, held: 2);
        Fits(parts, GearCutterBuild.MetalPartsCode);
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out _);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal(Stage("index"), restored.Build.Next);
        Assert.Equal([(GearCutterBuild.PlateCode, 2)], restored.Build.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
    }

    [Fact]
    public void A_save_drops_what_could_not_have_been_fitted()
    {
        // no feed screw: what follows the gap is dropped, and given back
        var saved = new Dictionary<string, int>
        {
            [StagedBuild.Key(0, 0, GearCutterBuild.RodCode)] = 2,
            [StagedBuild.Key(0, 1, GearCutterBuild.PlateCode)] = 2,
            [StagedBuild.Key(0, 2, GearCutterBuild.MetalPartsCode)] = 1,
            [StagedBuild.Key(Stage("camfeed"), 0, GearCutterBuild.GearboxCode)] = 1,
            [StagedBuild.Key(Stage("master"), 0, GearCutterBuild.MasterCode)] = 1,
        };
        var restored = MachineAssembly.Restore(Build(), saved, new Dictionary<string, (string, double)>(), out var dropped);
        Assert.Equal(Bill.Select(b => new StageDrop(b.Code, b.Count)), restored.Returns());
        Assert.Equal(new[] { GearCutterBuild.GearboxCode, GearCutterBuild.MasterCode }.Order(), dropped.Select(d => d.Code).Order());
        // a kit with nothing left is gone; the master stays
        var (build, _) = Complete().Snapshot();
        var spent = MachineAssembly.Restore(Build(), build,
            new Dictionary<string, (string, double)> { [GearCutterBuild.KitPart] = (GearCutterBuild.KitCode, 0) }, out _);
        Assert.Null(spent.Wearing.Fitted(GearCutterBuild.KitPart));
        Assert.Equal(1, GearCutterBuild.Master(spent));
    }

    // ---- The cut ----

    [Fact]
    public void A_gear_takes_turns_per_tooth_times_its_teeth()
    {
        Assert.Equal(12, GearCut.Teeth(1));
        Assert.Equal(20, GearCut.Teeth(2));
        Assert.Equal(0, GearCut.Teeth(0));
        Assert.Equal(144, GearCut.TurnsPerGear(1, 12));
        Assert.Equal(240, GearCut.TurnsPerGear(2, 12));
        Assert.Equal(1, GearCut.TeethFor(2 * Math.PI * 12, 12), 9);
        Assert.Equal(0, GearCut.TeethFor(-1, 12));
    }

    [Theory]
    [InlineData(1, 144)]
    [InlineData(2, 240)]
    public void The_cut_advances_by_the_shafts_angle_and_finishes_once(int k, int turns)
    {
        var job = GearCut.Start(k);
        int finished = 0, steps = 0;
        // a turn at a time, then one more to show it holds at the end
        for (int i = 0; i < turns + 1; i++)
        {
            (job, bool done) = GearCut.Advance(job, 2 * Math.PI, 12);
            steps++;
            if (done)
            {
                finished++;
                Assert.Equal(turns, steps);
            }
        }
        Assert.Equal(1, finished);
        Assert.Equal(GearCut.Teeth(k), job.Work, 9);
        Assert.True(job.Done);
    }

    [Fact]
    public void The_work_is_teeth_cut()
    {
        var (job, done) = GearCut.Advance(GearCut.Start(1), 2 * Math.PI * 12 * 3.4, 12);
        Assert.False(done);
        Assert.Equal(3.4, job.Work, 9);
        Assert.Equal(12, job.End);
        Assert.False(MachineJob.None.On);
        Assert.Equal(MachineJob.None, GearCut.Advance(MachineJob.None, 100, 12).Job);
        Assert.Equal(new MachineJob(2, 20, 20), GearCut.Restore(2, 99));
        Assert.Equal(MachineJob.None, GearCut.Restore(3, 1));
    }

    [Fact]
    public void The_master_decides_which_blank_goes_on()
    {
        Assert.Equal(GearCutterBlankVerdict.Loads, GearCut.CanLoad(GearCut.Blank, true, false, 1));
        Assert.Equal(GearCutterBlankVerdict.Loads, GearCut.CanLoad(GearCut.LargeBlank, true, false, 2));
        Assert.Equal(GearCutterBlankVerdict.WrongSize, GearCut.CanLoad(GearCut.LargeBlank, true, false, 1));
        Assert.Equal(GearCutterBlankVerdict.WrongSize, GearCut.CanLoad(GearCut.Blank, true, false, 2));
        Assert.Equal(GearCutterBlankVerdict.Incomplete, GearCut.CanLoad(GearCut.Blank, false, false, 1));
        Assert.Equal(GearCutterBlankVerdict.Occupied, GearCut.CanLoad(GearCut.Blank, true, true, 1));
        Assert.Equal(GearCutterBlankVerdict.NotABlank, GearCut.CanLoad("game:ingot-steel", true, false, 1));
        Assert.Equal(GearCut.Gear, GearCut.GearFor(1));
        Assert.Equal(GearCut.LargeGear, GearCut.GearFor(2));
    }

    [Fact]
    public void It_runs_only_complete_with_a_blank_at_speed()
    {
        Assert.True(GearCut.Running(true, true, 0.1f, 0.05f));
        Assert.True(GearCut.Running(true, true, -0.1f, 0.05f));
        Assert.False(GearCut.Running(true, true, 0.01f, 0.05f));
        Assert.False(GearCut.Running(false, true, 1, 0.05f));
        Assert.False(GearCut.Running(true, false, 1, 0.05f));
    }

    // ---- Wear ----

    [Theory]
    [InlineData(1.0, 10)]
    [InlineData(0.5, 20)]
    [InlineData(0.1, 100)]
    [InlineData(0.3, 34)]   // 33.3, rounded up
    public void A_small_gear_wears_the_base_over_the_fill(double fill, int wear)
    {
        Assert.Equal(wear, GearCutterWear.WearFor(10, 12, fill, 500));
    }

    [Fact]
    public void A_large_gear_wears_in_proportion_to_its_teeth()
    {
        Assert.Equal(17, GearCutterWear.WearFor(10, 20, 1, 500));    // 16.7
        Assert.Equal(34, GearCutterWear.WearFor(10, 20, 0.5, 500));   // 33.3
    }

    [Fact]
    public void An_empty_tank_takes_the_whole_kit()
    {
        Assert.Equal(500, GearCutterWear.WearFor(10, 12, 0, 500));
        Assert.Equal(3, GearCutterWear.WearFor(10, 12, 0, 3));
        Assert.Equal(3, GearCutterWear.WearFor(10, 12, 0.01, 3));   // never more than is left
        Assert.Equal(double.PositiveInfinity, GearCutterWear.Multiplier(0));
        Assert.Equal(2, GearCutterWear.Multiplier(0.5));
        Assert.Equal(0, GearCutterWear.WearFor(10, 12, 1, 0));
    }

    [Fact]
    public void A_full_tank_kit_cuts_about_fifty_small_gears()
    {
        var config = new GearCutterConfig();
        Assert.Equal(50, GearCutterWear.GearsLeft(config.CutterWearPerGear, 12, 1, 500));
        Assert.Equal(30, GearCutterWear.GearsLeft(config.CutterWearPerGear, 20, 1, 500));
        Assert.Equal(1, GearCutterWear.GearsLeft(config.CutterWearPerGear, 12, 0, 500));
        // cutting from full, the tank draining 10 a gear and never topped up
        var parts = Complete();
        var tank = new OilTank(1000, 1000);
        int gears = 0;
        while (parts.Ready)
        {
            int left = (int)parts.Wearing.Fitted(GearCutterBuild.KitPart)!.Value.Left;
            parts.Wearing.Wear(GearCutterBuild.KitPart, GearCutterWear.WearFor(config.CutterWearPerGear, 12, OilDrain.Fill(tank), left));
            tank = tank.Drain(10);
            gears++;
        }
        Assert.InRange(gears, 35, 45);
    }

    // ---- Settings and the rig ----

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new GearCutterConfig();
        Assert.Equal((12f, 10, 0.2f, 0.05f), (c.TurnsPerTooth, c.CutterWearPerGear, c.Resistance, c.MinSpeed));
        Assert.Empty(c.Sanitise());
        var bad = new GearCutterConfig { TurnsPerTooth = 0, CutterWearPerGear = -1, Resistance = float.NaN, MinSpeed = -2 };
        Assert.Equal(4, bad.Sanitise().Count);
        Assert.Equal((12f, 10, 0.2f, 0.05f), (bad.TurnsPerTooth, bad.CutterWearPerGear, bad.Resistance, bad.MinSpeed));
    }

    private static GearCutterRig ShippedRig() =>
        GearCutterRig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gearcutter-rig.json")));

    [Fact]
    public void The_shipped_rig_parses_with_its_anchors()
    {
        var rig = ShippedRig();
        Assert.Equal(8, rig.Cells.Count);
        Assert.Equal(new Int3(-1, 0, 0), rig.PowerCell);
        Assert.Equal(Side.West, rig.PowerFace);
        Assert.Equal(Side.South, rig.OutputSide);
        Assert.Equal(7, rig.OtherCells().Count());
        Assert.Equal(new Int3(0, 0, 2), rig.OutputNeighbour());
        Assert.Equal(2.15f, rig.OutputDrop().Z, 4);
        Assert.Equal(rig.Output.X, rig.OutputDrop().X);
        Assert.All(rig.MovingParts.Parts, p => Assert.True(p.Requires == null || GearCutterRequires.KnownRequires.Contains(p.Requires)));
    }

    // The gameplay's pace is the model's: the clutch's dogs and the worm agree only at the drawn 12
    // turns a tooth (GearCutter/README.md, known weak spot 5).
    [Fact]
    public void The_default_pace_is_the_rigs()
    {
        Assert.Equal(ShippedRig().TurnsPerTooth, new GearCutterConfig().TurnsPerTooth);
    }

    [Fact]
    public void Every_requires_of_the_rig_is_known_and_every_stage_draws_something()
    {
        var used = ShippedRig().MovingParts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Subset(GearCutterRequires.KnownRequires.ToHashSet(), used);
        foreach (var stage in GearCutterRequires.Stages)
            Assert.Contains(stage, used);
        Assert.Contains("masterlarge", used);
    }

    // ---- The renderer's clock ----

    [Fact]
    public void The_clock_follows_the_shaft_and_never_strays_from_the_server()
    {
        var clock = new GearCutterClock();
        // no blank on: W stands at the master's teeth, k follows the master while p eases in
        clock.Advance(0.2f, 0, 1, blankOn: false, 0, 0, running: false, 12);
        Assert.Equal((12.0, 1, 0.5f), (clock.Work, clock.Class, clock.Presence));
        // a blank on: W from the server's, then with the shaft, one tooth per 12 turns
        clock.Advance(0.2f, 0, 1, blankOn: true, 3, 12, running: true, 12);
        Assert.Equal(3, clock.Work, 9);
        clock.Advance(0.1f, 2 * Math.PI * 12 * 0.25, 1, blankOn: true, 3, 12, running: true, 12);
        Assert.Equal(3.25, clock.Work, 9);
        // too far ahead of the server: back to it
        clock.Advance(0.1f, 2 * Math.PI * 12, 1, blankOn: true, 3, 12, running: true, 12);
        Assert.Equal(3, clock.Work, 9);
        // the master off: k held while p eases out
        clock.Advance(0.2f, 0, 0, blankOn: false, 0, 0, running: false, 12);
        Assert.Equal(1, clock.Class);
        clock.Advance(1f, 0, 0, blankOn: false, 0, 0, running: false, 12);
        Assert.Equal((0, 0f), (clock.Class, clock.Presence));
    }
}
