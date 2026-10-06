using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Economy;

public class TraderRelationsTests
{
    private static TraderRelations Shipped() =>
        TraderRelations.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trader-relations.json")));

    [Fact]
    public void TheShippedTableParsesAndNamesOnlyTheElevenTypes()
    {
        var rel = Shipped();
        Assert.Empty(rel.Problems(TraderTypes.All.ToList()));
        Assert.Equal(1.0, rel.Listed);
        Assert.Equal(0.5, rel.Related);
        Assert.Equal(0.2, rel.Unrelated);
        Assert.Contains("seraphhorizons:oremap", rel.Refused);
    }

    [Fact]
    public void FitIsFullListedHalfRelatedAFifthOtherwise()
    {
        var rel = Shipped();
        Assert.Equal(1.0, rel.Fit("smith", ["smith", "mechanic"]));
        Assert.Equal(0.5, rel.Fit("smith", ["mechanic"]));
        Assert.Equal(0.5, rel.Fit("mechanic", ["smith"]));
        Assert.Equal(0.5, rel.Fit("carpenter", ["mason"]));
        Assert.Equal(0.5, rel.Fit("cook", ["farmer"]));
        Assert.Equal(0.5, rel.Fit("animaldealer", ["cook"]));
        Assert.Equal(0.5, rel.Fit("generalstore", ["tailor"]));
        Assert.Equal(0.2, rel.Fit("tailor", ["smith"]));
        // The best relation among the buyers counts.
        Assert.Equal(0.5, rel.Fit("tailor", ["smith", "generalstore"]));
        // Nobody buys it: unrelated everywhere, the curio dealer included.
        Assert.Equal(0.2, rel.Fit("smith", []));
        Assert.Equal(0.2, rel.Fit("curiodealer", []));
    }

    [Fact]
    public void TheCurioDealerRelatesToEveryoneAtItsOwnWeight()
    {
        var rel = Shipped();
        Assert.Equal(0.3, rel.Fit("curiodealer", ["smith"]));
        Assert.Equal(0.3, rel.Fit("smith", ["curiodealer"]));
        Assert.Equal(0.3, rel.Relation("mason", "curiodealer"));
    }

    [Fact]
    public void APairWeightAndTheRefusedPrefixes()
    {
        var rel = TraderRelations.Parse("""
            // comment
            { "related": 0.4, "pairs": [["smith", "mechanic"], ["tailor", "farmer", 0.3],], "refused": ["seraphhorizons:oremap"] }
            """);
        Assert.Equal(0.4, rel.Relation("smith", "mechanic"));
        Assert.Equal(0.3, rel.Relation("farmer", "tailor"));
        Assert.Equal(0, rel.Relation("smith", "smith"));
        Assert.True(rel.IsRefused("seraphhorizons:oremap-iron-tier2"));
        Assert.False(rel.IsRefused("game:map"));
        Assert.Contains(rel.Problems(["smith"]), p => p.Contains("unknown type 'mechanic'"));
    }

    [Fact]
    public void TheBuyerIndexTakesCodesWithOrWithoutTheirDomain()
    {
        var index = new BuyerIndex();
        index.Add("ingot-iron", "smith");
        index.Add("game:ingot-iron", "mechanic");
        Assert.Equal(["mechanic", "smith"], index.BuyersOf("game:ingot-iron").OrderBy(t => t));
        Assert.Empty(index.BuyersOf("game:plank-oak"));
    }
}

public class PricingTests
{
    [Fact]
    public void TheSupplyCurveStartsAtOneHalvesTowardsTheFloorAndNeverPassesIt()
    {
        var curve = PriceCurve.Default;
        Assert.Equal(1, curve.Factor(0));
        Assert.Equal(1, curve.Factor(-3));
        Assert.Equal(0.65, curve.Factor(5), 6);
        Assert.True(curve.Factor(1) < 1 && curve.Factor(1) > curve.Factor(2));
        Assert.True(curve.Factor(1e6) > 0.3);
        Assert.Equal(0.3, curve.Factor(1e9), 3);
    }

    [Fact]
    public void AnOffListGoodWorthAGearOrMoreIsPricedPerItem()
    {
        var o = Pricing.OffList(valuePerItem: 3.5, worthless: false, fit: 0.5, supply: 1, modifiers: 1, maxStackSize: 64);
        Assert.True(o.Accepted);
        Assert.Equal(1, o.UnitSize);
        Assert.Equal(2, o.UnitPrice); // 1.75 rounds to 2
        Assert.Equal(Budget.Side, o.Budget);
    }

    [Fact]
    public void ACheapGoodIsSoldByTheFewestItemsWorthAGear()
    {
        // Planks: 0.06 a plank, to the carpenter's relation at 0.5: 0.03 a plank, so 34 for a gear.
        var o = Pricing.OffList(0.06, false, 0.5, 1, 1, 64);
        Assert.Equal(34, o.UnitSize);
        Assert.Equal(1, o.UnitPrice);
        // At a fifth, a full stack is worth under a gear: refused.
        var low = Pricing.OffList(0.06, false, 0.2, 1, 1, 64);
        Assert.Equal(Refusal.TooCheap, low.Refusal);
    }

    [Fact]
    public void WorthlessAndUnvaluedGoodsAreRefused()
    {
        Assert.Equal(Refusal.Worthless, Pricing.OffList(5, true, 1, 1, 1, 64).Refusal);
        Assert.Equal(Refusal.NoValue, Pricing.OffList(0, false, 1, 1, 1, 64).Refusal);
    }

    [Fact]
    public void SupplyAndModifiersScaleTheOffer()
    {
        var full = Pricing.OffList(10, false, 1, 1, 1, 1);
        var glut = Pricing.OffList(10, false, 1, 0.5, 1, 1);
        var standing = Pricing.OffList(10, false, 1, 0.5, 1.2, 1);
        Assert.Equal(10, full.UnitPrice);
        Assert.Equal(5, glut.UnitPrice);
        Assert.Equal(6, standing.UnitPrice);
    }

    [Fact]
    public void ATraderPaysAtMostSixTenthsOfItsOwnPriceForGoodsItSells()
    {
        var o = Pricing.OffList(10, false, 1, 1, 1, 1, sellPricePerItem: 10);
        Assert.Equal(6, o.UnitPrice);
        Assert.True(o.Capped);
        var listed = Pricing.Listed(listPrice: 9, stackSize: 2, supply: 1, modifiers: 1, sellPricePerItem: 5);
        Assert.Equal(6, listed.UnitPrice);
        Assert.True(listed.Capped);
        Assert.Equal(Budget.Main, listed.Budget);
        // Not capped when buying under that.
        Assert.False(Pricing.Listed(4, 2, 1, 1, sellPricePerItem: 5).Capped);
        // A trader's own selling price is not capped.
        Assert.Equal(9, Pricing.Listed(9, 2, 1, 1, sellPricePerItem: 1, traderBuys: false).UnitPrice);
    }

    [Fact]
    public void AListedPriceFallsWithSupplyButNeverUnderAGear()
    {
        Assert.Equal(10, Pricing.Listed(10, 1, 1, 1).UnitPrice);
        Assert.Equal(8, Pricing.Listed(10, 1, PriceCurve.Default.Factor(2.5), 1).UnitPrice); // 0.767
        Assert.Equal(1, Pricing.Listed(1, 1, 0.3, 1).UnitPrice);
    }

    private sealed class Fixed(double f) : IPriceModifier
    {
        public string Name => "fixed";
        public double Factor(in PriceContext context) => f;
    }

    [Fact]
    public void ModifiersMultiplyAndCannotGoNegative()
    {
        var ctx = new PriceContext("game:ingot-iron", "smith", "0,0", true, 1, null);
        Assert.Equal(1, Pricing.Modifiers([], ctx));
        Assert.Equal(1.5, Pricing.Modifiers([new Fixed(1.25), new Fixed(1.2)], ctx), 6);
        Assert.Equal(0, Pricing.Modifiers([new Fixed(-1)], ctx));
    }
}

public class SideBudgetTests
{
    [Fact]
    public void TheSideBudgetIsAQuarterOfTheWallet()
    {
        Assert.Equal(28, SideBudget.RefillTo(110));
        Assert.Equal(15, SideBudget.RefillTo(60));
        Assert.Equal(0, SideBudget.RefillTo(0));
    }

    [Fact]
    public void ACartSplitsByBudget()
    {
        var (main, side) = SideBudget.Split([(3, 2, Budget.Main), (1, 5, Budget.Side), (4, 1, Budget.Main)]);
        Assert.Equal(10, main);
        Assert.Equal(5, side);
    }

    [Fact]
    public void EachBudgetPaysItsOwnShare()
    {
        Assert.True(SideBudget.CanPay(mainWallet: 10, sideBudget: 5, cost: 0, mainGain: 10, sideGain: 5));
        // The side budget can't cover off-list goods with the main wallet's money...
        Assert.False(SideBudget.CanPay(100, 4, 0, 0, 5));
        // ...nor the main wallet listed goods with the side budget's.
        Assert.False(SideBudget.CanPay(9, 50, 0, 10, 0));
        // What the player pays counts towards the main wallet, as in vanilla.
        Assert.True(SideBudget.CanPay(0, 0, 8, 8, 0));
    }
}
