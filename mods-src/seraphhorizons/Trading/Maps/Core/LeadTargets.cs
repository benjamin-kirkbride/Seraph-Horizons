using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>Where a lead goes (#455): the nearest camp (anyone), a prospector, a further camp, or
/// the nearest reserved settlement ground (behind the <c>mapsToTraders</c> unlock).</summary>
public enum LeadKind { Camp, Prospector, Far, Settlement }

/// <summary>A lead a trader offers: its kind, the camp cell (none for a settlement), the camp's
/// type, where it is as far as known (the placed camp, or the spot it waits for), and whether it
/// gives way when the shelf is full.</summary>
public readonly record struct LeadTarget(LeadKind Kind, CellKey Cell, string Type, int X, int Z, bool Optional);

/// <summary>A camp cell as the lead picker sees it: its type and site (null: the cell has none).</summary>
public readonly record struct CampSite(CellKey Cell, string Type, int X, int Z);

/// <summary>
/// Picks a trader's leads. Camp cells and their types come from the grid (the seed), so a lead can
/// point at a camp nobody has generated yet; the sale settles it (<c>MapsSystem</c>). Order: the
/// nearest camp of any type (everyone); then, when the shelf's tier has <c>mapsToTraders</c>, the
/// nearest prospector not already led to, up to <see cref="MapPriceTable.FarLeads"/> camps two or
/// more cells away, and the nearest settlement ground; those are optional (give way on a full
/// shelf). The trader's own cell is never a target.
/// </summary>
public static class LeadTargets
{
    public static string Code(LeadKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParse(string? code, out LeadKind kind) =>
        Enum.TryParse(code, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    /// <param name="own">The trader's cell.</param>
    /// <param name="x">The trader's position.</param>
    /// <param name="site">A cell's camp site, or null if it has none (all spots failed).</param>
    /// <param name="extras">Whether the leads behind <c>mapsToTraders</c> are shelved.</param>
    public static List<LeadTarget> Pick(CellKey own, int x, int z, Func<CellKey, CampSite?> site, int leadCells, int farLeads, bool extras)
    {
        var sites = new List<(CampSite Site, int Ring, double Distance)>();
        for (int dx = -leadCells; dx <= leadCells; dx++)
            for (int dz = -leadCells; dz <= leadCells; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var cell = new CellKey(own.X + dx, own.Z + dz);
                if (site(cell) is not { } s) continue;
                sites.Add((s, Math.Max(Math.Abs(dx), Math.Abs(dz)), Dist(s.X, s.Z, x, z)));
            }
        sites.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.Site.Cell.X != b.Site.Cell.X ? a.Site.Cell.X.CompareTo(b.Site.Cell.X) : a.Site.Cell.Z.CompareTo(b.Site.Cell.Z));
        var leads = new List<LeadTarget>();
        var taken = new HashSet<CellKey>();
        void Add(LeadKind kind, CampSite s, bool optional)
        {
            leads.Add(new LeadTarget(kind, s.Cell, s.Type, s.X, s.Z, optional));
            taken.Add(s.Cell);
        }
        if (sites.Count > 0) Add(LeadKind.Camp, sites[0].Site, false);
        if (!extras) return leads;
        foreach (var s in sites)
            if (s.Site.Type == TraderTypes.Prospector && !taken.Contains(s.Site.Cell))
            {
                Add(LeadKind.Prospector, s.Site, true);
                break;
            }
        foreach (var s in sites.Where(s => s.Ring >= 2 && !taken.Contains(s.Site.Cell)).Take(Math.Max(0, farLeads)).ToList())
            Add(LeadKind.Far, s.Site, true);
        var (sx, sz) = TraderGrid.SettlementCentre(x, z);
        leads.Add(new LeadTarget(LeadKind.Settlement, default, "", sx, sz, true));
        return leads;
    }

    private static double Dist(int ax, int az, int bx, int bz) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(az - bz) * (az - bz));
}
