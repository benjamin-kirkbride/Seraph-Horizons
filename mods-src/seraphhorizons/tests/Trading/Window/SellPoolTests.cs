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
    public void AUnitBiggerThanASlotSellsAcrossSlots()
    {
        // Rock cut to 16 a stack, priced by 40 a gear (its trade unit is the four slots' 64): three
        // full slots are 48, one lot of 40 from the first slots in order.
        static SellLine Rock(int slot) => new(slot, 16, 40, 1, Budget.Side, "off:game:rock-granite");
        var lines = new[] { Rock(36), Rock(37), Rock(38) };
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(1, lot.Gears);
        Assert.Equal([(36, 16), (37, 16), (38, 8)], lot.Takes.Select(t => (t.Slot, t.Items)));
        Assert.Null(SellPool.NextLot(new[] { Rock(36), Rock(37) }));
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
    public void APoorOreSellsAcrossTheSlots()
    {
        // Native copper, 0.82 a stack of 16, related: 0.0384 an ore, a gear per 27 (#910). One stack
        // is not a gear; four are 2.46, two lots, each paid at the ore's exact worth.
        const double worth = 0.82 / 16 * 0.75;
        static SellLine Ore(int slot) => new(slot, 16, 27, 1, Budget.Main, "off:game:ore-poor-nativecopper-granite", Worth: worth);
        Assert.Null(SellPool.NextLot([Ore(36)]));
        var lines = new[] { Ore(36), Ore(37), Ore(38), Ore(39) };
        Assert.Equal(2, SellPool.Gears(lines));
        var lot = SellPool.NextLot(lines)!;
        Assert.Equal(1, lot.Gears);
        Assert.Equal([(36, 16), (37, 11)], lot.Takes.Select(t => (t.Slot, t.Items)));
        Assert.True(lot.Value >= lot.Gears);
    }

    [Fact]
    public void TwoCheapGoodsMixedReachAGearTogether()
    {
        // 16 ore (0.62 g) and 16 rock at 0.025 (0.4 g): neither is a gear, together they are 1.02.
        // The dearer per item goes first, then as much of the other as reaches the gear; each good
        // is its own take (its own supply line).
        var ore = new SellLine(37, 16, 27, 1, Budget.Main, "off:game:ore-poor-nativecopper-granite", Worth: 0.82 / 16 * 0.75);
        var rock = new SellLine(36, 16, 40, 1, Budget.Side, "off:game:rock-granite", Worth: 0.025);
        Assert.Null(SellPool.NextLot([ore]));
        Assert.Null(SellPool.NextLot([rock]));
        var lot = SellPool.NextLot([rock, ore])!;
        Assert.Equal(1, lot.Gears);
        Assert.Equal([(37, 16, "off:game:ore-poor-nativecopper-granite"), (36, 16, "off:game:rock-granite")],
            lot.Takes.Select(t => (t.Slot, t.Items, t.Good)));
        Assert.Equal(1, lot.MainGears + lot.SideGears);
        // The same whatever order the slots are read in.
        Assert.Equal(lot.Takes, SellPool.NextLot([ore, rock])!.Takes);
    }

    [Fact]
    public void AGoodIsPaidItsExactWorthNotItsRoundedUnitPrice()
    {
        // A whole trade unit of 64 worth 0.128, priced at a rounded-up gear: worth 0.128, no lot.
        var cheap = new SellLine(36, 64, 64, 1, Budget.Side, "off:game:sand-granite", Worth: 0.002);
        Assert.Equal(0.128, SellPool.Value([cheap]), 9);
        Assert.Null(SellPool.NextLot([cheap]));
        // Without a worth the unit price over its size is the rate, as before.
        Assert.Equal(1, SellPool.Gears([cheap with { Worth = null }]));
    }

    [Fact]
    public void EqualRatesTakeTheSlotsInOrder()
    {
        var lines = new[] { Dirt(38, 20), Dirt(36, 20), Dirt(37, 20) };
        Assert.Equal([36, 37, 38], SellPool.Ordered(lines).Select(l => l.Slot));
        Assert.Equal([(36, 20), (37, 8)], SellPool.NextLot(lines)!.Takes.Select(t => (t.Slot, t.Items)));
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
