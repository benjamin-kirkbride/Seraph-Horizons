using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>What a standing tier gets of leads to other camps: how many are on offer at once, how
/// many rings of grid cells around the selling trader's cell they reach, the price curves' ease
/// (how steeply distance and leads bought raise the price) and the price's standing discount.</summary>
public sealed class CampLeadTier
{
    public int Maps { get; set; } = 1;
    /// <summary>Rings of grid cells (<see cref="CampLeads.Ring"/>) the leads reach.</summary>
    public int Reach { get; set; } = 1;
    /// <summary>Times both curves' strength (1: the full curve).</summary>
    public double Ease { get; set; } = 1;
    public double Discount { get; set; } = 1;
}

/// <summary>A player's very first map (the "pity map"): a flat price, to the nearest prospector
/// within <see cref="Reach"/> rings, else the nearest camp of any type there.</summary>
public sealed class PityMapRules
{
    public double Price { get; set; } = 10;
    public int Reach { get; set; } = 10;
}

/// <summary>
/// The <c>campLeads</c> part of <c>config/trading/map-prices.json</c>: leads to other trader camps
/// (the user's goal: "you can always buy a map to a trader within some radius that you don't
/// already have"). Distance is in rings of grid cells (<see cref="CampLeads.Ring"/>), from the
/// selling trader's cell to the camp's, so a lead's price and reach are known before its camp
/// generates and never change when it settles. Per standing tier (by code; one not listed is a
/// stranger) the maps on offer, their reach in rings, the curves' ease and the discount; a stranger
/// gets one map per trader per group, ever. The price is <see cref="Price"/>.
/// </summary>
public sealed class CampLeadRules
{
    /// <summary>The tier code the stranger's rule applies to.</summary>
    public const string Stranger = "stranger";

    /// <summary>Gears for a map, before the curves and the discount.</summary>
    public double Base { get; set; } = 12;
    /// <summary>The distance curve's strength: D = 1 + this × ease × ln(1 + ring).</summary>
    public double DistanceCurve { get; set; } = 1.5;
    /// <summary>The repeat curve's strength: R = 1 + this × ease × ln(1 + leads bought here).</summary>
    public double RepeatCurve { get; set; } = 3;
    /// <summary>A player's very first map.</summary>
    public PityMapRules Pity { get; set; } = new();
    /// <summary>By tier code, the stranger's included. A tier not listed gets the stranger's rule.</summary>
    public Dictionary<string, CampLeadTier> Tiers { get; set; } = new();

    /// <summary>Whether a tier code gets the stranger's rule (one map per trader per group, ever).</summary>
    public bool IsStranger(string? code) => code is null || code == Stranger || !Tiers.ContainsKey(code);

    /// <summary>The tier's rule; a code not listed gets the stranger's (or the defaults: one map,
    /// one ring, no ease, no discount).</summary>
    public CampLeadTier TierFor(string? code) =>
        code != null && Tiers.TryGetValue(code, out var t) ? t
        : Tiers.TryGetValue(Stranger, out var s) ? s
        : new CampLeadTier();

    /// <summary>The distance factor D = 1 + distanceCurve × ease × ln(1 + ring).</summary>
    public double DistanceFactor(int ring, double ease) => 1 + DistanceCurve * ease * Math.Log(1 + Math.Max(0, ring));

    /// <summary>The repeat factor R = 1 + repeatCurve × ease × ln(1 + n).</summary>
    public double RepeatFactor(int bought, double ease) => 1 + RepeatCurve * ease * Math.Log(1 + Math.Max(0, bought));

    /// <summary>
    /// base × D(ring) × R(bought) × the tier's discount, rounded, at least 1 gear, where both curves
    /// are scaled by the tier's ease. The discount takes the place of standing's buy price factor for
    /// these maps. For the same ring and count, a better tier never pays more (ease and discount fall).
    /// </summary>
    public int Price(int ring, int bought, string? tierCode)
    {
        var t = TierFor(tierCode);
        double gears = Base * DistanceFactor(ring, t.Ease) * RepeatFactor(bought, t.Ease) * t.Discount;
        return gears >= int.MaxValue ? int.MaxValue : MapPriceTable.Round(gears);
    }

    public int PityPrice() => MapPriceTable.Round(Pity.Price);

    public List<string> Problems()
    {
        var problems = new List<string>();
        if (Base <= 0) problems.Add("campLeads.base is 0 or less");
        if (DistanceCurve < 0) problems.Add("campLeads.distanceCurve is under 0");
        if (RepeatCurve < 0) problems.Add("campLeads.repeatCurve is under 0");
        if (Pity.Price <= 0) problems.Add("campLeads.pity.price is 0 or less");
        if (Pity.Reach < 1) problems.Add("campLeads.pity.reach is under 1 ring");
        if (!Tiers.ContainsKey(Stranger)) problems.Add($"campLeads has no '{Stranger}' tier");
        foreach (var (code, t) in Tiers)
        {
            if (t.Maps < 1) problems.Add($"campLeads tier '{code}' has no maps");
            if (t.Reach < 1) problems.Add($"campLeads tier '{code}' reaches under 1 ring");
            if (t.Ease <= 0) problems.Add($"campLeads tier '{code}' has an ease of 0 or less");
            if (t.Discount <= 0 || t.Discount > 1) problems.Add($"campLeads tier '{code}' has a discount outside (0, 1]");
        }
        return problems;
    }
}

/// <summary>A camp as the lead picker sees it, from the trader: its cell, type, site (the placed
/// camp, or the spot it waits for), the ring of its cell around the trader's and the distance.</summary>
public readonly record struct CampOption(CellKey Cell, string Type, int X, int Z, int Ring, double Distance);

/// <summary>A lead on offer to one buyer: the camp, its ring and distance, its price, whether it is
/// the prospector the first slot is kept for, and whether it is the buyer's very first map (the
/// pity map: a flat price, its camp settled by the nearest-prospector chain when bought).</summary>
public readonly record struct CampLeadOffer(CellKey Cell, string Type, int X, int Z, int Ring, double Distance, int Price, bool Prospector, bool Pity = false);

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
    /// <summary>Their very first map's camp, when this trader offers it to them (<see cref="PityMap"/>);
    /// null when they have had it, the trader is a prospector, or no camp is in its reach.</summary>
    public CampOption? Pity { get; init; }
}

/// <summary>
/// Which camp leads a trader offers one buyer, and at what price. Distances are rings of grid cells
/// (<see cref="Ring"/>): ring 1 is the eight cells around the trader's own.
/// <list type="bullet">
/// <item>The pity map (<see cref="LeadBuyer.Pity"/>) first, at its flat price. It is that trader's
/// one stranger map: to a stranger it is the only offer.</item>
/// <item>From "known" up: the tier's number of the nearest camps within its reach that the buyer
/// lacks (<see cref="LeadBuyer.Have"/>); if no prospector they have is within the reach, the first
/// slot is the nearest prospector they lack there. Buying one makes it theirs, so the next-nearest
/// takes the slot.</item>
/// <item>A stranger: one map per trader per group, ever: to the nearest camp within the stranger's
/// reach (ring 1) that they lack and their group has not visited. Every new camp sells one onward,
/// so a stranger chains from camp to camp but cannot map a region from one trader.</item>
/// </list>
/// The trader's own cell is never a target. Nearest is by ring, then distance. The price of every
/// offer counts the maps already bought here (<see cref="CampLeadRules.Price"/>).
/// </summary>
public static class CampLeads
{
    /// <summary>Rings of grid cells between two cells: the Chebyshev distance, so a diagonal
    /// neighbour is ring 1 as a straight one is.</summary>
    public static int Ring(CellKey a, CellKey b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));

    public static (List<CampLeadOffer> Offers, CampLeadsWhy Why) Pick(CampLeadRules rules, CellKey own, IEnumerable<CampOption> camps, LeadBuyer buyer)
    {
        var sorted = camps.Where(c => c.Cell != own)
            .OrderBy(c => c.Ring).ThenBy(c => c.Distance).ThenBy(c => c.Cell.X).ThenBy(c => c.Cell.Z)
            .ToList();
        var tier = rules.TierFor(buyer.Tier);
        var offers = new List<CampLeadOffer>();
        CampLeadOffer Offer(CampOption c, bool prospector) =>
            new(c.Cell, c.Type, c.X, c.Z, c.Ring, c.Distance, rules.Price(c.Ring, buyer.Bought, buyer.Tier), prospector);
        bool stranger = rules.IsStranger(buyer.Tier);
        if (stranger && buyer.StrangerUsed) return (offers, CampLeadsWhy.StrangerUsed);
        var taken = new HashSet<CellKey>();
        if (buyer.Pity is { } pity && pity.Cell != own)
        {
            offers.Add(new CampLeadOffer(pity.Cell, pity.Type, pity.X, pity.Z, pity.Ring, pity.Distance, rules.PityPrice(),
                pity.Type == TraderTypes.Prospector, Pity: true));
            // A stranger's one map from this trader.
            if (stranger) return (offers, CampLeadsWhy.None);
            taken.Add(pity.Cell);
        }
        if (stranger)
        {
            var first = sorted.FirstOrDefault(c => c.Ring <= tier.Reach && !buyer.Have.Contains(c.Cell) && !buyer.Visited.Contains(c.Cell));
            if (first.Type is null) return (offers, CampLeadsWhy.NoneInReach);
            offers.Add(Offer(first, false));
            return (offers, CampLeadsWhy.None);
        }
        int max = tier.Maps + offers.Count;
        var inReach = sorted.Where(c => c.Ring <= tier.Reach).ToList();
        var lacking = inReach.Where(c => !buyer.Have.Contains(c.Cell) && !taken.Contains(c.Cell)).ToList();
        bool hasProspector = inReach.Any(c => c.Type == TraderTypes.Prospector && buyer.Have.Contains(c.Cell));
        if (!hasProspector && lacking.FirstOrDefault(c => c.Type == TraderTypes.Prospector) is { Type: not null } p)
        {
            offers.Add(Offer(p, true));
            taken.Add(p.Cell);
        }
        foreach (var c in lacking)
        {
            if (offers.Count >= max) break;
            if (taken.Add(c.Cell)) offers.Add(Offer(c, false));
        }
        return (offers, offers.Count > 0 ? CampLeadsWhy.None : CampLeadsWhy.NoneInReach);
    }

    /// <summary>The cells within <paramref name="reach"/> rings of <paramref name="centre"/>, the
    /// centre itself included, ring by ring.</summary>
    public static IEnumerable<CellKey> CellsAround(CellKey centre, int reach)
    {
        for (int r = 0; r <= Math.Max(0, reach); r++)
            foreach (var c in RingCells(centre, r))
                yield return c;
    }

    /// <summary>The cells exactly <paramref name="ring"/> rings from <paramref name="centre"/> (8 × ring
    /// of them; the centre alone for ring 0).</summary>
    public static IEnumerable<CellKey> RingCells(CellKey centre, int ring)
    {
        if (ring <= 0)
        {
            yield return centre;
            yield break;
        }
        for (int dx = -ring; dx <= ring; dx++)
        {
            yield return new CellKey(centre.X + dx, centre.Z - ring);
            yield return new CellKey(centre.X + dx, centre.Z + ring);
        }
        for (int dz = -ring + 1; dz <= ring - 1; dz++)
        {
            yield return new CellKey(centre.X - ring, centre.Z + dz);
            yield return new CellKey(centre.X + ring, centre.Z + dz);
        }
    }

    public static double Distance(int ax, int az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}

/// <summary>
/// A player's very first map (the pity map): its camp is the nearest seeded prospector cell to the
/// selling trader that can still take a camp (or has one) and the buyer lacks, ring by ring out to
/// the pity's reach; with none there, the nearest camp of any type that exists or can be placed. A
/// cell's seeded type is never changed: the search only picks among the cells as seeded.
/// </summary>
public static class PityMap
{
    /// <param name="own">The selling trader's cell (never a target).</param>
    /// <param name="isProspector">Whether a cell is seeded a prospector (<c>TraderGrid.IsProspector</c>).</param>
    /// <param name="site">A cell's camp as the picker sees it (placed, or the spot it waits for), or
    /// null if it can take none (every spot missed, open without luck, failed, or a sale found none).</param>
    /// <param name="have">The camps the buyer has.</param>
    public static CampOption? Target(CellKey own, int reach, Func<CellKey, bool> isProspector, Func<CellKey, CampOption?> site, ISet<CellKey> have) =>
        Nearest(own, reach, isProspector, site, have) ?? Nearest(own, reach, _ => true, site, have);

    private static CampOption? Nearest(CellKey own, int reach, Func<CellKey, bool> filter, Func<CellKey, CampOption?> site, ISet<CellKey> have)
    {
        for (int r = 1; r <= reach; r++)
        {
            CampOption? best = null;
            foreach (var cell in CampLeads.RingCells(own, r))
            {
                if (!filter(cell) || have.Contains(cell) || site(cell) is not { } s) continue;
                if (best is not { } b || s.Distance < b.Distance
                    || (s.Distance == b.Distance && (s.Cell.X, s.Cell.Z).CompareTo((b.Cell.X, b.Cell.Z)) < 0))
                    best = s;
            }
            if (best != null) return best;
        }
        return null;
    }
}
