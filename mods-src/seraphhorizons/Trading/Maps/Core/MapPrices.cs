namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// <c>assets/seraphhorizons/config/trading/map-prices.json</c> (#455): what traders charge for ore
/// maps, gravel maps and leads, and how far they look. An ore map's price is by size class (the
/// deposit's last measurement, or <c>unsurveyed</c>) and precision (1–3), times the metal's factor.
/// </summary>
public sealed class MapPriceTable
{
    public const string Unsurveyed = "unsurveyed";

    /// <summary>How far a prospector looks for deposits, in blocks.</summary>
    public int OreRadius { get; set; } = 5000;
    /// <summary>How far any trader looks for a gravel field, in blocks.</summary>
    public int GravelRadius { get; set; } = 2000;
    /// <summary>At most this many ore map offers (one per metal, nearest first).</summary>
    public int MaxOreOffers { get; set; } = 4;
    /// <summary>How many camp cells away (Chebyshev) leads reach.</summary>
    public int LeadCells { get; set; } = 3;
    /// <summary>Leads to further camps (two or more cells away) on a shelf, at most.</summary>
    public int FarLeads { get; set; } = 1;

    /// <summary>Gears by size class (<c>unsurveyed</c>, <c>small</c>, <c>medium</c>, <c>large</c>),
    /// one per precision 1, 2, 3.</summary>
    public Dictionary<string, double[]> Ore { get; set; } = new();
    /// <summary>Times the ore price, by metal (1 when not listed).</summary>
    public Dictionary<string, double> MetalFactor { get; set; } = new();
    public double Gravel { get; set; } = 6;
    /// <summary>Gears by lead kind: <c>camp</c>, <c>prospector</c>, <c>far</c>, <c>settlement</c>.</summary>
    public Dictionary<string, double> Leads { get; set; } = new();

    public int OrePrice(string metal, string? sizeClass, int precision)
    {
        if (!Ore.TryGetValue(sizeClass ?? Unsurveyed, out var row) && !Ore.TryGetValue(Unsurveyed, out row)) return 1;
        double basePrice = row.Length == 0 ? 1 : row[Math.Clamp(precision, 1, row.Length) - 1];
        return Round(basePrice * MetalFactor.GetValueOrDefault(metal, 1));
    }

    public int GravelPrice() => Round(Gravel);

    public int LeadPrice(LeadKind kind) => Round(Leads.GetValueOrDefault(LeadTargets.Code(kind), 2));

    public static int Round(double gears) => Math.Max(1, (int)Math.Round(gears, MidpointRounding.AwayFromZero));

    /// <summary>What is wrong with the table, for the loader to log.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        if (!Ore.ContainsKey(Unsurveyed)) problems.Add($"ore has no '{Unsurveyed}' row");
        foreach (var (size, row) in Ore)
        {
            if (row.Length != 3) problems.Add($"ore '{size}' has {row.Length} prices, not one per precision 1–3");
            if (row.Any(p => p <= 0)) problems.Add($"ore '{size}' has a price of 0 or less");
        }
        foreach (var kind in Enum.GetValues<LeadKind>())
            if (!Leads.ContainsKey(LeadTargets.Code(kind))) problems.Add($"no price for '{LeadTargets.Code(kind)}' leads");
        if (OreRadius <= 0 || GravelRadius <= 0) problems.Add("a radius of 0 or less");
        if (LeadCells < 1) problems.Add("leadCells under 1");
        return problems;
    }
}
