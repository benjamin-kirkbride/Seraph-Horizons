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
/// A standing order (#453): a trader asks for <see cref="Quantity"/> of an item and pays
/// <see cref="Payout"/>, the goods' worth at their value times the taker's standing multiplier
/// (<see cref="OrderPlanner.Multiplier"/>), paid out pro rata as items are handed in and in full on
/// completion. The payout is new money, never the trader's wallet. An offer has no size until it is
/// taken: the taker's standing tier sizes it from <see cref="Roll"/>, unless an admin fixed it
/// (<see cref="BaseQuantity"/>).
/// </summary>
public sealed class Order
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("trader")] public string TraderId { get; set; } = "";
    [JsonPropertyName("type")] public string TraderType { get; set; } = "";
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    /// <summary>A quantity fixed when the order was made (an admin's), kept whoever takes it; 0: the
    /// taker's tier sizes it.</summary>
    [JsonPropertyName("base")] public int BaseQuantity { get; set; }
    /// <summary>Where in its tier's range of worth the order falls, in [0, 1).</summary>
    [JsonPropertyName("roll")] public double Roll { get; set; }
    /// <summary>0 until taken (unless fixed).</summary>
    [JsonPropertyName("qty")] public int Quantity { get; set; }
    [JsonPropertyName("delivered")] public int Delivered { get; set; }
    /// <summary>The item's value when the order was made, in gears per item.</summary>
    [JsonPropertyName("unit")] public double Value { get; set; }
    /// <summary>The taker's standing tier, 1 (stranger) to 5 (partner), and the multiplier it gave.</summary>
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("factor")] public double Multiplier { get; set; }
    /// <summary>The whole payout, in gears.</summary>
    [JsonPropertyName("premium")] public int Payout { get; set; }
    /// <summary>Only an order taken before payouts were new money (2026-10-08): its premium, held
    /// back from the trader's wallet then, and it pays its goods at <see cref="Value"/> (then the
    /// list's price) from the wallet on hand-in. 0 for every order since.</summary>
    [JsonPropertyName("reserved")] public int Reserved { get; set; }
    [JsonPropertyName("paid")] public int PayoutPaid { get; set; }
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

/// <summary>Something the trader would order: an item it buys, its value per item and stack size.</summary>
public sealed record OrderCandidate(string Item, double Value, int MaxStack);

/// <summary>
/// The maths of orders. <c>n</c> is a standing tier's 1-based number (stranger 1 … partner 5): a
/// trader offers 2n orders a week for its shelf tier; an order is worth a random lo(n)–2.5n gears of
/// goods, lo(n) = 1 + 0.375 (n − 1), and pays that worth × (10 + 2.5 (n − 1)), n being the taker's.
/// </summary>
public static class OrderPlanner
{
    public const int MinDays = 3, MaxDays = 6;
    /// <summary>At most this many full stacks in one order, whatever its worth.</summary>
    public const int MaxStacks = 4;

    private static int N(int n) => Math.Max(1, n);

    /// <summary>How many offers a trader puts up at its weekly restock: 2n for its shelf tier.</summary>
    public static int PerWeek(int n) => 2 * N(n);

    /// <summary>The least an order asks, in gears' worth of goods: 1 for a stranger, 2.5 for a partner.</summary>
    public static double MinWorth(int n) => 1 + 0.375 * (N(n) - 1);

    /// <summary>The most: 2.5n.</summary>
    public static double MaxWorth(int n) => 2.5 * N(n);

    /// <summary>The payout per gear of goods' worth: 10 for a stranger, 20 for a partner.</summary>
    public static double Multiplier(int n) => 10 + 2.5 * (N(n) - 1);

    public static double Worth(int n, double roll) => MinWorth(n) + (MaxWorth(n) - MinWorth(n)) * Math.Clamp(roll, 0, 1);

    public static int Days(double roll) => MinDays + (int)Math.Floor(Math.Clamp(roll, 0, 0.9999) * (MaxDays - MinDays + 1));

    /// <summary>Items worth <paramref name="worth"/> gears at <paramref name="value"/> each, rounded
    /// up, at least one and at most <see cref="MaxStacks"/> stacks.</summary>
    public static int Quantity(double value, double worth, int maxStack)
    {
        int max = MaxStacks * Math.Max(1, maxStack);
        if (value <= 0) return 1;
        return Math.Clamp((int)Math.Ceiling(worth / value - 1e-9), 1, max);
    }

    /// <summary>What <paramref name="quantity"/> items at <paramref name="value"/> pay at tier
    /// <paramref name="n"/>, at least a gear.</summary>
    public static int Payout(int quantity, double value, int n) =>
        Math.Max(1, (int)Math.Round(quantity * value * Multiplier(n)));

    /// <summary>The order's quantity and payout for a taker at tier <paramref name="n"/>: a fixed
    /// quantity as it is, else sized from the offer's roll.</summary>
    public static (int Quantity, int Payout) Terms(Order offer, int n, int maxStack)
    {
        int qty = offer.BaseQuantity > 0 ? offer.BaseQuantity : Quantity(offer.Value, Worth(n, offer.Roll), maxStack);
        return (qty, Payout(qty, offer.Value, n));
    }

    /// <summary>The payout due when the delivered count goes to <paramref name="deliveredAfter"/>:
    /// the pro-rata share not yet paid, and the rest of it on completion.</summary>
    public static int PayoutDue(int payout, int quantity, int deliveredAfter, int alreadyPaid)
    {
        if (quantity <= 0) return 0;
        int owed = deliveredAfter >= quantity ? payout : (int)Math.Floor((double)payout * deliveredAfter / quantity);
        return Math.Max(0, owed - alreadyPaid);
    }

    /// <summary>Picks what to order among <paramref name="candidates"/>, skipping items already on
    /// order at the trader; null when none is left.</summary>
    public static OrderCandidate? Pick(IReadOnlyList<OrderCandidate> candidates, ISet<string> onOrder, double roll)
    {
        var free = candidates.Where(c => c.Value > 0 && !onOrder.Contains(c.Item)).ToList();
        if (free.Count == 0) return null;
        return free[Math.Min(free.Count - 1, (int)Math.Floor(Math.Clamp(roll, 0, 1) * free.Count))];
    }
}

/// <summary>What changed for one order, for the game side to settle: gears back to the trader's
/// wallet (an old order's reserve), a payout to the player, standing.</summary>
public sealed record OrderChange(Order Order, OrderState From, OrderState To, int RefundToTrader, int PayoutToPlayer, int Taken)
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

    /// <summary>A new offer at the trader, sized when taken (<paramref name="roll"/>), or of
    /// <paramref name="fixedQuantity"/> items when that is positive.</summary>
    public Order Offer(string traderId, string traderType, OrderCandidate c, double roll, double days, double today, int fixedQuantity = 0)
    {
        var o = new Order
        {
            Id = NextId++,
            TraderId = traderId,
            TraderType = traderType,
            Item = c.Item,
            BaseQuantity = Math.Max(0, fixedQuantity),
            Quantity = Math.Max(0, fixedQuantity),
            Roll = Math.Clamp(roll, 0, 1),
            Value = c.Value,
            Days = days,
            CreatedDay = today,
            Deadline = today + days,
            State = OrderState.Offered,
        };
        _orders[o.Id] = o;
        return o;
    }

    /// <summary>The player, at standing tier <paramref name="n"/>, takes an offer, at the quantity and
    /// payout <see cref="OrderPlanner.Terms"/> gives; the deadline runs from now. An old offer's
    /// reserve is let go (the caller returns it to the wallet first). False when it is not on offer.</summary>
    public bool Accept(int id, string playerUid, string playerName, int n, int maxStack, double today)
    {
        if (Get(id) is not { State: OrderState.Offered } o) return false;
        var (qty, payout) = OrderPlanner.Terms(o, n, maxStack);
        o.State = OrderState.Accepted;
        o.PlayerUid = playerUid;
        o.PlayerName = playerName;
        o.AcceptedDay = today;
        o.Tier = Math.Max(1, n);
        o.Multiplier = OrderPlanner.Multiplier(n);
        o.Quantity = qty;
        o.Payout = payout;
        o.Reserved = 0;
        o.Deadline = today + o.Days;
        return true;
    }

    /// <summary>The player hands over <paramref name="count"/> of the item: up to what is still
    /// wanted counts, with its share of the payout. Null when the order is not theirs to deliver or is
    /// past its deadline.</summary>
    public OrderChange? Deliver(int id, string playerUid, int count, double today)
    {
        if (Get(id) is not { State: OrderState.Accepted } o || o.PlayerUid != playerUid || today > o.Deadline || count <= 0) return null;
        int taken = Math.Min(count, o.Remaining);
        if (taken <= 0) return null;
        o.Delivered += taken;
        int due = OrderPlanner.PayoutDue(o.Payout, o.Quantity, o.Delivered, o.PayoutPaid);
        o.PayoutPaid += due;
        var from = o.State;
        if (o.Remaining == 0) Close(o, OrderState.Done, today);
        return new OrderChange(o, from, o.State, 0, due, taken);
    }

    /// <summary>Admin: an accepted order counts as fully delivered.</summary>
    public OrderChange? Complete(int id, double today)
    {
        if (Get(id) is not { State: OrderState.Accepted } o) return null;
        int due = OrderPlanner.PayoutDue(o.Payout, o.Quantity, o.Quantity, o.PayoutPaid);
        o.PayoutPaid += due;
        o.Delivered = o.Quantity;
        Close(o, OrderState.Done, today);
        return new OrderChange(o, OrderState.Accepted, OrderState.Done, 0, due, 0);
    }

    /// <summary>Admin: closes an open order without penalty; an old order's unpaid reserve goes back.</summary>
    public OrderChange? Cancel(int id, double today)
    {
        if (Get(id) is not { IsOpen: true } o) return null;
        var from = o.State;
        Close(o, OrderState.Cancelled, today);
        return new OrderChange(o, from, OrderState.Cancelled, Unpaid(o), 0, 0);
    }

    /// <summary>Closes what is past its deadline: an untaken offer expires, a taken order with
    /// nothing delivered is abandoned, one delivered in part expires; an old order's unpaid reserve
    /// goes back to the trader.</summary>
    public List<OrderChange> Tick(double today)
    {
        var changes = new List<OrderChange>();
        foreach (var o in All.Where(o => o.IsOpen && today > o.Deadline).ToList())
        {
            var from = o.State;
            var to = from == OrderState.Accepted && o.Delivered == 0 ? OrderState.Abandoned : OrderState.Expired;
            Close(o, to, today);
            changes.Add(new OrderChange(o, from, to, Unpaid(o), 0, 0));
        }
        foreach (var o in _orders.Values.Where(o => !o.IsOpen && o.ClosedDay is double c && today - c > KeepClosedDays).ToList())
            _orders.Remove(o.Id);
        return changes;
    }

    private static int Unpaid(Order o) => o.Reserved > 0 ? Math.Max(0, o.Reserved - o.PayoutPaid) : 0;

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
