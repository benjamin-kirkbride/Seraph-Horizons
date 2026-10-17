using System.Text.Json;
using SeraphHorizons.Mod.AcidParting.Core;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.AcidParting;

/// <summary>The acid parting vessel (#931): its rules (<see cref="AcidPartingRules"/>), its rig's gameplay half, its
/// build file against its rig and the model README's stage table, and its figures in the shipped
/// <c>config/ore-processing.json</c> (95 %, 10 units an hour, 1 L of wash water a 10 units).</summary>
public class AcidPartingTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly string RigJson = File.ReadAllText(Out("rigs/acidparting-rig.json"));

    private static readonly AcidPartingConfig C = new();

    // ---- The figures ----

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.AcidParting);
        Assert.Equal(0.95, spec.Recovery, 9);
        Assert.Equal(10, spec.ItemsPerHour, 9);
        Assert.Equal(0.1, spec.WaterPerItem, 9);
        Assert.Equal(0, spec.Load);
        Assert.Equal("unit", spec.Item);
        Assert.Equal(0.95, R.Parting(PartingStation.AcidParting), 9);
    }

    [Fact]
    public void A_bar_of_dore_parts_into_its_gold_whole_and_its_silver_at_95()
    {
        // a bar of dore: 100 units of gold carrying gold quartz's 15 % silver
        var ore = R.Ore("quartz_nativegold");
        var yields = R.Smelted(ore, 100, PartingStation.AcidParting);
        Assert.Equal(100, yields.Single(m => m.Metal == "gold").Units, 9);
        Assert.Equal(15 * 0.95, yields.Single(m => m.Metal == "silver").Units, 9);
        // unparted, the gold at 85 % and the silver lost
        Assert.Equal(85, R.Smelted(ore, 100, null).Single().Units, 9);
        // galena's silver-lead is not acid parting's
        Assert.Single(R.Smelted(R.Ore("galena"), 100, PartingStation.AcidParting));
    }

    // ---- The rules ----

    [Fact]
    public void The_phase_follows_what_is_in_it()
    {
        Assert.Equal(VesselPhase.Empty, AcidPartingRules.PhaseOf(0, 0, 0, false));
        Assert.Equal(VesselPhase.Charged, AcidPartingRules.PhaseOf(2, 0, 0, false));
        Assert.Equal(VesselPhase.Parting, AcidPartingRules.PhaseOf(0, 200, 50, true));
        Assert.Equal(VesselPhase.Parted, AcidPartingRules.PhaseOf(0, 200, 200, true));
    }

    [Fact]
    public void Dore_and_acid_go_in_between_charges()
    {
        Assert.Equal((2, VesselRefusal.None), AcidPartingRules.Charge(VesselPhase.Empty, 0, 5, C));
        Assert.Equal((1, VesselRefusal.None), AcidPartingRules.Charge(VesselPhase.Charged, 1, 5, C));
        Assert.Equal((0, VesselRefusal.Full), AcidPartingRules.Charge(VesselPhase.Charged, 2, 5, C));
        Assert.Equal((0, VesselRefusal.Parting), AcidPartingRules.Charge(VesselPhase.Parting, 0, 5, C));
        Assert.Equal((0, VesselRefusal.GoldWaiting), AcidPartingRules.Charge(VesselPhase.Parted, 0, 5, C));
        // 2 L a bar, the vessel holding a full charge's 4 L
        Assert.Equal(4, AcidPartingRules.AcidCapacity(C), 9);
        Assert.Equal((2.5, VesselRefusal.None), AcidPartingRules.PourAcid(VesselPhase.Charged, 1.5, 10, C));
        Assert.Equal((0, VesselRefusal.AcidFull), AcidPartingRules.PourAcid(VesselPhase.Empty, 4, 10, C));
        Assert.Equal((0, VesselRefusal.Parting), AcidPartingRules.PourAcid(VesselPhase.Parting, 0, 10, C));
        Assert.True(AcidPartingRules.AcidReady(2, 4, C));
        Assert.False(AcidPartingRules.AcidReady(2, 3.9, C));
        Assert.True(AcidPartingRules.AcidReady(1, 2, C));
        Assert.False(AcidPartingRules.AcidReady(0, 4, C));
    }

    [Fact]
    public void The_draught_is_the_dampers_on_a_drawing_row()
    {
        Assert.Equal(1, AcidPartingRules.Draught(Damper.Open, true, C));
        Assert.Equal(0.5, AcidPartingRules.Draught(Damper.Half, true, C));
        Assert.Equal(0, AcidPartingRules.Draught(Damper.Shut, true, C));
        Assert.Equal(0, AcidPartingRules.Draught(Damper.Open, false, C));
        Assert.Equal(Damper.Half, AcidPartingRules.Next(Damper.Open));
        Assert.Equal(Damper.Shut, AcidPartingRules.Next(Damper.Half));
        Assert.Equal(Damper.Open, AcidPartingRules.Next(Damper.Shut));
    }

    [Fact]
    public void Fuel_burns_by_its_firepit_seconds()
    {
        Assert.True(AcidPartingRules.IsFuel(1300, 40, C)); // charcoal
        Assert.True(AcidPartingRules.IsFuel(700, 24, C)); // firewood
        Assert.False(AcidPartingRules.IsFuel(300, 10, C)); // dry grass
        Assert.False(AcidPartingRules.IsFuel(1300, 0, C));
        Assert.Equal(4, AcidPartingRules.FuelHours(40, C), 9);
        Assert.Equal(2.4, AcidPartingRules.FuelHours(24, C), 9);
    }

    [Fact]
    public void A_charge_parts_at_its_rate_as_far_as_its_water_and_copper_allow()
    {
        double silver = 0.15 * 0.95, acid = AcidPartingRules.AcidPerUnit(C, Bullion.UnitsPerItem);
        Assert.Equal(2 * 0.4 / 100, acid, 9);
        // an hour at full draught: 10 units, a litre of water, 1.425 silver
        var hour = AcidPartingRules.Part(1, 1, 10, 200, 20, 0.1, 680, silver, acid);
        Assert.Equal(10, hour.Units, 9);
        Assert.Equal(1, hour.Water, 9);
        Assert.Equal(10 * silver, hour.Silver, 9);
        Assert.Equal(10, hour.Gold, 9);
        Assert.Equal(10 * acid, hour.Acid, 9);
        // half draught, half the pace
        Assert.Equal(5, AcidPartingRules.Part(1, 0.5, 10, 200, 20, 0.1, 680, silver, acid).Units, 9);
        // no more than the charge has left, the water allows or the copper can throw down
        Assert.Equal(3, AcidPartingRules.Part(1, 1, 10, 3, 20, 0.1, 680, silver, acid).Units, 9);
        Assert.Equal(4, AcidPartingRules.Part(1, 1, 10, 200, 0.4, 0.1, 680, silver, acid).Units, 9);
        Assert.Equal(2, AcidPartingRules.Part(1, 1, 10, 200, 20, 0.1, 2 * silver, silver, acid).Units, 9);
        Assert.Equal(default, AcidPartingRules.Part(1, 0, 10, 200, 20, 0.1, 680, silver, acid));
        // a whole two-bar charge: 20 hours, 20 L of water, 40 % of its 4 L of acid spent
        var all = AcidPartingRules.Part(20, 1, 10, 200, 20, 0.1, 680, silver, acid);
        Assert.Equal(200, all.Units, 9);
        Assert.Equal(20, all.Water, 9);
        Assert.Equal(0.4 * AcidPartingRules.AcidFor(2, C), all.Acid, 9);
        Assert.Equal(28.5, all.Silver, 9);
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new AcidPartingConfig
        {
            ChargeBars = 0, AcidLitresPerBar = -1, AcidSpentShare = 2, FuelBurnSecondsPerHour = 0, FuelCapacity = 100,
            MinFuelTemperature = -5, HalfDamperDraught = double.NaN,
        };
        Assert.Equal(7, c.Sanitise().Count);
        Assert.Equal(2, c.ChargeBars);
        Assert.Equal(0.4, c.AcidSpentShare);
        Assert.Empty(new AcidPartingConfig().Sanitise());
    }

    // ---- The rig and the build ----

    [Fact]
    public void The_rig_gives_gameplay_its_mouths_outlet_cap_and_bricks()
    {
        var rig = AcidPartingRig.Parse(RigJson);
        Assert.Equal(new Int3(0, 1, 0), rig.HoodCell);
        Assert.Equal(new Int3(0, 1, 1), rig.OutletCell);
        Assert.Equal(new Int3(0, 2, 1), rig.ChimneyBase);
        Assert.Equal(("game:metalplate-iron", 1), (rig.CapCode, rig.CapCount));
        Assert.Equal((6, 6, 1), (rig.MinChimney, rig.RowMax, rig.Width));
        Assert.Equal("red", rig.Bricks.Default);
        Assert.Equal(9, rig.Bricks.Types.Count);
        // a click at the tub's mouth is the tub's, at the vessel's the vessel's; on the front, the ash mouth's or the door's
        Assert.True(rig.NearerTub(rig.Tub));
        Assert.False(rig.NearerTub(rig.Vessel));
        Assert.True(rig.NearerAsh(rig.Ash));
        Assert.False(rig.NearerAsh(rig.FireDoor));
    }

    [Fact]
    public void The_shared_rig_reads_every_part_with_the_vessels_own_requires()
    {
        var rig = OreMachineRig.Parse(RigJson, VesselRequires.Extra);
        Assert.Equal(["brickwork", "lining", "sandbath", "vesseltub", "services"], rig.StageRequires);
        var known = rig.StageRequires.Concat(VesselRequires.Extra).ToHashSet();
        foreach (var part in rig.Parts.Parts)
            Assert.True(part.Requires == null || known.Contains(part.Requires), $"{part.Id} requires {part.Requires}");
        // a batch machine worked by hand: no power, water or chute anchor
        Assert.Equal(["outlet", "tub", "vessel"], rig.Anchors.Keys.Order().ToArray());
    }

    [Fact]
    public void The_build_is_the_model_READMEs_stage_table()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/acidparting.json")));
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["brickwork", "lining", "sandbath", "vesseltub", "services"], build.Stages.Select(s => s.Id).ToArray());
        var rig = AcidPartingRig.Parse(RigJson);
        var bricks = build.Stages[0].Materials.Single();
        Assert.True(bricks.OneType);
        Assert.Equal(4, bricks.Quantity);
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), bricks.Codes.Order());
        Assert.Equal(rig.Bricks.ItemOf(rig.Bricks.Default), bricks.Codes[0]);
        (string, int)[] Of(int stage) => build.Stages[stage].Materials.Select(m => (m.Codes.Single(), m.Quantity)).ToArray();
        Assert.Equal([("game:refractorybrick-fired-tier3", 4), ("game:clay-fire", 2)], Of(1));
        Assert.Equal([("game:sand-*", 1), ("game:metalnailsandstrips-iron", 5)], Of(2));
        Assert.Equal([("game:crucible-*-fired", 1), ("game:metalnailsandstrips-iron", 1), ("game:plank-oak", 1)], Of(3));
        Assert.Equal([("seraphhorizons:pipesection-lead", 1)], Of(4));
        // the rig's stages agree, and the build checks clean against it
        using var doc = SeraphHorizons.Mod.Machines.Core.RigJson.Parse(RigJson);
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, doc.RootElement));
        // the copper plate in the tub wears by the silver thrown down: 200 units of copper for 680 of silver
        var copper = build.Wearing.Single();
        Assert.Equal(("copper", AcidPartingRules.CopperPlate, 1, "copper"), (copper.Id, copper.Codes.Single(), copper.Count, copper.Requires));
        Assert.InRange(copper.Uses - 200 / (63.55 / (2 * 107.87)), -5, 5);
    }
}
