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
        Assert.Equal(0.75, rel.Related);
        Assert.Equal(0.5, rel.Unrelated);
        Assert.Contains("seraphhorizons:oremap", rel.Refused);
    }

    [Fact]
    public void FitIsFullListedThreeQuartersRelatedHalfOtherwise()
    {
        var rel = Shipped();
        Assert.Equal(1.0, rel.Fit("smith", ["smith", "mechanic"]));
        Assert.Equal(0.75, rel.Fit("smith", ["mechanic"]));
        Assert.Equal(0.75, rel.Fit("mechanic", ["smith"]));
        Assert.Equal(0.75, rel.Fit("carpenter", ["mason"]));
        Assert.Equal(0.75, rel.Fit("cook", ["farmer"]));
        Assert.Equal(0.75, rel.Fit("animaldealer", ["cook"]));
        Assert.Equal(0.75, rel.Fit("generalstore", ["tailor"]));
        Assert.Equal(0.5, rel.Fit("tailor", ["smith"]));
        // A weak link sits between.
        Assert.Equal(0.6, rel.Fit("tailor", ["animaldealer"]));
        // The best relation among the buyers counts.
        Assert.Equal(0.75, rel.Fit("tailor", ["smith", "generalstore"]));
        // Nobody buys it: unrelated everywhere, the curio dealer included.
        Assert.Equal(0.5, rel.Fit("smith", []));
        Assert.Equal(0.5, rel.Fit("curiodealer", []));
    }

    [Fact]
    public void TheCurioDealerRelatesToEveryoneAtItsOwnWeight()
    {
        var rel = Shipped();
        Assert.Equal(0.6, rel.Fit("curiodealer", ["smith"]));
        Assert.Equal(0.6, rel.Fit("smith", ["curiodealer"]));
        Assert.Equal(0.6, rel.Relation("mason", "curiodealer"));
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
    public void RelatedGoodsArePaidFromTheMainWalletTheRestFromTheSideBudget()
    {
        var rel = Shipped();
        Assert.True(rel.PaysFromMain("smith", ["mechanic"]));
        // A pair with its own weight is related too.
        Assert.True(rel.PaysFromMain("tailor", ["animaldealer"]));
        // Its own type's list elsewhere.
        Assert.True(rel.PaysFromMain("smith", ["smith"]));
        Assert.False(rel.PaysFromMain("tailor", ["smith"]));
        Assert.False(rel.PaysFromMain("smith", []));
        // The curio dealer's interest in everything is no relation.
        Assert.False(rel.PaysFromMain("curiodealer", ["smith"]));
        Assert.False(rel.PaysFromMain("smith", ["curiodealer"]));
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
        // Iron at 4 a bar, a fifth of it, at a related trader: 4 × 0.2 × 0.75 = 0.6, two bars a gear.
        var o = Pricing.OffList(valuePerItem: 17.5, worthless: false, fit: 0.5, supply: 1, modifiers: 1, maxStackSize: 64);
        Assert.True(o.Accepted);
        Assert.Equal(1, o.UnitSize);
        Assert.Equal(2, o.UnitPrice); // 17.5 × 0.2 × 0.5 = 1.75 rounds to 2
        Assert.Equal(0.2, o.Spread);
        Assert.Equal(Budget.Side, o.Budget);
    }

    [Fact]
    public void ATraderPaysAFifthOfValueByDefaultAndTheSpreadIsAKnob()
    {
        Assert.Equal(0.2, Pricing.DefaultBuySpread);
        Assert.Equal(2, Pricing.OffList(10, false, 1, 1, 1, 1).UnitPrice);
        Assert.Equal(5, Pricing.OffList(10, false, 1, 1, 1, 1, spread: 0.5).UnitPrice);
        Assert.Equal(10, Pricing.OffList(10, false, 1, 1, 1, 1, spread: 1).UnitPrice);
    }

    [Fact]
    public void ACheapGoodIsSoldByTheFewestItemsWorthAGear()
    {
        // Planks: 0.3 a plank, a fifth of it, to the carpenter's relation at 0.75: 0.045 a plank, so 23 for a gear.
        var o = Pricing.OffList(0.3, false, 0.75, 1, 1, 64);
        Assert.Equal(23, o.UnitSize);
        Assert.Equal(1, o.UnitPrice);
        // Unrelated, at a tenth of value, a full stack is worth under a gear: refused.
        var low = Pricing.OffList(0.15, false, 0.5, 1, 1, 64);
        Assert.Equal(Refusal.TooCheap, low.Refusal);
        Assert.Equal(0.2, low.Spread);
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
        var full = Pricing.OffList(50, false, 1, 1, 1, 1);
        var glut = Pricing.OffList(50, false, 1, 0.5, 1, 1);
        var standing = Pricing.OffList(50, false, 1, 0.5, 1.2, 1);
        Assert.Equal(10, full.UnitPrice);
        Assert.Equal(5, glut.UnitPrice);
        Assert.Equal(6, standing.UnitPrice);
    }

    [Fact]
    public void AListedPriceIsTheListsWithNoSpreadOnTop()
    {
        // The list holds the final pay: no spread, no cap against the trader's own price.
        var listed = Pricing.Listed(listPrice: 9, stackSize: 2, supply: 1, modifiers: 1);
        Assert.Equal(9, listed.UnitPrice);
        Assert.Equal(2, listed.UnitSize);
        Assert.Equal(1, listed.Spread);
        Assert.Equal(Budget.Main, listed.Budget);
        Assert.Equal(9, Pricing.Listed(9, 2, 1, 1, traderBuys: false).UnitPrice);
    }

    [Fact]
    public void ACheapListedBuyingEntryIsBoughtByABiggerUnit()
    {
        // Rope at 0.2 a 4: bought by the 20 for a gear, not by the 4 for a rounded-up gear.
        var rope = Pricing.Listed(0.2, 4, 1, 1, traderBuys: true, maxStackSize: 64);
        Assert.Equal(20, rope.UnitSize);
        Assert.Equal(1, rope.UnitPrice);
        // Whole stacks of the entry, and never past the item's stack size.
        var capped = Pricing.Listed(0.4, 32, 1, 1, traderBuys: true, maxStackSize: 64);
        Assert.Equal(64, capped.UnitSize);
        Assert.Equal(1, capped.UnitPrice);
        var single = Pricing.Listed(0.6, 1, 1, 1, traderBuys: true, maxStackSize: 1);
        Assert.Equal(1, single.UnitSize);
        Assert.Equal(1, single.UnitPrice);
        // A gear or more per stack keeps the entry's stack; a trader's selling side always does.
        Assert.Equal(4, Pricing.Listed(3.2, 4, 1, 1, traderBuys: true, maxStackSize: 64).UnitSize);
        Assert.Equal(4, Pricing.Listed(0.2, 4, 1, 1, traderBuys: false, maxStackSize: 64).UnitSize);
    }

    [Fact]
    public void AListedPriceFallsWithSupplyButNeverUnderAGear()
    {
        Assert.Equal(10, Pricing.Listed(10, 1, 1, 1).UnitPrice);
        Assert.Equal(8, Pricing.Listed(10, 1, PriceCurve.Default.Factor(2.5), 1).UnitPrice); // 0.767
        Assert.Equal(1, Pricing.Listed(1, 1, 0.3, 1).UnitPrice);
    }

    private static readonly ListPriceRules Rules = new() { Sell = 1.0, Buy = 1.5, Roll = 0.25, OwnShelf = 0.2 };

    [Fact]
    public void TheShippedListPriceRulesParseAndAreSane()
    {
        var rules = ListPriceRules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "list-prices.json")));
        Assert.Empty(rules.Problems());
        Assert.Equal(1.0, rules.Sell);
        Assert.Equal(0.25, rules.Roll);
        Assert.True(rules.OwnShelf <= rules.Sell);
        Assert.Equal(ListPriceRules.Default, ListPriceRules.Parse("// none\n{ }"));
        Assert.Equal(4, new ListPriceRules { Sell = 0, Buy = -1, OwnShelf = 0, Roll = 1 }.Problems().Count);
    }

    [Fact]
    public void AListedGoodIsItsValueTimesTheSellOrBuyFactor()
    {
        var iron = new TradeEntry { Code = "ingot-iron", StackSize = 2 };
        Assert.Equal(20, Pricing.ListBase(iron, 10, traderBuys: false, roll: 1, Rules));
        Assert.Equal(30, Pricing.ListBase(iron, 10, traderBuys: true, roll: 1, Rules));
        // The roll scales both sides alike.
        Assert.Equal(25, Pricing.ListBase(iron, 10, traderBuys: false, roll: 1.25, Rules));
        Assert.Equal(22.5, Pricing.ListBase(iron, 10, traderBuys: true, roll: 0.75, Rules));
        // No value, no price: the slot keeps its placeholder.
        Assert.Null(Pricing.ListBase(iron, 0, traderBuys: false, roll: 1, Rules));
    }

    [Fact]
    public void AnOverrideIsItsOwnPriceUnrolledOnBothSides()
    {
        var schematic = new TradeEntry { Code = "seraphhorizons:schematic-gearbox", Price = 45, PriceReason = "a gate" };
        Assert.Equal(45, Pricing.ListBase(schematic, 0, traderBuys: false, roll: 1.2, Rules));
        Assert.Equal(45, Pricing.ListBase(schematic, 99, traderBuys: true, roll: 0.8, Rules));
    }

    [Fact]
    public void ARollIsWithinItsSpreadEitherWay()
    {
        var rng = new Random(7);
        var rolls = Enumerable.Range(0, 2000).Select(_ => Pricing.Roll(rng, 0.25)).ToList();
        Assert.All(rolls, r => Assert.InRange(r, 0.75, 1.25));
        Assert.True(rolls.Min() < 0.77 && rolls.Max() > 1.23);
        Assert.InRange(rolls.Average(), 0.98, 1.02);
        Assert.Equal(1, Pricing.Roll(rng, 0));
    }

    [Fact]
    public void ATraderBuysBackWhatItSellsItselfAtTheOwnShelfRateFromTheSideBudget()
    {
        var o = Pricing.OwnShelf(valuePerItem: 10, worthless: false, rate: 0.2, supply: 1, modifiers: 1, maxStackSize: 64);
        Assert.True(o.Accepted);
        Assert.True(o.OwnShelf);
        Assert.Equal(2, o.UnitPrice);
        Assert.Equal(1, o.UnitSize);
        // The rate is the whole share: no buy spread on top.
        Assert.Equal(0.2, o.Fit);
        Assert.Equal(1, o.Spread);
        Assert.Equal(Budget.Side, o.Budget);
        // Cheap goods by the fewest worth a gear, as off-list goods; worthless ones refused.
        Assert.Equal(10, Pricing.OwnShelf(0.5, false, 0.2, 1, 1, 64).UnitSize);
        Assert.Equal(Refusal.Worthless, Pricing.OwnShelf(5, true, 0.2, 1, 1, 64).Refusal);
    }

    [Fact]
    public void AnOffListOfferIsPaidFromTheBudgetItIsGiven()
    {
        Assert.Equal(Budget.Side, Pricing.OffList(10, false, 0.75, 1, 1, 64).Budget);
        Assert.Equal(Budget.Main, Pricing.OffList(10, false, 0.75, 1, 1, 64, budget: Budget.Main).Budget);
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
