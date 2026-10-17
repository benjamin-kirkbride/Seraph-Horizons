using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.ParkesKettle.Core;

namespace SeraphHorizons.Tests.ParkesKettle;

/// <summary>The Parkes kettle (#927): its rules (<see cref="ParkesKettling"/>), its rig's gameplay half, its build file
/// against its rig, and its figures in the shipped <c>config/ore-processing.json</c> (100 %, 2,400 units in 5 hours,
/// 80 % of the zinc back, the load following the charge).</summary>
public class ParkesKettleTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly string RigJson = File.ReadAllText(Out("rigs/parkeskettle-rig.json"));

    private static readonly ParkesKettleConfig C = new();

    // ---- The figures ----

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.ParkesKettle);
        Assert.Equal(1.0, spec.Recovery, 9);
        Assert.Equal(2400, spec.ChargeItems);
        Assert.Equal(5, spec.ChargeHours, 9);
        Assert.Equal(0.8, R.ZincReturn, 9);
        // the stirrer's load follows the charge: 0.04 empty to 0.2 full (#927)
        Assert.True(spec.LoadFollowsJob);
        Assert.Equal(0.04, R.LoadAt(OreMachine.ParkesKettle, 0), 9);
        Assert.Equal(0.12, R.LoadAt(OreMachine.ParkesKettle, 0.5), 9);
        Assert.Equal(0.2, R.LoadAt(OreMachine.ParkesKettle, 1), 9);
        Assert.Equal(0, spec.WaterPerItem);
        // 5 hours at the reference speed 0.6: 57.3 turns an hour
        Assert.Equal(OreRecovery.TurnsPerHourAtSpeed1 * 0.6 * 5, spec.TurnsPerCharge, 9);
    }

    [Fact]
    public void It_takes_plain_galenas_silver_lead_only()
    {
        Assert.True(Bullion.TakenBy(R, R.Ore("galena"), PartingStation.ParkesKettle));
        // argentiferous galena's bullion is richer than the crust: cupelled directly
        Assert.False(Bullion.TakenBy(R, R.Ore("galena_nativesilver"), PartingStation.ParkesKettle));
        Assert.False(Bullion.TakenBy(R, R.Ore("tetrahedrite"), PartingStation.ParkesKettle));
        Assert.False(Bullion.TakenBy(R, R.Ore("teallite"), PartingStation.ParkesKettle));
    }

    // ---- The rules ----

    [Fact]
    public void Zinc_one_to_a_hundred_of_lead_in_whole_bits_starts_the_stirring()
    {
        Assert.Equal(0, ParkesKettling.ZincBitsFor(0, C));
        Assert.Equal(1, ParkesKettling.ZincBitsFor(100, C));
        Assert.Equal(1, ParkesKettling.ZincBitsFor(500, C));
        Assert.Equal(2, ParkesKettling.ZincBitsFor(600, C));
        Assert.Equal(5, ParkesKettling.ZincBitsFor(2400, C));
        Assert.Equal(KettlePhase.Empty, ParkesKettling.PhaseOf(0, 0, false, C));
        Assert.Equal(KettlePhase.Charging, ParkesKettling.PhaseOf(2400, 4, false, C));
        Assert.Equal(KettlePhase.Stirring, ParkesKettling.PhaseOf(2400, 5, false, C));
        Assert.Equal(KettlePhase.Done, ParkesKettling.PhaseOf(0, 0, true, C));
    }

    [Fact]
    public void Lead_goes_in_up_to_a_charge_before_the_stirring()
    {
        Assert.Equal((16, KettleRefusal.None), ParkesKettling.AddLead(KettlePhase.Empty, 0, 16, 100, 2400));
        Assert.Equal((8, KettleRefusal.None), ParkesKettling.AddLead(KettlePhase.Charging, 1600, 16, 100, 2400));
        Assert.Equal((0, KettleRefusal.Full), ParkesKettling.AddLead(KettlePhase.Charging, 2400, 16, 100, 2400));
        Assert.Equal((0, KettleRefusal.Stirring), ParkesKettling.AddLead(KettlePhase.Stirring, 1000, 1, 100, 2400));
        Assert.Equal((0, KettleRefusal.Done), ParkesKettling.AddLead(KettlePhase.Done, 0, 1, 100, 2400));

        Assert.Equal((0, KettleRefusal.NoLead), ParkesKettling.AddZinc(KettlePhase.Empty, 0, 0, 8, C));
        Assert.Equal((5, KettleRefusal.None), ParkesKettling.AddZinc(KettlePhase.Charging, 2400, 0, 8, C));
        Assert.Equal((2, KettleRefusal.None), ParkesKettling.AddZinc(KettlePhase.Charging, 2400, 3, 8, C));
        Assert.Equal((0, KettleRefusal.ZincIn), ParkesKettling.AddZinc(KettlePhase.Stirring, 2400, 5, 8, C));
    }

    [Fact]
    public void The_fire_burns_six_coal_a_charge_and_heats_by_the_damper()
    {
        var rig = ParkesKettleRig.Parse(RigJson);
        double perHour = rig.CoalPerCharge / R.Machine(OreMachine.ParkesKettle).ChargeHours;
        Assert.Equal(1.2, perHour, 9);
        Assert.Equal((2.0, false), ParkesKettling.Burn(8, 5, perHour, 1));
        Assert.Equal((0.0, true), ParkesKettling.Burn(1, 1, perHour, 1));
        Assert.Equal(5, ParkesKettling.Burn(8, 5, perHour, ParkesKettling.BurnShare(true, Damper.Half, C)).Fuel, 9);
        Assert.Equal(6.5, ParkesKettling.Burn(8, 5, perHour, ParkesKettling.BurnShare(false, Damper.Open, C)).Fuel, 9);

        Assert.Equal(1, ParkesKettling.Heat(true, true, Damper.Open, C));
        Assert.Equal(0.5, ParkesKettling.Heat(true, true, Damper.Half, C));
        Assert.Equal(0, ParkesKettling.Heat(true, true, Damper.Shut, C));
        Assert.Equal(0, ParkesKettling.Heat(true, false, Damper.Open, C));
        Assert.Equal(0, ParkesKettling.Heat(false, true, Damper.Open, C));

        Assert.Equal((8, KettleRefusal.None), ParkesKettling.AddFuel(0, 64, 8));
        Assert.Equal((0, KettleRefusal.GrateFull), ParkesKettling.AddFuel(8, 64, 8));
        Assert.Equal(KettleRefusal.NoFuel, ParkesKettling.CannotLight(false, 0.5));
        Assert.Equal(KettleRefusal.Lit, ParkesKettling.CannotLight(true, 4));
        Assert.Equal(KettleRefusal.None, ParkesKettling.CannotLight(false, 1));
        Assert.True(ParkesKettling.IsFuel("game:ore-bituminouscoal") && ParkesKettling.IsFuel("game:charcoal"));
        Assert.False(ParkesKettling.IsFuel("game:firewood"));
    }

    [Fact]
    public void A_part_charge_takes_its_share_of_the_turns_and_the_bath_is_drawn_by_quarters()
    {
        Assert.Equal(286.5, ParkesKettling.TurnsFor(2400, 2400, 286.5), 9);
        Assert.Equal(286.5 / 4, ParkesKettling.TurnsFor(600, 2400, 286.5), 9);
        Assert.Equal(0.5, ParkesKettling.Fill(1200, 2400), 9);
        Assert.Equal(0, ParkesKettling.Levels(0, 2400, 4));
        Assert.Equal(1, ParkesKettling.Levels(100, 2400, 4));
        Assert.Equal(1, ParkesKettling.Levels(600, 2400, 4));
        Assert.Equal(2, ParkesKettling.Levels(700, 2400, 4));
        Assert.Equal(4, ParkesKettling.Levels(2400, 2400, 4));
        Assert.Equal(0, ParkesKettling.FuelLayers(0, 4, 2));
        Assert.Equal(1, ParkesKettling.FuelLayers(3.2, 4, 2));
        Assert.Equal(2, ParkesKettling.FuelLayers(6, 4, 2));
    }

    [Fact]
    public void A_galena_charge_gives_its_silver_whole_as_rich_lead_and_most_zinc_back()
    {
        // 24 bars of plain galena's silver-lead: 2,400 lead with 3 % silver, all of it at the kettle's 100 %
        double silver = R.Smelted(R.Ore("galena"), 2400, PartingStation.ParkesKettle).Where(m => m.Metal == "silver").Sum(m => m.Units);
        Assert.Equal(72, silver, 9);
        var y = ParkesKettling.Finish(2400, silver, 0, R.RichLeadSilver, 5, 0, R.ZincReturn);
        // 10 silver a rich bar: 7 bars, 2 silver carried to the next charge
        Assert.Equal(7, y.RichLead);
        Assert.Equal(2, y.SilverCarry, 9);
        Assert.Equal(17, y.Lead);
        // 25 units of zinc in, 20 back: 4 bits
        Assert.Equal(4, y.ZincBits);
        Assert.Equal(0, y.ZincCarry, 9);
        // the next charge takes the carried silver on: 74 → 7 bars, 4 carried
        var next = ParkesKettling.Finish(2400, silver, y.SilverCarry, R.RichLeadSilver, 5, y.ZincCarry, R.ZincReturn);
        Assert.Equal((7, 4.0), (next.RichLead, Math.Round(next.SilverCarry, 9)));
        // over charges, the silver comes out whole: in rich bars and what is carried
        double carry = 0;
        int bars = 0;
        for (int i = 0; i < 10; i++)
        {
            var k = ParkesKettling.Finish(2400, silver, carry, R.RichLeadSilver, 5, 0, R.ZincReturn);
            bars += k.RichLead;
            carry = k.SilverCarry;
        }
        Assert.Equal(720, bars * Bullion.UnitsPerItem * R.RichLeadSilver + carry, 6);
    }

    [Fact]
    public void A_small_charge_with_too_little_silver_for_a_bar_carries_it_all()
    {
        var y = ParkesKettling.Finish(100, 3, 0, R.RichLeadSilver, 1, 0, R.ZincReturn);
        Assert.Equal((0, 1, 0), (y.RichLead, y.Lead, y.ZincBits));
        Assert.Equal(3, y.SilverCarry, 9);
        Assert.Equal(4, y.ZincCarry, 9);
    }

    [Fact]
    public void Its_settings_out_of_range_fall_back()
    {
        var c = new ParkesKettleConfig { ZincPerLead = 0, DamperHalfHeat = 2, LadleIngots = 0 };
        Assert.Equal(3, c.Sanitise().Count);
        Assert.Equal(0.01, c.ZincPerLead, 9);
        Assert.Equal(0.5, c.DamperHalfHeat, 9);
        Assert.Equal(6, c.LadleIngots);
        Assert.Empty(new ParkesKettleConfig().Sanitise());
    }

    // ---- The rig and the build ----

    [Fact]
    public void Its_rig_holds_the_charge_the_grate_the_cap_and_the_row()
    {
        var rig = ParkesKettleRig.Parse(RigJson);
        Assert.Equal(new Int3(1, 1, 2), rig.OutletCell);
        Assert.Equal((2400, 4), (rig.ChargeUnits, rig.LeadLevels));
        Assert.Equal((2, 4, 6, 8), (rig.FuelLayers, rig.FuelPerLayer, rig.CoalPerCharge, rig.Grate));
        Assert.Equal(("game:metalplate-iron", 1), (rig.CapCode, rig.CapCount));
        Assert.Equal((6, 5, 3), (rig.MinChimney, rig.RowMax, rig.Width));
        Assert.Equal("red", rig.Bricks.Default);
        // the rig's charge is ore processing's
        Assert.Equal(R.Machine(OreMachine.ParkesKettle).ChargeItems, rig.ChargeUnits);
    }

    [Fact]
    public void The_shared_rig_reads_every_part_with_the_kettles_own_requires()
    {
        var extra = KettleRequires.Extra(4, 2);
        var rig = OreMachineRig.Parse(RigJson, extra);
        Assert.Equal(["brickwork", "lining", "kettle", "stirrer"], rig.StageRequires);
        var known = rig.StageRequires.Concat(extra).ToHashSet();
        foreach (var part in rig.Parts.Parts)
            Assert.True(part.Requires == null || known.Contains(part.Requires), $"{part.Id} requires {part.Requires}");
        // the axle at the back; the kettle's mouth, worked through its own cells; the outlet; no chute or water
        Assert.Equal(["kettle", "outlet", "power"], rig.Anchors.Keys.Order().ToArray());
        Assert.Equal(Axis.Z, rig.ShaftAxis);
    }

    [Fact]
    public void An_attached_anchor_still_may_not_face_into_the_footprint()
    {
        var bad = RigJson.Replace("\"powerFace\": \"south\"", "\"powerFace\": \"north\"");
        Assert.NotEqual(RigJson, bad);
        Assert.Contains("inside the footprint", Assert.Throws<FormatException>(() => OreMachineRig.Parse(bad, KettleRequires.Extra(4, 2))).Message);
    }

    [Fact]
    public void The_build_is_the_rigs_four_stages()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/parkeskettle.json")));
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["brickwork", "lining", "kettle", "stirrer"], build.Stages.Select(s => s.Id).ToArray());
        using var doc = JsonDocument.Parse(RigJson);
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, doc.RootElement));
        var rig = ParkesKettleRig.Parse(RigJson);
        var bricks = build.Stages[0].Materials.Single();
        Assert.True(bricks.OneType);
        Assert.Equal(20, bricks.Quantity);
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), bricks.Codes.Order());
        Assert.Equal(rig.Bricks.ItemOf(rig.Bricks.Default), bricks.Codes[0]);
        // the README's stage table and the rig's build.stages
        var rigStages = doc.RootElement.GetProperty("build").GetProperty("stages").EnumerateArray().ToList();
        for (int i = 0; i < 4; i++)
        {
            var want = rigStages[i].GetProperty("materials").EnumerateArray()
                .Select(m => (m.GetProperty("code").GetString()!, m.GetProperty("quantity").GetInt32())).ToList();
            var got = build.Stages[i].Materials.Select(m => (m.Codes.Count > 1 ? "game:burnedbrick-*" : m.Codes[0], m.Quantity)).ToList();
            Assert.Equal(want, got);
        }
        Assert.Empty(build.Wearing);
    }
}
