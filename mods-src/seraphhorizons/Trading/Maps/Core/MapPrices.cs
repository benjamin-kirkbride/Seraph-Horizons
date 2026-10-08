namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// <c>assets/seraphhorizons/config/trading/map-prices.json</c> (#455): what traders charge for ore
/// maps, gravel maps and leads, and how far they look. An ore map's price is by size class (the
/// deposit's last measurement, or <c>unsurveyed</c>) and precision (1–3), times the metal's factor.
/// Leads to camps are <see cref="CampLeads"/>.
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

    /// <summary>Gears by size class (<c>unsurveyed</c>, <c>small</c>, <c>medium</c>, <c>large</c>),
    /// one per precision 1, 2, 3.</summary>
    public Dictionary<string, double[]> Ore { get; set; } = new();
    /// <summary>Times the ore price, by metal (1 when not listed).</summary>
    public Dictionary<string, double> MetalFactor { get; set; } = new();
    public double Gravel { get; set; } = 6;
    /// <summary>Gears for a lead to the nearest settlement ground (behind <c>mapsToTraders</c>).</summary>
    public double Settlement { get; set; } = 6;
    /// <summary>Leads to other trader camps: per tier, and their price (<see cref="CampLeadRules"/>).</summary>
    public CampLeadRules CampLeads { get; set; } = new();

    public int OrePrice(string metal, string? sizeClass, int precision)
    {
        if (!Ore.TryGetValue(sizeClass ?? Unsurveyed, out var row) && !Ore.TryGetValue(Unsurveyed, out row)) return 1;
        double basePrice = row.Length == 0 ? 1 : row[Math.Clamp(precision, 1, row.Length) - 1];
        return Round(basePrice * MetalFactor.GetValueOrDefault(metal, 1));
    }

    public int GravelPrice() => Round(Gravel);

    public int SettlementPrice() => Round(Settlement);

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
        if (Settlement <= 0) problems.Add("a settlement lead price of 0 or less");
        if (OreRadius <= 0 || GravelRadius <= 0) problems.Add("a radius of 0 or less");
        problems.AddRange(CampLeads.Problems());
        return problems;
    }
}
