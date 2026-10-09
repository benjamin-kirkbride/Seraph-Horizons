using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Economy.Core;

/// <summary>Which of a trader's two wallets pays for goods it buys: the main one (vanilla's money
/// slot) for what its list buys, the side budget for everything else (#450).</summary>
public enum Budget { Main, Side }

/// <summary>Why a trader will not take something at all.</summary>
public enum Refusal
{
    None,
    /// <summary>A map or a lead (config <c>refused</c>): what they point at is the trader's own business.</summary>
    MapOrLead,
    /// <summary>Under a gear per full stack in the value table.</summary>
    Worthless,
    /// <summary>Neither the item nor a variant family of it is in the value table.</summary>
    NoValue,
    /// <summary>Money.</summary>
    Currency,
    /// <summary>Worth something, but at this trader's fit and supply under a gear per full stack.</summary>
    TooCheap,
}

/// <summary>Who prices what, for an <see cref="IPriceModifier"/>.</summary>
/// <param name="ItemCode">Full code, <c>game:ingot-iron</c>.</param>
/// <param name="TraderType">One of the eleven.</param>
/// <param name="SupplyRegion">The supply region key (<see cref="SupplyRegion"/>).</param>
/// <param name="TraderBuys">True when the trader buys from the player, false when it sells.</param>
/// <param name="TraderId">The trader entity's id.</param>
/// <param name="PlayerUid">The player trading, when known (null when a price is shown without one).</param>
public readonly record struct PriceContext(string ItemCode, string TraderType, string SupplyRegion, bool TraderBuys, long TraderId, string? PlayerUid);

/// <summary>
/// A factor on a price, after base value, fit and supply: the hook standing (#452, #463) uses for
/// better prices. Modifiers multiply. A modifier runs on both sides (the client shows the price it
/// computes from the same inputs), so it may only read what both sides know: the trader's watched
/// attributes, or data it syncs itself.
/// </summary>
public interface IPriceModifier
{
    /// <summary>A short name for the price breakdown (<c>standing</c>).</summary>
    string Name { get; }

    /// <summary>The factor for this trade; 1 for no change.</summary>
    double Factor(in PriceContext context);
}

/// <summary>
/// The supply price curve (#451): how a regional supply level scales an item's price. f(0) = 1,
/// falling towards <see cref="Floor"/> as the level rises, halfway there at <see cref="HalfLevel"/>:
/// <c>f(L) = floor + (1 − floor) / (1 + L / half)</c>. A hyperbola, not an exponential, so a glut
/// keeps lowering the price a little at a time without ever going under the floor.
/// </summary>
public readonly record struct PriceCurve(double Floor = 0.3, double HalfLevel = 5)
{
    public static readonly PriceCurve Default = new(0.3, 5);

    public double Factor(double level) => level <= 0 ? 1 : Floor + (1 - Floor) / (1 + level / HalfLevel);
}

/// <summary>A price with what went into it, for the price breakdown and the deal.</summary>
/// <param name="Refusal">Why not, or <see cref="Refusal.None"/>.</param>
/// <param name="Base">Base price per item (value table) or per list stack (list price).</param>
/// <param name="Fit">The share of value paid: the fit off the list (<see cref="TraderRelations"/>), the
/// own-shelf rate (<see cref="ListPriceRules.OwnShelf"/>) for goods on the trader's own shelf, 1 for
/// listed goods.</param>
/// <param name="Supply">Supply factor (<see cref="PriceCurve"/>).</param>
/// <param name="Modifiers">The product of every <see cref="IPriceModifier"/>.</param>
/// <param name="UnitSize">Items per price unit: the trade item's stack size.</param>
/// <param name="UnitPrice">Gears per unit, the ResolvedTradeItem's Price.</param>
/// <param name="Budget">Which wallet pays.</param>
/// <param name="OwnShelf">Bought back off-market because the trader has it on its own selling shelf
/// (<see cref="Pricing.OwnShelf"/>).</param>
public sealed record Offer(Refusal Refusal, double Base, double Fit, double Supply, double Modifiers, int UnitSize, int UnitPrice, Budget Budget,
    bool OwnShelf = false)
{
    public bool Accepted => Refusal == Refusal.None;

    public static Offer Refused(Refusal why) => new(why, 0, 0, 1, 1, 1, 0, Budget.Side);
}

/// <summary>
/// How listed goods are priced from the item value table (2026-10-08),
/// <c>assets/seraphhorizons/config/trading/list-prices.json</c>: a trader asks an item's value ×
/// <see cref="Sell"/> and its list buys it at value × <see cref="Buy"/>, both × one roll per item
/// per trader per restock, uniform within <see cref="Roll"/> either way (<see cref="Pricing.Roll"/>),
/// the same on both sides of its list. What a trader has on its own selling shelf it buys only
/// off-market, at value × <see cref="OwnShelf"/>, from the side budget.
/// </summary>
public sealed record ListPriceRules
{
    public static readonly ListPriceRules Default = new();

    /// <summary>What a trader asks for listed goods, as a share of their value.</summary>
    public double Sell { get; init; } = 1.0;

    /// <summary>What a trader pays for the goods its list buys, as a share of their value.</summary>
    public double Buy { get; init; } = 1.5;

    /// <summary>A listed item's price varies by up to this share either way.</summary>
    public double Roll { get; init; } = 0.25;

    /// <summary>What a trader pays for goods on its own selling shelf, as a share of their value.</summary>
    public double OwnShelf { get; init; } = 0.2;

    /// <summary>Reads the file (<c>{ "sell", "buy", "roll", "ownShelf" }</c>; comments and trailing
    /// commas allowed); a missing field keeps its default.</summary>
    public static ListPriceRules Parse(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
        {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var root = doc.RootElement;
        double Num(string name, double fallback) => root.TryGetProperty(name, out var p) ? p.GetDouble() : fallback;
        return new ListPriceRules
        {
            Sell = Num("sell", Default.Sell),
            Buy = Num("buy", Default.Buy),
            Roll = Num("roll", Default.Roll),
            OwnShelf = Num("ownShelf", Default.OwnShelf),
        };
    }

    /// <summary>What is wrong: factors not above 0, a roll outside [0, 1).</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        if (Sell <= 0) problems.Add($"sell {Sell} is not above 0");
        if (Buy <= 0) problems.Add($"buy {Buy} is not above 0");
        if (OwnShelf <= 0) problems.Add($"ownShelf {OwnShelf} is not above 0");
        if (Roll < 0 || Roll >= 1) problems.Add($"roll {Roll} is outside [0, 1)");
        return problems;
    }
}

/// <summary>
/// Prices (#450, #451; 2026-10-08), all from the item value table (#449). A listed good is priced
/// by the list price rules (<see cref="ListPriceRules"/>: its value × 1 to sell, × 1.5 to buy, × a
/// roll per item per trader per restock); a list entry may override its price with a reason
/// (schematics, maps). Goods off a trader's list are their value × the fit (three quarters for goods
/// a related trader buys, paid from its main wallet; a fifth for anything else and three tenths at
/// the curio dealer, from its side budget), and what it has on its own shelf it only buys back
/// off-market (<see cref="OwnShelf"/>). All are then scaled by the regional supply factor and the
/// modifiers.
/// </summary>
public static class Pricing
{
    /// <summary>A price roll, uniform in [1 − <paramref name="spread"/>, 1 + <paramref name="spread"/>].</summary>
    public static double Roll(Random rng, double spread) => 1 - spread + 2 * spread * rng.NextDouble();

    /// <summary>
    /// A list entry's base price per its stack, before supply and modifiers: its override as it
    /// stands (<see cref="TradeEntry.Price"/>, not rolled), else the item's value per item × the
    /// entry's stack × the rules' buy or sell factor × the <paramref name="roll"/>. Null when the
    /// entry has neither (it keeps its placeholder price).
    /// </summary>
    public static double? ListBase(TradeEntry entry, double valuePerItem, bool traderBuys, double roll, ListPriceRules rules)
    {
        if (entry.Price is double price) return price;
        if (valuePerItem <= 0) return null;
        return valuePerItem * Math.Max(1, entry.StackSize) * (traderBuys ? rules.Buy : rules.Sell) * roll;
    }

    /// <summary>
    /// A listed entry's price: <paramref name="listPrice"/> per its stack (<see cref="ListBase"/>),
    /// scaled; at least 1. A buying entry worth under a gear per stack is bought by a bigger unit, a
    /// whole number of the entry's stacks up to <paramref name="maxStackSize"/>, the fewest worth a
    /// gear (a cheap stack would otherwise round up to a gear and pay many times its value). Selling
    /// entries keep their stack.
    /// </summary>
    public static Offer Listed(double listPrice, int stackSize, double supply, double modifiers, bool traderBuys = true, int maxStackSize = 0)
    {
        int stack = Math.Max(1, stackSize);
        double price = listPrice * supply * modifiers;
        int unit = stack;
        if (traderBuys && price > 0 && price < 1 - 1e-9 && maxStackSize > stack)
        {
            int k = Math.Min(maxStackSize / stack, (int)Math.Ceiling(1 / price - 1e-9));
            if (k > 1)
            {
                unit = stack * k;
                price *= k;
            }
        }
        int unitPrice = Math.Max(1, (int)Math.Round(price, MidpointRounding.AwayFromZero));
        return new Offer(Refusal.None, listPrice, 1, supply, modifiers, unit, unitPrice, Budget.Main);
    }

    /// <summary>
    /// What a trader offers for goods off its list. Per item: value × fit × supply ×
    /// modifiers. The unit is one item when that is a gear or more, else the fewest items worth a gear
    /// (so a stack of planks sells by the 17, at one gear): the game prices trades in whole gears per
    /// unit. Under a gear per full stack it is <see cref="Refusal.TooCheap"/>. <paramref name="budget"/>
    /// is the wallet that pays (<see cref="TraderRelations.PaysFromMain"/>).
    /// </summary>
    public static Offer OffList(double valuePerItem, bool worthless, double fit, double supply, double modifiers, int maxStackSize,
        Budget budget = Budget.Side)
    {
        if (worthless) return Offer.Refused(Refusal.Worthless);
        if (valuePerItem <= 0) return Offer.Refused(Refusal.NoValue);
        double perItem = valuePerItem * fit * supply * modifiers;
        int maxStack = Math.Max(1, maxStackSize);
        if (perItem * maxStack < 1 - 1e-9)
            return Offer.Refused(Refusal.TooCheap) with { Base = valuePerItem, Fit = fit, Supply = supply, Modifiers = modifiers };
        int unit = perItem >= 1 ? 1 : Math.Min(maxStack, (int)Math.Ceiling(1 / perItem - 1e-9));
        int unitPrice = Math.Max(1, (int)Math.Round(unit * perItem, MidpointRounding.AwayFromZero));
        return new Offer(Refusal.None, valuePerItem, fit, supply, modifiers, unit, unitPrice, budget);
    }

    /// <summary>
    /// What a trader pays for goods it has on its own selling shelf (in stock), listed or not: it
    /// does not pay its list's price for what it is selling itself, but buys it back off-market at
    /// <paramref name="rate"/> (<see cref="ListPriceRules.OwnShelf"/>) of value × supply ×
    /// modifiers, with no fit, from the side budget. Priced and refused as
    /// <see cref="OffList"/>, with the rate as its fit.
    /// </summary>
    public static Offer OwnShelf(double valuePerItem, bool worthless, double rate, double supply, double modifiers, int maxStackSize) =>
        OffList(valuePerItem, worthless, rate, supply, modifiers, maxStackSize) with { OwnShelf = true };

    /// <summary>The product of the modifiers' factors (1 with none); a factor below 0 counts as 0.</summary>
    public static double Modifiers(IEnumerable<IPriceModifier> modifiers, in PriceContext context)
    {
        double f = 1;
        foreach (var m in modifiers) f *= Math.Max(0, m.Factor(context));
        return f;
    }
}

/// <summary>
/// The side budget (#450): a second wallet that pays for goods off a trader's list, a share
/// (<see cref="Share"/>) of the main wallet's restock amount, refilled to that at every restock.
/// </summary>
public static class SideBudget
{
    public const double Share = 0.25;

    public static int RefillTo(double walletAvg, double share = Share) => Math.Max(0, (int)Math.Round(walletAvg * share, MidpointRounding.AwayFromZero));

    /// <summary>Whether a deal can be paid: the side budget covers the off-list gain on its own, and
    /// the main wallet, plus what the player pays, covers the rest (as vanilla's check).</summary>
    public static bool CanPay(int mainWallet, int sideBudget, int cost, int mainGain, int sideGain) =>
        sideGain <= sideBudget && mainWallet + cost - mainGain >= 0;

    /// <summary>Totals a selling cart by budget: (gears per unit, units, budget) per line.</summary>
    public static (int Main, int Side) Split(IEnumerable<(int UnitPrice, int Units, Budget Budget)> lines)
    {
        int main = 0, side = 0;
        foreach (var (price, units, budget) in lines)
            if (budget == Budget.Side) side += price * units;
            else main += price * units;
        return (main, side);
    }
}
