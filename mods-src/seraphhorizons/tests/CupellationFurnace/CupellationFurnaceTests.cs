using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.CupellationFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.CupellationFurnace;

/// <summary>The cupellation furnace (#740): its rules (<see cref="Cupelling"/>), its rig's gameplay half, its build
/// file, and its figures in the shipped <c>config/ore-processing.json</c> (95 %, 55 units of lead an hour).</summary>
public class CupellationFurnaceTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly CupellationFurnaceConfig C = new();

    private static FurnaceCharge Bars(string ore, int n) => FurnaceCharge.Bar(ore, n * Bullion.UnitsPerItem);
    private static FurnaceCharge LeadIngots(int n) => FurnaceCharge.Lead(n * Bullion.UnitsPerItem);

    // ---- The figures ----

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.CupellationFurnace);
        Assert.Equal(0.95, spec.Recovery, 9);
        Assert.Equal(55, spec.ItemsPerHour, 9);
        Assert.Equal(0, spec.Load);
        Assert.Equal(200, C.ChargeUnits);
        Assert.Equal(0.7, C.CoalPerHour);
        Assert.Empty(new CupellationFurnaceConfig().Sanitise());
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new CupellationFurnaceConfig { ChargeUnits = 5, CoalPerHour = -1, LeadPerOtherUnit = double.NaN, DamperHalfDraught = 2, BankedBurn = -0.1 };
        Assert.Equal(5, c.Sanitise().Count);
        Assert.Equal(200, c.ChargeUnits);
        Assert.Equal(0.7, c.CoalPerHour);
    }

    // ---- What it takes ----

    [Theory]
    [InlineData("galena", true)]
    [InlineData("galena_nativesilver", true)]
    [InlineData("tetrahedrite", true)]
    [InlineData("freibergite", true)]
    [InlineData("teallite", false)]
    [InlineData("franckeite", false)]
    [InlineData("quartz_nativegold", false)]
    public void It_cupels_the_bullion_cupellation_parts(string ore, bool takes) => Assert.Equal(takes, Cupelling.Takes(R, R.Ore(ore)));

    [Fact]
    public void Its_refusals()
    {
        Assert.Equal(CupellingRefusal.Empty, Cupelling.Check(R, C, []));
        Assert.Equal(CupellingRefusal.Foreign, Cupelling.Check(R, C, [Bars("galena", 1), FurnaceCharge.Other]));
        Assert.Equal(CupellingRefusal.Foreign, Cupelling.Check(R, C, [Bars("teallite", 1)]));
        Assert.Equal(CupellingRefusal.NothingToPart, Cupelling.Check(R, C, [LeadIngots(2)]));
        Assert.Equal(CupellingRefusal.TooMuch, Cupelling.Check(R, C, [Bars("galena", 3)]));
        Assert.Equal(CupellingRefusal.TooLittleLead, Cupelling.Check(R, C, [Bars("tetrahedrite", 2)]));
        Assert.Equal(CupellingRefusal.None, Cupelling.Check(R, C, [Bars("tetrahedrite", 1), LeadIngots(1)]));
        Assert.Equal(CupellingRefusal.None, Cupelling.Check(R, C, [Bars("galena", 2)]));
        Assert.Equal(CupellingRefusal.None, Cupelling.Check(R, C, [FurnaceCharge.Rich(200)]));
        Assert.True(Cupelling.Fits(C, 100, 100));
        Assert.False(Cupelling.Fits(C, 200, 100));
    }

    // ---- What it gives ----

    // Two bars of galena's silver-lead: 200 lead to litharge, 6 silver at 95 %.
    [Fact]
    public void Silver_lead_at_95_per_cent()
    {
        var y = Cupelling.Yield(R, [Bars("galena", 2)]);
        Assert.Equal(200, y.LeadUnits, 9);
        Assert.Equal(6 * 0.95, y.UnitsOf("silver"), 9);
        Assert.Equal(40, Cupelling.LithargeFor(y.LeadUnits));
        var rich = Cupelling.Yield(R, [Bars("galena_nativesilver", 2)]);
        Assert.Equal(76 * 0.95, rich.UnitsOf("silver"), 9);
    }

    // The Parkes kettle's rich lead: all of its silver.
    [Fact]
    public void Rich_lead_whole()
    {
        var y = Cupelling.Yield(R, [FurnaceCharge.Rich(200)]);
        Assert.Equal(200, y.LeadUnits, 9);
        Assert.Equal(200 * R.RichLeadSilver, y.UnitsOf("silver"), 9);
        Assert.Equal(20, y.UnitsOf("silver"), 9);
    }

    // Argentiferous copper with its lead: the copper back whole beside the cake, 5 silver at 95 %; cupriferous silver:
    // its silver whole, 30 copper at 95 %.
    [Fact]
    public void Copper_and_silver_bars_with_lead_added()
    {
        var t = Cupelling.Yield(R, [Bars("tetrahedrite", 1), LeadIngots(1)]);
        Assert.Equal(100, t.LeadUnits, 9);
        Assert.Equal(100, t.UnitsOf("copper"), 9);
        Assert.Equal(5 * 0.95, t.UnitsOf("silver"), 9);
        var f = Cupelling.Yield(R, [Bars("freibergite", 1), LeadIngots(1)]);
        Assert.Equal(100, f.UnitsOf("silver"), 9);
        Assert.Equal(30 * 0.95, f.UnitsOf("copper"), 9);
    }

    // ---- The pace, the fire and the blast ----

    [Fact]
    public void A_full_charge_at_full_blast_takes_200_over_55_hours()
    {
        var (done, finished) = Cupelling.Advance(0, 200.0 / 55 - 0.01, 1, 55, 200);
        Assert.False(finished);
        Assert.True(done < 200);
        (done, finished) = Cupelling.Advance(done, 0.02, 1, 55, 200);
        Assert.True(finished);
        Assert.Equal(200, done);
        // Half the air, half the pace (the owner's ruling: pace x min(1, air / 10)).
        Assert.Equal(27.5, Cupelling.Advance(0, 1, Cupelling.BlastShare(5, 10), 55, 200).Done, 9);
        Assert.Equal(1, Cupelling.BlastShare(20, 10));
        Assert.Equal(0, Cupelling.BlastShare(0, 10));
    }

    [Fact]
    public void The_damper_and_the_row_set_the_draught()
    {
        Assert.Equal(1, Cupelling.Draught(Damper.Open, true, C));
        Assert.Equal(0.5, Cupelling.Draught(Damper.Half, true, C));
        Assert.Equal(0, Cupelling.Draught(Damper.Shut, true, C));
        Assert.Equal(0, Cupelling.Draught(Damper.Open, false, C));
        Assert.Equal(0.25, Cupelling.BurnShare(Damper.Shut, true, C));
        Assert.Equal(0.25, Cupelling.BurnShare(Damper.Open, false, C));
    }

    // 0.7 coal an hour: ten hours open burn 7 coal; a banked fire a quarter of that.
    [Fact]
    public void The_fire_burns_its_coal()
    {
        var (ember, taken, lit) = Cupelling.Burn(0, 10, 10, 0.7, 1);
        Assert.Equal(7, taken);
        Assert.Equal(0, ember, 9);
        Assert.True(lit);
        (ember, taken, lit) = Cupelling.Burn(0, 10, 1, 0.7, 1);
        Assert.Equal(1, taken);
        Assert.Equal(0.3, ember, 9);
        (ember, taken, lit) = Cupelling.Burn(0.3, 0, 0.1, 0.7, 1);
        Assert.Equal(0, taken);
        Assert.Equal(0.23, ember, 9);
        Assert.True(lit);
        (_, taken, lit) = Cupelling.Burn(0.1, 1, 10, 0.7, 1);
        Assert.Equal(1, taken);
        Assert.False(lit);
        (_, taken, _) = Cupelling.Burn(0, 10, 4, 0.7, 0.25);
        Assert.Equal(1, taken);
    }

    // ---- The rig and the build ----

    [Fact]
    public void The_rig_holds_the_rulings()
    {
        var rig = CupellationFurnaceRig.Parse(File.ReadAllText(Out("rigs/cupellationfurnace-rig.json")));
        Assert.Equal(new Int3(-1, 1, 2), rig.OutletCell);
        Assert.Equal(new Int3(0, 1, 1), rig.BlastCell);
        Assert.Equal(CellFace.South, rig.BlastFace);
        Assert.Equal("Air", rig.BlastGas);
        Assert.Equal(10, rig.BlastRate);
        Assert.Equal(1.5, rig.BlastPressure);
        Assert.Equal(15, rig.TestCharges);
        Assert.Equal(6, rig.MinChimney);
        Assert.Equal(5, rig.RowMax);
        Assert.Equal(3, rig.Width);
        Assert.Equal("game:metalplate-iron", rig.CapCode);
        Assert.Equal("red", rig.Bricks.Default);
    }

    [Fact]
    public void The_build_is_the_models_stages_and_the_test_its_wearing_part()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/cupellationfurnace.json")));
        var rig = CupellationFurnaceRig.Parse(File.ReadAllText(Out("rigs/cupellationfurnace-rig.json")));
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["brickwork", "lining", "fittings"], build.Stages.Select(s => s.Id).ToArray());
        var bricks = build.Stages[0].Materials.Single();
        Assert.True(bricks.OneType);
        Assert.Equal(17, bricks.Quantity);
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), bricks.Codes.Order());
        Assert.Equal([("game:refractorybrick-fired-tier3", 12), ("game:clay-fire", 5)],
            build.Stages[1].Materials.Select(m => (m.Codes.Single(), m.Quantity)).ToArray());
        Assert.Equal([("game:metalplate-iron", 3), ("game:metalplate-copper", 1)],
            build.Stages[2].Materials.Select(m => (m.Codes.Single(), m.Quantity)).ToArray());
        var test = build.Wearing.Single();
        Assert.Equal(("test", "game:bonemeal", 48, (double)rig.TestCharges, "test"), (test.Id, test.Codes.Single(), test.Count, test.Uses, test.Requires));
    }
}
