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
/// <param name="Fit">Fit factor (1 for listed goods).</param>
/// <param name="Supply">Supply factor (<see cref="PriceCurve"/>).</param>
/// <param name="Modifiers">The product of every <see cref="IPriceModifier"/>.</param>
/// <param name="UnitSize">Items per price unit: the trade item's stack size.</param>
/// <param name="UnitPrice">Gears per unit, the ResolvedTradeItem's Price.</param>
/// <param name="Capped">The sell-back cap lowered it (<see cref="Pricing.SellBackShare"/>).</param>
/// <param name="Budget">Which wallet pays.</param>
public sealed record Offer(Refusal Refusal, double Base, double Fit, double Supply, double Modifiers, int UnitSize, int UnitPrice, bool Capped, Budget Budget)
{
    public bool Accepted => Refusal == Refusal.None;

    public static Offer Refused(Refusal why) => new(why, 0, 0, 1, 1, 1, 0, false, Budget.Side);
}

/// <summary>
/// Prices (#450, #451). Goods on a trader's list keep the list's price as their base (curated,
/// within the value table's scale: the lists' buying prices are 0.94 × the table's value at the
/// median); goods off its list are priced from the value table (#449). Both are then scaled by the
/// regional supply factor and the modifiers; a trader buying goods it also sells pays at most
/// <see cref="SellBackShare"/> of its own selling price for them.
/// </summary>
public static class Pricing
{
    /// <summary>The most a trader pays for goods it sells, as a share of its selling price.</summary>
    public const double SellBackShare = 0.6;

    /// <summary>A listed entry's price: <paramref name="listPrice"/> per its stack, scaled; at least
    /// 1. <paramref name="sellPricePerItem"/>: what the trader asks per item for the same goods, if it
    /// sells them (buying sides only).</summary>
    public static Offer Listed(double listPrice, int stackSize, double supply, double modifiers, double? sellPricePerItem = null, bool traderBuys = true)
    {
        double price = listPrice * supply * modifiers;
        bool capped = false;
        if (traderBuys && sellPricePerItem is double sell && price > SellBackShare * sell * stackSize)
        {
            price = SellBackShare * sell * stackSize;
            capped = true;
        }
        int unitPrice = Math.Max(1, (int)Math.Round(price, MidpointRounding.AwayFromZero));
        return new Offer(Refusal.None, listPrice, 1, supply, modifiers, Math.Max(1, stackSize), unitPrice, capped, Budget.Main);
    }

    /// <summary>
    /// What a trader offers for goods off its list. Per item: value × fit × supply × modifiers, capped
    /// by the sell-back share. The unit is one item when that is a gear or more, else the fewest items
    /// worth a gear (so a stack of planks sells by the 17, at one gear): the game prices trades in whole
    /// gears per unit. Under a gear per full stack it is <see cref="Refusal.TooCheap"/>.
    /// </summary>
    public static Offer OffList(double valuePerItem, bool worthless, double fit, double supply, double modifiers, int maxStackSize,
        double? sellPricePerItem = null)
    {
        if (worthless) return Offer.Refused(Refusal.Worthless);
        if (valuePerItem <= 0) return Offer.Refused(Refusal.NoValue);
        double perItem = valuePerItem * fit * supply * modifiers;
        bool capped = false;
        if (sellPricePerItem is double sell && perItem > SellBackShare * sell)
        {
            perItem = SellBackShare * sell;
            capped = true;
        }
        int maxStack = Math.Max(1, maxStackSize);
        if (perItem * maxStack < 1 - 1e-9)
            return Offer.Refused(Refusal.TooCheap) with { Base = valuePerItem, Fit = fit, Supply = supply, Modifiers = modifiers, Capped = capped };
        int unit = perItem >= 1 ? 1 : Math.Min(maxStack, (int)Math.Ceiling(1 / perItem - 1e-9));
        int unitPrice = Math.Max(1, (int)Math.Round(unit * perItem, MidpointRounding.AwayFromZero));
        return new Offer(Refusal.None, valuePerItem, fit, supply, modifiers, unit, unitPrice, capped, Budget.Side);
    }

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
