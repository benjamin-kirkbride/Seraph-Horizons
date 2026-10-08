using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Orders.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Orders;

/// <summary>
/// Standing orders (#453), server side, switch <see cref="SeraphHorizonsConfig.TraderOrders"/>.
///
/// <list type="bullet">
/// <item>At every restock (<see cref="EntitySeraphTrader.Restocked"/>) a trader tops its open
/// orders up to one or two, each for an item its list buys in its region, at standing scale 1,
/// with its premium (<see cref="OrderPlanner"/>) taken out of its wallet then; a trader whose wallet
/// can't hold it back makes no order.</item>
/// <item>A player takes one in the trade window's Orders tab (<see cref="Accept"/>): the quantity
/// grows with their <c>orderScale</c> as far as the wallet covers the larger premium. Items count
/// when sold to the trader (<see cref="EntitySeraphTrader.Dealt"/>) or handed in from the Orders
/// tab (<see cref="HandIn"/>), which pays the normal price from the wallet; each pays its share of
/// the premium, completion the rest and standing.</item>
/// <item>Past the deadline (<see cref="Tick"/>, every few seconds and every simulated day) an
/// untaken offer lapses, a taken order with nothing delivered is abandoned (standing lost), one
/// delivered in part just expires; what is left of the premium goes back to the trader's wallet if
/// it is loaded (else it is gone: the weekly top-up refills the wallet anyway).</item>
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
        EntitySeraphTrader.Dealt += OnDealt;
        if (_economy != null) _economy.SimulatedDay += OnSimulatedDay;
        _tick = api.Event.RegisterGameTickListener(_ => Tick(), 5000);
        OrderCommands.Register(api, this);
        api.Logger.Notification("[seraphhorizons] Trader orders: on");
    }

    public override void Dispose()
    {
        EntitySeraphTrader.Restocked -= OnRestocked;
        EntitySeraphTrader.Dealt -= OnDealt;
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

    /// <summary>What the trader would order: its list's buying side for its region, plain stacks with
    /// a value, at the list's buying price per item times the region's supply factor.</summary>
    public List<OrderCandidate> Candidates(EntitySeraphTrader trader)
    {
        if (_trading?.Lists?.For(trader.TraderType) is not { } def) return [];
        var side = TradeListResolver.Resolve(def, trader.Region).Buying;
        var values = Values.ItemValuesSystem.For(trader.Api);
        var rules = EconomySystem.Of(trader.Api)?.ListPrices ?? ListPriceRules.Default;
        var list = new List<OrderCandidate>();
        foreach (var e in side.Core.Concat(side.Rotating))
        {
            if (e.AttributesKey.Length > 0) continue;
            string code = BuyerIndex.FullCode(e.Code);
            // The list's buying price before its roll (Pricing.ListBase: value × the buy factor, or an override).
            // The table's value as the shelf prices it (Lookup, so a good under a gear a stack has one too).
            var lookup = values.Lookup(code);
            double value = lookup.Source == Values.Core.ValueSource.Missing ? 0 : lookup.Value;
            if (Pricing.ListBase(e, value, traderBuys: true, roll: 1, rules) is not double price || price <= 0) continue;
            if (TraderFinder.Collectible(trader.World, code) is not { } c) continue;
            double unit = price / Math.Max(1, e.StackSize) * EconomySystem.SupplyFactor(trader, code);
            list.Add(new OrderCandidate(code, unit, Math.Max(1, e.StackSize), Math.Max(1, c.MaxStackSize)));
        }
        return list;
    }

    private void OnRestocked(EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi || !Enabled) return;
        string id = TraderFinder.IdOf(_sapi!, trader);
        int target = OrderPlanner.TargetOpen(trader.World.Rand.NextDouble());
        var candidates = Candidates(trader);
        while (Book.OpenAt(id).Count() < target)
        {
            var onOrder = Book.OpenAt(id).Select(o => o.Item).ToHashSet();
            if (OrderPlanner.Pick(candidates, onOrder, trader.World.Rand.NextDouble()) is not { } c) break;
            if (MakeOffer(trader, id, c, OrderPlanner.Quantity(c.UnitPrice, c.Lot, c.MaxStack, 1),
                    OrderPlanner.Factor(trader.World.Rand.NextDouble()), OrderPlanner.Days(trader.World.Rand.NextDouble())) is null)
                break;
        }
    }

    /// <summary>An offer at the trader, its premium held back from the wallet; null when the wallet
    /// can't hold it.</summary>
    public Order? MakeOffer(EntitySeraphTrader trader, string id, OrderCandidate c, int quantity, double factor, double days)
    {
        int premium = OrderPlanner.Premium(quantity, c.UnitPrice, factor);
        if (trader.Inventory is null || trader.Inventory.GetTraderAssets() < premium) return null;
        TraderFinder.TakeFromWallet(trader, premium);
        return Book.Offer(id, trader.TraderType, c, quantity, factor, days, Today);
    }

    // ---- Taking and delivering ----

    public double ScaleFor(IPlayer player, EntitySeraphTrader trader)
    {
        var standing = _trading!.Standing;
        return standing.Enabled ? standing.UnlocksFor(player, trader).OrderScale : 1;
    }

    /// <summary>The player takes an offer at the trader; the error's lang key, or null.</summary>
    public string? Accept(IServerPlayer player, EntitySeraphTrader trader, int orderId)
    {
        string id = TraderFinder.IdOf(_sapi!, trader);
        if (Book.Get(orderId) is not { State: OrderState.Offered } o || o.TraderId != id) return "trading-orders-notoffered";
        double scale = ScaleFor(player, trader);
        if (scale <= 0) return "trading-orders-notyet";
        int maxStack = TraderFinder.Collectible(_sapi!.World, o.Item)?.MaxStackSize ?? 64;
        var (qty, premium) = OrderPlanner.Scaled(o, scale, maxStack, trader.Inventory.GetTraderAssets());
        TraderFinder.TakeFromWallet(trader, premium - o.Reserved);
        Book.Accept(orderId, player.PlayerUID, player.PlayerName, qty, premium, Today);
        return null;
    }

    /// <summary>Counts goods the player sold to the trader towards their orders here.</summary>
    private void OnDealt(IServerPlayer player, EntitySeraphTrader trader, IReadOnlyList<ItemStack> sold)
    {
        if (trader.Api != _sapi || !Enabled) return;
        string id = TraderFinder.IdOf(_sapi!, trader);
        foreach (var group in sold.GroupBy(s => s.Collectible.Code.ToString()))
        {
            int n = group.Sum(s => s.StackSize);
            foreach (var o in Book.OpenAt(id).Where(o => o.State == OrderState.Accepted && o.PlayerUid == player.PlayerUID && o.Item == group.Key).ToList())
            {
                if (n <= 0) break;
                if (Book.Deliver(o.Id, player.PlayerUID, n, Today) is not { } change) continue;
                n -= change.Taken;
                Settle(change, player);
            }
        }
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
    /// it (the Orders tab's Hand in), up to what is still wanted, paid at the order's normal price from
    /// the wallet; the error's lang key and its arguments, or null.</summary>
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
        int price = (int)Math.Round(n * o.UnitPrice);
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
        TraderFinder.TakeFromWallet(trader, price);
        TraderFinder.GiveGears(_sapi!, player.Entity, price);
        Settle(change, player, price);
        return null;
    }

    /// <summary>Pays a delivery's premium and, on completion, standing; tells the player.</summary>
    public void Settle(OrderChange change, IServerPlayer? player, int pricePaid = 0)
    {
        var o = change.Order;
        if (player?.Entity != null) TraderFinder.GiveGears(_sapi!, player.Entity, change.PremiumToPlayer);
        if (change.Completed && o.PlayerUid != null) _trading!.Standing.OnOrderDone(o.PlayerUid, o.TraderId);
        if (player is null) return;
        string item = TraderFinder.ItemName(_sapi!.World, o.Item);
        string msg = change.Completed
            ? L("trading-orders-done", o.Id, o.Quantity, item, change.PremiumToPlayer)
            : L("trading-orders-progress", o.Id, change.Taken, item, o.Delivered, o.Quantity, change.PremiumToPlayer);
        if (pricePaid > 0) msg += " " + L("trading-orders-paid", pricePaid);
        player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
    }

    // ---- Time ----

    public void Tick()
    {
        if (!Enabled || _sapi is null) return;
        foreach (var change in Book.Tick(Today)) Close(change);
    }

    /// <summary>Settles an order closed by time or by an admin: what is left of the premium back
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
