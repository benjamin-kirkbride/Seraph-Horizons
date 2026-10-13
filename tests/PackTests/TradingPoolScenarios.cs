using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The pooled sale for every item (#910): a trader judges a sale by everything in its four sell
/// slots together, for every item by what the slots hold of it (four stacks), with no floor per
/// stack, and the slots may hold different goods. A copper ore worth a gear only by the four slots
/// sells across them; two cheap goods neither of which is a gear sell together, each at its own
/// exact worth and each counted in its own supply; and what a trader refuses outright (money, a map,
/// a worthless good) never goes in a sell slot, while a listed good it wants no more of does. At
/// (-65, -10), (-65, 30) and (-65, -30).
/// </summary>
public partial class TradingScenarios
{
    private const string PoorCopper = "game:ore-poor-nativecopper-granite";
    private const string MediumCopper = "game:ore-medium-nativecopper-granite";
    private const string RichCopper = "game:ore-rich-nativecopper-granite";

    /// <summary>Fills the sell slots in order with full stacks of the given goods.</summary>
    private void FillSellSlots(SeraphTraderInventory inv, params (string Code, int Stacks)[] goods)
    {
        foreach (var s in inv.SellSlotList) s.Itemstack = null;
        int slot = SeraphTraderInventory.SellSlot;
        foreach (var (code, stacks) in goods)
            for (int i = 0; i < stacks; i++)
            {
                var stack = Stack(code, 1);
                stack.StackSize = stack.Collectible.MaxStackSize;
                inv[slot++].Itemstack = stack;
            }
    }

    private double PoolValue(SeraphTraderInventory inv, params (string Code, int Stacks)[] goods)
    {
        FillSellSlots(inv, goods);
        return SellPool.Value(inv.SellLines());
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_copper_ore_worth_under_a_gear_a_stack_sells_across_the_four_sell_slots()
    {
        FreshSupply();
        var trader = await SpawnTrader("mason", -65, -10);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        try
        {
            // Poor native copper (0.82 a stack of 16 in the table): bought off the list, with no
            // floor per stack, by a unit bigger than a slot, each ore at its exact worth.
            var poor = Assert.IsType<OffListSlot>(inv.GetBuyingConditionsSlot(Stack(PoorCopper, 1))).Offer;
            int stack = Stack(PoorCopper, 1).Collectible.MaxStackSize;
            output.WriteLine($"{PoorCopper}: {poor.UnitPrice} g per {poor.UnitSize}, {poor.Worth:0.####} an ore, stack {stack}");
            Assert.True(poor.Accepted);
            Assert.True(poor.UnitSize > stack, $"a unit of {poor.UnitSize}");
            Assert.Equal(SeraphTraderInventory.SellSlots * stack, EconomySystem.TradeUnit(Stack(PoorCopper, 1).Collectible));
            // Four stacks are worth what four stacks are worth, never a rounded-up gear.
            Assert.Equal(SeraphTraderInventory.SellSlots * stack * poor.Worth, PoolValue(inv, (PoorCopper, 4)), 6);

            // Medium native copper: under a gear a stack, a gear by the four slots.
            double one = PoolValue(inv, (MediumCopper, 1));
            output.WriteLine($"{MediumCopper}: a stack is worth {one:0.###}");
            Assert.True(one is > 0 and < 1, $"a stack is worth {one}");
            Assert.Equal("trading-window-sell-under", Window(sp, trader, Req(TradeAction.Sell)).Key);
            Assert.Equal(stack, inv[SeraphTraderInventory.SellSlot].Itemstack?.StackSize);

            FillSellSlots(inv, (MediumCopper, 4));
            var lines = inv.SellLines();
            Assert.Equal(4, lines.Count);
            var lot = SellPool.NextLot(lines);
            Assert.True(lot != null, $"four stacks are worth {SellPool.Value(lines):0.###}");
            Assert.True(lot!.Takes.Count > 1, "one lot takes from several slots");
            Assert.True(lot.Gears <= lot.Value);
            int gears = Gears(sp);
            Assert.True(Window(sp, trader, Req(TradeAction.Sell)).Ok);
            Assert.Equal(gears + lot.Gears, Gears(sp));
            Assert.Equal(4 * stack - lot.Items, inv.SellSlotList.Sum(s => s.Itemstack?.StackSize ?? 0));
            Assert.True(Economy.Supply.Level(EconomySystem.RegionOf(trader), MediumCopper) > 0);
        }
        finally
        {
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
            trader.Die(EnumDespawnReason.Removed);
        }
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Two_cheap_goods_mixed_in_the_sell_slots_sell_together()
    {
        FreshSupply();
        var trader = await SpawnTrader("mason", -65, 30);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        string region = EconomySystem.RegionOf(trader);
        try
        {
            // Two stacks of each, neither good a gear on its own.
            double rich = PoolValue(inv, (RichCopper, 2)), poor = PoolValue(inv, (PoorCopper, 2));
            output.WriteLine($"two stacks of rich {rich:0.###}, two of poor {poor:0.###}");
            Assert.True(rich < 1 && poor < 1 && rich + poor >= 1, $"rich {rich}, poor {poor}");
            FillSellSlots(inv, (PoorCopper, 2), (RichCopper, 2));
            Assert.Equal("trading-window-sell-total", TradeWindowModel.SellOffer(inv.SellLines(), true)[1].Key);

            var lot = SellPool.NextLot(inv.SellLines())!;
            Assert.Equal(1, lot.Gears);
            // The dearer per item first (the rich, slots 3 and 4), then as much poor as makes the gear.
            Assert.Equal([RichCopper, PoorCopper], lot.Takes.Select(t => t.Good[4..]).Distinct());
            int gears = Gears(sp), side = EconomySystem.SideBudgetOf(trader);
            Assert.True(Window(sp, trader, Req(TradeAction.Sell)).Ok);
            Assert.Equal(gears + 1, Gears(sp));
            Assert.Equal(side - lot.SideGears, EconomySystem.SideBudgetOf(trader));
            // Each good in its own supply, by its own items.
            Assert.True(Economy.Supply.Level(region, RichCopper) > 0);
            Assert.True(Economy.Supply.Level(region, PoorCopper) > 0);
            int richLeft = inv.SellSlotList.Where(s => s.Itemstack?.Collectible.Code.ToString() == RichCopper).Sum(s => s.Itemstack!.StackSize);
            int poorLeft = inv.SellSlotList.Where(s => s.Itemstack?.Collectible.Code.ToString() == PoorCopper).Sum(s => s.Itemstack!.StackSize);
            Assert.Equal(0, richLeft);
            Assert.Equal(2 * Stack(PoorCopper, 1).Collectible.MaxStackSize - lot.Takes.Where(t => t.Good == "off:" + PoorCopper).Sum(t => t.Items), poorLeft);
            // What is left is not a gear: the next hold is refused, nothing taken.
            Assert.Equal("trading-window-sell-under", Window(sp, trader, Req(TradeAction.Sell)).Key);
        }
        finally
        {
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
            trader.Die(EnumDespawnReason.Removed);
        }
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_sell_slot_never_takes_what_the_trader_refuses_outright()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", -65, -30);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        var hotbar = sp.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!;
        var saved = Enumerable.Range(0, 10).Select(i => hotbar[i].Itemstack).ToArray();
        var sellSlot = inv[SeraphTraderInventory.SellSlot];
        try
        {
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
            // Money, a map and a worthless good: refused outright, by the slot and by shift-click.
            foreach (var code in new[] { "game:gear-rusty", "seraphhorizons:oremap", "game:clutter-fishing/oldboot" })
            {
                var stack = Stack(code, 1);
                Assert.False(inv.MayPlace(stack), code);
                Assert.False(sellSlot.CanHold(new DummySlot(stack)), code);
                hotbar[0].Itemstack = stack;
                ShiftClick(sp, 0);
                Assert.Equal(code, hotbar[0].Itemstack?.Collectible.Code.ToString());
                Assert.All(inv.SellSlotList, s => Assert.Null(s.Itemstack));
            }
            // A cheap good it buys goes in.
            Assert.True(sellSlot.CanHold(new DummySlot(Stack(PoorCopper, 16))));
            // A listed good it wants no more of goes in too, and waits there unsold.
            var buying = inv.BuyingSlots.First(s => s.TradeItem?.Stack != null && !EconomySystem.OnOwnShelf(trader, s.TradeItem.Stack.Collectible));
            var listed = buying.TradeItem.Stack.Clone();
            var same = inv.BuyingSlots.Where(s => s.TradeItem?.Stack?.Collectible.Code == listed.Collectible.Code).ToList();
            var stocks = same.Select(s => s.TradeItem.Stock).ToList();
            same.ForEach(s => s.TradeItem.Stock = 0);
            try
            {
                Assert.IsNotType<OffListSlot>(inv.GetBuyingConditionsSlot(listed));
                Assert.True(sellSlot.CanHold(new DummySlot(listed)), listed.Collectible.Code.ToString());
                sellSlot.Itemstack = listed;
                Assert.Equal("trading-window-selected-nodemand", inv.RefusalOf(listed));
                Assert.Null(SellPool.NextLot(inv.SellLines()));
            }
            finally
            {
                for (int i = 0; i < same.Count; i++) same[i].TradeItem.Stock = stocks[i];
            }
        }
        finally
        {
            for (int i = 0; i < 10; i++) hotbar[i].Itemstack = saved[i];
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
            trader.Die(EnumDespawnReason.Removed);
        }
    }
}
