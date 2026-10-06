namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>A deposit a trader could sell a map to: its id, metal (or <c>gravel</c>), distance,
/// whether it is still unsold, and its last measured size class (null: unsurveyed).</summary>
public readonly record struct DepositOption(string Id, string Metal, double Distance, bool Unsold, string? SizeClass = null);

/// <summary>
/// Which maps a trader offers (#455). The shelf is filled at a restock with no buyer, so it shows
/// the offer and the buyer's standing decides at the deal: the precision of an ore map
/// (<see cref="MaxPrecision"/>) and whether leads past the nearest camp are for sale.
/// </summary>
public static class MapOffers
{
    /// <summary>The best precision a buyer's <c>mapTier</c> unlock allows: 1 (within 400 m) for a
    /// stranger, 2 at map tier 1, 3 (exact) from 2.</summary>
    public static int MaxPrecision(int mapTier) => Math.Clamp(mapTier + 1, 1, 3);

    /// <summary>One offer per metal, the nearest deposit neither sold nor <paramref name="reserved"/>
    /// (a sale being checked), the metals nearest first, at most <paramref name="max"/>. Sold out:
    /// there were deposits in range but none is left to sell.</summary>
    public static (List<DepositOption> Offers, bool SoldOut) PickOre(IEnumerable<DepositOption> options, int max, ISet<string>? reserved = null)
    {
        var all = options.ToList();
        var offers = all.Where(o => o.Unsold && reserved?.Contains(o.Id) != true)
            .GroupBy(o => o.Metal)
            .Select(g => g.OrderBy(o => o.Distance).ThenBy(o => o.Id, StringComparer.Ordinal).First())
            .OrderBy(o => o.Distance).ThenBy(o => o.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, max))
            .ToList();
        return (offers, all.Count > 0 && offers.Count == 0);
    }

    /// <summary>The nearest unsold, unreserved gravel field, or none; sold out as for ore.</summary>
    public static (DepositOption? Offer, bool SoldOut) PickGravel(IEnumerable<DepositOption> options, ISet<string>? reserved = null)
    {
        var (offers, soldOut) = PickOre(options.Select(o => o with { Metal = "gravel" }), 1, reserved);
        return (offers.Count > 0 ? offers[0] : null, soldOut);
    }
}
