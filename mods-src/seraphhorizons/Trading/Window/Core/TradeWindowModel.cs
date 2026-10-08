using System.Globalization;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>
/// A line of text to show: a lang key in the mod's domain and its arguments. An argument may itself
/// be a <see cref="Text"/> (resolved first) or an <see cref="ItemRef"/> (the item's name); anything
/// else is shown as it is. Keeps the window's wording testable without the game's Lang.
/// </summary>
public sealed record Text(string Key, params object[] Args)
{
    public override string ToString() => Args.Length == 0 ? Key : $"{Key}({string.Join(", ", Args.Select(a => a.ToString()))})";
}

/// <summary>An item's name, resolved by the game side from its full code.</summary>
public sealed record ItemRef(string Code)
{
    public override string ToString() => Code;
}

/// <summary>The window's tabs (mockup "A · Tabs"); a tab of a feature that is off is left out.</summary>
public enum WindowTab { Trade, Orders, Deliveries, Maps, Standing }

/// <summary>The header's standing: the tier, the points, the next tier's threshold and how far
/// towards it, with the raw numbers in brackets ("Regular [310 / 800]").</summary>
public sealed record StandingBar(string TierCode, double Points, double? NextPoints, double Fraction, Text Label);

/// <summary>An order on the Orders tab, with what the player can do about it.</summary>
public sealed record OrderLine(OrderRow Row, Text Line, bool CanTake, bool CanHandIn);

/// <summary>A delivery line on the Deliveries tab: the trader's offer (<see cref="Row"/> null) or one
/// of the player's deliveries.</summary>
public sealed record DeliveryLine(DeliveryRow? Row, Text Line, bool CanTake, bool CanHandIn, bool CanMark);

/// <summary>A tier on the Standing tab.</summary>
public sealed record TierLine(Text Line, bool Current);

/// <summary>One paragraph the trader says, its facts in brackets after it.</summary>
public sealed record SpeechLine(Text Voice, IReadOnlyList<Text> Facts);

/// <summary>What a map or lead offer on the shelf is to this player.</summary>
public enum MapOfferStatus { Available, SoldOut, Locked, Owned }

/// <summary>
/// The trade window's view model (#436, the playtest's mockup "A · Tabs"): tabs, header, footer, and
/// every tab's lines, from the server's <see cref="TradeWindowState"/> and what the client reads off
/// the trader. Game-independent, so the window's GUI stays a thin layer over tested logic.
/// </summary>
public static class TradeWindowModel
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>A number as the window shows it (at most two decimals, invariant).</summary>
    public static string F(double v) => v.ToString("0.##", Ci);

    public static Text TierName(string code) => new("trading-standing-tier-" + code);

    public static Text TypeName(string type) => new("trading-type-" + type);

    // ---- Tabs ----

    /// <summary>The tabs to show, in order, with their labels: Trade always; Orders (n), Deliveries (n),
    /// Maps &amp; leads and Standing when their feature is on.</summary>
    public static List<(WindowTab Tab, Text Label)> Tabs(TradeWindowState state)
    {
        var sw = state.Switches;
        var tabs = new List<(WindowTab, Text)> { (WindowTab.Trade, new Text("trading-window-tab-trade")) };
        if (sw.Orders) tabs.Add((WindowTab.Orders, new Text("trading-window-tab-orders", state.Orders.Count)));
        if (sw.Deliveries) tabs.Add((WindowTab.Deliveries, new Text("trading-window-tab-deliveries", DeliveryCount(state))));
        if (sw.Maps) tabs.Add((WindowTab.Maps, new Text("trading-window-tab-maps")));
        if (sw.Standing && state.Standing != null) tabs.Add((WindowTab.Standing, new Text("trading-window-tab-standing")));
        return tabs;
    }

    public static int DeliveryCount(TradeWindowState state) => (state.DeliveryOffer != null ? 1 : 0) + state.Deliveries.Count;

    // ---- Header and footer ----

    /// <summary>"smith · temperate, igneous" after the trader's name.</summary>
    public static Text Header(string name, string type, string climate, string rock) =>
        new("trading-window-header", name, TypeName(type), new Text("trading-region-" + climate), new Text("trading-region-" + rock));

    /// <summary>The standing bar, or null with standing off.</summary>
    public static StandingBar? Bar(StandingSummary? s, WindowSwitches sw)
    {
        if (!sw.Standing || s is null) return null;
        double points = Math.Floor(s.Points);
        if (s.Next is not { } next)
            return new StandingBar(s.Tier.Code, points, null, 1, new Text("trading-window-standing-top", TierName(s.Tier.Code), points));
        double from = s.Tier.Points, span = Math.Max(1, next.Points - from);
        double fraction = Math.Clamp((s.Points - from) / span, 0, 1);
        return new StandingBar(s.Tier.Code, points, next.Points, fraction, new Text("trading-window-standing-bar", TierName(s.Tier.Code), points, F(next.Points)));
    }

    /// <summary>"You have 34 g · Hanna has 120 g · for goods off her list 21 g" (the side budget only
    /// with everything has a price).</summary>
    public static List<Text> Footer(int playerGears, string traderName, int wallet, int? sideBudget, bool female)
    {
        var parts = new List<Text> { new("trading-window-footer-you", playerGears), new("trading-window-footer-trader", traderName, wallet) };
        if (sideBudget is int side) parts.Add(new Text(female ? "trading-window-footer-side-her" : "trading-window-footer-side-his", side));
        return parts;
    }

    // ---- Trade tab ----

    /// <summary>The selected good on a shelf: what it is (<paramref name="item"/>, its name or an
    /// <see cref="ItemRef"/>), its price and stock, and how to trade it.</summary>
    public static List<Text> ShelfDetails(object item, int unitSize, int price, int stock, bool traderSells, bool lockedForMe)
    {
        var lines = new List<Text> { new("trading-window-selected", unitSize, item) };
        if (traderSells)
        {
            lines.Add(new Text("trading-window-selected-price", price, unitSize));
            lines.Add(stock <= 0 ? new Text("trading-window-selected-soldout") : new Text("trading-window-selected-stock", stock));
            if (stock > 0 && !lockedForMe) lines.Add(new Text("trading-window-hold-buy"));
        }
        else
        {
            lines.Add(new Text("trading-window-selected-pays", price, unitSize));
            lines.Add(stock <= 0 ? new Text("trading-window-selected-nodemand") : new Text("trading-window-selected-demand", stock));
            lines.Add(new Text("trading-window-hold-sell-hint"));
        }
        return lines;
    }

    /// <summary>A locked good: what it is and the tier that unlocks it.</summary>
    public static List<Text> LockedDetails(LockedRow row, StandingSummary? standing)
    {
        string tier = standing?.Tiers.ElementAtOrDefault(row.Tier)?.Code ?? "";
        var lines = new List<Text>
        {
            new("trading-window-selected", row.StackSize, new ItemRef(row.Code)),
            new(row.Reason switch
            {
                LockReason.Rare => "trading-window-locked-rare",
                LockReason.Leads => "trading-window-locked-leads",
                _ => "trading-window-locked-tier",
            }, tier.Length > 0 ? TierName(tier) : row.Tier),
        };
        if (row.Rotating) lines.Add(new Text("trading-window-locked-rotating"));
        return lines;
    }

    /// <summary>What the trader pays for a stack, or why not (the sell slot and the hover price in the
    /// player's inventory). <paramref name="offer"/> is the listed or off-list offer; null when the
    /// trader neither lists it nor prices goods off its list.</summary>
    public static List<Text> OfferLines(Offer? offer, bool listed, int sideBudget, int? stackSize = null)
    {
        if (offer is null) return [new Text("trading-window-pays-none")];
        if (!offer.Accepted) return [new Text(RefusalKey(offer.Refusal))];
        var lines = new List<Text>();
        if (listed)
            lines.Add(new Text("trading-economy-offer-listed", offer.UnitPrice, offer.UnitSize, F(offer.Supply)));
        else
        {
            lines.Add(new Text("trading-economy-offer-offlist", offer.UnitPrice, offer.UnitSize, F(offer.Base), F(offer.Spread), F(offer.Fit), F(offer.Supply)));
            if (Math.Abs(offer.Modifiers - 1) > 1e-3) lines.Add(new Text("trading-economy-offer-modifiers", F(offer.Modifiers)));
            lines.Add(new Text("trading-economy-offer-sidebudget", sideBudget));
        }
        if (stackSize is int n)
            lines.Add(n < offer.UnitSize
                ? new Text("trading-window-sell-short", offer.UnitSize)
                : new Text("trading-window-sell-lots", n / offer.UnitSize, offer.UnitPrice * (n / offer.UnitSize)));
        return lines;
    }

    /// <summary>The lang key for why a trader will not take something.</summary>
    public static string RefusalKey(Refusal refusal) => refusal switch
    {
        Refusal.MapOrLead => "trading-economy-refused-map",
        Refusal.Worthless => "trading-economy-refused-worthless",
        Refusal.NoValue => "trading-economy-refused-novalue",
        Refusal.Currency => "trading-economy-refused-currency",
        _ => "trading-economy-refused-toocheap",
    };

    // ---- Orders tab ----

    public static List<OrderLine> Orders(TradeWindowState state) => state.Orders
        .OrderBy(o => o.Mine ? 0 : 1).ThenBy(o => o.Id)
        .Select(o => o.Mine
            ? new OrderLine(o, new Text("trading-window-order-taken", o.Quantity, new ItemRef(o.Item), o.Delivered, F(o.UnitPrice),
                Math.Max(0, o.Premium - o.PremiumPaid), F(Math.Max(0, o.DaysLeft))), false, o.Held > 0 && o.Remaining > 0 && o.DaysLeft >= 0)
            : new OrderLine(o, new Text("trading-window-order-offer", o.Quantity, new ItemRef(o.Item), F(o.UnitPrice), o.Premium,
                F(o.Days), F(Math.Max(0, o.DaysLeft))), true, false))
        .ToList();

    // ---- Deliveries tab ----

    /// <summary>The 8-point compass direction (lang key) from a trader to a point <paramref name="dx"/>
    /// east and <paramref name="dz"/> south of it (the game's north is −z).</summary>
    public static Text Direction(double dx, double dz)
    {
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dz) < 1e-6) return new Text("trading-window-dir-here");
        double angle = Math.Atan2(dx, -dz) * 180 / Math.PI; // 0 north, 90 east
        int octant = (int)Math.Round(((angle % 360) + 360) % 360 / 45) % 8;
        string[] codes = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];
        return new Text("trading-window-dir-" + codes[octant]);
    }

    public static List<DeliveryLine> Deliveries(TradeWindowState state)
    {
        var lines = new List<DeliveryLine>();
        foreach (var d in state.Deliveries.OrderBy(d => d.ForHere ? 0 : 1).ThenBy(d => d.Id))
        {
            Text line = d.ForHere
                ? new Text(d.Carried ? "trading-window-delivery-forhere" : "trading-window-delivery-forhere-nopackage", d.Id, d.Deposit, d.Fee)
                : d.HoursLeft >= 0
                    ? new Text("trading-window-delivery-mine", d.Id, TypeName(d.ToType), F(Math.Round(d.Distance / 1000, 1)), Direction(d.Dx, d.Dz), TimeLeft(d.DaysLeft, d.HoursLeft), d.Deposit, d.Fee)
                    : new Text("trading-window-delivery-late", d.Id, TypeName(d.ToType), F(Math.Round(d.Distance / 1000, 1)), Direction(d.Dx, d.Dz), F(Math.Round(-d.HoursLeft, 1)));
            lines.Add(new DeliveryLine(d, line, false, d.ForHere && d.Carried, !d.ForHere));
        }
        if (state.DeliveryOffer is { } o)
            lines.Add(new DeliveryLine(null, new Text("trading-window-delivery-offer", TypeName(o.ToType), F(Math.Round(o.Distance / 1000, 1)),
                Direction(o.Dx, o.Dz), new Text("trading-window-days", F(Math.Round(o.Days, 1))), o.Deposit, o.Fee), true, false, true));
        else if (state.DeliveryWhy is { Length: > 0 } why)
            lines.Add(new DeliveryLine(null, new Text(why), false, false, false));
        return lines;
    }

    /// <summary>"2.5 days", or under a day "7.5 hours".</summary>
    public static Text TimeLeft(double days, double hours) =>
        days >= 1 ? new Text("trading-window-days", F(Math.Round(days, 1))) : new Text("trading-window-hours", F(Math.Round(Math.Max(0, hours), 1)));

    // ---- Maps & leads tab ----

    /// <summary>A map or lead offer on the shelf, to this player: sold out (stock gone or the sold-out
    /// marker), locked (a lead past the nearest camp, which their own standing does not buy), or for
    /// sale. One the player has already (<paramref name="owned"/>: <see cref="TradeWindowState.OwnedMaps"/>)
    /// is shown as such, unless it is sold out anyway.</summary>
    public static MapOfferStatus MapStatus(string? offer, string? leadKind, int stock, bool playerLeads, bool owned = false)
    {
        if (offer == "soldout" || stock <= 0) return MapOfferStatus.SoldOut;
        if (owned) return MapOfferStatus.Owned;
        if (offer == "lead" && LeadTargets.TryParse(leadKind, out var kind) && kind != LeadKind.Camp && !playerLeads) return MapOfferStatus.Locked;
        return MapOfferStatus.Available;
    }

    /// <summary>What the Maps &amp; leads tab says in place of an offer's price when it cannot be
    /// bought (sold out, locked with the tier that sells it, the player has it), or null for the
    /// price.</summary>
    public static Text? MapStatusText(MapOfferStatus status, Text? lockedTier) => status switch
    {
        MapOfferStatus.SoldOut => new Text("trading-window-map-soldout"),
        MapOfferStatus.Locked => new Text("trading-window-map-locked", (object?)lockedTier ?? ""),
        MapOfferStatus.Owned => new Text("trading-window-map-owned"),
        _ => null,
    };

    /// <summary>An ore map offer: metal, size class, distance, precision.</summary>
    public static Text OreMapLine(string metal, string? sizeClass, double distance, int precision) =>
        new("trading-window-map-ore", new Text("oremap-metal-" + metal), sizeClass is null ? new Text("trading-window-map-unsurveyed") : new Text("ore-size-" + sizeClass),
            F(Math.Round(distance)), precision);

    public static Text LeadLine(string? leadKind, string type, double distance, Text direction) =>
        LeadTargets.TryParse(leadKind, out var kind) && kind == LeadKind.Settlement
            ? new Text("trading-window-map-lead-settlement", F(Math.Round(distance)), direction)
            : new Text("trading-window-map-lead", TypeName(type), F(Math.Round(distance)), direction);

    // ---- Standing ----

    /// <summary>The five tiers with their thresholds in brackets, the current one marked.</summary>
    public static List<TierLine> Tiers(StandingSummary s) =>
        s.Tiers.Select((t, i) => new TierLine(new Text("trading-window-tier-row", TierName(t.Code), F(t.Points)), i == s.TierIndex)).ToList();

    /// <summary>What a tier gives, for the features that are on.</summary>
    public static List<Text> Facts(TierView t, WindowSwitches sw)
    {
        var u = t.Unlocks;
        var facts = new List<Text>();
        if (sw.StandingPrices && (Math.Abs(u.BuyPriceFactor - 1) > 1e-6 || Math.Abs(u.SellPriceFactor - 1) > 1e-6))
            facts.Add(new Text("trading-window-fact-prices", F(u.BuyPriceFactor), F(u.SellPriceFactor)));
        if (u.WalletTier > 0) facts.Add(new Text("trading-window-fact-wallet", u.WalletTier));
        if (sw.Orders) facts.Add(u.OrderScale <= 0 ? new Text("trading-window-fact-noorders") : new Text("trading-window-fact-orders", F(u.OrderScale)));
        if (sw.Deliveries)
            facts.Add(u.DeliveryScale <= 0 ? new Text("trading-window-fact-nodeliveries")
                : new Text("trading-window-fact-deliveries", F(DeliveryPlanner.Reach(u.DeliveryScale) / 1000)));
        if (sw.Maps)
        {
            facts.Add(new Text("trading-window-fact-maps", t.MapPrecision));
            if (u.MapsToTraders) facts.Add(new Text("trading-window-fact-leads"));
        }
        if (u.RareStock) facts.Add(new Text("trading-window-fact-rare"));
        return facts;
    }

    /// <summary>What the next tier adds over the current one.</summary>
    public static List<Text> Gains(TierView current, TierView next, WindowSwitches sw)
    {
        var now = Facts(current, sw).Select(f => f.ToString()).ToHashSet();
        return Facts(next, sw).Where(f => !now.Contains(f.ToString())).ToList();
    }

    /// <summary>How standing is earned here.</summary>
    public static List<Text> Earning(StandingSummary s, WindowSwitches sw, int ordersOnOffer, bool deliveryOffered)
    {
        var lines = new List<Text> { new("trading-window-earn-deals", F(s.Earn.PerGear)) };
        if (sw.Orders) lines.Add(new Text("trading-window-earn-orders", F(s.Earn.Order), ordersOnOffer));
        if (sw.Deliveries) lines.Add(new Text(deliveryOffered ? "trading-window-earn-deliveries-offered" : "trading-window-earn-deliveries", F(s.Earn.Delivery)));
        lines.Add(new Text("trading-window-earn-wants"));
        return lines;
    }

    /// <summary>The Standing tab below the tiers: you have, at the next tier, how to earn.</summary>
    public static List<(Text Heading, List<Text> Lines)> StandingSections(TradeWindowState state)
    {
        var s = state.Standing!;
        var sw = state.Switches;
        var sections = new List<(Text, List<Text>)>();
        var have = Facts(s.Tier, sw);
        sections.Add((new Text("trading-window-standing-have", TierName(s.Tier.Code)), have.Count > 0 ? have : [new Text("trading-window-fact-nothing")]));
        if (s.Next is { } next)
            sections.Add((new Text("trading-window-standing-next", TierName(next.Code), F(next.Points), F(Math.Max(0, next.Points - Math.Floor(s.Points)))),
                Gains(s.Tier, next, sw)));
        var extra = new List<Text>();
        if (s.Spill >= 1) extra.Add(new Text("trading-window-standing-spill", F(Math.Floor(s.Spill))));
        if (s.Company is double c) extra.Add(new Text("trading-window-standing-company", F(Math.Floor(c))));
        sections.Add((new Text("trading-window-standing-earn"),
            [.. Earning(s, sw, state.Orders.Count(o => !o.Mine), state.DeliveryOffer != null), .. extra]));
        return sections;
    }
}

/// <summary>
/// The trader's answer to "How do you see me these days?" (the pack's trader dialogue): in its own
/// voice, the tier and progress, what the tier gives and the next unlocks, and how to earn standing,
/// the raw numbers in square brackets after each line, so it is plain the trader isn't saying them:
/// "You're a regular here now, I'd say. [Regular · 310 points · trusted at 800]".
/// Generic for every trader type.
/// </summary>
public static class StandingSpeech
{
    /// <summary>The tier codes the lang file has a voice line for; another tier gets the generic one.</summary>
    public static readonly IReadOnlySet<string> Voiced = new HashSet<string> { "stranger", "known", "regular", "trusted", "partner" };

    public static List<SpeechLine> Build(TradeWindowState state)
    {
        var s = state.Standing;
        var sw = state.Switches;
        if (s is null || !sw.Standing) return [new SpeechLine(new Text("dialogue-standing-off"), [])];
        var lines = new List<SpeechLine>();
        var tier = s.Tier;
        var bracket = new List<Text> { new("trading-window-speech-points", TradeWindowModel.TierName(tier.Code), TradeWindowModel.F(Math.Floor(s.Points))) };
        if (s.Next is { } next) bracket.Add(new Text("trading-window-speech-next", TradeWindowModel.TierName(next.Code), TradeWindowModel.F(next.Points)));
        lines.Add(new SpeechLine(new Text(Voiced.Contains(tier.Code) ? "dialogue-standing-tier-" + tier.Code : "dialogue-standing-tier-other"), bracket));

        var have = TradeWindowModel.Facts(tier, sw);
        lines.Add(new SpeechLine(new Text(s.TierIndex == 0 ? "dialogue-standing-gives-first" : "dialogue-standing-gives"),
            have.Count > 0 ? have : [new Text("trading-window-fact-nothing")]));

        if (s.Next is { } n)
        {
            var gains = TradeWindowModel.Gains(tier, n, sw);
            lines.Add(new SpeechLine(new Text("dialogue-standing-next"), [new Text("trading-window-speech-at", TradeWindowModel.TierName(n.Code)), .. gains]));
        }
        else lines.Add(new SpeechLine(new Text("dialogue-standing-top"), []));

        lines.Add(new SpeechLine(new Text("dialogue-standing-earn"),
            TradeWindowModel.Earning(s, sw, state.Orders.Count(o => !o.Mine), state.DeliveryOffer != null)));
        return lines;
    }
}
