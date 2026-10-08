using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Economy (#450, #451) against the pinned mods: a trader of ours
/// buys goods off its list from its side budget, refuses worthless goods and money, every deal moves
/// the regional supply level, simulated days bring it back down, and player-supplied goods reach the
/// shelf once supply is high enough. Deals go through vanilla's own <c>InventoryTrader.TryBuySell</c>
/// (what the dialog's Buy / Sell button sends), with the goods put straight into the selling cart.
/// </summary>
public partial class TradingScenarios
{
    private const string Iron = "game:ingot-iron";

    /// <summary>The customer, as a seller standing wherever it is: these deals go straight through
    /// <c>TryBuySell</c>.</summary>
    private async Task<IServerPlayer> Seller() => (IServerPlayer)(await Customer()).Player;

    private ItemStack Stack(string code, int size)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = (CollectibleObject?)W.GetItem(loc) ?? W.GetBlock(loc);
        Assert.True(c != null && c.Id != 0, $"no {code}");
        return new ItemStack(c!, size);
    }

    private static EnumTransactionResult Deal(InventoryTrader inv, IPlayer player) =>
        (EnumTransactionResult)AccessTools.Method(typeof(InventoryTrader), "TryBuySell").Invoke(inv, [player])!;

    private static double PerItem(Offer o) => o.UnitPrice / (double)o.UnitSize;

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_trader_buys_goods_off_its_list_from_its_side_budget_and_supply_rises_then_decays()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", 30, -30);
        var player = await Seller();
        var inv = trader.Inventory;
        string region = EconomySystem.RegionOf(trader);
        Assert.True(EconomySystem.IsPriced(trader), "the trader is not priced");
        // A quarter of the general store's 80 gears.
        Assert.Equal(20, EconomySystem.SideBudgetOf(trader));

        var ingots = Stack(Iron, 16);
        Assert.Null(TradeListsBuy(trader, Iron));
        var before = Economy.QuoteOffList(trader, ingots);
        output.WriteLine($"offer for iron at the general store: {before}");
        Assert.True(before.Accepted, before.Refusal.ToString());
        Assert.Equal(Budget.Side, before.Budget);
        Assert.Equal(0.5, before.Fit, 3);
        Assert.Equal(Pricing.DefaultBuySpread, before.Spread, 3);
        Assert.True(before.UnitPrice > 0);
        Assert.True(inv.IsTraderInterestedIn(ingots), "the selling cart would not take an off-list good");
        Assert.Contains("side budget", EconomyPatches.Describe(inv, ingots));

        int mainBefore = inv.GetTraderAssets(), gearsBefore = Gears(player);
        inv.GetSellingCartSlot(0).Itemstack = ingots;
        var result = Deal(inv, player);
        Assert.Equal(EnumTransactionResult.Success, result);
        int paid = Gears(player) - gearsBefore;
        output.WriteLine($"paid {paid}, side budget now {EconomySystem.SideBudgetOf(trader)}, main {inv.GetTraderAssets()} (was {mainBefore})");
        int units = 16 / before.UnitSize;
        Assert.Equal(units * before.UnitPrice, paid);
        Assert.Equal(20 - paid, EconomySystem.SideBudgetOf(trader));
        Assert.Equal(mainBefore, inv.GetTraderAssets());

        // Supply rose by the ingots' value in ten-gear levels, and the price fell.
        double level = Economy.Supply.Level(region, Iron);
        double weight = Economy.Supply.Weight(Economy.ValueOf(Iron), 16);
        Assert.Equal(units * before.UnitSize * weight, level, 6);
        var after = Economy.QuoteOffList(trader, Stack(Iron, 16));
        output.WriteLine($"level {level:0.###}, offer now {after}");
        Assert.True(after.Supply < 1);
        Assert.True(PerItem(after) < PerItem(before), $"{PerItem(after)} per ingot is not under {PerItem(before)}");

        // Twenty days later the level has mostly gone (half-life 10 days, a tenth spreading a day).
        var sim = await World.ExecuteCommand("/sh trade simulate 20");
        output.WriteLine(sim.Message);
        Assert.True(sim.Ok, sim.Message);
        double later = Economy.Supply.Level(region, Iron);
        output.WriteLine($"after 20 days: {later:0.###}");
        Assert.True(later < level * 0.25, $"{later} is not under a quarter of {level}");
        Assert.True(Economy.Supply.Level(NextTo(region), Iron) > 0 || later < 0.05, "nothing spread to the next region");
        var recovered = Economy.QuoteOffList(trader, Stack(Iron, 16));
        Assert.True(recovered.Supply > after.Supply);
        Assert.True(PerItem(recovered) >= PerItem(after));
        trader.Die(EnumDespawnReason.Removed);
        FreshSupply();
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Worthless_goods_and_money_are_refused()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", -30, 30);
        var player = await Seller();
        var inv = trader.Inventory;
        var stone = Stack("game:stone-granite", 64);
        Assert.Equal(Refusal.Worthless, Economy.QuoteOffList(trader, stone).Refusal);
        Assert.False(inv.IsTraderInterestedIn(stone));
        Assert.Equal(Refusal.Currency, Economy.QuoteOffList(trader, Stack("game:gear-rusty", 5)).Refusal);
        Assert.Contains("Not worth", EconomyPatches.Describe(inv, stone));

        int budget = EconomySystem.SideBudgetOf(trader);
        inv.GetSellingCartSlot(0).Itemstack = stone;
        Assert.Equal(EnumTransactionResult.TraderNotEnoughSupplyOrDemand, Deal(inv, player));
        Assert.Equal(budget, EconomySystem.SideBudgetOf(trader));
        inv.GetSellingCartSlot(0).Itemstack = null;
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task More_than_the_side_budget_is_refused_and_nothing_moves()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", 40, 40);
        var player = await Seller();
        var inv = trader.Inventory;
        EconomySystem.SetSideBudget(trader, 1);
        int main = inv.GetTraderAssets(), gears = Gears(player);
        inv.GetSellingCartSlot(0).Itemstack = Stack(Iron, 16);
        Assert.Equal(EnumTransactionResult.TraderNotEnoughAssets, Deal(inv, player));
        Assert.Equal(1, EconomySystem.SideBudgetOf(trader));
        Assert.Equal(main, inv.GetTraderAssets());
        Assert.Equal(gears, Gears(player));
        Assert.NotNull(inv.GetSellingCartSlot(0).Itemstack);
        inv.GetSellingCartSlot(0).Itemstack = null;
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Selling_metal_to_the_smith_puts_it_on_the_shelves_after_the_next_restock()
    {
        FreshSupply();
        var smith = await SpawnTrader("smith", -40, -40);
        var player = await Seller();
        var inv = smith.Inventory;
        string region = EconomySystem.RegionOf(smith);
        bool Shelved(string code) => inv.SellingSlots.Any(s => s.Itemstack?.Collectible.Code.ToString() == code && s.TradeItem.Stock > 0);
        Assert.False(Shelved(Iron), "a fresh smith sells iron");

        // Listed: the smith buys iron from its wallet.
        var buying = inv.GetBuyingConditionsSlot(Stack(Iron, 1));
        Assert.True(buying is not null and not OffListSlot, "the smith does not list iron");
        int price = buying!.TradeItem.Price, budget = EconomySystem.SideBudgetOf(smith);
        // Per item: a cheaper price may buy by a bigger unit (Pricing.Listed).
        static double PerItemOf(ItemSlotTrade slot) => slot.TradeItem.Price / (double)Math.Max(1, slot.TradeItem.Stack.StackSize);
        double perItem = PerItemOf(buying);
        inv.GetSellingCartSlot(0).Itemstack = Stack(Iron, 8);
        Assert.Equal(EnumTransactionResult.Success, Deal(inv, player));
        Assert.Equal(budget, EconomySystem.SideBudgetOf(smith));
        double level = Economy.Supply.Level(region, Iron);
        var now = inv.GetBuyingConditionsSlot(Stack(Iron, 1))!;
        output.WriteLine($"sold 8 iron at {price}: level {level:0.###}; buying price now {now.TradeItem.Price} per {now.TradeItem.Stack.StackSize}");
        Assert.True(level >= Economy.Supply.Settings.ShelfMinLevel, $"level {level}");
        // The pay is a fifth of value, a gear or so an ingot, so whole gears may hide the fall: the
        // factor shows it, and the price per item never rises.
        Assert.True(EconomySystem.SupplyFactor(smith, Iron) < 1, "the listed price did not fall");
        Assert.True(PerItemOf(now) <= perItem, $"{PerItemOf(now)} an ingot is over {perItem}");

        smith.Restock(1.1f);
        var shelf = inv.SellingSlots.FirstOrDefault(s => s.Itemstack?.Collectible.Code.ToString() == Iron);
        Assert.True(shelf != null, "no iron on the shelf after the restock");
        output.WriteLine($"iron on the shelf: stock {shelf!.TradeItem.Stock} at {shelf.TradeItem.Price}");
        Assert.Equal(Economy.Supply.ShelfStock(level, Economy.ValueOf(Iron), 4), shelf.TradeItem.Stock);
        // On its own shelf now: the smith no longer pays its list's price for iron, but buys it back
        // off-market at the own-shelf rate, from the side budget.
        var own = inv.GetBuyingConditionsSlot(Stack(Iron, 1));
        Assert.True(own is OffListSlot { Offer.OwnShelf: true }, $"iron on the shelf is still bought at {own?.TradeItem.Price}");
        Assert.Equal(Budget.Side, ((OffListSlot)own!).Offer.Budget);
        Assert.Equal(Economy.ListPrices.OwnShelf, ((OffListSlot)own).Offer.Fit, 3);
        Assert.Contains("sells this itself", EconomyPatches.Describe(inv, Stack(Iron, 1)));

        // An admin sets steel high: it is shelved too, with stock scaling with the level.
        string steel = "game:ingot-steel";
        Assert.False(Shelved(steel));
        var set = await World.ExecuteCommand($"/sh trade supply set {steel} 20");
        output.WriteLine(set.Message);
        Assert.True(set.Ok, set.Message);
        if (Economy.Supply.Level(region, steel) == 0) Economy.Supply.Set(region, steel, 20); // the spawn's region differs
        smith.Restock(1.1f);
        Assert.True(Shelved(steel), "no steel on the shelf after supply was set high");
        var show = await World.ExecuteCommand($"/sh trade supply {steel}");
        Assert.True(show.Ok, show.Message);
        var trace = await World.ExecuteCommand($"/sh trade supply trace {Iron}");
        output.WriteLine(trace.Message);
        Assert.True(trace.Ok, trace.Message);
        smith.Die(EnumDespawnReason.Removed);
        FreshSupply();
    }

    private TradeEntryProbe? TradeListsBuy(EntitySeraphTrader trader, string code)
    {
        var slot = trader.Inventory.GetBuyingConditionsSlot(Stack(code, 1));
        return slot is null or OffListSlot ? null : new TradeEntryProbe(slot.TradeItem.Price);
    }

    private sealed record TradeEntryProbe(int Price);

    private static string NextTo(string region)
    {
        SupplyRegion.TryParse(region, out int rx, out int rz);
        return $"{rx + 1},{rz}";
    }
}
