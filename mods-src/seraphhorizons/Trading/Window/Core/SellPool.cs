using SeraphHorizons.Mod.Trading.Economy.Core;

namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>
/// One sell slot's goods the trader buys, as the pooled sale sees them: <see cref="Items"/> items at
/// <see cref="UnitPrice"/> gears per <see cref="UnitSize"/> (the listed or off-list offer), paid from
/// <see cref="Budget"/>. <see cref="Good"/> groups slots holding the same good for the trader's demand
/// (<see cref="Demand"/>, in units of <see cref="UnitSize"/>; null when only the side budget limits it,
/// as off its list).
/// </summary>
public sealed record SellLine(int Slot, int Items, int UnitSize, int UnitPrice, Budget Budget, string Good, int? Demand = null)
{
    /// <summary>Gears per item.</summary>
    public double Rate => UnitPrice / (double)Math.Max(1, UnitSize);
}

/// <summary>Items a lot takes from one sell slot.</summary>
public sealed record SellTake(int Slot, int Items, string Good, int UnitSize, Budget Budget);

/// <summary>
/// One hold's sale: <see cref="Gears"/> whole gears paid (<see cref="MainGears"/> from the trader's
/// wallet, <see cref="SideGears"/> from its side budget) for <see cref="Takes"/>, and how many units of
/// each listed good's demand that uses up (<see cref="DemandUsed"/>).
/// </summary>
public sealed record SellLot(int Gears, int MainGears, int SideGears, double Value, IReadOnlyList<SellTake> Takes, IReadOnlyDictionary<string, int> DemandUsed)
{
    public int Items => Takes.Sum(t => t.Items);
}

/// <summary>
/// The trade window's pooled sale (the playtest after #436): four sell slots, valued together. What
/// the trader pays is whole gears for whole items, never more than the items are worth, so goods worth
/// under a gear in one slot (a few dirt, the end of a stack) count with the rest.
///
/// <list type="bullet">
/// <item>The pool's value is every item's worth (its offer's gears per unit ÷ the unit's size), only as
/// many of a listed good as the trader's demand takes.</item>
/// <item>A hold sells one lot, as one unit sold before: the lot's target is the first good's unit
/// price (in the order below), at most the pool's whole gears. Items are taken in a stable order, the
/// dearest per item first (then by slot), only as many as reach the target, so what is left over
/// stays in the slots. The lot pays the floor of what it took is worth: the trader never pays more
/// than the goods are worth, and at most a fraction of the last (cheapest) item goes unpaid.</item>
/// <item>The pay is split between the wallet and the side budget in proportion to what each kind of
/// good in the lot is worth.</item>
/// </list>
/// Game-independent: the game side reads the slots and the offers into <see cref="SellLine"/>s, and
/// pays and takes what <see cref="NextLot"/> says.
/// </summary>
public static class SellPool
{
    private const double Eps = 1e-9;

    /// <summary>The lines in the order a lot takes from them: dearest per item first, then by slot.</summary>
    public static List<SellLine> Ordered(IEnumerable<SellLine> lines) =>
        lines.Where(l => l.Items > 0 && l.UnitPrice > 0).OrderByDescending(l => l.Rate).ThenBy(l => l.Slot).ToList();

    /// <summary>How many items of each line the trader takes at most: a listed good only as many as its
    /// demand's units hold, shared by the slots holding it, in order.</summary>
    public static List<(SellLine Line, int Items)> Sellable(IEnumerable<SellLine> lines)
    {
        var left = new Dictionary<string, int>();
        var result = new List<(SellLine, int)>();
        foreach (var line in Ordered(lines))
        {
            int items = line.Items;
            if (line.Demand is int demand)
            {
                if (!left.TryGetValue(line.Good, out int cap)) cap = Math.Max(0, demand) * Math.Max(1, line.UnitSize);
                items = Math.Min(items, cap);
                left[line.Good] = cap - items;
            }
            if (items > 0) result.Add((line, items));
        }
        return result;
    }

    /// <summary>What everything the trader would take from the slots is worth, in gears (not rounded).</summary>
    public static double Value(IEnumerable<SellLine> lines) => Sellable(lines).Sum(s => s.Items * s.Line.Rate);

    /// <summary>The whole gears all of it would fetch, lot by lot.</summary>
    public static int Gears(IEnumerable<SellLine> lines) => (int)Math.Floor(Value(lines) + Eps);

    /// <summary>The next lot a hold sells, or null when the slots are not worth a whole gear.</summary>
    public static SellLot? NextLot(IEnumerable<SellLine> lines)
    {
        var sellable = Sellable(lines);
        if (sellable.Count == 0) return null;
        double pool = sellable.Sum(s => s.Items * s.Line.Rate);
        int whole = (int)Math.Floor(pool + Eps);
        if (whole < 1) return null;
        int target = Math.Min(Math.Max(1, sellable[0].Line.UnitPrice), whole);

        var takes = new List<SellTake>();
        double value = 0, side = 0;
        foreach (var (line, items) in sellable)
        {
            if (value >= target - Eps) break;
            int k = Math.Min(items, (int)Math.Ceiling((target - value) / line.Rate - Eps));
            if (k <= 0) continue;
            takes.Add(new SellTake(line.Slot, k, line.Good, line.UnitSize, line.Budget));
            value += k * line.Rate;
            if (line.Budget == Budget.Side) side += k * line.Rate;
        }
        int gears = (int)Math.Floor(value + Eps);
        if (gears < 1) return null;
        int sideGears = value <= 0 ? 0 : Math.Clamp((int)Math.Round(gears * side / value, MidpointRounding.AwayFromZero), 0, gears);
        var demand = new Dictionary<string, int>();
        foreach (var group in takes.Where(t => sellable.Any(s => s.Line.Good == t.Good && s.Line.Demand != null)).GroupBy(t => t.Good))
            demand[group.Key] = (int)Math.Ceiling(group.Sum(t => t.Items) / (double)Math.Max(1, group.First().UnitSize) - Eps);
        return new SellLot(gears, gears - sideGears, sideGears, value, takes, demand);
    }
}
