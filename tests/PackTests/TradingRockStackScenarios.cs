using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Rock and earth stack to 16 (#863, <see cref="RockStackSystem"/>) at a trader: a cut block is
/// judged by what the four sell slots hold of it (64), not by one slot of 16, so a block worth a gear
/// only by more than 16 is still bought, by a unit bigger than a slot, which the trade window's
/// pooled sale takes across the slots. At (-65, ±10).
/// </summary>
public partial class TradingScenarios
{
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_rock_block_worth_a_gear_only_by_the_four_sell_slots_sells_across_them()
    {
        FreshSupply();
        var trader = await SpawnTrader("mason", -65, 10);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        // A cut block the mason takes off its list by more than one slot of it to the gear.
        string? code = null;
        Offer? offer = null;
        int tried = 0;
        foreach (var b in W.Blocks)
        {
            if (b?.Code is null || b.Id == 0 || RockStackSystem.TradeStack(b) <= b.MaxStackSize) continue;
            tried++;
            if (inv.GetBuyingConditionsSlot(new ItemStack(b, 1)) is not OffListSlot { Offer: { Accepted: true } o } || o.UnitSize <= b.MaxStackSize) continue;
            code = b.Code.ToString();
            offer = o;
            break;
        }
        Assert.True(code != null, $"none of the {tried} cut blocks sells to a mason by more than a slot");
        int u = offer!.UnitSize, p = offer.UnitPrice;
        var block = W.GetBlock(new AssetLocation(code!))!;
        output.WriteLine($"{code}: {p} g per {u}, stack {block.MaxStackSize}, judged by {RockStackSystem.TradeStack(block)}");
        Assert.Equal(RockStackRules.Cap, block.MaxStackSize);
        Assert.InRange(u, RockStackRules.Cap + 1, RockStackRules.TradeStack(RockStackRules.Cap));
        // Judged by one slot, as before the trade stack, the same block would be too cheap to take.
        Assert.Equal(Refusal.TooCheap,
            Pricing.OffList(offer.Base, false, offer.Fit, offer.Supply, offer.Modifiers, block.MaxStackSize).Refusal);

        try
        {
            // Full stacks of 16, as many slots as make one unit.
            int slots = (u + RockStackRules.Cap - 1) / RockStackRules.Cap;
            for (int i = 0; i < slots; i++) inv[SeraphTraderInventory.SellSlot + i].Itemstack = Stack(code!, RockStackRules.Cap);
            Assert.True(SellPool.Gears(inv.SellLines()) >= p);
            int gears = Gears(sp);
            Assert.True(Window(sp, trader, Req(TradeAction.Sell)).Ok);
            Assert.Equal(gears + p, Gears(sp));
            int left = inv.SellSlotList.Sum(s => s.Itemstack?.StackSize ?? 0);
            Assert.Equal(slots * RockStackRules.Cap - u, left);
        }
        finally
        {
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
            trader.Die(EnumDespawnReason.Removed);
        }
    }
}
