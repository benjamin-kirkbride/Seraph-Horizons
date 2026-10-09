using System.Text.Json;
using System.Text.Json.Serialization;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>
/// Which of the trading features the server has on, as far as the trade window shows them: a tab
/// or a line for a feature that is off is left out (<see cref="TradeWindowModel"/>).
/// </summary>
public sealed class WindowSwitches
{
    [JsonPropertyName("standing")] public bool Standing { get; set; }
    [JsonPropertyName("orders")] public bool Orders { get; set; }
    [JsonPropertyName("deliveries")] public bool Deliveries { get; set; }
    [JsonPropertyName("maps")] public bool Maps { get; set; }
    /// <summary>Everything has a price: the trader takes goods off its list, paid from its side budget.</summary>
    [JsonPropertyName("priced")] public bool EverythingPriced { get; set; }
    /// <summary>Standing's price factors reach the prices: the economy prices the shelves
    /// (everything has a price or regional supply on; with both off standing moves no price).</summary>
    [JsonPropertyName("standingPrices")] public bool StandingPrices { get; set; }
}

/// <summary>One standing tier as the window and the dialogue show it.</summary>
public sealed class TierView
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    /// <summary>The tier's 1-based number (stranger 1 … partner 5), which sizes and pays orders.</summary>
    [JsonPropertyName("n")] public int Number { get; set; } = 1;
    [JsonPropertyName("points")] public double Points { get; set; }
    [JsonPropertyName("unlocks")] public TierUnlocks Unlocks { get; set; } = new();
    /// <summary>The best ore map precision the tier buys (<c>MapOffers.MaxPrecision</c> of its map tier).</summary>
    [JsonPropertyName("precision")] public int MapPrecision { get; set; } = 1;
    /// <summary>Camp leads on offer at once (0: the stranger's one map per trader).</summary>
    [JsonPropertyName("leadMaps")] public int LeadMaps { get; set; }
    /// <summary>How far they reach from the trader's cell, in rings of grid cells.</summary>
    [JsonPropertyName("leadReach")] public int LeadReach { get; set; }
}

/// <summary>A camp lead this trader offers this player (Maps &amp; leads tab): the camp, where it is
/// from the trader, and its price.</summary>
public sealed class LeadOfferRow
{
    /// <summary>The camp's grid cell (<c>x,z</c>), what a buy names.</summary>
    [JsonPropertyName("cell")] public string Cell { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("distance")] public double Distance { get; set; }
    /// <summary>From the trader to the camp, blocks east (x) and south (z).</summary>
    [JsonPropertyName("dx")] public double Dx { get; set; }
    [JsonPropertyName("dz")] public double Dz { get; set; }
    [JsonPropertyName("price")] public int Price { get; set; }
    /// <summary>The prospector the first slot is kept for.</summary>
    [JsonPropertyName("prospector")] public bool Prospector { get; set; }
    /// <summary>Rings of grid cells from the trader's cell to the camp's (1: next door).</summary>
    [JsonPropertyName("ring")] public int Ring { get; set; }
    /// <summary>The player's very first map: a flat price, to the nearest prospector (a buy names
    /// <c>pity</c>, not the cell, since a cell that places no camp passes it on to the next).</summary>
    [JsonPropertyName("pity")] public bool Pity { get; set; }
}

/// <summary>A player's standing at one trader, with every tier, for the header, the Standing tab and
/// the dialogue's "How do you see me these days?".</summary>
public sealed class StandingSummary
{
    [JsonPropertyName("tier")] public int TierIndex { get; set; }
    /// <summary>Effective standing: max(personal, company) plus spillover.</summary>
    [JsonPropertyName("points")] public double Points { get; set; }
    [JsonPropertyName("spill")] public double Spill { get; set; }
    /// <summary>The company's record here when it is better than the player's own.</summary>
    [JsonPropertyName("company")] public double? Company { get; set; }
    [JsonPropertyName("tiers")] public List<TierView> Tiers { get; set; } = [];
    [JsonPropertyName("earn")] public StandingPoints Earn { get; set; } = new();

    [JsonIgnore] public TierView Tier => Tiers.Count == 0 ? new TierView { Code = "stranger" } : Tiers[Math.Clamp(TierIndex, 0, Tiers.Count - 1)];

    [JsonIgnore] public TierView? Next => TierIndex + 1 < Tiers.Count ? Tiers[TierIndex + 1] : null;
}

/// <summary>An order on the Orders tab: on offer at this trader, or taken by this player here.</summary>
public sealed class OrderRow
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    [JsonPropertyName("qty")] public int Quantity { get; set; }
    [JsonPropertyName("delivered")] public int Delivered { get; set; }
    /// <summary>The item's value per item, in gears.</summary>
    [JsonPropertyName("unit")] public double Value { get; set; }
    /// <summary>What the order pays in all: for an offer, what it would pay this player.</summary>
    [JsonPropertyName("pay")] public int Payout { get; set; }
    [JsonPropertyName("paid")] public int PayoutPaid { get; set; }
    /// <summary>Game days until the offer lapses (offered) or the deadline (taken).</summary>
    [JsonPropertyName("left")] public double DaysLeft { get; set; }
    /// <summary>Days to deliver once taken.</summary>
    [JsonPropertyName("days")] public double Days { get; set; }
    /// <summary>Taken by this player (else on offer).</summary>
    [JsonPropertyName("mine")] public bool Mine { get; set; }
    /// <summary>How many of the item the player carries now.</summary>
    [JsonPropertyName("held")] public int Held { get; set; }

    [JsonIgnore] public int Remaining => Math.Max(0, Quantity - Delivered);
}

/// <summary>The trader's delivery offer to this player today.</summary>
public sealed class DeliveryOfferRow
{
    [JsonPropertyName("toType")] public string ToType { get; set; } = "";
    [JsonPropertyName("distance")] public double Distance { get; set; }
    /// <summary>From the trader to the destination, blocks east (x) and south (z).</summary>
    [JsonPropertyName("dx")] public double Dx { get; set; }
    [JsonPropertyName("dz")] public double Dz { get; set; }
    /// <summary>Game days to deliver it once taken.</summary>
    [JsonPropertyName("days")] public double Days { get; set; }
    [JsonPropertyName("deposit")] public int Deposit { get; set; }
    [JsonPropertyName("fee")] public int Fee { get; set; }
}

/// <summary>One of the player's active deliveries that concerns this trader: sent from it, or bound for it.</summary>
public sealed class DeliveryRow
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("toType")] public string ToType { get; set; } = "";
    [JsonPropertyName("forHere")] public bool ForHere { get; set; }
    [JsonPropertyName("distance")] public double Distance { get; set; }
    [JsonPropertyName("dx")] public double Dx { get; set; }
    [JsonPropertyName("dz")] public double Dz { get; set; }
    /// <summary>Game hours to the deadline; below 0 it is late (until the grace runs out).</summary>
    [JsonPropertyName("hours")] public double HoursLeft { get; set; }
    /// <summary>The same in game days.</summary>
    [JsonPropertyName("days")] public double DaysLeft { get; set; }
    [JsonPropertyName("deposit")] public int Deposit { get; set; }
    [JsonPropertyName("fee")] public int Fee { get; set; }
    /// <summary>The player carries its package (a hand-in is possible here when <see cref="ForHere"/>).</summary>
    [JsonPropertyName("carried")] public bool Carried { get; set; }
}

/// <summary>Why a good is not on the shelf for this player yet.</summary>
public enum LockReason { Tier, Rare, Leads }

/// <summary>A good of the trader's list that the player's standing does not reach yet, with the tier
/// that unlocks it (<see cref="LockedStock"/>).</summary>
public sealed class LockedRow
{
    [JsonPropertyName("type")] public string Type { get; set; } = "item";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    /// <summary>The stack's attributes as JSON, or null.</summary>
    [JsonPropertyName("attrs")] public string? Attributes { get; set; }
    [JsonPropertyName("size")] public int StackSize { get; set; } = 1;
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("reason"), JsonConverter(typeof(JsonStringEnumConverter))] public LockReason Reason { get; set; }
    /// <summary>Rotating stock: shelved some weeks, not always.</summary>
    [JsonPropertyName("rotating")] public bool Rotating { get; set; }
}

/// <summary>
/// What the server tells a player's trade window about one trader, beyond the shelves (which the
/// client has from the trader's own inventory): standing, orders, deliveries, the goods still locked,
/// and which features are on. Sent when the window opens and after every action, as JSON in a
/// protobuf packet (the game side's <c>TradeWindowPacket</c>); the dialogue's standing reply reads
/// the same thing.
/// </summary>
public sealed class TradeWindowState
{
    [JsonPropertyName("trader")] public long TraderId { get; set; }
    [JsonPropertyName("switches")] public WindowSwitches Switches { get; set; } = new();
    [JsonPropertyName("standing")] public StandingSummary? Standing { get; set; }
    [JsonPropertyName("orders")] public List<OrderRow> Orders { get; set; } = [];
    [JsonPropertyName("deliveryOffer")] public DeliveryOfferRow? DeliveryOffer { get; set; }
    /// <summary>Why there is no delivery offer (a lang key), when there is none.</summary>
    [JsonPropertyName("deliveryWhy")] public string? DeliveryWhy { get; set; }
    [JsonPropertyName("deliveries")] public List<DeliveryRow> Deliveries { get; set; } = [];
    [JsonPropertyName("locked")] public List<LockedRow> Locked { get; set; } = [];
    /// <summary>Whether this player's own standing here buys the settlement ground's lead.</summary>
    [JsonPropertyName("leads")] public bool LeadsToTraders { get; set; }
    /// <summary>The tier from which the settlement ground's lead is sold (-1: none).</summary>
    [JsonPropertyName("leadsTier")] public int LeadsTier { get; set; } = -1;
    /// <summary>The camp leads this trader offers this player, nearest first (the prospector kept
    /// for first).</summary>
    [JsonPropertyName("leadOffers")] public List<LeadOfferRow> LeadOffers { get; set; } = [];
    /// <summary>Why there is no camp lead on offer (a lang key), when there is none.</summary>
    [JsonPropertyName("leadsWhy")] public string? LeadsWhy { get; set; }
    /// <summary>Maps this player's group has bought from this trader (each one doubles the next price).</summary>
    [JsonPropertyName("leadsBought")] public int LeadsBought { get; set; }
    /// <summary>The selling slots (0–15) holding a map or lead this player has already: its target on
    /// their map as precisely or more, or a copy carried. Shown greyed out, "you have this"; the
    /// server refuses the buy.</summary>
    [JsonPropertyName("owned")] public List<int> OwnedMaps { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static TradeWindowState FromJson(string json) => JsonSerializer.Deserialize<TradeWindowState>(json, Options) ?? new TradeWindowState();
}

/// <summary>What a player asks of the trader through the window.</summary>
public enum TradeAction
{
    /// <summary>Send the window's state again.</summary>
    Refresh,
    /// <summary>One trade unit of a selling slot (<see cref="TradeRequest.Slot"/>, 0–15): goods or a map.</summary>
    Buy,
    /// <summary>One trade unit of the stack in the window's sell slot.</summary>
    Sell,
    /// <summary>Take order <see cref="TradeRequest.Id"/>.</summary>
    TakeOrder,
    /// <summary>Hand in what the player carries towards their order <see cref="TradeRequest.Id"/>.</summary>
    HandInOrder,
    TakeDelivery,
    HandInDelivery,
    /// <summary>A waypoint for the delivery offer (<see cref="TradeRequest.Id"/> 0) or the player's
    /// delivery <see cref="TradeRequest.Id"/>.</summary>
    MarkDelivery,
    /// <summary>The camp lead to cell <see cref="TradeRequest.Code"/> (<c>x,z</c>) at
    /// <see cref="TradeRequest.Price"/>.</summary>
    BuyLead,
}

public sealed class TradeRequest
{
    [JsonPropertyName("action"), JsonConverter(typeof(JsonStringEnumConverter))] public TradeAction Action { get; set; }
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("id")] public int Id { get; set; }
    /// <summary>Buy: the item the player saw in the slot (its full code), or null not to check; BuyLead:
    /// the camp's cell.</summary>
    [JsonPropertyName("code")] public string? Code { get; set; }
    /// <summary>Buy: the price the player saw, or null not to check.</summary>
    [JsonPropertyName("price")] public int? Price { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static TradeRequest FromJson(string json) => JsonSerializer.Deserialize<TradeRequest>(json) ?? new TradeRequest();
}

/// <summary>The server's answer to a <see cref="TradeRequest"/>: whether it went through, and what
/// to say (a lang key in the mod's domain and its arguments, formatted in the player's language).</summary>
public sealed class TradeResult
{
    [JsonPropertyName("action"), JsonConverter(typeof(JsonStringEnumConverter))] public TradeAction Action { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("args")] public List<string> Args { get; set; } = [];

    public static TradeResult Done(TradeAction action, string? key = null, params object[] args) =>
        new() { Action = action, Ok = true, Key = key, Args = args.Select(Format).ToList() };

    public static TradeResult Refused(TradeAction action, string key, params object[] args) =>
        new() { Action = action, Ok = false, Key = key, Args = args.Select(Format).ToList() };

    private static string Format(object a) => a switch
    {
        double d => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static TradeResult FromJson(string json) => JsonSerializer.Deserialize<TradeResult>(json) ?? new TradeResult();
}
