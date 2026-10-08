using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>What a standing tier gets of leads to other camps: how many are on the shelf at once,
/// how far from the selling trader they reach (blocks), and the price's standing discount.</summary>
public sealed class CampLeadTier
{
    public int Maps { get; set; }
    public int Radius { get; set; }
    public double Discount { get; set; } = 1;
}

/// <summary>
/// The <c>campLeads</c> part of <c>config/trading/map-prices.json</c>: leads to other trader camps
/// (the user's goal: "you can always buy a map to a trader within some radius that you don't
/// already have"). Per standing tier (by code) the maps on the shelf and their radius; a stranger
/// gets one map per trader per group ever, to the nearest camp they lack within
/// <see cref="StrangerReach"/>. The price is exponential in distance and in how many maps the
/// group has bought from this trader (<see cref="Price"/>).
/// </summary>
public sealed class CampLeadRules
{
    /// <summary>The tier code the stranger's rule applies to.</summary>
    public const string Stranger = "stranger";

    /// <summary>Gears for a map to a camp at distance 0, before the factors.</summary>
    public double Base { get; set; } = 2;
    /// <summary>The price doubles every this many blocks of distance from the trader.</summary>
    public double DoublingDistance { get; set; } = 2500;
    /// <summary>Times the price per map the group has bought from this trader already.</summary>
    public double PerBought { get; set; } = 2;
    /// <summary>How far a stranger's one map may reach for the nearest camp they lack, in blocks.</summary>
    public int StrangerReach { get; set; } = 8000;
    /// <summary>By tier code. A tier not listed gets the stranger's rule.</summary>
    public Dictionary<string, CampLeadTier> Tiers { get; set; } = new();

    /// <summary>The tier's rule, or null for the stranger's (one map, chained).</summary>
    public CampLeadTier? TierFor(string? code) =>
        code is null || code == Stranger || !Tiers.TryGetValue(code, out var t) ? null : t;

    /// <summary>The stranger's discount (1 if not listed).</summary>
    public double DiscountFor(string? code) =>
        code != null && Tiers.TryGetValue(code, out var t) ? t.Discount : 1;

    /// <summary>
    /// base × 2^(distance / doublingDistance) × perBought^bought × the tier's discount, rounded, at
    /// least 1 gear. The discount takes the place of standing's buy price factor for these maps.
    /// </summary>
    public int Price(double distance, int bought, string? tierCode)
    {
        double d = Math.Max(0, distance) / Math.Max(1, DoublingDistance);
        double gears = Base * Math.Pow(2, d) * Math.Pow(Math.Max(1, PerBought), Math.Max(0, bought)) * DiscountFor(tierCode);
        return gears >= int.MaxValue ? int.MaxValue : MapPriceTable.Round(gears);
    }

    public List<string> Problems()
    {
        var problems = new List<string>();
        if (Base <= 0) problems.Add("campLeads.base is 0 or less");
        if (DoublingDistance <= 0) problems.Add("campLeads.doublingDistance is 0 or less");
        if (PerBought < 1) problems.Add("campLeads.perBought is under 1");
        if (StrangerReach <= 0) problems.Add("campLeads.strangerReach is 0 or less");
        if (Tiers.Count == 0) problems.Add("campLeads has no tiers");
        foreach (var (code, t) in Tiers)
        {
            if (t.Maps < 1) problems.Add($"campLeads tier '{code}' has no maps");
            if (t.Radius <= 0) problems.Add($"campLeads tier '{code}' has a radius of 0 or less");
            if (t.Discount <= 0 || t.Discount > 1) problems.Add($"campLeads tier '{code}' has a discount outside (0, 1]");
        }
        return problems;
    }
}

/// <summary>A camp as the lead picker sees it, from the trader: its cell, type, site (the placed
/// camp, or the spot it waits for) and distance.</summary>
public readonly record struct CampOption(CellKey Cell, string Type, int X, int Z, double Distance);

/// <summary>A lead on the shelf for one buyer: the camp, its distance, its price, and whether it is
/// the prospector the first slot is kept for.</summary>
public readonly record struct CampLeadOffer(CellKey Cell, string Type, int X, int Z, double Distance, int Price, bool Prospector);

/// <summary>Why a buyer has no camp lead on offer.</summary>
public enum CampLeadsWhy
{
    /// <summary>There are offers.</summary>
    None,
    /// <summary>A stranger whose group has had its one map from this trader.</summary>
    StrangerUsed,
    /// <summary>Every camp in reach is one they have (marked, carried, or for a stranger visited).</summary>
    NoneInReach,
}

/// <summary>What the picker knows of one buyer (their group's and their own map).</summary>
public sealed class LeadBuyer
{
    /// <summary>Their standing tier's code here.</summary>
    public string Tier { get; init; } = CampLeadRules.Stranger;
    /// <summary>Camps they have: on their map (any precision) or a lead to it carried or being drawn.</summary>
    public ISet<CellKey> Have { get; init; } = new HashSet<CellKey>();
    /// <summary>Camps their group has visited (met the trader at).</summary>
    public ISet<CellKey> Visited { get; init; } = new HashSet<CellKey>();
    /// <summary>Maps their group has bought from this trader.</summary>
    public int Bought { get; init; }
    /// <summary>Whether their group has had its stranger's map from this trader.</summary>
    public bool StrangerUsed { get; init; }
}

/// <summary>
/// Which camp leads a trader offers one buyer, and at what price.
/// <list type="bullet">
/// <item>From "known" up (<see cref="CampLeadRules.TierFor"/>): the tier's number of the nearest
/// camps within its radius that the buyer lacks (<see cref="LeadBuyer.Have"/>); if no prospector they
/// have is within the radius, the first slot is the nearest prospector they lack there. Buying one
/// makes it theirs, so the next-nearest takes the slot.</item>
/// <item>A stranger: one map per trader per group, ever: to the nearest camp they lack that their
/// group has not visited either, within <see cref="CampLeadRules.StrangerReach"/>. Every new camp sells
/// one onward, so a stranger chains from camp to camp but cannot map a region from one trader.</item>
/// </list>
/// The trader's own cell is never a target. The price of every offer counts the maps already bought
/// here (<see cref="CampLeadRules.Price"/>): the next one bought costs that, the one after twice as
/// much again.
/// </summary>
public static class CampLeads
{
    public static (List<CampLeadOffer> Offers, CampLeadsWhy Why) Pick(CampLeadRules rules, CellKey own, IEnumerable<CampOption> camps, LeadBuyer buyer)
    {
        var sorted = camps.Where(c => c.Cell != own)
            .OrderBy(c => c.Distance).ThenBy(c => c.Cell.X).ThenBy(c => c.Cell.Z)
            .ToList();
        var tier = rules.TierFor(buyer.Tier);
        var offers = new List<CampLeadOffer>();
        CampLeadOffer Offer(CampOption c, bool prospector) =>
            new(c.Cell, c.Type, c.X, c.Z, c.Distance, rules.Price(c.Distance, buyer.Bought, buyer.Tier), prospector);
        if (tier is null)
        {
            if (buyer.StrangerUsed) return (offers, CampLeadsWhy.StrangerUsed);
            var first = sorted.FirstOrDefault(c => c.Distance <= rules.StrangerReach && !buyer.Have.Contains(c.Cell) && !buyer.Visited.Contains(c.Cell));
            if (first.Type is null) return (offers, CampLeadsWhy.NoneInReach);
            offers.Add(Offer(first, false));
            return (offers, CampLeadsWhy.None);
        }
        var inReach = sorted.Where(c => c.Distance <= tier.Radius).ToList();
        var lacking = inReach.Where(c => !buyer.Have.Contains(c.Cell)).ToList();
        bool hasProspector = inReach.Any(c => c.Type == TraderTypes.Prospector && buyer.Have.Contains(c.Cell));
        var taken = new HashSet<CellKey>();
        if (!hasProspector && lacking.FirstOrDefault(c => c.Type == TraderTypes.Prospector) is { Type: not null } p)
        {
            offers.Add(Offer(p, true));
            taken.Add(p.Cell);
        }
        foreach (var c in lacking)
        {
            if (offers.Count >= tier.Maps) break;
            if (taken.Add(c.Cell)) offers.Add(Offer(c, false));
        }
        return (offers, offers.Count > 0 ? CampLeadsWhy.None : CampLeadsWhy.NoneInReach);
    }

    /// <summary>The camp cells within <paramref name="reach"/> blocks of (x, z), as rings of grid
    /// cells to look at (a camp is somewhere in its cell, so one cell more than the reach).</summary>
    public static IEnumerable<CellKey> CellsAround(int x, int z, int reach)
    {
        var centre = TraderGrid.CellOf(x, z);
        int n = reach / TraderGrid.CellSize + 1;
        for (int dx = -n; dx <= n; dx++)
            for (int dz = -n; dz <= n; dz++)
                yield return new CellKey(centre.X + dx, centre.Z + dz);
    }

    public static double Distance(int ax, int az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
