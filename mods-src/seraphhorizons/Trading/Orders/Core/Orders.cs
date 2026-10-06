using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Trading.Orders.Core;

public enum OrderState
{
    /// <summary>On offer at its trader; nobody has taken it.</summary>
    Offered,
    /// <summary>Taken by a player, who delivers before the deadline.</summary>
    Accepted,
    Done,
    /// <summary>An offer nobody took, or a taken order delivered in part, past its deadline.</summary>
    Expired,
    /// <summary>A taken order with nothing delivered by its deadline: costs standing.</summary>
    Abandoned,
    Cancelled,
}

/// <summary>
/// A standing order (#453): a trader asks for <see cref="Quantity"/> of an item and pays, on top of
/// its normal price for each item (paid as it is handed over, by the deal or the hand-in), a premium
/// reserved from its wallet when the order is made (<see cref="Reserved"/>), paid out pro rata as
/// items come in and in full on completion.
/// </summary>
public sealed class Order
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("trader")] public string TraderId { get; set; } = "";
    [JsonPropertyName("type")] public string TraderType { get; set; } = "";
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    /// <summary>The quantity at standing scale 1, as offered.</summary>
    [JsonPropertyName("base")] public int BaseQuantity { get; set; }
    [JsonPropertyName("qty")] public int Quantity { get; set; }
    /// <summary>Items come in multiples of this (the list's stack size).</summary>
    [JsonPropertyName("lot")] public int Lot { get; set; } = 1;
    [JsonPropertyName("delivered")] public int Delivered { get; set; }
    /// <summary>The trader's normal price per item when the order was made, in gears.</summary>
    [JsonPropertyName("unit")] public double UnitPrice { get; set; }
    [JsonPropertyName("factor")] public double PremiumFactor { get; set; }
    /// <summary>The whole premium, in gears, held back from the trader's wallet.</summary>
    [JsonPropertyName("premium")] public int Premium { get; set; }
    [JsonPropertyName("reserved")] public int Reserved { get; set; }
    [JsonPropertyName("paid")] public int PremiumPaid { get; set; }
    [JsonPropertyName("days")] public double Days { get; set; }
    [JsonPropertyName("created")] public double CreatedDay { get; set; }
    /// <summary>Offered: when the offer lapses; accepted: the delivery deadline.</summary>
    [JsonPropertyName("deadline")] public double Deadline { get; set; }
    [JsonPropertyName("player")] public string? PlayerUid { get; set; }
    [JsonPropertyName("playerName")] public string? PlayerName { get; set; }
    [JsonPropertyName("accepted")] public double? AcceptedDay { get; set; }
    [JsonPropertyName("state"), JsonConverter(typeof(JsonStringEnumConverter))] public OrderState State { get; set; }
    [JsonPropertyName("closed")] public double? ClosedDay { get; set; }

    [JsonIgnore] public bool IsOpen => State is OrderState.Offered or OrderState.Accepted;
    [JsonIgnore] public int Remaining => Math.Max(0, Quantity - Delivered);
}

/// <summary>Something the trader would order: an item it buys, its price and lot.</summary>
public sealed record OrderCandidate(string Item, double UnitPrice, int Lot, int MaxStack);

/// <summary>The maths of orders: what to order, how many, the premium, the scaling with standing.</summary>
public static class OrderPlanner
{
    /// <summary>What an order is worth at the normal price at standing scale 1, in gears.</summary>
    public const double BaseGears = 24;
    public const double MinFactor = 1.3, MaxFactor = 1.6;
    public const int MinDays = 3, MaxDays = 6;
    /// <summary>At most this many full stacks in one order, whatever the scale.</summary>
    public const int MaxStacks = 4;

    /// <summary>How many open orders a trader keeps after a restock: one or two.</summary>
    public static int TargetOpen(double roll) => roll < 0.5 ? 1 : 2;

    public static double Factor(double roll) => Math.Round((MinFactor + (MaxFactor - MinFactor) * Math.Clamp(roll, 0, 1)) * 20) / 20;

    public static int Days(double roll) => MinDays + (int)Math.Floor(Math.Clamp(roll, 0, 0.9999) * (MaxDays - MinDays + 1));

    /// <summary>Items in an order worth <see cref="BaseGears"/> × <paramref name="scale"/> at
    /// <paramref name="unitPrice"/>, in whole lots, at least one lot and at most
    /// <see cref="MaxStacks"/> stacks.</summary>
    public static int Quantity(double unitPrice, int lot, int maxStack, double scale)
    {
        lot = Math.Max(1, lot);
        int max = Math.Max(lot, MaxStacks * Math.Max(1, maxStack) / lot * lot);
        if (unitPrice <= 0) return lot;
        double items = BaseGears * Math.Max(0, scale) / unitPrice;
        int lots = (int)Math.Ceiling(items / lot - 1e-9);
        return Math.Clamp(lots * lot, lot, max);
    }

    /// <summary>The premium over the normal price for the whole order, at least a gear.</summary>
    public static int Premium(int quantity, double unitPrice, double factor) =>
        Math.Max(1, (int)Math.Round(quantity * unitPrice * (factor - 1)));

    /// <summary>The premium due when the delivered count goes to <paramref name="deliveredAfter"/>:
    /// the pro-rata share not yet paid, and the rest of it on completion.</summary>
    public static int PremiumDue(int premium, int quantity, int deliveredAfter, int alreadyPaid)
    {
        if (quantity <= 0) return 0;
        int owed = deliveredAfter >= quantity ? premium : (int)Math.Floor((double)premium * deliveredAfter / quantity);
        return Math.Max(0, owed - alreadyPaid);
    }

    /// <summary>Picks what to order among <paramref name="candidates"/>, skipping items already on
    /// order at the trader; null when none is left.</summary>
    public static OrderCandidate? Pick(IReadOnlyList<OrderCandidate> candidates, ISet<string> onOrder, double roll)
    {
        var free = candidates.Where(c => c.UnitPrice > 0 && !onOrder.Contains(c.Item)).ToList();
        if (free.Count == 0) return null;
        return free[Math.Min(free.Count - 1, (int)Math.Floor(Math.Clamp(roll, 0, 1) * free.Count))];
    }

    /// <summary>The order for a player at <paramref name="scale"/> (their standing's
    /// <c>orderScale</c>): the offered quantity times the scale in whole lots (at most
    /// <see cref="MaxStacks"/> stacks), and the premium with it, as far as
    /// <paramref name="affordableExtra"/> gears more than the order already holds back allow (down to
    /// the offered quantity). A scale under 1 keeps the offer as it is.</summary>
    public static (int Quantity, int Premium) Scaled(Order offer, double scale, int maxStack, int affordableExtra)
    {
        int lot = Math.Max(1, offer.Lot);
        int max = Math.Max(offer.BaseQuantity, MaxStacks * Math.Max(1, maxStack) / lot * lot);
        int qty = Math.Min(max, (int)Math.Ceiling(offer.BaseQuantity * Math.Max(1, scale) / lot - 1e-9) * lot);
        qty = Math.Max(offer.BaseQuantity, qty);
        while (true)
        {
            int premium = Math.Max(offer.Reserved, Premium(qty, offer.UnitPrice, offer.PremiumFactor));
            if (premium - offer.Reserved <= Math.Max(0, affordableExtra) || qty <= offer.BaseQuantity)
                return qty <= offer.BaseQuantity ? (offer.BaseQuantity, offer.Reserved) : (qty, premium);
            qty -= lot;
        }
    }
}

/// <summary>What changed for one order, for the game side to settle: gears back to the trader's
/// wallet, a premium to the player, standing.</summary>
public sealed record OrderChange(Order Order, OrderState From, OrderState To, int RefundToTrader, int PremiumToPlayer, int Taken)
{
    public bool Abandoned => To == OrderState.Abandoned;
    public bool Completed => To == OrderState.Done && From != OrderState.Done;
}

/// <summary>Every order in the world (saved with it), and their state machine.</summary>
public sealed class OrderBook
{
    /// <summary>Closed orders are kept this many days for the admin listings, then dropped.</summary>
    public const double KeepClosedDays = 30;

    private readonly Dictionary<int, Order> _orders = new();

    public int NextId { get; private set; } = 1;

    public IEnumerable<Order> All => _orders.Values.OrderBy(o => o.Id);

    public Order? Get(int id) => _orders.GetValueOrDefault(id);

    public IEnumerable<Order> OpenAt(string traderId) => All.Where(o => o.IsOpen && o.TraderId == traderId);

    public IEnumerable<Order> OfPlayer(string playerUid) => All.Where(o => o.PlayerUid == playerUid);

    /// <summary>A new offer at the trader, its premium for <paramref name="baseQuantity"/> already
    /// held back from the trader's wallet by the caller.</summary>
    public Order Offer(string traderId, string traderType, OrderCandidate c, int baseQuantity, double factor, double days, double today)
    {
        var o = new Order
        {
            Id = NextId++,
            TraderId = traderId,
            TraderType = traderType,
            Item = c.Item,
            BaseQuantity = baseQuantity,
            Quantity = baseQuantity,
            Lot = Math.Max(1, c.Lot),
            UnitPrice = c.UnitPrice,
            PremiumFactor = factor,
            Premium = OrderPlanner.Premium(baseQuantity, c.UnitPrice, factor),
            Days = days,
            CreatedDay = today,
            Deadline = today + days,
            State = OrderState.Offered,
        };
        o.Reserved = o.Premium;
        _orders[o.Id] = o;
        return o;
    }

    /// <summary>The player takes an offer, at the quantity and premium <see cref="OrderPlanner.Scaled"/>
    /// gave; the deadline runs from now. False when it is not on offer.</summary>
    public bool Accept(int id, string playerUid, string playerName, int quantity, int premium, double today)
    {
        if (Get(id) is not { State: OrderState.Offered } o) return false;
        o.State = OrderState.Accepted;
        o.PlayerUid = playerUid;
        o.PlayerName = playerName;
        o.AcceptedDay = today;
        o.Quantity = Math.Max(o.BaseQuantity, quantity);
        o.Premium = Math.Max(o.Reserved, premium);
        o.Reserved = o.Premium;
        o.Deadline = today + o.Days;
        return true;
    }

    /// <summary>The player hands over <paramref name="count"/> of the item (by a deal or the
    /// hand-in): up to what is still wanted counts, with its share of the premium. Null when the
    /// order is not theirs to deliver or is past its deadline.</summary>
    public OrderChange? Deliver(int id, string playerUid, int count, double today)
    {
        if (Get(id) is not { State: OrderState.Accepted } o || o.PlayerUid != playerUid || today > o.Deadline || count <= 0) return null;
        int taken = Math.Min(count, o.Remaining);
        if (taken <= 0) return null;
        o.Delivered += taken;
        int due = OrderPlanner.PremiumDue(o.Premium, o.Quantity, o.Delivered, o.PremiumPaid);
        o.PremiumPaid += due;
        var from = o.State;
        if (o.Remaining == 0) Close(o, OrderState.Done, today);
        return new OrderChange(o, from, o.State, 0, due, taken);
    }

    /// <summary>Admin: an accepted order counts as fully delivered.</summary>
    public OrderChange? Complete(int id, double today)
    {
        if (Get(id) is not { State: OrderState.Accepted } o) return null;
        int due = OrderPlanner.PremiumDue(o.Premium, o.Quantity, o.Quantity, o.PremiumPaid);
        o.PremiumPaid += due;
        o.Delivered = o.Quantity;
        Close(o, OrderState.Done, today);
        return new OrderChange(o, OrderState.Accepted, OrderState.Done, 0, due, 0);
    }

    /// <summary>Admin: closes an open order without penalty; the unpaid premium goes back.</summary>
    public OrderChange? Cancel(int id, double today)
    {
        if (Get(id) is not { IsOpen: true } o) return null;
        var from = o.State;
        int refund = Math.Max(0, o.Reserved - o.PremiumPaid);
        Close(o, OrderState.Cancelled, today);
        return new OrderChange(o, from, OrderState.Cancelled, refund, 0, 0);
    }

    /// <summary>Closes what is past its deadline: an untaken offer expires, a taken order with
    /// nothing delivered is abandoned, one delivered in part expires; the unpaid premium goes back
    /// to the trader.</summary>
    public List<OrderChange> Tick(double today)
    {
        var changes = new List<OrderChange>();
        foreach (var o in All.Where(o => o.IsOpen && today > o.Deadline).ToList())
        {
            var from = o.State;
            var to = from == OrderState.Accepted && o.Delivered == 0 ? OrderState.Abandoned : OrderState.Expired;
            int refund = Math.Max(0, o.Reserved - o.PremiumPaid);
            Close(o, to, today);
            changes.Add(new OrderChange(o, from, to, refund, 0, 0));
        }
        foreach (var o in _orders.Values.Where(o => !o.IsOpen && o.ClosedDay is double c && today - c > KeepClosedDays).ToList())
            _orders.Remove(o.Id);
        return changes;
    }

    /// <summary>Moves every open order's clock on by <paramref name="days"/> (the economy's
    /// simulate): its deadline comes that much closer.</summary>
    public void Advance(double days)
    {
        foreach (var o in _orders.Values.Where(o => o.IsOpen))
        {
            o.Deadline -= days;
            o.CreatedDay -= days;
            if (o.AcceptedDay is double a) o.AcceptedDay = a - days;
        }
    }

    /// <summary>Admin: makes a taken order (or an offer) by hand, its premium reserved by the caller.</summary>
    public Order Add(Order o)
    {
        o.Id = NextId++;
        _orders[o.Id] = o;
        return o;
    }

    private static void Close(Order o, OrderState to, double today)
    {
        o.State = to;
        o.ClosedDay = today;
    }

    private sealed class Dto
    {
        [JsonPropertyName("next")] public int Next { get; set; } = 1;
        [JsonPropertyName("orders")] public List<Order> Orders { get; set; } = [];
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto { Next = NextId, Orders = All.ToList() });

    public static OrderBook FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json) ?? new Dto();
        var book = new OrderBook();
        foreach (var o in dto.Orders) book._orders[o.Id] = o;
        book.NextId = Math.Max(dto.Next, dto.Orders.Select(o => o.Id + 1).DefaultIfEmpty(1).Max());
        return book;
    }
}
