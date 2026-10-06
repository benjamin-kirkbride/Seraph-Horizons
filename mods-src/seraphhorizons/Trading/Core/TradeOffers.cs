namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>
/// Turns a list's special entries (<see cref="TradeEntry.Kind"/>: ore and gravel maps, leads, #455)
/// into the offers a trader makes at a restock. A special entry in the core is replaced, where it
/// stands, by what the expander makes of it (none, one, several); in the rotating pool it is left
/// out. Over <see cref="TradeListResolver.Slots"/>, <see cref="TradeEntry.Optional"/> offers give way
/// first (from the last), then the core is cut from the end as the resolver cuts it; the rotating
/// slots shrink to what is left.
/// </summary>
public static class TradeOffers
{
    public static ResolvedList Expand(ResolvedList list, Func<TradeEntry, IEnumerable<TradeEntry>>? expand) =>
        list with { Selling = Expand(list.Selling, expand), Buying = Expand(list.Buying, expand) };

    public static ResolvedSide Expand(ResolvedSide side, Func<TradeEntry, IEnumerable<TradeEntry>>? expand)
    {
        if (!side.Core.Any(e => e.Kind != null) && !side.Rotating.Any(e => e.Kind != null)) return side;
        var keys = new HashSet<string>();
        var core = new List<TradeEntry>();
        foreach (var e in side.Core)
        {
            if (e.Kind is null)
            {
                if (keys.Add(e.Key)) core.Add(e);
                continue;
            }
            if (expand is null) continue;
            foreach (var offer in expand(e))
                if (offer.Kind is null && keys.Add(offer.Key)) core.Add(offer);
        }
        for (int i = core.Count - 1; i >= 0 && core.Count > TradeListResolver.Slots; i--)
            if (core[i].Optional) core.RemoveAt(i);
        if (core.Count > TradeListResolver.Slots) core.RemoveRange(TradeListResolver.Slots, core.Count - TradeListResolver.Slots);
        var rotating = side.Rotating.Where(e => e.Kind is null && !keys.Contains(e.Key)).ToList();
        int max = Math.Max(0, Math.Min(Math.Min(side.MaxRotating, TradeListResolver.Slots - core.Count), rotating.Count));
        return new ResolvedSide(core, rotating, max);
    }
}
