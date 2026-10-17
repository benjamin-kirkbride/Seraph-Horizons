using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.StallRoaster.Core;

namespace SeraphHorizons.Tests.StallRoaster;

/// <summary>The stall roaster (#925): its rules (<see cref="StallRoasting"/>), its rig's gameplay half, its build file
/// against its rig, and its figures in the shipped <c>config/ore-processing.json</c> (92 %, 32 rock in 40 hours, a
/// third of the sulfur).</summary>
public class StallRoasterTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly string RigJson = File.ReadAllText(Out("rigs/stallroaster-rig.json"));

    private static readonly StallRoasterConfig C = new();

    // ---- The figures ----

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.StallRoaster);
        Assert.Equal(0.92, spec.Recovery, 9);
        Assert.Equal(32, spec.ChargeItems);
        Assert.Equal(40, spec.ChargeHours, 9);
        Assert.Equal(0.8, spec.ItemsPerHour, 9);
        Assert.Equal(0, spec.Load);
        Assert.Equal(0, spec.WaterPerItem);
        // the roasters' sulfur: none from the firepit, a third from the stall, 0.7 from the reverberatory (#765)
        Assert.Equal(0, R.RoastSulfur(Roaster.Firepit));
        Assert.Equal(1.0 / 3, R.RoastSulfur(Roaster.Stall), 9);
        Assert.Equal(0.7, R.RoastSulfur(Roaster.Reverberatory), 9);
        Assert.Equal(5, R.SulfurUnitsPerPowder);
    }

    [Fact]
    public void Every_sulfide_has_its_sulfur_and_no_other_ore_any()
    {
        foreach (var ore in R.Ores)
            if (ore.IsSulfide && ore.Ore != "sperrylite")
                Assert.True(ore.Sulfur > 0, $"{ore.Ore} has no sulfur");
            else
                Assert.Equal(0, ore.Sulfur);
        // the owner's figures from the formulas (#765)
        Assert.Equal(0.155, R.Ore("galena").Sulfur, 9);
        Assert.Equal(1.01, R.Ore("chalcopyrite").Sulfur, 9);
        Assert.Equal(1.15, R.Ore("pyrite").Sulfur, 9);
        // a sulfide's sulfur through each roaster; an oxide has none
        Assert.Equal(7 * 0.155 / 3, R.SulfurUnits(R.Ore("galena"), 7, Roaster.Stall), 9);
        Assert.Equal(0, R.SulfurUnits(R.Ore("galena"), 7, Roaster.Firepit));
        Assert.Equal(0, R.SulfurUnits(R.Ore("hematite"), 8, Roaster.Stall));
    }

    [Theory]
    [InlineData("poor", 0.10 * 0.92)]
    [InlineData("medium", 0.25 * 0.92)]
    [InlineData("rich", 0.40 * 0.92)]
    [InlineData("bountiful", 0.50 * 0.92)]
    public void Stall_roasted_rock_smelts_at_its_grade_times_92_and_the_firepits_at_85(string grade, double share)
    {
        Assert.Equal(share, R.SmeltShare(R.Ore("galena"), OreForm.RoastedRock, grade, Roaster.Stall), 9);
        Assert.Equal(share / 0.92 * 0.85, R.SmeltShare(R.Ore("galena"), OreForm.RoastedRock, grade), 9);
        Assert.Equal("seraphhorizons:stallroastedore-galena-rich", OreProducts.StallRoastedRockCode("galena", "rich"));
    }

    // ---- The rules ----

    [Fact]
    public void It_takes_a_sulfides_raw_or_crushed_rock_only()
    {
        Assert.True(StallRoasting.Takes(OreForm.Raw, R.Ore("galena")));
        Assert.True(StallRoasting.Takes(OreForm.Crushed, R.Ore("chalcopyrite")));
        Assert.False(StallRoasting.Takes(OreForm.Ground, R.Ore("galena")));
        Assert.False(StallRoasting.Takes(OreForm.Concentrate, R.Ore("galena")));
        Assert.False(StallRoasting.Takes(OreForm.RoastedRock, R.Ore("galena")));
        Assert.False(StallRoasting.Takes(OreForm.Raw, R.Ore("hematite")));
        Assert.False(StallRoasting.Takes(null, R.Ore("galena")));
    }

    [Fact]
    public void The_phase_follows_what_is_in_it()
    {
        Assert.Equal(StallPhase.Empty, StallRoasting.PhaseOf(0, 0, 4, false, false));
        Assert.Equal(StallPhase.Bed, StallRoasting.PhaseOf(2, 0, 4, false, false));
        Assert.Equal(StallPhase.Bed, StallRoasting.PhaseOf(4, 0, 4, false, false));
        Assert.Equal(StallPhase.Charged, StallRoasting.PhaseOf(4, 1, 4, false, false));
        Assert.Equal(StallPhase.Burning, StallRoasting.PhaseOf(0, 32, 4, true, false));
        Assert.Equal(StallPhase.Roasted, StallRoasting.PhaseOf(0, 8, 4, false, true));
    }

    [Fact]
    public void The_bed_goes_in_through_the_open_front_before_the_rock()
    {
        Assert.Equal((4, StallRefusal.None), StallRoasting.LayBed(StallPhase.Empty, StallFront.Open, 0, 10, 4));
        Assert.Equal((1, StallRefusal.None), StallRoasting.LayBed(StallPhase.Bed, StallFront.Open, 3, 10, 4));
        Assert.Equal((0, StallRefusal.BedLaid), StallRoasting.LayBed(StallPhase.Bed, StallFront.Open, 4, 10, 4));
        Assert.Equal((0, StallRefusal.FrontNotOpen), StallRoasting.LayBed(StallPhase.Empty, StallFront.Half, 0, 10, 4));
        Assert.Equal((0, StallRefusal.BedUnderCharge), StallRoasting.LayBed(StallPhase.Charged, StallFront.Open, 4, 10, 4));
        Assert.Equal((0, StallRefusal.Burning), StallRoasting.LayBed(StallPhase.Burning, StallFront.Open, 0, 10, 4));

        Assert.Equal((0, StallRefusal.NoBed), StallRoasting.Charge(StallPhase.Bed, StallFront.Open, 3, 0, 16, 4, 32));
        Assert.Equal((16, StallRefusal.None), StallRoasting.Charge(StallPhase.Bed, StallFront.Open, 4, 0, 16, 4, 32));
        Assert.Equal((4, StallRefusal.None), StallRoasting.Charge(StallPhase.Charged, StallFront.Open, 4, 28, 16, 4, 32));
        Assert.Equal((0, StallRefusal.Full), StallRoasting.Charge(StallPhase.Charged, StallFront.Open, 4, 32, 16, 4, 32));
        Assert.Equal((0, StallRefusal.FrontNotOpen), StallRoasting.Charge(StallPhase.Charged, StallFront.Walled, 4, 8, 16, 4, 32));
        Assert.Equal((0, StallRefusal.Roasted), StallRoasting.Charge(StallPhase.Roasted, StallFront.Open, 0, 8, 16, 4, 32));
    }

    [Fact]
    public void It_is_lit_charged_through_a_front_not_walled_up()
    {
        Assert.Equal(StallRefusal.None, StallRoasting.CannotLight(StallPhase.Charged, StallFront.Open));
        Assert.Equal(StallRefusal.None, StallRoasting.CannotLight(StallPhase.Charged, StallFront.Half));
        Assert.Equal(StallRefusal.FrontWalled, StallRoasting.CannotLight(StallPhase.Charged, StallFront.Walled));
        Assert.Equal(StallRefusal.NotCharged, StallRoasting.CannotLight(StallPhase.Bed, StallFront.Open));
        Assert.Equal(StallRefusal.NotCharged, StallRoasting.CannotLight(StallPhase.Empty, StallFront.Open));
        Assert.Equal(StallRefusal.Burning, StallRoasting.CannotLight(StallPhase.Burning, StallFront.Open));
        Assert.Equal(StallRefusal.Roasted, StallRoasting.CannotLight(StallPhase.Roasted, StallFront.Open));
    }

    [Fact]
    public void The_draught_is_the_fronts_on_a_drawing_row()
    {
        Assert.Equal(1, StallRoasting.Draught(StallFront.Walled, true, C));
        Assert.Equal(0.5, StallRoasting.Draught(StallFront.Half, true, C));
        Assert.Equal(0, StallRoasting.Draught(StallFront.Open, true, C));
        Assert.Equal(0, StallRoasting.Draught(StallFront.Walled, false, C));
        // 40 hours walled up; half walled twice as long; nothing open or without a chimney
        var (burned, done) = StallRoasting.Advance(0, 39.9, 1, 40);
        Assert.False(done);
        Assert.Equal(39.9, burned, 9);
        Assert.Equal((40, true), StallRoasting.Advance(burned, 0.2, 1, 40));
        Assert.Equal((20, false), StallRoasting.Advance(0, 40, 0.5, 40));
        Assert.Equal((10, false), StallRoasting.Advance(10, 40, 0, 40));
        Assert.True(StallRoasting.Kindling(0, 40));
        Assert.False(StallRoasting.Kindling(4, 40));
    }

    [Fact]
    public void The_front_is_walled_with_bricks_the_stall_keeps()
    {
        Assert.Equal((StallFront.Half, 9, StallRefusal.None), StallRoasting.WallUp(StallFront.Open, 0, 12, 9, 18));
        Assert.Equal((StallFront.Walled, 9, StallRefusal.None), StallRoasting.WallUp(StallFront.Half, 9, 12, 9, 18));
        Assert.Equal((StallFront.Open, 0, StallRefusal.TooFewBricks), StallRoasting.WallUp(StallFront.Open, 0, 8, 9, 18));
        Assert.Equal((StallFront.Walled, 0, StallRefusal.AlreadyWalled), StallRoasting.WallUp(StallFront.Walled, 18, 12, 9, 18));
        // taken down, the bricks stay: walled up again for none
        Assert.Equal((StallFront.Half, StallRefusal.None), StallRoasting.TakeDown(StallFront.Walled));
        Assert.Equal((StallFront.Open, StallRefusal.None), StallRoasting.TakeDown(StallFront.Half));
        Assert.Equal((StallFront.Open, StallRefusal.AlreadyOpen), StallRoasting.TakeDown(StallFront.Open));
        Assert.Equal((StallFront.Half, 0, StallRefusal.None), StallRoasting.WallUp(StallFront.Open, 18, 1, 9, 18));
        Assert.Equal((StallFront.Walled, 0, StallRefusal.None), StallRoasting.WallUp(StallFront.Half, 18, 0, 9, 18));
        Assert.Equal(StallRefusal.FrontNotOpen, StallRoasting.CannotRake(StallPhase.Roasted, StallFront.Half));
        Assert.Equal(StallRefusal.None, StallRoasting.CannotRake(StallPhase.Roasted, StallFront.Open));
        Assert.Equal(StallRefusal.NotCharged, StallRoasting.CannotRake(StallPhase.Burning, StallFront.Open));
    }

    [Fact]
    public void A_charges_sulfur_comes_out_whole_powders_the_rest_carried()
    {
        // 32 rich galena: 224 units of lead, 0.155 sulfur a unit, a third of it, 5 a powder
        double units = 32 * R.SulfurUnits(R.Ore("galena"), OreProducts.RawUnits("galena", "rich")!.Value, Roaster.Stall);
        var (powders, carried) = StallRoasting.Sulfur(0, units, 5);
        Assert.Equal(2, powders);
        Assert.Equal(units - 10, carried, 9);
        // the next burn takes the carry on
        var (next, _) = StallRoasting.Sulfur(carried, units, 5);
        Assert.Equal((int)Math.Floor(2 * units / 5) - 2, next);
        // 32 rich pyrite (8 units of iron each, 1.15 sulfur a unit)
        Assert.Equal(19, StallRoasting.Sulfur(0, 32 * 8 * 1.15 / 3, 5).Powders);
        Assert.Equal(4, StallRoasting.Courses(32, 8, 4));
        Assert.Equal(2, StallRoasting.Courses(9, 8, 4));
        Assert.Equal(0, StallRoasting.Courses(0, 8, 4));
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new StallRoasterConfig { BedFirewood = 0, HalfWalledDraught = 2, OpenDraught = double.NaN };
        Assert.Equal(3, c.Sanitise().Count);
        Assert.Equal(4, c.BedFirewood);
        Assert.Equal(0.5, c.HalfWalledDraught);
        Assert.Equal(0, c.OpenDraught);
        Assert.Empty(new StallRoasterConfig().Sanitise());
    }

    // ---- The rig and the build ----

    [Fact]
    public void The_rig_gives_gameplay_its_outlet_charge_front_cap_and_bricks()
    {
        var rig = StallRoasterRig.Parse(RigJson);
        Assert.Equal(new Int3(0, 1, 1), rig.OutletCell);
        Assert.Equal(new Int3(0, 2, 1), rig.ChimneyBase);
        // the model's charge is ore processing's
        Assert.Equal(R.Machine(OreMachine.StallRoaster).ChargeItems, rig.Charge);
        Assert.Equal(4, rig.Courses);
        Assert.Equal(8, rig.PerCourse);
        Assert.Equal((9, 18), (rig.FrontHalf, rig.FrontWalled));
        Assert.Equal(("game:metalplate-iron", 1), (rig.CapCode, rig.CapCount));
        Assert.Equal((6, 6, 2), (rig.MinChimney, rig.RowMax, rig.Width));
        Assert.Equal("red", rig.Bricks.Default);
        Assert.Equal(9, rig.Bricks.Types.Count);
        Assert.Equal("clinker", rig.Bricks.TypeOf("game:burnedbrick-clinker"));
    }

    [Fact]
    public void The_shared_rig_reads_every_part_with_the_stalls_own_requires()
    {
        var rig = OreMachineRig.Parse(RigJson, StallRequires.Extra(4));
        Assert.Equal(["brickwork", "lining"], rig.StageRequires);
        var known = rig.StageRequires.Concat(StallRequires.Extra(4)).ToHashSet();
        foreach (var part in rig.Parts.Parts)
            Assert.True(part.Requires == null || known.Contains(part.Requires), $"{part.Id} requires {part.Requires}");
        // a batch machine: no power, water or chute anchor; only the outlet
        Assert.Equal(["outlet"], rig.Anchors.Keys.ToArray());
    }

    [Fact]
    public void The_build_is_the_rigs_its_brickwork_any_one_fired_brick()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/stallroaster.json")));
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["brickwork", "lining"], build.Stages.Select(s => s.Id).ToArray());
        var rig = StallRoasterRig.Parse(RigJson);
        var bricks = build.Stages[0].Materials.Single();
        Assert.True(bricks.OneType);
        Assert.Equal(7, bricks.Quantity);
        // every fired brick type of the rig, red (its default) first for the creative shortcut
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), bricks.Codes.Order());
        Assert.Equal(rig.Bricks.ItemOf(rig.Bricks.Default), bricks.Codes[0]);
        Assert.Equal([("game:refractorybrick-fired-tier3", 9), ("game:clay-fire", 4)],
            build.Stages[1].Materials.Select(m => (m.Codes.Single(), m.Quantity)).ToArray());

        // one type: a red brick in, a cream one refused; the red then fills it
        var b = build.NewBuild();
        Assert.Equal(StageFitVerdict.TooFew, b.CanFit("game:burnedbrick-red", 3, 64).Verdict);
        Assert.Equal(StageFitVerdict.Fits, b.Fit("game:burnedbrick-red", 3, 3).Verdict);
        Assert.Equal(StageFitVerdict.OtherKind, b.CanFit("game:burnedbrick-cream", 64, 64).Verdict);
        Assert.Equal(StageFitVerdict.Fits, b.Fit("game:burnedbrick-red", 64, 64).Verdict);
        Assert.True(b.StageComplete(0));
        Assert.Equal("game:burnedbrick-red", b.CodeIn(0, 0));
    }
}
