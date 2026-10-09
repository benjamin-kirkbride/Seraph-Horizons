using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Orders.Core;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Orders;

/// <summary>
/// Standing orders (#453), server side, switch <see cref="SeraphHorizonsConfig.TraderOrders"/>.
///
/// <list type="bullet">
/// <item>At every restock (<see cref="EntitySeraphTrader.Restocked"/>) a trader tops its offers
/// up to 2n (<see cref="OrderPlanner.PerWeek"/>, n its shelf tier's number), each for an item its
/// list buys in its region that has a value. Nothing comes out of its wallet.</item>
/// <item>A player takes one in the trade window's Orders tab (<see cref="Accept"/>): their own
/// tier sizes it and sets its payout (<see cref="OrderPlanner.Terms"/>). Items count when handed
/// in from the Orders tab (<see cref="HandIn"/>); each pays its share of the payout, new money, and
/// completion the rest and standing. Selling the goods to the trader is a sale, not a delivery.</item>
/// <item>Past the deadline (<see cref="Tick"/>, every few seconds and every simulated day) an
/// untaken offer lapses, a taken order with nothing delivered is abandoned (standing lost), one
/// delivered in part just expires. An order taken before payouts were new money gives what is left
/// of its reserve back to the trader's wallet if it is loaded.</item>
/// </list>
///
/// Saved with the world (<see cref="SaveKey"/>). ExecuteOrder after the economy (0.65), whose
/// restock handler prices the shelves first and whose <c>simulate</c> raises
/// <see cref="EconomySystem.SimulatedDay"/>.
/// </summary>
public class OrdersSystem : ModSystem
{
    public const string SaveKey = "seraphhorizons:orders";

    private ICoreServerAPI? _sapi;
    private TradingSystem? _trading;
    private EconomySystem? _economy;
    private long _tick;

    public static OrdersSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<OrdersSystem>();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override double ExecuteOrder() => 0.66;

    public bool Enabled { get; private set; }

    public OrderBook Book { get; private set; } = new();

    private double Today => _sapi!.World.Calendar.TotalDays;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (!SeraphHorizonsSystem.ConfigFor(api).TraderOrders)
        {
            api.Logger.Notification("[seraphhorizons] Trader orders: off (TraderOrders in ModConfig/{0})", SeraphHorizonsSystem.ConfigFile);
            return;
        }
        _trading = TradingSystem.Of(api);
        if (_trading is null)
        {
            api.Logger.Warning("[seraphhorizons] Trader orders: no trading system; off");
            return;
        }
        _economy = EconomySystem.Of(api);
        Enabled = true;
        api.Event.SaveGameLoaded += Load;
        api.Event.GameWorldSave += Save;
        EntitySeraphTrader.Restocked += OnRestocked;
        if (_economy != null) _economy.SimulatedDay += OnSimulatedDay;
        _tick = api.Event.RegisterGameTickListener(_ => Tick(), 5000);
        OrderCommands.Register(api, this);
        api.Logger.Notification("[seraphhorizons] Trader orders: on");
    }

    public override void Dispose()
    {
        EntitySeraphTrader.Restocked -= OnRestocked;
        if (_economy != null) _economy.SimulatedDay -= OnSimulatedDay;
        if (_sapi != null && _tick != 0) _sapi.Event.UnregisterGameTickListener(_tick);
    }

    private void Load()
    {
        var api = _sapi!;
        try
        {
            var json = api.WorldManager.SaveGame.GetData<string>(SaveKey);
            Book = json is null ? new OrderBook() : OrderBook.FromJson(json);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Trader orders: the saved orders do not load ({0}); starting afresh, the old data stays under {1}.broken",
                e.Message, SaveKey);
            api.WorldManager.SaveGame.StoreData(SaveKey + ".broken", api.WorldManager.SaveGame.GetData(SaveKey));
            Book = new OrderBook();
        }
    }

    private void Save() => _sapi!.WorldManager.SaveGame.StoreData(SaveKey, Book.ToJson());

    private void OnSimulatedDay(int day)
    {
        Book.Advance(1);
        Tick();
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    // ---- Making orders ----

    /// <summary>What the trader would order: its list's buying side for its region, plain stacks
    /// whose item has a value (<see cref="ItemValuesSystem"/>), at that value per item.</summary>
    public List<OrderCandidate> Candidates(EntitySeraphTrader trader)
    {
        if (_trading?.Lists?.For(trader.TraderType) is not { } def) return [];
        var values = ItemValuesSystem.For(trader.Api);
        var side = TradeListResolver.Resolve(def, trader.Region).Buying;
        var list = new List<OrderCandidate>();
        foreach (var e in side.Core.Concat(side.Rotating))
        {
            if (e.AttributesKey.Length > 0) continue;
            string code = BuyerIndex.FullCode(e.Code);
            if (list.Any(c => c.Item == code) || TraderFinder.Collectible(trader.World, code) is not { } c) continue;
            double value = values.ValueOf(code);
            if (value > 0) list.Add(new OrderCandidate(code, value, Math.Max(1, c.MaxStackSize)));
        }
        return list;
    }

    private void OnRestocked(EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi || !Enabled) return;
        string id = TraderFinder.IdOf(_sapi!, trader);
        int target = OrderPlanner.PerWeek(_trading!.Standing.ShelfTierFor(trader) + 1);
        var candidates = Candidates(trader);
        while (Book.OpenAt(id).Count(o => o.State == OrderState.Offered) < target)
        {
            var onOrder = Book.OpenAt(id).Select(o => o.Item).ToHashSet();
            if (OrderPlanner.Pick(candidates, onOrder, trader.World.Rand.NextDouble()) is not { } c) break;
            Book.Offer(id, trader.TraderType, c, trader.World.Rand.NextDouble(), OrderPlanner.Days(trader.World.Rand.NextDouble()), Today);
        }
    }

    // ---- Taking and delivering ----

    /// <summary>The player's standing tier at the trader as a number, 1 (stranger) to 5 (partner);
    /// 1 with standing off.</summary>
    public int TierFor(IPlayer player, EntitySeraphTrader trader)
    {
        var standing = _trading!.Standing;
        return standing.Enabled ? standing.TierFor(player, trader) + 1 : 1;
    }

    public int MaxStackOf(string code) => TraderFinder.Collectible(_sapi!.World, code)?.MaxStackSize ?? 64;

    /// <summary>The player takes an offer at the trader; the error's lang key, or null.</summary>
    public string? Accept(IServerPlayer player, EntitySeraphTrader trader, int orderId)
    {
        string id = TraderFinder.IdOf(_sapi!, trader);
        if (Book.Get(orderId) is not { State: OrderState.Offered } o || o.TraderId != id) return "trading-orders-notoffered";
        // An offer made before payouts were new money gives its reserve back.
        int reserve = o.Reserved;
        Book.Accept(orderId, player.PlayerUID, player.PlayerName, TierFor(player, trader), MaxStackOf(o.Item), Today);
        if (reserve > 0) TraderFinder.ReturnToWallet(trader, reserve);
        return null;
    }

    /// <summary>The player's own slots (hotbar and backpack) holding <paramref name="code"/>, fresh:
    /// never a worn bag (the backpack inventory's bag slots) nor a bag with anything in it, so an
    /// order for sacks never takes the one holding the player's goods.</summary>
    public static List<ItemSlot> SlotsWith(IPlayer player, string code)
    {
        var slots = new List<ItemSlot>();
        foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            if (player.InventoryManager.GetOwnInventory(name) is { } inv)
                foreach (var slot in inv)
                    if (Offerable(player, slot, code)) slots.Add(slot);
        return slots;
    }

    private static bool Offerable(IPlayer player, ItemSlot? slot, string code)
    {
        if (slot is null or ItemSlotBackpack || slot.Itemstack is not { } stack || stack.Collectible.Code.ToString() != code) return false;
        if (!stack.Collectible.IsReasonablyFresh(player.Entity.World, stack)) return false;
        return stack.Collectible.GetCollectibleInterface<IHeldBag>() is not { } bag || bag.IsEmpty(stack);
    }

    /// <summary>How many of <paramref name="code"/> the player carries (<see cref="SlotsWith"/>).</summary>
    public static int Carried(IPlayer player, string code) => SlotsWith(player, code).Sum(s => s.StackSize);

    /// <summary>Hands what the player carries of order <paramref name="orderId"/>'s item over towards
    /// it (the Orders tab's Hand in), up to what is still wanted; the error's lang key and its
    /// arguments, or null. An order taken before payouts were new money also pays the goods at its
    /// price from the wallet, as it did then.</summary>
    public (string Key, object[] Args)? HandIn(IServerPlayer player, EntitySeraphTrader trader, int orderId)
    {
        string id = TraderFinder.IdOf(_sapi!, trader);
        if (Book.Get(orderId) is not { State: OrderState.Accepted } o || o.TraderId != id || o.PlayerUid != player.PlayerUID)
            return ("trading-orders-handin-notyours", [orderId]);
        if (Today > o.Deadline) return ("trading-orders-handin-late", []);
        var slots = SlotsWith(player, o.Item);
        int carried = slots.Sum(s => s.StackSize);
        if (carried <= 0) return ("trading-orders-handin-none", [TraderFinder.ItemName(_sapi!.World, o.Item)]);
        int n = Math.Min(carried, o.Remaining);
        int price = o.Reserved > 0 ? (int)Math.Round(n * o.Value) : 0;
        if (trader.Inventory.GetTraderAssets() < price) return ("trading-orders-handin-broke", [price]);
        if (Book.Deliver(o.Id, player.PlayerUID, n, Today) is not { } change) return ("trading-orders-handin-none", [TraderFinder.ItemName(_sapi!.World, o.Item)]);
        int left = change.Taken;
        foreach (var slot in slots)
        {
            if (left <= 0) break;
            int take = Math.Min(left, slot.StackSize);
            slot.TakeOut(take);
            slot.MarkDirty();
            left -= take;
        }
        if (price > 0)
        {
            TraderFinder.TakeFromWallet(trader, price);
            TraderFinder.GiveGears(_sapi!, player.Entity, price);
        }
        Settle(change, player, price);
        return null;
    }

    /// <summary>Pays a delivery's share of the payout and, on completion, standing; tells the player.</summary>
    public void Settle(OrderChange change, IServerPlayer? player, int pricePaid = 0)
    {
        var o = change.Order;
        if (player?.Entity != null) TraderFinder.GiveGears(_sapi!, player.Entity, change.PayoutToPlayer);
        if (change.Completed && o.PlayerUid != null) _trading!.Standing.OnOrderDone(o.PlayerUid, o.TraderId);
        if (player is null) return;
        string item = TraderFinder.ItemName(_sapi!.World, o.Item);
        string msg = change.Completed
            ? L("trading-orders-done", o.Id, o.Quantity, item, change.PayoutToPlayer)
            : L("trading-orders-progress", o.Id, change.Taken, item, o.Delivered, o.Quantity, change.PayoutToPlayer);
        if (pricePaid > 0) msg += " " + L("trading-orders-paid", pricePaid);
        player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
    }

    // ---- Time ----

    public void Tick()
    {
        if (!Enabled || _sapi is null) return;
        foreach (var change in Book.Tick(Today)) Close(change);
    }

    /// <summary>Settles an order closed by time or by an admin: an old order's unpaid reserve back
    /// to the trader if it is loaded, standing for an abandoned one.</summary>
    public void Close(OrderChange change)
    {
        var o = change.Order;
        if (change.RefundToTrader > 0 && TraderFinder.ById(_sapi!, o.TraderId) is { } trader)
            TraderFinder.ReturnToWallet(trader, change.RefundToTrader);
        if (o.PlayerUid is null || change.From != OrderState.Accepted) return;
        if (change.Abandoned) _trading!.Standing.OnOrderAbandoned(o.PlayerUid, o.TraderId);
        if (_sapi!.World.PlayerByUid(o.PlayerUid) is IServerPlayer { ConnectionState: EnumClientState.Playing } p)
        {
            string item = TraderFinder.ItemName(_sapi.World, o.Item);
            string key = change.To switch
            {
                OrderState.Abandoned => "trading-orders-abandoned",
                OrderState.Cancelled => "trading-orders-cancelled",
                _ => "trading-orders-expired",
            };
            p.SendMessage(GlobalConstants.GeneralChatGroup, L(key, o.Id, item, o.Delivered, o.Quantity), EnumChatType.Notification);
        }
    }
}
