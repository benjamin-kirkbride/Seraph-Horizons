using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.GearCutter;

/// <summary>GearCutter/Core: the build order, what each stage takes (the spindle and the index fed
/// their materials), take-back and drops (#480); the cut's arithmetic; the cutter kit's wear by oil
/// (#481); the settings and the shipped rig.</summary>
public class GearCutterGameplayTests
{
    /// <summary>A material stage's bill: each material and how many.</summary>
    private static readonly (string Code, int Count)[] Bill =
        [(GearCutterParts.RodCode, 2), (GearCutterParts.PlateCode, 2), (GearCutterParts.MetalPartsCode, 1)];

    /// <summary>The one-item stages up to the head, in order; null where a material stage's bill goes.</summary>
    private static readonly string?[] Order =
    [
        null, GearCutterParts.FeedScrewCode, GearCutterParts.GearboxCode, GearCutterParts.GearboxCode,
        GearCutterParts.LiftCamCode, null, GearCutterParts.ValveCode, GearCutterParts.HeadCode,
    ];

    private static void Fits(GearCutterParts parts, string code, int held = 1) =>
        Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(code, held: held).Verdict);

    /// <summary>Feeds the next stage its whole bill, a material a click.</summary>
    private static void FeedBill(GearCutterParts parts)
    {
        foreach (var (code, n) in Bill)
            Assert.Equal(new GearCutterFit(GearCutterFitVerdict.Fits, parts.Next!.Value, n), parts.Fit(code, held: 16));
    }

    private static void FitStep(GearCutterParts parts, string? code)
    {
        if (code == null)
            FeedBill(parts);
        else
            Fits(parts, code);
    }

    /// <summary>Every stage up to the head fitted.</summary>
    private static GearCutterParts UpToHead(int steps = 8)
    {
        var parts = new GearCutterParts();
        foreach (var code in Order.Take(steps))
            FitStep(parts, code);
        return parts;
    }

    private static GearCutterParts Complete(string master = GearCutterParts.MasterCode, int kit = 500)
    {
        var parts = UpToHead();
        Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(GearCutterParts.KitCode, kit, 500).Verdict);
        Fits(parts, master);
        return parts;
    }

    // ---- Parts ----

    [Fact]
    public void The_stages_go_in_in_the_decided_order()
    {
        Assert.Equal(["spindle", "feedscrew", "camfeed", "camindex", "liftcam", "index", "oiler", "head", "cutter", "master"],
            GearCutterRequires.Stages.Select(GearCutterRequires.Name));
        var parts = new GearCutterParts();
        var fitted = new List<GearCutterStage>();
        foreach (var code in Order)
        {
            var next = parts.Next!.Value;
            FitStep(parts, code);
            Assert.True(parts.Has(next));
            fitted.Add(next);
        }
        Assert.Equal(GearCutterRequires.Stages.Take(8), fitted);
        Assert.Equal(GearCutterStage.Cutter, parts.Next);
        Assert.False(parts.Complete);
    }

    [Fact]
    public void Only_the_spindle_and_the_index_take_materials_their_old_recipes_bill()
    {
        Assert.Equal([GearCutterStage.Spindle, GearCutterStage.Index], GearCutterRequires.Stages.Where(GearCutterParts.TakesMaterials));
        foreach (var stage in new[] { GearCutterStage.Spindle, GearCutterStage.Index })
            Assert.Equal(Bill, GearCutterParts.MaterialsFor(stage).Select(m => (Assert.Single(m.Codes), m.Quantity)));
        // their former items are no part of the cutter
        Assert.False(GearCutterParts.IsPart(GearCutterParts.LegacySpindleCode));
        Assert.False(GearCutterParts.IsPart(GearCutterParts.LegacyIndexCode));
        Assert.Equal([GearCutterStage.Spindle, GearCutterStage.Index], GearCutterParts.StagesOf("metal-parts"));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new GearCutterParts();
        Assert.Equal(GearCutterFitVerdict.OutOfOrder, parts.Fit(GearCutterParts.FeedScrewCode).Verdict);
        Assert.Equal(GearCutterFitVerdict.OutOfOrder, parts.Fit(GearCutterParts.MasterCode).Verdict);
        Assert.Equal(GearCutterFitVerdict.OutOfOrder, parts.Fit(GearCutterParts.KitCode, 500, 500).Verdict);
        Assert.Equal(GearCutterFitVerdict.NotAPart, parts.Fit("game:gear-rusty").Verdict);
        Assert.Equal(GearCutterFitVerdict.NotAPart, parts.Fit(null).Verdict);
        Assert.Equal(GearCutterFitVerdict.NotAPart, parts.Fit(GearCutterParts.LegacySpindleCode).Verdict);
        FeedBill(parts);
        Assert.Equal(GearCutterStage.FeedScrew, parts.Next);
        // the index takes the same materials, but not before its turn
        Assert.Equal(GearCutterFitVerdict.OutOfOrder, parts.Fit(GearCutterParts.RodCode, held: 2).Verdict);
        Fits(parts, GearCutterParts.FeedScrewCode);
        Assert.Equal(GearCutterFitVerdict.AlreadyFitted, parts.Fit(GearCutterParts.FeedScrewCode).Verdict);
        // once both are in, the materials are fitted
        var built = UpToHead();
        Assert.Equal(GearCutterFitVerdict.AlreadyFitted, built.Fit(GearCutterParts.MetalPartsCode).Verdict);
    }

    [Fact]
    public void A_material_stage_takes_a_material_a_click_and_is_drawn_once_complete()
    {
        var parts = new GearCutterParts();
        // fewer rods than the stage still needs, and not a full stack: refused
        Assert.Equal(GearCutterFitVerdict.TooFew, parts.Fit(GearCutterParts.RodCode, held: 1, maxStack: 16).Verdict);
        // all it needs of one material from a bigger stack, no more
        Assert.Equal(new GearCutterFit(GearCutterFitVerdict.Fits, GearCutterStage.Spindle, 2), parts.Fit(GearCutterParts.RodCode, held: 10));
        Assert.Equal(2, parts.Count(GearCutterStage.Spindle, 0));
        Assert.Equal(GearCutterFitVerdict.AlreadyFitted, parts.Fit(GearCutterParts.RodCode, held: 10).Verdict);
        Assert.False(parts.Has(GearCutterStage.Spindle));
        Assert.False(parts.Fitted("spindle"));
        Assert.Equal(GearCutterStage.Spindle, parts.Next);
        Assert.Equal([(GearCutterParts.PlateCode, 2), (GearCutterParts.MetalPartsCode, 1)],
            parts.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
        // a full stack short of the need goes in whole (a plate that stacks to one, say)
        Assert.Equal(new GearCutterFit(GearCutterFitVerdict.Fits, GearCutterStage.Spindle, 1),
            parts.Fit(GearCutterParts.PlateCode, held: 1, maxStack: 1));
        Assert.Equal(1, parts.Count(GearCutterStage.Spindle, 1));
        Assert.Equal([(GearCutterParts.PlateCode, 1), (GearCutterParts.MetalPartsCode, 1)],
            parts.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
        Fits(parts, GearCutterParts.PlateCode);
        Fits(parts, "metal-parts");   // a code with no domain is the game's
        Assert.True(parts.Has(GearCutterStage.Spindle));
        Assert.True(parts.Fitted("spindle"));
        Assert.Empty(parts.NextNeeds());
        Assert.Equal(GearCutterStage.FeedScrew, parts.Next);
    }

    [Fact]
    public void The_two_cam_drums_take_one_eccentric_gearbox_each()
    {
        var parts = UpToHead(2);
        Fits(parts, GearCutterParts.GearboxCode);
        Assert.True(parts.Has(GearCutterStage.CamFeed));
        Assert.False(parts.Has(GearCutterStage.CamIndex));
        Fits(parts, "jonasframes-gearbox02");   // a code with no domain is the game's
        Assert.True(parts.Has(GearCutterStage.CamIndex));
        Assert.Equal(GearCutterFitVerdict.AlreadyFitted, parts.Fit(GearCutterParts.GearboxCode).Verdict);
    }

    [Theory]
    [InlineData(GearCutterParts.HeadCode)]
    [InlineData(GearCutterParts.HeadAltCode)]
    public void Either_Jonas_gear_assembly_is_the_dividing_head(string head)
    {
        var parts = UpToHead(7);
        Fits(parts, head);
        Assert.Equal(head, parts.FittedIn(GearCutterStage.Head));
    }

    [Fact]
    public void The_master_decides_the_class_and_what_is_drawn()
    {
        var small = Complete();
        Assert.True(small.Complete);
        Assert.Equal(1, small.Master);
        Assert.True(small.Fitted("master"));
        Assert.False(small.Fitted("masterlarge"));
        var large = Complete(GearCutterParts.LargeMasterCode);
        Assert.Equal(2, large.Master);
        Assert.True(large.Fitted("masterlarge"));
        Assert.False(large.Fitted("master"));
        Assert.True(large.Fitted(null));
        Assert.True(large.Fitted("cover"));
        Assert.False(large.Fitted("blanksmall"));
        Assert.False(new GearCutterParts().Fitted("spindle"));
    }

    [Fact]
    public void A_spent_kit_is_refused()
    {
        var parts = UpToHead();
        Assert.Equal(GearCutterFitVerdict.KitSpent, parts.Fit(GearCutterParts.KitCode, 0, 500).Verdict);
        Assert.False(parts.Has(GearCutterStage.Cutter));
    }

    [Fact]
    public void Take_back_is_the_kit_then_the_blank_then_the_master()
    {
        var parts = Complete(kit: 321);
        Assert.Equal(GearCutterTakeBack.Kit, parts.TakeBack(blankOn: true));
        Assert.Null(parts.RemoveMaster());   // not while the kit is in
        Assert.Equal((321, 500), parts.RemoveKit());
        Assert.Equal(GearCutterTakeBack.Blank, parts.TakeBack(blankOn: true));
        Assert.Equal(GearCutterTakeBack.Master, parts.TakeBack(blankOn: false));
        Assert.Equal(GearCutterParts.MasterCode, parts.RemoveMaster());
        Assert.Equal(GearCutterTakeBack.Nothing, parts.TakeBack(blankOn: false));
        Assert.Equal(GearCutterStage.Cutter, parts.Next);
        // the stages before stay
        Assert.True(parts.Has(GearCutterStage.Head));
        Assert.True(parts.Has(GearCutterStage.Index));
        // and a new kit then a master go back in
        Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(GearCutterParts.KitCode, 500, 500).Verdict);
        Fits(parts, GearCutterParts.LargeMasterCode);
        Assert.Equal(2, parts.Master);
    }

    [Fact]
    public void A_spent_kit_leaves_the_master_and_needs_a_new_kit()
    {
        var parts = Complete(kit: 15);
        Assert.False(parts.WearKit(10));
        Assert.Equal(5, parts.KitLeft);
        Assert.True(parts.WearKit(10));
        Assert.False(parts.Complete);
        Assert.Equal(GearCutterStage.Cutter, parts.Next);
        Assert.Equal(1, parts.Master);
        Assert.Equal(GearCutterTakeBack.Master, parts.TakeBack(false));
        Assert.Equal(GearCutterParts.KitCode, parts.NextPart);
        Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(GearCutterParts.KitCode, 500, 500).Verdict);
        Assert.True(parts.Complete);
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_the_kit_with_its_durability()
    {
        var parts = Complete(kit: 480);
        var drops = parts.Returns();
        // the spindle's and the index's three materials each, eight one-item stages
        Assert.Equal(14, drops.Count);
        Assert.Equal(2, drops.Count(d => d.Code == GearCutterParts.GearboxCode));
        Assert.Equal(new GearCutterDrop(GearCutterParts.KitCode, 480), drops.Single(d => d.Code == GearCutterParts.KitCode));
        Assert.All(drops.Where(d => d.Code != GearCutterParts.KitCode), d => Assert.Null(d.Durability));
        Assert.Contains(new GearCutterDrop(GearCutterParts.MasterCode), drops);
        foreach (var (code, n) in Bill)
            Assert.Equal(2 * n, drops.Where(d => d.Code == code).Sum(d => d.Count));
        Assert.Empty(new GearCutterParts().Returns());
    }

    [Fact]
    public void Breaking_a_stage_part_way_in_returns_exactly_what_went_in()
    {
        var parts = new GearCutterParts();
        Fits(parts, GearCutterParts.RodCode, held: 2);
        Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(GearCutterParts.PlateCode, held: 1, maxStack: 1).Verdict);
        Assert.Equal([new GearCutterDrop(GearCutterParts.RodCode, null, 2), new GearCutterDrop(GearCutterParts.PlateCode, null, 1)],
            parts.Returns());
    }

    [Fact]
    public void The_creative_shortcut_fits_each_stage_in_turn()
    {
        var parts = new GearCutterParts();
        var codes = new List<string?>();
        while (!parts.Complete)
        {
            if (parts.FillNextMaterials())
            {
                codes.Add(null);
                continue;
            }
            var code = Assert.IsType<string>(parts.NextPart);
            codes.Add(code);
            Assert.Equal(GearCutterFitVerdict.Fits, parts.Fit(code, 500, 500).Verdict);
        }
        Assert.Equal([.. Order, GearCutterParts.KitCode, GearCutterParts.MasterCode], codes);
        Assert.Equal(Complete().Returns().Where(d => d.Code != GearCutterParts.KitCode), parts.Returns().Where(d => d.Code != GearCutterParts.KitCode));
        Assert.False(parts.FillNextMaterials());
    }

    [Fact]
    public void A_save_keeps_every_fitted_code()
    {
        var parts = Complete(GearCutterParts.LargeMasterCode, kit: 77);
        var restored = GearCutterParts.Restore(parts.Snapshot(), parts.KitLeft, parts.KitCapacity, parts.MaterialSnapshot());
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.True(restored.Complete);
        Assert.Equal(2, restored.Master);
        Assert.Equal(77, restored.KitLeft);
        Assert.Equal(500, restored.KitCapacity);
    }

    [Fact]
    public void A_save_keeps_a_material_stage_part_way_in()
    {
        var parts = UpToHead(5);
        Fits(parts, GearCutterParts.RodCode, held: 2);
        Fits(parts, GearCutterParts.MetalPartsCode);
        var restored = GearCutterParts.Restore(parts.Snapshot(), 0, 0, parts.MaterialSnapshot());
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal(GearCutterStage.Index, restored.Next);
        Assert.Equal([(GearCutterParts.PlateCode, 2)], restored.NextNeeds().Select(n => (n.Material.Codes[0], n.Missing)));
    }

    [Fact]
    public void A_cutter_saved_with_the_old_spindle_and_index_items_restores_them_as_their_bill()
    {
        var full = Complete().Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);
        full["spindle"] = GearCutterParts.LegacySpindleCode;
        full["index"] = GearCutterParts.LegacyIndexCode;
        var restored = GearCutterParts.Restore(full, 500, 500);   // an old save has no material counts
        Assert.True(restored.Complete);
        Assert.Equal(Complete().Returns(), restored.Returns());
        Assert.DoesNotContain(restored.Returns(), d => d.Code is GearCutterParts.LegacySpindleCode or GearCutterParts.LegacyIndexCode);
        // and it saves in the new form
        var again = GearCutterParts.Restore(restored.Snapshot(), 500, 500, restored.MaterialSnapshot());
        Assert.Equal(restored.Returns(), again.Returns());
        Assert.DoesNotContain("spindle", restored.Snapshot().Keys);
    }

    [Fact]
    public void A_save_drops_what_could_not_have_been_fitted()
    {
        var saved = new Dictionary<string, string>
        {
            ["spindle"] = GearCutterParts.LegacySpindleCode,
            // no feed screw: what follows the gap is dropped
            ["camfeed"] = GearCutterParts.GearboxCode,
            ["master"] = GearCutterParts.MasterCode,
        };
        var restored = GearCutterParts.Restore(saved, 0, 0);
        Assert.Equal(Bill.Select(b => new GearCutterDrop(b.Code, null, b.Count)), restored.Returns());

        // a spindle part way in: nothing after it
        var partial = GearCutterParts.Restore(Complete().Snapshot(), 500, 500,
            new Dictionary<string, int> { ["spindle/" + StagedBuild.Key(0, 0, GearCutterParts.RodCode)] = 2 });
        Assert.Equal([new GearCutterDrop(GearCutterParts.RodCode, null, 2)], partial.Returns());

        var full = Complete().Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);
        full["head"] = "game:gear-rusty";
        Assert.Equal(GearCutterStage.Head, GearCutterParts.Restore(full, 500, 500, Complete().MaterialSnapshot()).Next);
        // a kit with nothing left is gone; the master stays
        var spent = GearCutterParts.Restore(Complete().Snapshot(), 0, 500, Complete().MaterialSnapshot());
        Assert.False(spent.Has(GearCutterStage.Cutter));
        Assert.Equal(1, spent.Master);
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
        var job = new CutJob(k, 0);
        int finished = 0, steps = 0;
        // a turn at a time, then one more to show it holds at the end
        for (int i = 0; i < turns + 1; i++)
        {
            (job, bool done) = job.Advance(2 * Math.PI, 12);
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
        var (job, done) = new CutJob(1, 0).Advance(2 * Math.PI * 12 * 3.4, 12);
        Assert.False(done);
        Assert.Equal(3.4, job.Work, 9);
        Assert.Equal(12, job.End);
        Assert.False(CutJob.None.On);
        Assert.Equal(CutJob.None, CutJob.None.Advance(100, 12).Job);
        Assert.Equal(new CutJob(2, 20), CutJob.Restore(2, 99));
        Assert.Equal(CutJob.None, CutJob.Restore(3, 1));
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
        while (parts.Complete)
        {
            parts.WearKit(GearCutterWear.WearFor(config.CutterWearPerGear, 12, OilDrain.Fill(tank), parts.KitLeft));
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
        Assert.Equal(7, rig.GhostCells.Count());
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
            Assert.Contains(GearCutterRequires.Name(stage), used);
        Assert.Contains("masterlarge", used);
    }
}
