using System.Text.Json;
using SeraphHorizons.Mod.CupellationFurnace.Core;
using SeraphHorizons.Mod.LiquationFurnace.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.ParkesKettle.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>
/// The whole route through the crucible (README "Ore processing: base bullion"): a bar carries the share of its
/// units smelted from whole-route concentrate, bars merge at their weighted share, and the parting machine that is
/// the ore's final step makes that share whole, its main metal and by-product both, so the ideal route ends at 100 %
/// as <see cref="OreRecovery.FinalStep"/> models. Unmarked bars keep their figures exactly. On the shipped
/// <c>config/ore-processing.json</c>.
/// </summary>
public class BullionWholeTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private const double Bar = Bullion.UnitsPerItem;

    // ---- The share ----

    [Fact]
    public void A_crucibles_share_is_its_marked_units_over_all()
    {
        Assert.Equal(0, Bullion.WholeShare([]));
        Assert.Equal(0, Bullion.WholeShare([(40, false), (20, false)]));
        Assert.Equal(1, Bullion.WholeShare([(40, true), (20, true)]));
        // 30 marked concentrates and 10 unmarked of one ore: three quarters
        Assert.Equal(0.75, Bullion.WholeShare([(30, true), (10, false)]), 12);
        // nothing that smelts counts for nothing
        Assert.Equal(1, Bullion.WholeShare([(30, true), (0, false)]));
    }

    [Fact]
    public void Bars_merge_at_their_weighted_share()
    {
        Assert.Equal(1, Bullion.MergeWhole(1, 4, 1, 12));
        Assert.Equal(0, Bullion.MergeWhole(0, 4, 0, 12));
        Assert.Equal(0.25, Bullion.MergeWhole(1, 4, 0, 12), 12);
        Assert.Equal(0.5, Bullion.MergeWhole(0.75, 2, 0.25, 2), 12);
        // merging into nothing takes the other's share
        Assert.Equal(0.6, Bullion.MergeWhole(0, 0, 0.6, 3), 12);
        // float noise at the ends is shaved, so all-marked bars stack as one
        Assert.Equal(1, Bullion.Clamp(1 - 1e-12));
        Assert.Equal(0, Bullion.Clamp(1e-12));
        Assert.Equal(0, Bullion.Clamp(double.NaN));
    }

    // ---- The model: Smelted on a share ----

    [Fact]
    public void A_share_is_its_part_whole_and_the_rest_as_it_is()
    {
        var ore = R.Ore("galena_nativesilver");
        Assert.Equal(PartingStation.CupellationFurnace, Station(R.FinalStep(ore)));
        foreach (double share in new[] { 0, 0.3, 1 })
        {
            var mixed = R.Smelted(ore, 1000, PartingStation.CupellationFurnace, wholeShare: share);
            var expected = Sum(R.Smelted(ore, 1000 * (1 - share), PartingStation.CupellationFurnace),
                R.Smelted(ore, 1000 * share, PartingStation.CupellationFurnace, whole: true));
            Assert.Equal(expected.Count, mixed.Count);
            foreach (var m in expected)
                Assert.Equal(m.Units, mixed.Single(x => x.Metal == m.Metal).Units, 9);
        }
        // No share: today's figures exactly.
        Assert.Equal(R.Smelted(ore, 1000, PartingStation.CupellationFurnace), R.Smelted(ore, 1000, PartingStation.CupellationFurnace, wholeShare: 0));
        // A station that is not the ore's final step (the hand cupel) ignores the share.
        Assert.Equal(R.Smelted(ore, 1000, PartingStation.Cupel), R.Smelted(ore, 1000, PartingStation.Cupel, wholeShare: 1));
    }

    /// <summary>Every by-product ore, from the units entering the concentrators to the end of its ideal route: the
    /// chain's share (98.9 %) as whole-route bars, made whole at its final parting machine: all its main metal and
    /// all its by-product.</summary>
    [Fact]
    public void Every_by_product_ores_ideal_route_ends_at_100_percent()
    {
        foreach (var ore in R.Ores.Where(Bullion.Makes))
        {
            double entering = 1000;
            double bars = entering * R.ChainShare(ore);
            var station = Station(R.FinalStep(ore));
            var parted = R.Smelted(ore, bars, station, wholeShare: 1);
            Assert.Equal(entering, parted.Single(m => m.Metal == ore.Metal).Units, 6);
            foreach (var bp in ore.ByProducts)
                Assert.Equal(entering * bp.Share, parted.Single(m => m.Metal == bp.Metal).Units, 6);
            // unmarked, the same bars give what the line caught, and the by-product at the machine's figure
            var plain = R.Smelted(ore, bars, station);
            Assert.Equal(bars, plain.Single(m => m.Metal == ore.Metal).Units, 6);
            Assert.True(bars < entering);
        }
    }

    private static PartingStation Station(OreMachine m) => m switch
    {
        OreMachine.CupellationFurnace => PartingStation.CupellationFurnace,
        OreMachine.ParkesKettle => PartingStation.ParkesKettle,
        OreMachine.LiquationFurnace => PartingStation.LiquationFurnace,
        OreMachine.AcidParting => PartingStation.AcidParting,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    private static List<MetalUnits> Sum(params IEnumerable<MetalUnits>[] lists) =>
        lists.SelectMany(l => l).GroupBy(m => m.Metal).Select(g => new MetalUnits(g.Key, g.Sum(m => m.Units))).ToList();

    // ---- Each parting machine ----

    [Fact]
    public void The_cupellation_furnace_makes_argentiferous_galenas_whole_bars_whole()
    {
        var ore = R.Ore("galena_nativesilver");
        double restore = R.WholeRestore(ore);
        var plain = Cupelling.Yield(R, [FurnaceCharge.Bar("galena_nativesilver", 2 * Bar)]);
        Assert.Equal(200, plain.LeadUnits, 9);
        Assert.Equal(200 * 0.38 * 0.95, plain.UnitsOf("silver"), 9);
        var whole = Cupelling.Yield(R, [FurnaceCharge.Bar("galena_nativesilver", 2 * Bar, whole: 1)]);
        Assert.Equal(200 * restore, whole.LeadUnits, 9);
        Assert.Equal(200 * restore * 0.38, whole.UnitsOf("silver"), 9);
        // half the bars' units off the whole route: half each way
        var half = Cupelling.Yield(R, [FurnaceCharge.Bar("galena_nativesilver", 2 * Bar, whole: 0.5)]);
        Assert.Equal((plain.UnitsOf("silver") + whole.UnitsOf("silver")) / 2, half.UnitsOf("silver"), 9);
        // tetrahedrite and freibergite end here too, lead added as it is
        var t = Cupelling.Yield(R, [FurnaceCharge.Bar("tetrahedrite", Bar, whole: 1), FurnaceCharge.Lead(Bar)]);
        double tr = R.WholeRestore(R.Ore("tetrahedrite"));
        Assert.Equal(100 * tr, t.UnitsOf("copper"), 9);
        Assert.Equal(100 * tr * 0.05, t.UnitsOf("silver"), 9);
        Assert.Equal(100, t.LeadUnits, 9);
        var f = Cupelling.Yield(R, [FurnaceCharge.Bar("freibergite", Bar, whole: 1), FurnaceCharge.Lead(Bar)]);
        double fr = R.WholeRestore(R.Ore("freibergite"));
        Assert.Equal(100 * fr, f.UnitsOf("silver"), 9);
        Assert.Equal(100 * fr * 0.30, f.UnitsOf("copper"), 9);
    }

    [Fact]
    public void The_cupellation_furnace_is_not_plain_galenas_last_step()
    {
        // Plain galena's last step is the kettle: cupelled directly its whole bars keep the furnace's figure.
        var y = Cupelling.Yield(R, [FurnaceCharge.Bar("galena", Bar, whole: 1)]);
        Assert.Equal(Cupelling.Yield(R, [FurnaceCharge.Bar("galena", Bar)]).UnitsOf("silver"), y.UnitsOf("silver"), 12);
        Assert.Equal(100 * 0.03 * 0.95, y.UnitsOf("silver"), 9);
    }

    /// <summary>Plain galena: 24 whole-route bars through the kettle, its rich lead through the cupellation furnace,
    /// all the lead and all the silver of the units that entered the concentrators.</summary>
    [Fact]
    public void Plain_galena_kettle_then_furnace_ends_whole()
    {
        var ore = R.Ore("galena");
        double restore = R.WholeRestore(ore);
        Assert.True(restore > 1.01 && restore < 1.012, restore.ToString());
        var (lead, silver) = ParkesKettling.Parted(R, ore, 24 * Bar, 24 * Bar);
        Assert.Equal(2400 * restore, lead, 9);
        Assert.Equal(2400 * restore * 0.03, silver, 9);
        // unmarked: the bars' lead and their silver at the kettle's 100 %, as before
        Assert.Equal((2400.0, 72.0), Round(ParkesKettling.Parted(R, ore, 2400, 0)));
        // a quarter of the bath off the whole route
        var (ql, qs) = ParkesKettling.Parted(R, ore, 2400, 600);
        Assert.Equal(1800 + 600 * restore, ql, 9);
        Assert.Equal((1800 + 600 * restore) * 0.03, qs, 9);

        // The charge: rich bars with their silver, the rest of the lead ladled, what is short of an item carried.
        var y = ParkesKettling.Finish(lead, silver, 0, R.RichLeadSilver, 5, 0, R.ZincReturn);
        double leadOut = y.RichLead * Bar + y.Lead * Bar + y.LeadCarry;
        Assert.Equal(lead, leadOut, 9);
        Assert.Equal(silver, y.RichLead * Bullion.RichLeadSilverUnits(R) + y.SilverCarry, 9);
        Assert.True(y.LeadCarry > 0 && y.LeadCarry < Bar);
        // the carried lead goes on with the next charge
        var next = ParkesKettling.Finish(lead, silver, y.SilverCarry, R.RichLeadSilver, 5, y.ZincCarry, R.ZincReturn, y.LeadCarry);
        Assert.Equal(2 * lead, (y.RichLead + y.Lead + next.RichLead + next.Lead) * Bar + next.LeadCarry, 9);

        // The furnace parts rich lead's silver whole: over the chain, all the silver that entered.
        var cupelled = Cupelling.Yield(R, [FurnaceCharge.Rich(y.RichLead * Bar)]);
        Assert.Equal(y.RichLead * Bullion.RichLeadSilverUnits(R), cupelled.UnitsOf("silver"), 9);
        double entering = 2400 * restore;
        Assert.Equal(entering * 0.03, cupelled.UnitsOf("silver") + y.SilverCarry, 9);
    }

    [Fact]
    public void An_unmarked_kettle_charge_is_as_before()
    {
        var y = ParkesKettling.Finish(2400, 72, 0, R.RichLeadSilver, 5, 0, R.ZincReturn);
        Assert.Equal((7, 17, 4), (y.RichLead, y.Lead, y.ZincBits));
        Assert.Equal(2, y.SilverCarry, 9);
        Assert.Equal(0, y.LeadCarry);
    }

    [Fact]
    public void The_liquation_furnace_makes_whole_tin_lead_whole()
    {
        foreach (string ore in new[] { "teallite", "franckeite" })
        {
            var spec = R.Ore(ore);
            double restore = R.WholeRestore(spec);
            double share = spec.ByProducts[0].Share;
            var plain = LiquationFurnacing.Yield(R, [new LiquationBars(ore, 4)]);
            Assert.Equal(400, plain.MetalUnits, 9);
            Assert.Equal(400 * share * 0.95, plain.ByProductUnits, 9);
            var whole = LiquationFurnacing.Yield(R, [new LiquationBars(ore, 4, 1)]);
            Assert.Equal(400 * restore, whole.MetalUnits, 9);
            Assert.Equal(400 * restore * share, whole.ByProductUnits, 9);
            // mixed stacks on the plate each count their own share
            var mixed = LiquationFurnacing.Yield(R, [new LiquationBars(ore, 2, 1), new LiquationBars(ore, 2)]);
            Assert.Equal(200 * restore + 200, mixed.MetalUnits, 9);
            Assert.Equal(200 * restore * share + 200 * share * 0.95, mixed.ByProductUnits, 9);
        }
    }

    [Fact]
    public void Acid_parting_makes_whole_dore_whole()
    {
        var spec = R.Ore("quartz_nativegold");
        Assert.Equal(OreMachine.AcidParting, R.FinalStep(spec));
        double restore = R.WholeRestore(spec);
        // the vessel reads a unit's yield and parts unit by unit
        var plain = R.Smelted(spec, 1, PartingStation.AcidParting, wholeShare: 0);
        Assert.Equal(1, plain.Single(m => m.Metal == "gold").Units, 12);
        Assert.Equal(0.15 * 0.95, plain.Single(m => m.Metal == "silver").Units, 12);
        var whole = R.Smelted(spec, 1, PartingStation.AcidParting, wholeShare: 1);
        Assert.Equal(restore, whole.Single(m => m.Metal == "gold").Units, 12);
        Assert.Equal(0.15 * restore, whole.Single(m => m.Metal == "silver").Units, 12);
    }

    private static (double, double) Round((double A, double B) t) => (Math.Round(t.A, 9), Math.Round(t.B, 9));
}
