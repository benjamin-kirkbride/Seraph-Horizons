using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>
/// The recovery core (#685, per machine #765) on the shipped <c>config/ore-processing.json</c>, held to the design
/// doc's worked examples (epic #684): one pocket of 256 blocks at vanilla's 1.25 ore a block, the
/// 64 blocks of the design at vanilla's units made four times the blocks at the pack's (#863). A
/// medium ore holds the same 1,600 units as before; poor hematite and gold quartz, whose units were
/// cut by more than four, a little less.
/// </summary>
public class OreRecoveryTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static OreProcessingConfig Shipped() =>
        JsonSerializer.Deserialize<OreProcessingConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")), Options)!;

    private static readonly OreRecovery R = new(Shipped());

    /// <summary>A pocket's ore items: 256 blocks × 1.25 ore.</summary>
    private const int PocketOres = 320;

    /// <summary>A pocket's units: its ore items × the ore's units at its grade.</summary>
    private static double Pocket(string ore, string grade) => PocketOres * OreProducts.RawUnits(ore, grade)!.Value;

    /// <summary>One unit of <paramref name="ore"/> at <paramref name="grade"/> in <paramref name="state"/> through
    /// the concentrators in order: the share caught, and whether the last pass marked its concentrate whole.</summary>
    private static (double Caught, bool Whole) Chain(string ore, string grade, OreState state, params OreMachine[] machines)
    {
        var s = new OreStream(ore, grade, 1, State: state);
        double caught = 0;
        bool whole = false;
        foreach (var m in machines)
        {
            var pass = R.Concentrate(m, s);
            caught += pass.Concentrate;
            whole = pass.Whole;
            s = pass.Tailings;
        }
        return (caught, whole);
    }

    private const OreState Full = OreState.Classified | OreState.Ground | OreState.Amalgamated;
    private static readonly OreMachine[] All = [OreMachine.Sluice, OreMachine.Jig, OreMachine.Table, OreMachine.Vanner];

    [Fact]
    public void ShippedOresAreKnownOres()
    {
        foreach (var ore in R.Ores)
            Assert.True(OreMetals.MetalOf(ore.Ore) != null, $"{ore.Ore} is not an ore OreMetals knows");
    }

    // #765, Derivation: every sub-chain on fully catchable feed, 1 − Π(1 − rᵢ). The vanner of a sub-chain
    // missing an earlier machine is not the whole route, so it keeps its chained figure.
    [Theory]
    [InlineData(0.550, OreMachine.Sluice)]
    [InlineData(0.600, OreMachine.Jig)]
    [InlineData(0.700, OreMachine.Table)]
    [InlineData(0.800, OreMachine.Vanner)]
    [InlineData(0.820, OreMachine.Sluice, OreMachine.Jig)]
    [InlineData(0.865, OreMachine.Sluice, OreMachine.Table)]
    [InlineData(0.880, OreMachine.Jig, OreMachine.Table)]
    [InlineData(0.910, OreMachine.Sluice, OreMachine.Vanner)]
    [InlineData(0.920, OreMachine.Jig, OreMachine.Vanner)]
    [InlineData(0.940, OreMachine.Table, OreMachine.Vanner)]
    [InlineData(0.946, OreMachine.Sluice, OreMachine.Jig, OreMachine.Table)]
    [InlineData(0.964, OreMachine.Sluice, OreMachine.Jig, OreMachine.Vanner)]
    [InlineData(0.973, OreMachine.Sluice, OreMachine.Table, OreMachine.Vanner)]
    [InlineData(0.976, OreMachine.Jig, OreMachine.Table, OreMachine.Vanner)]
    public void SubChains(double share, params OreMachine[] machines)
    {
        Assert.Equal(share, Chain("hematite", "medium", Full, machines).Caught, 9);
        // The order does not change the product.
        Assert.Equal(share, Chain("hematite", "medium", Full, machines.Reverse().ToArray()).Caught, 9);
    }

    [Fact]
    public void TheChainOfAllFourIs98Point9AndTheIdealRouteEndsWhole()
    {
        var hematite = R.Ore("hematite");
        Assert.Equal(0.9892, R.ChainShare(hematite), 9);
        // The vanner last on the whole route recovers whatever is left.
        var (caught, whole) = Chain("hematite", "medium", Full, All);
        Assert.Equal(1, caught, 9);
        Assert.False(whole); // the vanner is an oxide's final step: nothing to mark
        Assert.Equal(OreMachine.Vanner, R.FinalStep(hematite));
        // In another order the vanner isn't last after the other three: the chain's figure.
        Assert.Equal(0.9892, Chain("hematite", "medium", Full, OreMachine.Vanner, OreMachine.Sluice, OreMachine.Jig, OreMachine.Table).Caught, 9);
    }

    [Fact]
    public void NoGainFromAMachinesOwnKind()
    {
        Assert.Equal(0.80, Chain("hematite", "medium", Full, OreMachine.Vanner, OreMachine.Vanner).Caught, 9);
        Assert.Equal(0.55, Chain("hematite", "medium", Full, OreMachine.Sluice, OreMachine.Sluice, OreMachine.Sluice).Caught, 9);
        var pass = R.Concentrate(OreMachine.Jig, new OreStream("hematite", "medium", 1, State: Full));
        Assert.Equal(WorkedBy.Jig, pass.Tailings.Worked);
        Assert.Equal(0.4, pass.Tailings.Units, 9);
    }

    // #765, "Why the locked share applies once": the line recovers its chain figure of the catchable share only.
    [Theory]
    [InlineData("poor", OreState.Classified, 0.4)]       // unground poor ore: 39.6 %
    [InlineData("medium", OreState.None, 0.85)]          // unclassified: 84.1 %
    [InlineData("poor", OreState.None, 0.34)]            // both: 33.6 %
    public void LockedSharesApplyOnce(string grade, OreState state, double factor)
    {
        var (caught, whole) = Chain("hematite", grade, state, All);
        Assert.Equal(factor * 0.9892, caught, 9);
        Assert.False(whole);
        // A lone jig is the same either way: unground poor ore 0.4 × 60 % = 24 %.
        Assert.Equal(factor * 0.60, Chain("hematite", grade, state, OreMachine.Jig).Caught, 9);
    }

    [Fact]
    public void TheTablePutsTheLockedShareOutAsMiddlingsAndTheBallMillSendsThemBack()
    {
        // Poor hematite, classified but not ground: 60 % locked.
        var s = new OreStream("hematite", "poor", 1, State: OreState.Classified);
        double caught = 0;
        OreStream? middlings = null;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, s);
            caught += pass.Concentrate;
            middlings ??= pass.Middlings;
            s = pass.Tailings;
        }
        Assert.Equal(0.4 * 0.9892, caught, 9);
        Assert.NotNull(middlings);
        Assert.Equal(0.6, middlings!.Units, 9);
        Assert.Equal(0.6, middlings.Locked!.Value, 9);
        Assert.Equal(0, s.Locked!.Value, 9);
        // The ball mill grinds and screens: nothing locked, back to the head, where the whole route ends whole.
        var milled = R.Lift(middlings, OreState.Ground | OreState.Classified);
        Assert.Equal(0, milled.Locked!.Value, 9);
        Assert.Equal(WorkedBy.None, milled.Worked);
        double again = 0;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, milled);
            again += pass.Concentrate;
            milled = pass.Tailings;
        }
        Assert.Equal(0.6, again, 9);
        // Without a table the locked share leaves with the last machine's tailings.
        var noTable = Chain("hematite", "poor", OreState.Classified, OreMachine.Sluice, OreMachine.Jig, OreMachine.Vanner);
        Assert.Equal(0.4 * 0.964, noTable.Caught, 9);
    }

    [Fact]
    public void ALiftReleasesLockedUnitsInProportion()
    {
        // Poor, unclassified: 0.34 catchable, 0.66 locked. The arrastra grinds (not screens): 0.85 catchable.
        var s = R.Concentrate(OreMachine.Sluice, new OreStream("hematite", "poor", 1)).Tailings;
        Assert.Equal(0.66, s.Locked!.Value, 9);
        var ground = R.Lift(s, OreState.Ground);
        Assert.Equal(0.15, ground.Locked!.Value, 9);
        Assert.Equal(s.Units, ground.Units, 9);
        Assert.Equal(WorkedBy.None, ground.Worked);
        // A lift that releases nothing keeps the worked kinds.
        var full = R.Concentrate(OreMachine.Sluice, new OreStream("hematite", "medium", 1, State: Full)).Tailings;
        Assert.Equal(WorkedBy.Sluice, R.Lift(full, OreState.Classified).Worked);
    }

    [Fact]
    public void DensityIsPerPassButNotOnTheFinalStep()
    {
        var smithsonite = R.Ore("smithsonite");
        Assert.Equal(0.976, Math.Round(R.ChainShare(smithsonite), 3));
        Assert.Equal(0.55 * 0.9, Chain("smithsonite", "medium", Full, OreMachine.Sluice).Caught, 9);
        Assert.Equal(1, Chain("smithsonite", "medium", Full, All).Caught, 9);
        // Native copper's 1.1 on the vanner alone, 88 %; chromite's 1.05 on the jig, 63 %.
        Assert.Equal(0.88, Chain("nativecopper", "medium", Full, OreMachine.Vanner).Caught, 9);
        Assert.Equal(0.63, Chain("chromite", "medium", Full, OreMachine.Jig).Caught, 9);
    }

    [Fact]
    public void SulfideFullLineEndsWholeAtTheReverberatory()
    {
        var chalcopyrite = R.Ore("chalcopyrite");
        var (caught, whole) = Chain("chalcopyrite", "medium", Full, All);
        Assert.Equal(0.9892, caught, 9);
        Assert.True(whole);
        Assert.Equal(OreMachine.Reverberatory, R.FinalStep(chalcopyrite));
        Assert.Equal(1, caught * R.Roasting(chalcopyrite, Roaster.Reverberatory, whole), 9);
        // Not whole: the reverberatory's 100 % of what it gets; the firepit 85 %, the stall 92 %.
        Assert.Equal(0.9892, caught * R.Roasting(chalcopyrite, Roaster.Reverberatory), 9);
        Assert.Equal(0.85, R.Roasting(chalcopyrite, Roaster.Firepit, true), 9);
        Assert.Equal(0.92, R.Roasting(chalcopyrite, Roaster.Stall, true), 9);
    }

    // #765: sulfide rock through the stall roaster smelts at its grade's share × 92 %.
    [Theory]
    [InlineData("poor", 0.092)]
    [InlineData("medium", 0.23)]
    [InlineData("rich", 0.368)]
    [InlineData("bountiful", 0.46)]
    public void StallRoastedRockSmeltsAtItsGradeTimes92(string grade, double share) =>
        Assert.Equal(share, R.SmeltShare(R.Ore("galena"), OreForm.RoastedRock, grade, Roaster.Stall), 9);

    [Fact]
    public void FreeMetalWantsThePlates()
    {
        // Silver quartz without the plates: 0.7 × 98.9 % = 69.2 %.
        Assert.Equal(0.7 * 0.9892, Chain("quartz_nativesilver", "medium", OreState.Classified | OreState.Ground, All).Caught, 9);
        // Over the plates: 65 % as amalgam, the rest at × 1.0, and the vanner ends it whole.
        var plates = R.Amalgamate(OreMachine.AmalgamationPlates, new OreStream("quartz_nativesilver", "medium", 1, State: OreState.Classified | OreState.Ground));
        Assert.Equal(0.65, plates.Amalgam, 9);
        Assert.True(plates.Rest.State.HasFlag(OreState.Amalgamated));
        var s = plates.Rest;
        double caught = plates.Amalgam;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, s);
            caught += pass.Concentrate;
            s = pass.Tailings;
        }
        Assert.Equal(1, caught, 9);
        // An ore without free metal gives no amalgam.
        Assert.Equal(0, R.Amalgamate(OreMachine.AmalgamationPlates, new OreStream("hematite", "medium", 1)).Amalgam);
    }

    [Fact]
    public void GoldQuartzHoldsItsEightyFivePercentUntilAcidParting()
    {
        var gold = R.Ore("quartz_nativegold");
        Assert.Equal(OreMachine.AcidParting, R.FinalStep(gold));
        var plates = R.Amalgamate(OreMachine.AmalgamationPlates, new OreStream("quartz_nativegold", "medium", 1, State: OreState.Classified | OreState.Ground));
        var s = plates.Rest;
        double concentrate = 0;
        bool whole = false;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, s);
            concentrate += pass.Concentrate;
            whole = pass.Whole;
            s = pass.Tailings;
        }
        Assert.True(whole);
        Assert.Equal(0.996, Math.Round(plates.Amalgam + concentrate, 3)); // 0.65 + 0.35 × 98.9 %
        // Smelted unparted: 85 % of it.
        Assert.Equal([new MetalUnits("gold", (plates.Amalgam + concentrate) * 0.85)], R.Smelted(gold, plates.Amalgam + concentrate, null));
        // Acid parted: the amalgam's sponge as it is, the whole-route concentrate made whole: all the gold.
        var sponge = R.Smelted(gold, plates.Amalgam, PartingStation.AcidParting);
        var conc = R.Smelted(gold, concentrate, PartingStation.AcidParting, whole: true);
        Assert.Equal(1, sponge[0].Units + conc[0].Units, 9);
        Assert.Equal(0.15 * 0.65 * 0.95 + 0.15 * 0.35, sponge[1].Units + conc[1].Units, 9);
        // Not whole: 95 % of the silver.
        Assert.Equal(0.15 * 0.95, R.Smelted(gold, 1, PartingStation.AcidParting)[1].Units, 9);
    }

    [Fact]
    public void PlacerHasNoLockedShare()
    {
        var config = Shipped();
        config.Ores["nativegold"] = new OreProcessingConfig.OreEntry { Class = "placer" };
        var r = new OreRecovery(config);
        Assert.Equal(1, r.LockFactor(r.Ore("nativegold"), "-", OreState.None));
        Assert.Equal(OreMachine.Vanner, r.FinalStep(r.Ore("nativegold")));
        var s = new OreStream("nativegold", "-", 1);
        double caught = 0;
        foreach (var m in All)
        {
            var pass = r.Concentrate(m, s);
            caught += pass.Concentrate;
            s = pass.Tailings;
        }
        Assert.Equal(1, caught, 9);
    }

    [Theory]
    [InlineData("galena", PartingStation.ParkesKettle, "lead", "silver", 0.03)]
    [InlineData("galena_nativesilver", PartingStation.ParkesKettle, "lead", "silver", 0.38)]
    [InlineData("tetrahedrite", PartingStation.CupellationFurnace, "copper", "silver", 0.05)]
    [InlineData("freibergite", PartingStation.CupellationFurnace, "silver", "copper", 0.30)]
    [InlineData("teallite", PartingStation.LiquationFurnace, "tin", "lead", 0.40)]
    [InlineData("franckeite", PartingStation.LiquationFurnace, "tin", "lead", 0.30)]
    public void ByProductsComeOutWholeAtTheFinalParting(string ore, PartingStation final, string main, string second, double share)
    {
        var spec = R.Ore(ore);
        Assert.Equal(OreRecovery.MachineOf(final), R.FinalStep(spec));
        var (caught, whole) = Chain(ore, "medium", Full, All);
        Assert.True(whole);
        // The reverberatory gives the chained 98.9 % of the main metal: it is not the final step.
        double roasted = caught * R.Roasting(spec, Roaster.Reverberatory, whole);
        Assert.Equal(0.9892, roasted, 9);
        var parted = R.Smelted(spec, roasted, final, whole);
        Assert.Equal(main, parted[0].Metal);
        Assert.Equal(1, parted[0].Units, 9);
        Assert.Equal(second, parted[1].Metal);
        Assert.Equal(share, parted[1].Units, 9);
        // Unparted: the main metal, no by-product.
        Assert.Equal([new MetalUnits(main, 100)], R.Smelted(spec, 100, null));
    }

    [Fact]
    public void GalenaCupelledWithoutTheKettleIs95Percent()
    {
        var galena = R.Ore("galena");
        var parted = R.Smelted(galena, 100, PartingStation.CupellationFurnace, whole: true);
        Assert.Equal(new MetalUnits("lead", 100), parted[0]);
        Assert.Equal(100 * 0.03 * 0.95, parted[1].Units, 9);
        // By hand, the bone-ash cupel: 85 %.
        Assert.Equal(100 * 0.03 * 0.85, R.Smelted(galena, 100, PartingStation.Cupel)[1].Units, 9);
        // A station that parts by another method parts nothing: the lead, unparted.
        Assert.Equal([new MetalUnits("lead", 100)], R.Smelted(galena, 100, PartingStation.LiquationFurnace));
    }

    [Fact]
    public void UnroastedSulfideConcentrateDoesNotSmelt()
    {
        var galena = R.Ore("galena");
        Assert.Equal(0, R.Roasting(galena, null));
        Assert.Equal(0, R.SmeltShare(galena, OreForm.Concentrate));
        Assert.Equal(1, R.SmeltShare(galena, OreForm.RoastedConcentrate));
        // Roasting is nothing to an oxide.
        Assert.Equal(1, R.Roasting(R.Ore("hematite"), null));
        Assert.Equal(1, R.SmeltShare(R.Ore("hematite"), OreForm.Concentrate));
    }

    // The hand stations (#765): their figures, no chaining.
    [Fact]
    public void HandStations()
    {
        Assert.Equal(0.45, R.Figure(HandStation.Pan));
        Assert.Equal(0.55, R.Figure(HandStation.Rocker));
        double units = Pocket("quartz_nativegold", "medium");
        Assert.Equal(960, units);
        var crushed = OreStream.FromRock("quartz_nativegold", "medium", OreForm.Crushed, OreState.Classified, units);
        Assert.Equal(369.6, R.HandConcentrate(HandStation.Rocker, crushed), 6); // 55 % × 0.7
        // The amalgam pan, then the rest to the rocker.
        var pan = R.Amalgamate(HandStation.AmalgamPan, crushed);
        Assert.Equal(960 * 0.45, pan.Amalgam, 6);
        Assert.Equal(960 * 0.55 * 0.55, R.HandConcentrate(HandStation.Rocker, pan.Rest), 6);
        // Poor hematite, riddled, by the rocker: 55 % × 0.4 = 22 %.
        var poor = OreStream.FromRock("hematite", "poor", OreForm.Crushed, OreState.Classified);
        Assert.Equal(4, poor.Units);
        Assert.Equal(4 * 0.22, R.HandConcentrate(HandStation.Rocker, poor), 9);
        // Ground ore is ground by its form.
        Assert.Equal(4 * 0.55, R.HandConcentrate(HandStation.Rocker, OreStream.FromRock("hematite", "poor", OreForm.Ground, OreState.Classified)), 9);
        // Native copper's density.
        Assert.Equal(0.605, R.HandConcentrate(HandStation.Rocker, new OreStream("nativecopper", "medium", 1, State: OreState.Classified)), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => R.HandConcentrate(HandStation.AmalgamPan, poor));
    }

    [Fact]
    public void DrawsAndRates()
    {
        var battery = R.Machine(OreMachine.StampBattery);
        Assert.Equal(0.75, battery.Load);
        Assert.Equal(40, battery.WaterPerItem);
        Assert.Equal(6, battery.ItemsPerHour);
        Assert.Equal(9.55, battery.TurnsPerItem, 9); // 95.5 × 0.6 ÷ 6
        Assert.False(battery.LoadFollowsJob);
        Assert.Equal(0.75, R.LoadAt(OreMachine.StampBattery, 0));
        // The jaw breaker's load follows its charge: light empty, 0.8 full.
        Assert.Equal(0.16, R.LoadAt(OreMachine.JawBreaker, 0), 9);
        Assert.Equal(0.48, R.LoadAt(OreMachine.JawBreaker, 0.5), 9);
        Assert.Equal(0.8, R.LoadAt(OreMachine.JawBreaker, 2), 9);
        Assert.Equal(0.5, R.LoadAt(OreMachine.Reverberatory, 1), 9);
        Assert.Equal(0.2, R.LoadAt(OreMachine.ParkesKettle, 1), 9);
        // Batch: the arrastra's charge of 2 in 7.5 hours.
        var arrastra = R.Machine(OreMachine.Arrastra);
        Assert.True(arrastra.Batch);
        Assert.Equal(2 / 7.5, arrastra.ItemsPerHour, 9);
        Assert.Equal(95.5 * 0.6 * 7.5, arrastra.TurnsPerCharge, 9);
        Assert.Equal("concentrate", R.Machine(OreMachine.Reverberatory).Item);
        Assert.Equal("unit", R.Machine(OreMachine.AcidParting).Item);
        Assert.Equal(0.8, R.ZincReturn);
        // Every empty load a fifth of the working one; the sluice, plates and furnaces draw none.
        foreach (var m in Enum.GetValues<OreMachine>())
            Assert.Equal(R.Machine(m).Load / 5, R.Machine(m).EmptyLoad, 9);
        foreach (var m in new[] { OreMachine.Sluice, OreMachine.AmalgamationPlates, OreMachine.StallRoaster, OreMachine.LiquationFurnace, OreMachine.AcidParting, OreMachine.CupellationFurnace })
            Assert.Equal(0, R.Machine(m).Load);
        // The wet machines' water a rock (#940).
        Assert.Equal(70, R.Machine(OreMachine.Jig).WaterPerItem);
        Assert.Equal(0, R.Machine(OreMachine.JawBreaker).WaterPerItem);
    }

    // #847: rock smelts by its grade, whether raw, crushed or ground: poor 10 %, medium 25 %, rich 40 %,
    // bountiful 50 %.
    [Theory]
    [InlineData("poor", 0.10)]
    [InlineData("medium", 0.25)]
    [InlineData("rich", 0.40)]
    [InlineData("bountiful", 0.50)]
    public void RockSmeltsByGrade(string grade, double share)
    {
        var hematite = R.Ore("hematite");
        foreach (var form in new[] { OreForm.Raw, OreForm.Crushed, OreForm.Ground })
            Assert.Equal(share, R.SmeltShare(hematite, form, grade), 9);
        Assert.Equal(share, R.GradeShare(grade), 9);
        // Concentrate is the same at any grade: its rock was thrown away.
        Assert.Equal(1, R.SmeltShare(hematite, OreForm.Concentrate, grade));
        // An oxide has no roasted rock.
        Assert.Equal(0, R.SmeltShare(hematite, OreForm.RoastedRock, grade));
    }

    [Fact]
    public void RockWithNoGradeDoesNotSmelt()
    {
        Assert.Equal(0, R.SmeltShare(R.Ore("hematite"), OreForm.Crushed));
        Assert.Equal(0, R.SmeltShare(R.Ore("hematite"), OreForm.Raw, "lignite"));
    }

    // #847: unroasted sulfide rock does not smelt, raw, crushed or ground; roasted rock smelts at its
    // grade's share times the firepit's 85 %.
    [Theory]
    [InlineData("poor", 0.085)]
    [InlineData("medium", 0.2125)]
    [InlineData("rich", 0.34)]
    [InlineData("bountiful", 0.425)]
    public void SulfideRockSmeltsOnlyRoasted(string grade, double roasted)
    {
        foreach (var ore in R.Ores.Where(o => o.IsSulfide))
        {
            foreach (var form in new[] { OreForm.Raw, OreForm.Crushed, OreForm.Ground, OreForm.Concentrate })
                Assert.Equal(0, R.SmeltShare(ore, form, grade));
            Assert.Equal(roasted, R.SmeltShare(ore, OreForm.RoastedRock, grade), 9);
            Assert.Equal(1, R.SmeltShare(ore, OreForm.RoastedConcentrate, grade));
        }
    }

    // The issue's worked figures (#847): by hand at the mine, smelted as mined against the rocker
    // (classified, unground poor ore at 0.4).
    [Theory]
    [InlineData("poor", 0.10, 0.22)]
    [InlineData("medium", 0.25, 0.55)]
    [InlineData("rich", 0.40, 0.55)]
    [InlineData("bountiful", 0.50, 0.55)]
    public void ConcentratingByHandBeatsOrMatchesSmeltingRock(string grade, double asMined, double rocker)
    {
        var hematite = R.Ore("hematite");
        Assert.Equal(asMined, R.SmeltShare(hematite, OreForm.Raw, grade), 9);
        Assert.Equal(rocker, R.HandConcentrate(HandStation.Rocker, new OreStream("hematite", grade, 1, State: OreState.Classified)), 9);
        Assert.True(rocker >= asMined);
    }

    [Fact]
    public void ConcentrateCarriesItsFractionOver()
    {
        // The poor hematite pocket by hand, one raw ore (4 units) at a time: 281.6 units, exactly.
        var carry = new UnitCarry();
        double perOre = R.HandConcentrate(HandStation.Rocker, OreStream.FromRock("hematite", "poor", OreForm.Crushed, OreState.Classified));
        int items = 0;
        for (int i = 0; i < PocketOres; i++) items += carry.Add("concentrate", perOre, R.ConcentrateUnits);
        Assert.Equal(56, items);
        Assert.Equal(1.6, carry.HeldFor("concentrate"), 6);
        Assert.Equal(281.6, items * R.ConcentrateUnits + carry.HeldFor("concentrate"), 6);
    }

    [Fact]
    public void CrushingKeepsEveryUnit()
    {
        var carry = new UnitCarry();
        Assert.Equal(5, carry.Add("crushed", 25, 5));
        Assert.Equal(0, carry.HeldFor("crushed"));
        Assert.Equal(1, carry.Add("crushed", 7, 5));
        Assert.Equal(2, carry.HeldFor("crushed"), 9);
        Assert.Equal(2, carry.Add("crushed", 8, 5));
        Assert.Empty(carry.Held);
    }

    [Fact]
    public void CarryIsPerOutputAndRestores()
    {
        var carry = new UnitCarry();
        Assert.Equal(0, carry.Add("a", 4.4, 5));
        Assert.Equal(0, carry.Add("b", 1, 5));
        var saved = carry.Held.ToDictionary();
        var again = new UnitCarry();
        again.Restore(saved);
        Assert.Equal(1, again.Add("a", 4.4, 5));
        Assert.Equal(3.8, again.HeldFor("a"), 9);
        Assert.Equal(1, again.HeldFor("b"), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => again.Add("a", 1, 0));
    }

    [Fact]
    public void ReportsABrokenConfig()
    {
        var config = Shipped();
        config.SmeltByGrade.Remove("rich");
        config.SmeltByGrade["poor"] = 2;
        config.Machines.Remove("jig");
        config.Machines["stampmill"] = new();
        config.Machines["vanner"].EmptyLoad = 1;
        config.HandStations.Remove("rocker");
        config.Unclassified = 1.5;
        config.Ores["galena"].Class = "sulphide";
        config.Ores["galena"].ByProducts[0].PartedBy = "magic";
        var problems = new OreRecovery(config).Problems;
        Assert.Contains(problems, p => p == "machines: no entry for 'jig'");
        Assert.Contains(problems, p => p.Contains("'stampmill'"));
        Assert.Contains(problems, p => p.StartsWith("machines.vanner.emptyLoad"));
        Assert.Contains(problems, p => p == "handStations: no figure for 'rocker'");
        Assert.Contains(problems, p => p.StartsWith("unclassified"));
        Assert.Contains(problems, p => p.Contains("'sulphide'"));
        Assert.Contains(problems, p => p.Contains("'magic'"));
        Assert.Contains(problems, p => p == "smeltByGrade: no figure for 'rich'");
        Assert.Contains(problems, p => p.StartsWith("smeltByGrade.poor"));
    }

    [Fact]
    public void ShippedConfigIsSound() => Assert.Empty(R.Problems);
}
