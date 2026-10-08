using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Deliveries;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Orders.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Orders and Trading/Deliveries (#453, #454) against the pinned
/// mods: a spawned trader puts orders on offer at its first restock; an order taken in the trade
/// window (its server side, <see cref="TradeWindowSystem.Handle"/>) and filled by selling the goods
/// there pays its premium and standing; <c>/sh trade simulate</c> past the deadline abandons a taken
/// one; a delivery between two spawned traders handed in on time through the receiver's window returns
/// the deposit and a fee, and one left past its grace fails and keeps the deposit. Atlas' default world: no camps, so traders are
/// known by their entity and deliveries are made by the admin command.
/// </summary>
public partial class TradingScenarios
{
    private OrdersSystem Orders => OrdersSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no OrdersSystem");
    private DeliveriesSystem Deliveries => DeliveriesSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no DeliveriesSystem");

    /// <summary>The role's player, beside the trader (within vanilla's trading reach: a squared
    /// distance over 5 closes the trade).</summary>
    private static async Task<ITestPlayer> At(ITestPlayer player, EntitySeraphTrader trader)
    {
        await player.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        return player;
    }

    private string Id(EntitySeraphTrader trader) => Standing.TraderIdOf(trader);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_spawned_trader_puts_one_or_two_orders_on_offer_from_what_it_buys()
    {
        Assert.True(Orders.Enabled && Deliveries.Enabled);
        FreshSupply();
        var trader = await SpawnTrader("smith", 0, -25);
        var open = Orders.Book.OpenAt(Id(trader)).ToList();
        foreach (var o in open) output.WriteLine(OrderCommandsLine(o));
        Assert.InRange(open.Count, 1, 2);
        var buys = Orders.Candidates(trader).Select(c => c.Item).ToHashSet();
        Assert.All(open, o =>
        {
            Assert.Equal(OrderState.Offered, o.State);
            Assert.Contains(o.Item, buys);
            Assert.InRange(o.PremiumFactor, 1.3, 1.6);
            Assert.True(o.Reserved > 0 && o.Reserved == o.Premium);
        });
        trader.Die(EnumDespawnReason.Removed);
    }

    private string OrderCommandsLine(Order o) => OrderCommands.AdminLine(Api, o, W.Calendar.TotalDays);

    /// <summary>An order made by the admin command for one lot of the first thing on the trader's
    /// buying shelf, so a deal can fill it.</summary>
    private async Task<(Order Order, ItemSlotTrade Buying)> OrderOnShelf(EntitySeraphTrader trader, ITestPlayer at, int days)
    {
        var buying = trader.Inventory.BuyingSlots.First(s => s.TradeItem is { Stock: > 0, Price: > 0 } && s.Itemstack != null);
        string code = buying.Itemstack.Collectible.Code.ToString();
        int lot = buying.TradeItem.Stack.StackSize;
        int before = Orders.Book.NextId;
        var made = await World.ExecuteCommand($"/sh trade orders create {Id(trader)} {code} {lot} {days}");
        output.WriteLine(made.Message);
        Assert.True(made.Ok, made.Message);
        var order = Orders.Book.Get(before)!;
        var sp = (IServerPlayer)at.Player;
        Assert.True(trader.BeginTrade(sp));
        var accept = WindowSystem.Handle(sp, trader, new TradeRequest { Action = TradeAction.TakeOrder, Id = order.Id });
        Assert.True(accept.Ok, accept.Key);
        Assert.Equal(OrderState.Accepted, order.State);
        return (order, buying);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Filling_an_order_by_selling_in_the_trade_window_pays_the_premium_and_raises_standing()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", 0, 25);
        var p = await At(await Customer(), trader);
        var sp = (IServerPlayer)p.Player;
        var (order, buying) = await OrderOnShelf(trader, p, 4);
        // A stranger's orderScale is 1: the order stays one lot.
        Assert.Equal(order.BaseQuantity, order.Quantity);

        var inv = trader.Inventory;
        var offered = buying.TradeItem.Stack.Clone();
        offered.ResolveBlockOrItem(W);
        // The window's sell slot, one unit sold by a hold.
        inv[SeraphTraderInventory.SellSlot].Itemstack = offered;
        int received = buying.TradeItem.Price;
        int gearsBefore = Gears(sp);
        Assert.True(received > 0);
        var sold = WindowSystem.Handle(sp, trader, new TradeRequest { Action = TradeAction.Sell });
        Assert.True(sold.Ok, sold.Key);
        Assert.Null(inv[SeraphTraderInventory.SellSlot].Itemstack);

        Assert.Equal(OrderState.Done, order.State);
        Assert.Equal(order.Premium, order.PremiumPaid);
        Assert.Equal(gearsBefore + received + order.Premium, Gears(sp));
        var record = Standing.Ledger.Personal(sp.PlayerUID, Id(trader))!;
        Assert.Contains(record.Events, e => e.Kind == StandingKinds.Order);
        Assert.Equal(received + Standing.Rules.Points.Order, record.Points);

        var list = await World.ExecuteCommand($"/sh trade orders {sp.PlayerName}");
        output.WriteLine(list.Message);
        Assert.True(list.Ok, list.Message);
        Assert.Contains($"#{order.Id} done", list.Message);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_taken_order_left_past_its_deadline_is_abandoned_and_costs_standing()
    {
        FreshSupply();
        var trader = await SpawnTrader("carpenter", 25, 0);
        var p = await At(await Customer(), trader);
        string uid = p.Player.PlayerUID;
        Assert.True((await World.ExecuteCommand($"/sh trade standing set {p.Player.PlayerName} {Id(trader)} 100")).Ok);
        var (order, _) = await OrderOnShelf(trader, p, 2);

        var sim = await World.ExecuteCommand("/sh trade simulate 3");
        output.WriteLine(sim.Message);
        Assert.True(sim.Ok, sim.Message);
        Assert.Equal(OrderState.Abandoned, order.State);
        Assert.Equal(100 - Standing.Rules.Points.OrderAbandoned, Standing.Ledger.Personal(uid, Id(trader))!.Points);
        trader.Die(EnumDespawnReason.Removed);
    }

    private ItemSlot? PackageSlot(IPlayer player, int id) => DeliveriesSystem.PackageSlots(player, id).FirstOrDefault();

    private async Task<Delivery> Create(EntitySeraphTrader from, EntitySeraphTrader to, string player)
    {
        int before = Deliveries.Book.NextId;
        var made = await World.ExecuteCommand($"/sh trade deliveries create {Id(from)} {Id(to)} {player}");
        output.WriteLine(made.Message);
        Assert.True(made.Ok, made.Message);
        return Deliveries.Book.Get(before)!;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_delivery_handed_in_on_time_returns_the_deposit_and_pays_a_fee()
    {
        FreshSupply();
        var a = await SpawnTrader("mason", -25, 0);
        var b = await SpawnTrader("cook", 0, -40);
        var p = await At(await Courier(), a);
        var sp = (IServerPlayer)p.Player;
        await p.GiveItem("game:gear-rusty", 20);
        int start = Gears(sp);

        var d = await Create(a, b, sp.PlayerName);
        Assert.Equal(DeliveryState.Active, d.State);
        Assert.InRange(d.Deposit, 1, 30);
        Assert.Equal(start - d.Deposit, Gears(sp));
        var package = PackageSlot(sp, d.Id);
        Assert.NotNull(package);
        Assert.Equal(Id(b), package!.Itemstack.Attributes.GetString(ItemPackage.AttrTo));
        // One at a time per sender.
        Assert.False((await World.ExecuteCommand($"/sh trade deliveries create {Id(a)} {Id(b)} {sp.PlayerName}")).Ok);

        // Not at the sender.
        Assert.True(a.BeginTrade(sp));
        Assert.False(WindowSystem.Handle(sp, a, new TradeRequest { Action = TradeAction.HandInDelivery }).Ok);
        await p.TeleportTo(b.Pos.AsBlockPos.AddCopy(1, 0, 0));
        int wallet = b.Inventory.GetTraderAssets();
        Assert.True(b.BeginTrade(sp));
        // The receiver's window knows the package is for it.
        Assert.Contains(WindowSystem.BuildState(sp, b).Deliveries, r => r.Id == d.Id && r.ForHere && r.Carried);
        var handin = WindowSystem.Handle(sp, b, new TradeRequest { Action = TradeAction.HandInDelivery });
        Assert.True(handin.Ok, handin.Key);
        Assert.Equal(DeliveryState.OnTime, d.State);
        Assert.Equal(start + d.Fee, Gears(sp));
        Assert.Equal(wallet - d.Fee, b.Inventory.GetTraderAssets());
        Assert.Null(PackageSlot(sp, d.Id));
        Assert.Equal(Standing.Rules.Points.Delivery, Standing.Ledger.Personal(sp.PlayerUID, Id(a))!.Points);
        Assert.Equal(Standing.Rules.Points.Delivery, Standing.Ledger.Personal(sp.PlayerUID, Id(b))!.Points);
        a.Die(EnumDespawnReason.Removed);
        b.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_delivery_left_past_its_grace_fails_and_keeps_the_deposit()
    {
        FreshSupply();
        var a = await SpawnTrader("tailor", -40, 0);
        var b = await SpawnTrader("farmer", 40, 0);
        var p = await At(await Courier(), a);
        var sp = (IServerPlayer)p.Player;
        await p.GiveItem("game:gear-rusty", 20);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set {sp.PlayerName} {Id(a)} 200")).Ok);
        var d = await Create(a, b, sp.PlayerName);
        int afterDeposit = Gears(sp);

        // 80 m: a day to deliver (the least) and a day's grace; the third day fails it.
        var sim = await World.ExecuteCommand("/sh trade simulate 3");
        Assert.True(sim.Ok, sim.Message);
        Assert.Equal(DeliveryState.Failed, d.State);
        Assert.Equal(afterDeposit, Gears(sp));
        Assert.Equal(200 - Standing.Rules.Points.DeliveryFailed, Standing.Ledger.Personal(sp.PlayerUID, Id(a))!.Points);
        Assert.True(PackageSlot(sp, d.Id)!.Itemstack.Attributes.GetBool(ItemPackage.AttrFailed));
        await p.TeleportTo(b.Pos.AsBlockPos.AddCopy(1, 0, 0));
        Assert.True(b.BeginTrade(sp));
        Assert.False(WindowSystem.Handle(sp, b, new TradeRequest { Action = TradeAction.HandInDelivery }).Ok);
        a.Die(EnumDespawnReason.Removed);
        b.Die(EnumDespawnReason.Removed);
    }
}
