using SeraphHorizons.Mod.Trading.Core;
using static SeraphHorizons.Mod.Tests.Trading.TradeListTests;

namespace SeraphHorizons.Mod.Tests.Trading;

public class RestockTests
{
    private static readonly ResolvedSide Side = new(
        [E("coke"), E("ingot-iron", ps: true), E("charcoal")],
        [E("borax"), E("flux"), E("chisel", ps: true), E("anvil", ps: true), E("rope"), E("sand"), E("lime")],
        3);

    private static SlotState[] Empty => Enumerable.Repeat(new SlotState(null, false), 16).ToArray();

    private static SlotState[] From(SlotPlan[] plan, Func<int, bool>? inStock = null) =>
        plan.Select((p, i) => new SlotState(p.Entry?.Key, p.Entry != null && (inStock?.Invoke(i) ?? true))).ToArray();

    [Fact]
    public void TheCoreIsAlwaysStockedFirstAndPlayerSuppliedOnlyWithSupply()
    {
        var plan = RestockPlanner.Plan(Side, Empty, 1.1, new Random(1), _ => 0);
        Assert.Equal(16, plan.Length);
        Assert.Equal(["item:game:coke", "item:game:charcoal"], plan.Take(2).Select(p => p.Entry!.Key));
        Assert.DoesNotContain(plan, p => p.Entry?.PlayerSupplied == true);
        Assert.Equal(5, plan.Count(p => !p.IsEmpty));

        var supplied = RestockPlanner.Plan(Side, Empty, 1.1, new Random(1), e => e.Code == "ingot-iron" ? 6 : 0);
        Assert.Equal("item:game:ingot-iron", supplied[1].Entry!.Key);
        Assert.Equal(6, supplied[1].Stock);
        Assert.Null(supplied[0].Stock);
    }

    [Fact]
    public void ABuyingSideHasNoGate()
    {
        var plan = RestockPlanner.Plan(Side, Empty, 1.1, new Random(3), null);
        Assert.Contains(plan, p => p.Entry?.Key == "item:game:ingot-iron");
        Assert.Equal(6, plan.Count(p => !p.IsEmpty));
    }

    [Fact]
    public void RotatingSlotsAreDrawnWithoutRepeats()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = RestockPlanner.Plan(Side, Empty, 1.1, new Random(seed), _ => 0);
            var keys = plan.Where(p => !p.IsEmpty).Select(p => p.Entry!.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.Equal(3, plan.Skip(2).Count(p => !p.IsEmpty));
        }
        // Over many draws every unguarded rotating entry comes up.
        var seen = Enumerable.Range(0, 200).SelectMany(s => RestockPlanner.Plan(Side, Empty, 1.1, new Random(s), _ => 0))
            .Where(p => !p.IsEmpty).Select(p => p.Entry!.Code).ToHashSet();
        Assert.Equal(5, seen.Intersect(new[] { "borax", "flux", "rope", "sand", "lime" }).Count());
    }

    [Fact]
    public void AWeeklyRestockKeepsRotatingGoodsStillInStockAtTheRefreshChance()
    {
        var first = RestockPlanner.Plan(Side, Empty, 1.1, new Random(4), _ => 0);
        // Nothing redrawn: every rotating slot still in stock stays, as it was.
        var kept = RestockPlanner.Plan(Side, From(first), 0, new Random(5), _ => 0);
        var before = first.Where(p => !p.IsEmpty).Skip(2).Select(p => p.Entry!.Key).ToList();
        var after = kept.Where(p => !p.IsEmpty).Skip(2).Select(p => p.Entry!.Key).ToList();
        Assert.Equal(before, after);
        Assert.All(kept.Where(p => !p.IsEmpty).Skip(2), p => Assert.NotNull(p.KeptFrom));
        // The core is never kept: it is restocked fresh.
        Assert.All(kept.Take(2), p => Assert.Null(p.KeptFrom));
        // Sold out: replaced even when nothing is meant to be redrawn.
        var soldOut = RestockPlanner.Plan(Side, From(first, _ => false), 0, new Random(5), _ => 0);
        Assert.All(soldOut.Where(p => !p.IsEmpty), p => Assert.Null(p.KeptFrom));
        // At 0.5 about half stay over many restocks.
        int stayed = 0;
        for (int s = 0; s < 400; s++)
            stayed += RestockPlanner.Plan(Side, From(first), 0.5, new Random(s), _ => 0).Count(p => p.KeptFrom != null);
        Assert.InRange(stayed / 400.0, 1.2, 1.8);
    }

    [Fact]
    public void AnEntryTheGameRefusesIsSkipped()
    {
        var plan = RestockPlanner.Plan(Side, Empty, 1.1, new Random(2), _ => 0, e => e.Code != "coke" && e.Code != "rope");
        Assert.DoesNotContain(plan, p => p.Entry?.Code is "coke" or "rope");
        Assert.Equal(3, plan.Count(p => p.Entry != null && p.Entry.Code != "charcoal"));
    }

    [Fact]
    public void NoSupplyNeverStocks()
    {
        var context = new TraderContext("smith", new Region(Region.Cold, Region.Igneous), 1, 0, 0);
        Assert.Equal(0, NoSupply.Instance.Stock(context, E("ingot-iron", ps: true)));
    }
}
