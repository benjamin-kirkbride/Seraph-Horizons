using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Orders.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The pack's trade window (mods-src/seraphhorizons/Trading/Window), its server side against the
/// pinned mods: the requests a client sends (<see cref="TradeWindowSystem.Handle"/>, as the window's
/// holds and buttons send them), the state it is sent (<see cref="TradeWindowSystem.BuildState"/>),
/// and the dialogue it is opened from. One unit bought moves gears, stock and standing; one unit sold
/// off the list is paid from the side budget and the rest of the stack stays (and goes back to the
/// player when the window closes); requests from afar, from another player or to a trader gone are
/// refused; an order taken and handed in; a delivery taken and marked on the map; every pack trader's
/// dialogue (BetterRuins' two included) has the standing option, and the reply carries the player's
/// numbers. Atlas' default world, offsets on (±20, ±55) and (±55, ±20).
/// </summary>
public partial class TradingScenarios
{
    private TradeWindowSystem WindowSystem => TradeWindowSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no TradeWindowSystem");

    private static TradeRequest Req(TradeAction action, int slot = 0, int id = 0) => new() { Action = action, Slot = slot, Id = id };

    private TradeResult Window(IServerPlayer player, EntitySeraphTrader trader, TradeRequest request)
    {
        var result = WindowSystem.Handle(player, trader, request);
        output.WriteLine($"{request.Action} {request.Slot}/{request.Id}: {(result.Ok ? "ok" : "refused")} {result.Key} {string.Join(",", result.Args)}");
        return result;
    }

    /// <summary>The player, next to the trader with the window open on it (the dialogue's opentrade).</summary>
    private async Task<IServerPlayer> Trading(Task<ITestPlayer> role, EntitySeraphTrader trader)
    {
        var p = await role;
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        var sp = (IServerPlayer)p.Player;
        Assert.True(trader.BeginTrade(sp));
        return sp;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Holding_a_good_in_the_window_buys_one_unit_for_gears_stock_and_standing()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", 20, 55);
        var sp = await Trading(Customer(), trader);
        GiveGears(sp, 60);
        var inv = trader.Inventory;
        int slot = Enumerable.Range(0, 16).First(i => inv.GetSellingSlot(i) is { Itemstack: { } s, TradeItem: { Stock: > 0, Price: > 0 and <= 60 } }
                                                      && s.Attributes.GetString("offer") is null);
        var shelf = inv.GetSellingSlot(slot);
        int price = shelf.TradeItem.Price, stock = shelf.TradeItem.Stock, unit = shelf.TradeItem.Stack.StackSize;
        string code = shelf.Itemstack.Collectible.Code.ToString();
        int gears = Gears(sp), wallet = inv.GetTraderAssets(), carried = OrdersSystem.Carried(sp, code);
        output.WriteLine($"buying {unit} × {code} for {price} (stock {stock})");

        Assert.True(Window(sp, trader, Req(TradeAction.Buy, slot)).Ok);
        Assert.Equal(gears - price, Gears(sp));
        Assert.Equal(wallet + price, inv.GetTraderAssets());
        Assert.Equal(stock - 1, shelf.TradeItem.Stock);
        Assert.Equal(carried + unit, OrdersSystem.Carried(sp, code));
        Assert.Equal(price * Standing.Rules.Points.PerGear, Standing.Ledger.Personal(sp.PlayerUID, Standing.TraderIdOf(trader))!.Points);
        // No carts: nothing is left in them.
        for (int i = 0; i < 4; i++)
        {
            Assert.Null(inv.GetBuyingCartSlot(i).Itemstack);
            Assert.Null(inv.GetSellingCartSlot(i).Itemstack);
        }

        // A slot with nothing on it, and one sold out, refuse.
        Assert.Equal("trading-window-noslot", Window(sp, trader, Req(TradeAction.Buy, 99)).Key);
        int saved = shelf.TradeItem.Stock;
        shelf.TradeItem.Stock = 0;
        Assert.Equal("trading-window-soldout", Window(sp, trader, Req(TradeAction.Buy, slot)).Key);
        shelf.TradeItem.Stock = saved;
        trader.Die(EnumDespawnReason.Removed);
    }

    private static void GiveGears(IServerPlayer player, int gears) =>
        InventoryTrader.GiveOrDrop(player.Entity, new ItemStack(player.Entity.World.GetItem(new AssetLocation("game:gear-rusty")), 1), gears, null);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Selling_in_the_window_is_one_unit_a_hold_off_the_list_from_the_side_budget()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", -20, 55);
        var sp = await Trading(Customer(), trader);
        var inv = trader.Inventory;
        Assert.True(EconomySystem.IsPriced(trader));
        Assert.Null(TradeListsBuy(trader, Iron));
        var offer = Economy.QuoteOffList(trader, Stack(Iron, 1));
        Assert.True(offer.Accepted, offer.Refusal.ToString());
        int side = EconomySystem.SideBudgetOf(trader), wallet = inv.GetTraderAssets(), gears = Gears(sp);
        output.WriteLine($"iron off the list: {offer.UnitPrice} per {offer.UnitSize}, side budget {side}");
        var sellSlot = inv[SeraphTraderInventory.SellSlot];
        sellSlot.Itemstack = Stack(Iron, offer.UnitSize * 3);

        Assert.True(Window(sp, trader, Req(TradeAction.Sell)).Ok);
        Assert.Equal(gears + offer.UnitPrice, Gears(sp));
        Assert.Equal(side - offer.UnitPrice, EconomySystem.SideBudgetOf(trader));
        Assert.Equal(wallet, inv.GetTraderAssets());
        Assert.Equal(offer.UnitSize * 2, sellSlot.Itemstack.StackSize);
        Assert.True(Economy.Supply.Level(EconomySystem.RegionOf(trader), Iron) > 0);
        Assert.Equal(offer.UnitPrice * Standing.Rules.Points.PerGear, Standing.Ledger.Personal(sp.PlayerUID, Standing.TraderIdOf(trader))!.Points);

        // An empty side budget refuses, and the stack stays.
        EconomySystem.SetSideBudget(trader, 0);
        Assert.Equal("trading-window-trader-broke", Window(sp, trader, Req(TradeAction.Sell)).Key);
        Assert.Equal(offer.UnitSize * 2, sellSlot.Itemstack.StackSize);
        // Money is refused outright.
        var stash = sellSlot.Itemstack;
        sellSlot.Itemstack = Stack("game:gear-rusty", 5);
        Assert.Equal("trading-window-sell-refused", Window(sp, trader, Req(TradeAction.Sell)).Key);
        sellSlot.Itemstack = stash;

        // Closing the window gives the rest back.
        int before = OrdersSystem.Carried(sp, Iron);
        inv.Close(sp);
        Assert.Null(sellSlot.Itemstack);
        Assert.Equal(before + offer.UnitSize * 2, OrdersSystem.Carried(sp, Iron));
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_window_refuses_from_afar_another_player_and_a_trader_gone()
    {
        FreshSupply();
        var trader = await SpawnTrader("carpenter", 20, -55);
        var sp = await Trading(Customer(), trader);
        GiveGears(sp, 30);
        int slot = Enumerable.Range(0, 16).First(i => trader.Inventory.GetSellingSlot(i) is { TradeItem.Stock: > 0 });

        var other = await Courier();
        await other.TeleportTo(trader.Pos.AsBlockPos.AddCopy(-1, 0, 0));
        var osp = (IServerPlayer)other.Player;
        Assert.False(trader.BeginTrade(osp));
        Assert.Equal(TradeGuard.Key(GuardRefusal.NotTrading), Window(osp, trader, Req(TradeAction.Buy, slot)).Key);

        var p = _players["tradecustomer"];
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(12, 0, 0));
        // Vanilla's tick drops a trading player who walks off; as if it had not yet.
        trader.WatchedAttributes.SetString("tradingPlayerUID", sp.PlayerUID);
        int gears = Gears(sp);
        Assert.Equal(TradeGuard.Key(GuardRefusal.TooFar), Window(sp, trader, Req(TradeAction.Buy, slot)).Key);
        Assert.Equal(gears, Gears(sp));

        trader.Die(EnumDespawnReason.Removed);
        Assert.Equal(TradeGuard.Key(GuardRefusal.Gone), Window(sp, trader, Req(TradeAction.Buy, slot)).Key);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_sell_slot_is_its_owners_alone()
    {
        FreshSupply();
        var trader = await SpawnTrader("smith", -55, 20);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        Assert.Equal(sp.PlayerUID, inv.OwnerUid);
        var sellSlot = inv[SeraphTraderInventory.SellSlot];
        sellSlot.Itemstack = Stack(Iron, 4);

        // Another player with the inventory open (vanilla's packet 1001 opens it to anyone).
        var other = await Courier();
        await other.TeleportTo(trader.Pos.AsBlockPos.AddCopy(-1, 0, 0));
        var osp = (IServerPlayer)other.Player;
        osp.InventoryManager.OpenInventory(inv);
        int theirGears = Gears(osp);
        // Neither vanilla's deal packet nor a sale pays them for the owner's goods.
        trader.OnReceivedClientPacket(osp, 1000, []);
        Assert.Equal(4, sellSlot.Itemstack?.StackSize);
        Assert.False(Window(osp, trader, Req(TradeAction.Sell)).Ok);
        Assert.Equal(theirGears, Gears(osp));
        // Shift-click offers them nothing; the owner the first free sell slot.
        var theirs = osp.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)![0];
        theirs.Itemstack = Stack(Iron, 2);
        var op = new ItemStackMoveOperation(W, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge) { ActingPlayer = osp };
        Assert.Null(inv.GetBestSuitedSlot(theirs, op).slot);
        op.ActingPlayer = sp;
        Assert.Same(inv[SeraphTraderInventory.SellSlot + 1], inv.GetBestSuitedSlot(theirs, op).slot);
        theirs.Itemstack = null;
        // Their closing the inventory neither takes nor drops the owner's goods.
        inv.Close(osp);
        Assert.Equal(4, sellSlot.Itemstack?.StackSize);
        Assert.Equal(0, OrdersSystem.Carried(osp, Iron));

        // A buy of something other than what the player saw is refused.
        int slot = Enumerable.Range(0, 16).First(i => inv.GetSellingSlot(i) is { TradeItem.Stock: > 0, Itemstack: not null });
        var shelf = inv.GetSellingSlot(slot);
        GiveGears(sp, 50);
        int gears = Gears(sp);
        Assert.Equal("trading-window-changed", Window(sp, trader, new TradeRequest
        {
            Action = TradeAction.Buy, Slot = slot, Code = shelf.Itemstack.Collectible.Code.ToString(), Price = shelf.TradeItem.Price + 1,
        }).Key);
        Assert.Equal("trading-window-changed", Window(sp, trader, new TradeRequest { Action = TradeAction.Buy, Slot = slot, Code = "game:nothing" }).Key);
        Assert.Equal(gears, Gears(sp));

        // The trader leaving hands the sell slot back.
        int carried = OrdersSystem.Carried(sp, Iron);
        trader.Die(EnumDespawnReason.Removed);
        await World.Ticks(2);
        Assert.Equal(carried + 4, OrdersSystem.Carried(sp, Iron));
        Assert.Null(sellSlot.Itemstack);
    }

    /// <summary>A shift-click on a hotbar slot, as the client's packet makes the server do it.</summary>
    private static void ShiftClick(IServerPlayer player, int slot)
    {
        var hotbar = player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!;
        var op = new ItemStackMoveOperation(player.Entity.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge)
        {
            ActingPlayer = player,
        };
        hotbar.ActivateSlot(slot, hotbar[slot], ref op);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Shift_click_moves_goods_into_a_sell_slot_and_never_loses_money_or_a_package()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", 55, -55);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        var hotbar = sp.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!;
        var saved = Enumerable.Range(0, 10).Select(i => hotbar[i].Itemstack).ToArray();
        try
        {
            foreach (var mode in new[] { EnumGameMode.Creative, EnumGameMode.Survival })
            {
                sp.WorldData.CurrentGameMode = mode;
                // A full hotbar and no bags: with no room left, creative's black hole was where a
                // shift-clicked stack went.
                for (int i = 0; i < 10; i++) hotbar[i].Itemstack = Stack("game:stick", 1);
                hotbar[0].Itemstack = new ItemStack(W.GetItem(SeraphHorizons.Mod.Trading.Deliveries.ItemPackage.PackageCode), 1);
                hotbar[1].Itemstack = Stack("game:gear-rusty", 7);
                hotbar[2].Itemstack = Stack(Iron, 3);
                foreach (var s in inv.SellSlotList) s.Itemstack = null;

                ShiftClick(sp, 0);
                ShiftClick(sp, 1);
                Assert.Equal("seraphhorizons:package", hotbar[0].Itemstack?.Collectible.Code.ToString());
                Assert.Equal(7, hotbar[1].Itemstack?.StackSize);
                Assert.All(inv.SellSlotList, s => Assert.Null(s.Itemstack));

                // Goods go into the first free sell slot, whole.
                ShiftClick(sp, 2);
                Assert.Null(hotbar[2].Itemstack);
                Assert.Equal(3, inv[SeraphTraderInventory.SellSlot].Itemstack?.StackSize);

                // With every sell slot taken, nothing moves.
                foreach (var s in inv.SellSlotList.Skip(1)) s.Itemstack = Stack("game:stick", 1);
                hotbar[2].Itemstack = Stack(Iron, 2);
                ShiftClick(sp, 2);
                Assert.Equal(2, hotbar[2].Itemstack?.StackSize);
                output.WriteLine($"{mode}: the package and gears stayed, the iron went into the sell slot");
            }
        }
        finally
        {
            sp.WorldData.CurrentGameMode = EnumGameMode.Survival;
            for (int i = 0; i < 10; i++) hotbar[i].Itemstack = saved[i];
            foreach (var s in inv.SellSlotList) s.Itemstack = null;
        }
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_pooled_sale_across_the_sell_slots_pays_whole_gears_and_closing_gives_the_rest_back()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", -55, -55);
        var sp = await Trading(Customer(), trader);
        var inv = (SeraphTraderInventory)trader.Inventory;
        // A good the trader takes off its list by the several items to the gear.
        string? code = null;
        Offer? offer = null;
        foreach (var c in W.Items.Cast<CollectibleObject>().Concat(W.Blocks))
        {
            if (c?.Code is null || c.Id == 0 || c.MaxStackSize < 16 || c.TransitionableProps is { Length: > 0 }) continue;
            if (inv.GetBuyingConditionsSlot(new ItemStack(c, 1)) is not OffListSlot { Offer: { Accepted: true, UnitSize: >= 3 } o }) continue;
            if (o.UnitSize > c.MaxStackSize) continue;
            code = c.Code.ToString();
            offer = o;
            break;
        }
        Assert.True(code != null, "no cheap off-list good to pool");
        int u = offer!.UnitSize, p = offer.UnitPrice;
        output.WriteLine($"{code}: {p} g per {u}");
        // Neither slot alone makes a unit; the two together do.
        inv[SeraphTraderInventory.SellSlot].Itemstack = Stack(code!, u - 1);
        inv[SeraphTraderInventory.SellSlot + 1].Itemstack = Stack(code!, u - 1);
        int gears = Gears(sp), side = EconomySystem.SideBudgetOf(trader);
        Assert.True(Window(sp, trader, Req(TradeAction.Sell)).Ok);
        Assert.Equal(gears + p, Gears(sp));
        Assert.Equal(side - p, EconomySystem.SideBudgetOf(trader));
        Assert.Null(inv[SeraphTraderInventory.SellSlot].Itemstack);
        Assert.Equal(u - 2, inv[SeraphTraderInventory.SellSlot + 1].Itemstack?.StackSize ?? 0);
        Assert.True(Economy.Supply.Level(EconomySystem.RegionOf(trader), code!) > 0);
        Assert.Equal(p * Standing.Rules.Points.PerGear, Standing.Ledger.Personal(sp.PlayerUID, Standing.TraderIdOf(trader))!.Points);

        // Under a whole gear: refused, nothing taken.
        inv[SeraphTraderInventory.SellSlot + 1].Itemstack = Stack(code!, 1);
        if (p * 1.0 / u < 1)
        {
            Assert.Equal("trading-window-sell-under", Window(sp, trader, Req(TradeAction.Sell)).Key);
            Assert.Equal(1, inv[SeraphTraderInventory.SellSlot + 1].Itemstack?.StackSize);
        }

        // Closing gives back what is left.
        int carried = OrdersSystem.Carried(sp, code!);
        inv.Close(sp);
        Assert.All(inv.SellSlotList, s => Assert.Null(s.Itemstack));
        Assert.Equal(carried + 1, OrdersSystem.Carried(sp, code!));
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_order_hand_in_never_takes_a_worn_bag()
    {
        var p = await Customer();
        var sp = (IServerPlayer)p.Player;
        var backpack = sp.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)!;
        var bagSlot = backpack.First(s => s is ItemSlotBackpack);
        bagSlot.Itemstack = Stack("game:linensack", 1);
        bagSlot.MarkDirty();
        await World.Ticks(2);
        Assert.Equal(0, OrdersSystem.Carried(sp, "game:linensack"));
        var loose = sp.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)![1];
        loose.Itemstack = Stack("game:linensack", 1);
        Assert.Equal(1, OrdersSystem.Carried(sp, "game:linensack"));
        loose.Itemstack = null;
        bagSlot.Itemstack = null;
        bagSlot.MarkDirty();
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_order_is_taken_and_handed_in_from_the_inventory_in_the_window()
    {
        FreshSupply();
        var trader = await SpawnTrader("generalstore", -20, -55);
        var sp = await Trading(Customer(), trader);
        string id = Standing.TraderIdOf(trader);
        var buying = trader.Inventory.BuyingSlots.First(s => s.TradeItem is { Stock: > 0, Price: > 0 } && s.Itemstack != null);
        string code = buying.Itemstack.Collectible.Code.ToString();
        int lot = buying.TradeItem.Stack.StackSize;
        int next = OrdersSystem.Of(Api)!.Book.NextId;
        var made = await World.ExecuteCommand($"/sh trade orders create {id} {code} {lot} 4");
        Assert.True(made.Ok, made.Message);
        var order = OrdersSystem.Of(Api)!.Book.Get(next)!;
        Assert.Contains(WindowSystem.BuildState(sp, trader).Orders, o => o.Id == order.Id && !o.Mine);

        Assert.True(Window(sp, trader, Req(TradeAction.TakeOrder, id: order.Id)).Ok);
        Assert.Equal(OrderState.Accepted, order.State);
        Assert.Equal(sp.PlayerUID, order.PlayerUid);
        // Nothing to hand in yet.
        Assert.Equal("trading-orders-handin-none", Window(sp, trader, Req(TradeAction.HandInOrder, id: order.Id)).Key);

        // The goods in the backpack or hotbar, wherever: handed in from the inventory.
        InventoryTrader.GiveOrDrop(sp.Entity, Stack(code, 1), order.Quantity, null);
        var row = WindowSystem.BuildState(sp, trader).Orders.Single(o => o.Id == order.Id);
        Assert.True(row.Mine);
        Assert.Equal(order.Quantity, row.Held);
        int gears = Gears(sp);
        int price = (int)Math.Round(order.Quantity * order.UnitPrice);
        Assert.True(Window(sp, trader, Req(TradeAction.HandInOrder, id: order.Id)).Ok);
        Assert.Equal(OrderState.Done, order.State);
        Assert.Equal(gears + price + order.Premium, Gears(sp));
        Assert.Equal(0, OrdersSystem.Carried(sp, code));
        Assert.Contains(Standing.Ledger.Personal(sp.PlayerUID, id)!.Events, e => e.Kind == StandingKinds.Order);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_delivery_is_taken_and_marked_on_the_map_in_the_window()
    {
        FreshSupply();
        var trader = await SpawnTrader("mason", 55, 20);
        var p = await Courier();
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        var sp = (IServerPlayer)p.Player;
        GiveGears(sp, 40);
        string id = Standing.TraderIdOf(trader);
        // A stranger is given no deliveries.
        Assert.True(trader.BeginTrade(sp));
        Assert.Equal("trading-deliveries-notyet", WindowSystem.BuildState(sp, trader).DeliveryWhy);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set {sp.PlayerName} {id} 100")).Ok);

        // The plain world has no camps: one placed 1.2 km east for the delivery to go to.
        var camps = TradingSystem.Of(Api)!.Camps!;
        var registry = typeof(TraderCamps).GetProperty(nameof(TraderCamps.Registry))!;
        var saved = camps.Registry;
        int cx = (int)trader.Pos.X + 1200, cz = (int)trader.Pos.Z;
        var cell = TraderGrid.CellOf(cx, cz);
        registry.SetValue(camps, new CampRegistry([new CampRecord
        {
            CellX = cell.X, CellZ = cell.Z, Status = CampStatus.Placed, Type = "cook", X = cx, Y = 110, Z = cz, Region = "temperate/sedimentary",
        }]));
        try
        {
            var state = WindowSystem.BuildState(sp, trader);
            var offer = state.DeliveryOffer;
            Assert.NotNull(offer);
            output.WriteLine($"offer: to the {offer!.ToType}, {offer.Distance:0} m, {offer.Hours:0.#} h, deposit {offer.Deposit}, fee {offer.Fee}");
            Assert.Equal("cook", offer.ToType);
            Assert.Equal("trading-window-dir-e", TradeWindowModel.Direction(offer.Dx, offer.Dz).Key);

            var layer = Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single();
            int marks = layer.Waypoints.Count(w => w.OwningPlayerUid == sp.PlayerUID);
            Assert.True(Window(sp, trader, Req(TradeAction.MarkDelivery)).Ok);
            Assert.Equal(marks + 1, layer.Waypoints.Count(w => w.OwningPlayerUid == sp.PlayerUID));

            int gears = Gears(sp);
            int next = Deliveries.Book.NextId;
            Assert.True(Window(sp, trader, Req(TradeAction.TakeDelivery)).Ok);
            var d = Deliveries.Book.Get(next)!;
            Assert.Equal(DeliveryState.Active, d.State);
            Assert.Equal(TraderIds.Camp(cell.X, cell.Z), d.To);
            Assert.Equal(gears - offer.Deposit, Gears(sp));
            Assert.NotNull(PackageSlot(sp, d.Id));
            // One at a time from this sender: the offer is gone and the delivery listed.
            var after = WindowSystem.BuildState(sp, trader);
            Assert.Null(after.DeliveryOffer);
            Assert.Contains(after.Deliveries, r => r.Id == d.Id && !r.ForHere && r.Carried);
            Assert.False(Window(sp, trader, Req(TradeAction.TakeDelivery)).Ok);
            Assert.NotNull(Deliveries.Book.Fail(d.Id, W.Calendar.TotalDays));
        }
        finally
        {
            registry.SetValue(camps, saved);
        }
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Every_pack_trader_talks_with_the_standing_option_and_the_reply_carries_the_numbers()
    {
        // Every pack trader's dialogue, BetterRuins' quest dialogues for the curio dealer and the
        // farmer included, has the option (on the standing variable) and its answer.
        var paths = new Dictionary<string, string>();
        foreach (var type in W.EntityTypes.Where(t => t.Code.Domain == "seraphhorizons" && (t.Code.Path.StartsWith("trader-") || t.Code.Path.StartsWith("visitor-"))))
        {
            var conversable = type.Server.BehaviorsAsJsonObj.Single(b => b["code"].AsString() == "conversable");
            string? path = conversable["dialogue"].AsString();
            Assert.False(string.IsNullOrEmpty(path), $"{type.Code} has no dialogue");
            paths[type.Code.ToString()] = path!;
        }
        Assert.Equal(78, paths.Count);
        Assert.Equal("betterruins:config/dialogue/luxuries", paths["seraphhorizons:trader-male-curiodealer-temperate"]);
        Assert.Equal("betterruins:config/dialogue/agriculture", paths["seraphhorizons:trader-female-farmer-cold"]);
        Assert.Equal("seraphhorizons:config/dialogue/trader", paths["seraphhorizons:trader-male-smith-cold"]);
        Assert.Equal("seraphhorizons:config/dialogue/trader", paths["seraphhorizons:visitor-female-travellingcurio-temperate"]);
        foreach (string path in paths.Values.Distinct())
        {
            var asset = Api.Assets.Get(AssetLocation.Create(path, "seraphhorizons").WithPathAppendixOnce(".json"));
            var config = asset.ToObject<DialogueConfig>();
            var main = (DlgTalkComponent)config.components.Single(c => c.Code == "main");
            Assert.Equal("opentrade", main.Text[0].JumpTo);
            var option = main.Text.Single(t => t.JumpTo == TradeWindowSystem.StandingComponent);
            Assert.Equal("entity." + TradeWindowSystem.StandingVariable, Assert.Single(option.Conditions).Variable);
            var reply = Assert.Single(config.components, c => c.Code == TradeWindowSystem.StandingComponent);
            Assert.Equal("main", reply.JumpTo);
            output.WriteLine($"{path}: {config.components.Length} components, the standing option at {Array.IndexOf(main.Text, option)}");
        }

        var trader = await SpawnTrader("smith", 55, -20);
        Assert.Equal("on", trader.WatchedAttributes.GetTreeAttribute("variables")?.GetString(TradeWindowSystem.StandingVariable));
        var p = await Customer();
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        var sp = (IServerPlayer)p.Player;
        Assert.True((await World.ExecuteCommand($"/sh trade standing set {sp.PlayerName} {Standing.TraderIdOf(trader)} 310")).Ok);
        var state = WindowSystem.BuildState(sp, trader);
        Assert.Equal(2, state.Standing!.TierIndex);
        Assert.Equal(310, state.Standing.Points);
        Assert.Equal(5, state.Standing.Tiers.Count);
        var speech = StandingSpeech.Build(state);
        Assert.Equal("dialogue-standing-tier-regular", speech[0].Voice.Key);
        Assert.Equal("trading-window-speech-points(trading-standing-tier-regular, 310)", speech[0].Facts[0].ToString());
        Assert.Equal("trading-window-speech-next(trading-standing-tier-trusted, 800)", speech[0].Facts[1].ToString());

        // Every line the window and the reply can show is in the lang file.
        state.DeliveryOffer = new DeliveryOfferRow { ToType = "cook" };
        state.Deliveries.Add(new DeliveryRow { ToType = "cook", HoursLeft = -1 });
        state.Deliveries.Add(new DeliveryRow { ToType = "cook", ForHere = true, Carried = true });
        state.Orders.Add(new OrderRow { Item = "game:ingot-iron" });
        state.Orders.Add(new OrderRow { Item = "game:ingot-iron", Mine = true });
        state.Locked.Add(new LockedRow { Code = "game:ingot-iron", Tier = 3, Reason = LockReason.Rare, Rotating = true });
        var texts = new List<Text>();
        texts.AddRange(TradeWindowModel.Tabs(state).Select(t => t.Label));
        texts.Add(TradeWindowModel.Bar(state.Standing, state.Switches)!.Label);
        texts.Add(TradeWindowModel.Header("x", "smith", "cold", "igneous"));
        texts.AddRange(TradeWindowModel.Footer(1, "x", 1, 1, true));
        texts.AddRange(TradeWindowModel.Footer(1, "x", 1, 1, false));
        texts.AddRange(TradeWindowModel.Orders(state).Select(o => o.Line));
        texts.AddRange(TradeWindowModel.Deliveries(state).Select(d => d.Line));
        texts.AddRange(TradeWindowModel.StandingSections(state).SelectMany(s => s.Lines.Prepend(s.Heading)));
        texts.AddRange(TradeWindowModel.Tiers(state.Standing).Select(t => t.Line));
        texts.AddRange(TradeWindowModel.LockedDetails(state.Locked[0], state.Standing));
        texts.AddRange(TradeWindowModel.ShelfDetails("x", 1, 1, 0, true, false));
        texts.AddRange(TradeWindowModel.ShelfDetails("x", 1, 1, 1, false, false));
        texts.AddRange(TradeWindowModel.OfferLines(Pricing.OffList(10, false, 0.5, 1, 1.1, 64), false, 3, 0));
        var dirt = new SellLine(36, 20, 28, 1, Budget.Side, "off:soil");
        texts.AddRange(TradeWindowModel.SellOffer([], false));
        texts.AddRange(TradeWindowModel.SellOffer([], true));
        texts.AddRange(TradeWindowModel.SellOffer([dirt], true));
        texts.AddRange(TradeWindowModel.SellOffer([dirt, dirt with { Slot = 37 }], true));
        texts.AddRange(new[] { "trading-window-sell-wontbuy", "trading-window-sell-breakdown", "trading-window-sell-never", "trading-window-sell-under" }.Select(k => new Text(k)));
        texts.Add(TradeWindowModel.OreMapLine("copper", null, 10, 1));
        texts.Add(TradeWindowModel.OreMapLine("iron", "large", 10, 3));
        texts.Add(TradeWindowModel.LeadLine("camp", "cook", 10, TradeWindowModel.Direction(1, 1)));
        texts.Add(TradeWindowModel.LeadLine("settlement", "", 10, TradeWindowModel.Direction(-1, 0)));
        foreach (var tier in state.Standing.Tiers)
        {
            state.Standing.TierIndex = state.Standing.Tiers.IndexOf(tier);
            texts.AddRange(StandingSpeech.Build(state).SelectMany(l => l.Facts.Prepend(l.Voice)));
        }
        var missing = Keys(texts).Where(k => !Lang.HasTranslation("seraphhorizons:" + k, findWildcarded: false, logErrors: false)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Missing lang keys: " + string.Join(", ", missing));
        trader.Die(EnumDespawnReason.Removed);
    }

    private static IEnumerable<string> Keys(IEnumerable<Text> texts)
    {
        foreach (var t in texts)
        {
            yield return t.Key;
            foreach (var key in Keys(t.Args.OfType<Text>())) yield return key;
        }
    }
}
