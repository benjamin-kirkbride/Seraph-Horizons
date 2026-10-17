using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.LiquationFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.LiquationFurnace;

/// <summary>The liquation furnace (#929): its rules (<see cref="LiquationFurnacing"/>), its rig's gameplay half, its
/// build file against its rig, and its figures in the shipped <c>config/ore-processing.json</c> (95 %, 100 units an
/// hour) and its settings (0.36 fuel an hour).</summary>
public class LiquationFurnaceTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly string RigJson = File.ReadAllText(Out("rigs/liquationfurnace-rig.json"));

    private static readonly LiquationFurnaceConfig C = new();

    // ---- The figures ----

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.LiquationFurnace);
        Assert.Equal(0.95, spec.Recovery, 9);
        Assert.Equal(100, spec.ItemsPerHour, 9);
        Assert.Equal(0, spec.Load);
        Assert.Equal(0, spec.WaterPerItem);
        // the owner's ruling on #929: 0.36 coal an hour
        Assert.Equal(0.36, C.FuelPerHour, 9);
        Assert.Equal(4, C.ChargeBars);
    }

    [Fact]
    public void It_takes_teallites_and_franckeites_tin_lead_only()
    {
        Assert.Equal("teallite", LiquationFurnacing.OreOf(R, Bullion.Code(R.Ore("teallite"))));
        Assert.Equal("franckeite", LiquationFurnacing.OreOf(R, "seraphhorizons:bullion-franckeite-tin"));
        Assert.Null(LiquationFurnacing.OreOf(R, Bullion.Code(R.Ore("galena"))));
        Assert.Null(LiquationFurnacing.OreOf(R, "game:ingot-tin"));
        Assert.Null(LiquationFurnacing.OreOf(R, "seraphhorizons:bullion-nosuchore-tin"));
        Assert.Null(LiquationFurnacing.OreOf(R, null));
    }

    [Fact]
    public void A_bar_gives_its_tin_whole_and_its_lead_at_95_percent()
    {
        var y = LiquationFurnacing.Yield(R, [new LiquationBars("teallite", 1)]);
        Assert.Equal(("tin", "lead"), (y.Metal, y.ByProduct));
        Assert.Equal(100, y.MetalUnits, 9);
        Assert.Equal(100 * 0.40 * 0.95, y.ByProductUnits, 9);
        var mixed = LiquationFurnacing.Yield(R, [new LiquationBars("teallite", 2), new LiquationBars("franckeite", 2)]);
        Assert.Equal(400, mixed.MetalUnits, 9);
        Assert.Equal(200 * 0.40 * 0.95 + 200 * 0.30 * 0.95, mixed.ByProductUnits, 9);
        // better than the clay pan's 85 %
        Assert.True(R.Parting(PartingStation.LiquationFurnace) > R.Parting(PartingStation.LiquationPan));
        Assert.Equal(400, LiquationFurnacing.ChargeUnits([new LiquationBars("teallite", 2), new LiquationBars("franckeite", 2)]));
    }

    // ---- The rules ----

    [Fact]
    public void The_phase_follows_what_is_in_it()
    {
        Assert.Equal(LiquationPhase.Empty, LiquationFurnacing.PhaseOf(0, false));
        Assert.Equal(LiquationPhase.Charged, LiquationFurnacing.PhaseOf(3, false));
        Assert.Equal(LiquationPhase.Sweated, LiquationFurnacing.PhaseOf(0, true));
    }

    [Fact]
    public void The_plate_takes_bars_up_to_its_charge_and_not_over_a_sweated_one()
    {
        Assert.Equal((4, LiquationFurnaceRefusal.None), LiquationFurnacing.Charge(LiquationPhase.Empty, 0, 10, 4));
        Assert.Equal((1, LiquationFurnaceRefusal.None), LiquationFurnacing.Charge(LiquationPhase.Charged, 3, 10, 4));
        Assert.Equal((0, LiquationFurnaceRefusal.Full), LiquationFurnacing.Charge(LiquationPhase.Charged, 4, 10, 4));
        Assert.Equal((0, LiquationFurnaceRefusal.Sweated), LiquationFurnacing.Charge(LiquationPhase.Sweated, 0, 10, 4));
    }

    [Fact]
    public void Fuel_goes_through_the_open_door_one_kind_up_to_the_grate()
    {
        Assert.Equal((0, LiquationFurnaceRefusal.DoorShut), LiquationFurnacing.Stoke(true, null, 0, "game:coke", 4, 8));
        Assert.Equal((8, LiquationFurnaceRefusal.None), LiquationFurnacing.Stoke(false, null, 0, "game:coke", 20, 8));
        Assert.Equal((0, LiquationFurnaceRefusal.OtherFuel), LiquationFurnacing.Stoke(false, "game:coke", 2, "game:charcoal", 4, 8));
        Assert.Equal((0, LiquationFurnaceRefusal.GrateFull), LiquationFurnacing.Stoke(false, "game:coke", 8, "game:coke", 4, 8));
        Assert.True(LiquationFurnacing.IsFuel("game:ore-bituminouscoal"));
        Assert.False(LiquationFurnacing.IsFuel("game:firewood"));
        Assert.Equal(LiquationFurnaceRefusal.NoFuel, LiquationFurnacing.CannotLight(false, 0));
        Assert.Equal(LiquationFurnaceRefusal.Lit, LiquationFurnacing.CannotLight(true, 3));
        Assert.Equal(LiquationFurnaceRefusal.None, LiquationFurnacing.CannotLight(false, 3));
    }

    [Fact]
    public void The_pace_is_the_dampers_and_the_doors_on_a_drawing_row()
    {
        Assert.Equal(1, LiquationFurnacing.Pace(true, true, Damper.Open, true, C));
        Assert.Equal(0.5, LiquationFurnacing.Pace(true, true, Damper.Half, true, C));
        Assert.Equal(0, LiquationFurnacing.Pace(true, true, Damper.Shut, true, C));
        Assert.Equal(0.5, LiquationFurnacing.Pace(true, true, Damper.Open, false, C));
        Assert.Equal(0, LiquationFurnacing.Pace(true, false, Damper.Open, true, C));
        Assert.Equal(0, LiquationFurnacing.Pace(false, true, Damper.Open, true, C));
        Assert.Equal(Damper.Half, LiquationFurnacing.NextDamper(Damper.Open));
        Assert.Equal(Damper.Open, LiquationFurnacing.NextDamper(Damper.Shut));
    }

    [Fact]
    public void A_charge_sweats_at_100_units_an_hour()
    {
        Assert.Equal((200, false), LiquationFurnacing.Advance(0, 2, 1, 100, 400));
        Assert.Equal((400, true), LiquationFurnacing.Advance(300, 2, 1, 100, 400));
        Assert.Equal((100, false), LiquationFurnacing.Advance(0, 2, 0.5, 100, 400));
        Assert.Equal((50, false), LiquationFurnacing.Advance(50, 3, 0, 100, 400));
    }

    [Fact]
    public void The_fire_burns_its_fuel_by_the_damper_and_goes_out_when_it_is_gone()
    {
        // 0.36 an hour open: 10 hours burn 3.6, three whole pieces off the grate and 0.6 of the fourth
        var (burnt, pieces, hours, lit) = LiquationFurnacing.Burn(0, 8, 10, Damper.Open, C);
        Assert.Equal((3, 10.0, true), (pieces, hours, lit));
        Assert.Equal(0.6, burnt, 9);
        // banked: a quarter of the rate
        Assert.Equal(0.09, LiquationFurnacing.Burn(0, 8, 1, Damper.Shut, C).Burnt, 9);
        // two pieces, half of one gone, last 1.5 / 0.36 hours
        var (_, all, burning, stillLit) = LiquationFurnacing.Burn(0.5, 2, 100, Damper.Open, C);
        Assert.Equal((2, false), (all, stillLit));
        Assert.Equal(1.5 / 0.36, burning, 9);
        Assert.False(LiquationFurnacing.Burn(0, 0, 1, Damper.Open, C).Lit);
    }

    [Fact]
    public void Metal_comes_out_in_ingots_and_bits_the_rest_carried()
    {
        Assert.Equal((1, 10, 2.0), Round(LiquationFurnacing.Whole(152)));
        Assert.Equal((4, 0, 0.0), Round(LiquationFurnacing.Whole(400)));
        Assert.Equal((0, 0, 3.0), Round(LiquationFurnacing.Whole(3)));
        Assert.False(LiquationFurnacing.Waiting(3));
        Assert.True(LiquationFurnacing.Waiting(5));
        static (int, int, double) Round((int I, int B, double C) w) => (w.I, w.B, Math.Round(w.C, 9));
    }

    [Fact]
    public void The_fuel_is_drawn_as_its_layers()
    {
        Assert.Equal(0, LiquationFurnacing.FuelLayers(0, 8, 2));
        Assert.Equal(1, LiquationFurnacing.FuelLayers(4, 8, 2));
        Assert.Equal(2, LiquationFurnacing.FuelLayers(5, 8, 2));
        Assert.Equal(2, LiquationFurnacing.FuelLayers(8, 8, 2));
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new LiquationFurnaceConfig { ChargeBars = 0, FuelPerHour = -1, DamperHalfPace = 2, DoorOpenPace = double.NaN };
        Assert.Equal(4, c.Sanitise().Count);
        Assert.Equal((4, 0.36, 0.5, 0.5), (c.ChargeBars, c.FuelPerHour, c.DamperHalfPace, c.DoorOpenPace));
        Assert.Empty(new LiquationFurnaceConfig().Sanitise());
    }

    // ---- The rig and the build ----

    [Fact]
    public void The_rig_gives_gameplay_its_outlet_faces_fuel_cap_and_bricks()
    {
        var rig = LiquationFurnaceRig.Parse(RigJson);
        Assert.Equal(new Int3(1, 1, 1), rig.OutletCell);
        Assert.Equal(new Int3(1, 2, 1), rig.ChimneyBase);
        Assert.Equal((Side.South, Side.North), (rig.BackSide, rig.FrontSide));
        Assert.True(rig.IsAsh(0.2));
        Assert.False(rig.IsAsh(0.7));
        Assert.Equal((2, 4), (rig.FuelLayers, rig.Cakes));
        // the model's cakes are the settings' charge
        Assert.Equal(C.ChargeBars, rig.Cakes);
        Assert.Equal(("game:metalplate-iron", 1), (rig.CapCode, rig.CapCount));
        Assert.Equal((6, 5, 2), (rig.MinChimney, rig.RowMax, rig.Width));
        Assert.Equal("red", rig.Bricks.Default);
        Assert.Equal(9, rig.Bricks.Types.Count);
    }

    [Fact]
    public void The_shared_rig_reads_every_part_with_the_furnaces_own_requires()
    {
        var extra = LiquationRequires.Extra(2);
        var rig = OreMachineRig.Parse(RigJson, extra);
        Assert.Equal(["brickwork", "lining", "fittings"], rig.StageRequires);
        var known = rig.StageRequires.Concat(extra).ToHashSet();
        foreach (var part in rig.Parts.Parts)
            Assert.True(part.Requires == null || known.Contains(part.Requires), $"{part.Id} requires {part.Requires}");
        // every gameplay requires is a part of the model
        foreach (var r in extra)
            Assert.Contains(rig.Parts.Parts, p => p.Requires == r);
        // a batch machine: no power, water or chute anchor; only the outlet
        Assert.Equal(["outlet"], rig.Anchors.Keys.ToArray());
    }

    [Fact]
    public void The_build_is_the_rigs_three_stages()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/liquationfurnace.json")));
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["brickwork", "lining", "fittings"], build.Stages.Select(s => s.Id).ToArray());
        var rig = LiquationFurnaceRig.Parse(RigJson);
        var bricks = build.Stages[0].Materials.Single();
        Assert.True(bricks.OneType);
        Assert.Equal(8, bricks.Quantity);
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), bricks.Codes.Order());
        Assert.Equal(rig.Bricks.ItemOf(rig.Bricks.Default), bricks.Codes[0]);
        // the rig's own stage materials (LiquationFurnace/README.md's stage table)
        using var doc = JsonDocument.Parse(RigJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var stages = doc.RootElement.GetProperty("build").GetProperty("stages");
        for (int i = 1; i < 3; i++)
            Assert.Equal(
                stages[i].GetProperty("materials").EnumerateArray().Select(m => (m.GetProperty("code").GetString(), m.GetProperty("quantity").GetInt32())).ToArray(),
                build.Stages[i].Materials.Select(m => ((string?)m.Codes.Single(), m.Quantity)).ToArray());
    }
}
