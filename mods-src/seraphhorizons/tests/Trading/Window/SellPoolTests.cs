using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

public class SellPoolTests
{
    private static SellLine Dirt(int slot, int items) => new(slot, items, 28, 1, Budget.Side, "off:game:soil");

    [Fact]
    public void GoodsUnderAGearInEachSlotSellPooled()
    {
        // 20 + 20 dirt at 1 g per 28: neither slot alone is a gear, together they are one.
        var lines = new[] { Dirt(36, 20), Dirt(37, 20) };
        Assert.Equal(1, SellPool.Gears(lines));
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(1, lot.Gears);
        Assert.Equal(28, lot.Items);
        Assert.Equal([(36, 20), (37, 8)], lot.Takes.Select(t => (t.Slot, t.Items)));
        Assert.Equal(1, lot.SideGears);
        Assert.Equal(0, lot.MainGears);
    }

    [Fact]
    public void UnderAWholeGearThereIsNoLot()
    {
        var lines = new[] { Dirt(36, 12) };
        Assert.Null(SellPool.NextLot(lines));
        Assert.Equal(12 / 28.0, SellPool.Value(lines), 6);
        Assert.Equal(0, SellPool.Gears(lines));
    }

    [Fact]
    public void ALotIsTheFirstGoodsUnitPriceDearestFirstAndLeavesTheRest()
    {
        // An ingot at 3 g each in the second slot sells before the dirt in the first.
        var lines = new[] { Dirt(36, 40), new SellLine(37, 2, 1, 3, Budget.Main, "slot:21", 5) };
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(3, lot.Gears);
        Assert.Equal(3, lot.MainGears);
        Assert.Equal([(37, 1)], lot.Takes.Select(t => (t.Slot, t.Items)));
        Assert.Equal(1, lot.DemandUsed["slot:21"]);
        // Everything: 6 g of ingots and 40/28 of dirt.
        Assert.Equal(7, SellPool.Gears(lines));
    }

    [Fact]
    public void TheTraderNeverPaysMoreThanTheGoodsAreWorth()
    {
        // 0.6 g an item, 3 g for 5: a lot of 3 g takes five.
        var lines = new[] { new SellLine(36, 5, 5, 3, Budget.Main, "slot:20", 9) };
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(3, lot.Gears);
        Assert.Equal(5, lot.Items);

        Assert.True(lot.Value >= lot.Gears);
    }

    [Fact]
    public void DemandCapsAListedGoodAcrossSlots()
    {
        // One unit of 4 wanted: 6 + 6 in two slots, only 4 sell.
        var lines = new[] { new SellLine(36, 6, 4, 2, Budget.Main, "slot:22", 1), new SellLine(37, 6, 4, 2, Budget.Main, "slot:22", 1) };
        var sellable = SellPool.Sellable(lines);
        Assert.Equal(4, sellable.Sum(s => s.Items));
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(2, lot.Gears);
        Assert.Equal(4, lot.Items);
        Assert.Equal(1, lot.DemandUsed["slot:22"]);
        // No demand left: nothing sells.
        Assert.Null(SellPool.NextLot([lines[0] with { Demand = 0 }]));
    }

    [Fact]
    public void AMixedLotSplitsThePayByWhatEachBudgetsGoodsAreWorth()
    {
        // 14 dirt (0.5 g, side) and a listed good at 1 g per 2 (main), 1 of it (0.5 g): one gear.
        var lines = new[] { Dirt(36, 14), new SellLine(37, 1, 2, 1, Budget.Main, "slot:20", 3) };
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(1, lot.Gears);
        Assert.Equal(1, lot.MainGears + lot.SideGears);
    }

    [Fact]
    public void TheSellOfferIsShortAndSaysWhatEverythingComesTo()
    {
        Assert.Equal(["trading-window-sell-hint"], TradeWindowModel.SellOffer([], false).Select(t => t.ToString()));
        Assert.Equal(["trading-window-sell-none"], TradeWindowModel.SellOffer([], true).Select(t => t.ToString()));
        Assert.Equal(["trading-window-sell-pays(1, 28)", "trading-window-sell-total(2)"],
            TradeWindowModel.SellOffer([Dirt(36, 40), Dirt(37, 20)], true).Select(t => t.ToString()));
        Assert.Equal(["trading-window-sell-pays(1, 28)", "trading-window-sell-under(0.42)"],
            TradeWindowModel.SellOffer([Dirt(36, 12)], true).Select(t => t.ToString()));
    }
}
