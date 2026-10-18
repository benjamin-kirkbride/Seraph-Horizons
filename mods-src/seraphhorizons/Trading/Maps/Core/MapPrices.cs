using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// <c>assets/seraphhorizons/config/trading/map-prices.json</c> (#455): what traders charge for ore
/// maps, gravel maps and leads, and how far they look. An ore map's price is computed
/// (<see cref="OrePrice"/>): the ingots in the middle of the deposit's size band, times the value of
/// the metal's ingot, times a share by precision (1–3), times the metal's scarcity, times
/// <see cref="Underwater"/> for a deposit under water. An offer's precision is drawn when it is
/// shelved (<see cref="RollPrecision"/>, by <see cref="PrecisionWeights"/>), never set by the
/// buyer's standing. Leads to camps are <see cref="CampLeads"/>.
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

    /// <summary>The share of the ingots' value a map costs, one per precision 1, 2, 3.</summary>
    public double[] Share { get; set; } = [];
    /// <summary>How likely a shelved ore map offer is to be each precision 1, 2, 3 (relative weights).</summary>
    public double[] PrecisionWeights { get; set; } = [];
    /// <summary>Times the ore price, by metal (1 when not listed): the metals a district hides.</summary>
    public Dictionary<string, double> Scarcity { get; set; } = new();
    /// <summary>Times the ore price for a deposit under water (<see cref="DepositWater"/>).</summary>
    public double Underwater { get; set; } = 1;
    public double Gravel { get; set; } = 6;
    /// <summary>Gears for a lead to the nearest settlement ground (behind <c>mapsToTraders</c>).</summary>
    public double Settlement { get; set; } = 6;
    /// <summary>Leads to other trader camps: per tier, and their price (<see cref="CampLeadRules"/>).</summary>
    public CampLeadRules CampLeads { get; set; } = new();

    /// <summary>The item value table's code for a metal's ingot.</summary>
    public static string IngotCode(string metal) => $"game:ingot-{metal}";

    /// <summary>The ingots in the middle of a size class's band: small, medium and large are the
    /// bottom, middle and top third of small..large (<see cref="DepositSizing.Classify"/>);
    /// <c>unsurveyed</c> (or anything else) is the medium band. Never the measured size, so a map's
    /// price says no more about its deposit than the class does.</summary>
    public static double BandMiddleIngots(SizeTargets targets, string? sizeClass)
    {
        double third = (targets.Large - targets.Small) / 3;
        int band = sizeClass switch { "small" => 0, "large" => 2, _ => 1 };
        return targets.Small + (band + 0.5) * third;
    }

    /// <summary>An ore map's gears: <see cref="BandMiddleIngots"/> × the ingot's value × the
    /// precision's share × the metal's scarcity (× <see cref="Underwater"/> under water), at least 1. Null when the metal has no size range
    /// or its ingot no value: then there is nothing to price it by.</summary>
    public int? OrePrice(string metal, string? sizeClass, int precision, SizeTargets? targets, double ingotValue, bool underwater = false)
    {
        if (targets is not { } t || ingotValue <= 0 || Share.Length == 0) return null;
        double share = Share[Math.Clamp(precision, 1, Share.Length) - 1];
        return Round(BandMiddleIngots(t, sizeClass) * ingotValue * share * Scarcity.GetValueOrDefault(metal, 1) * (underwater ? Underwater : 1));
    }

    /// <summary>A precision for an ore map offer being shelved, drawn by <see cref="PrecisionWeights"/>
    /// (<see cref="MapPrecisionRoll.Roll"/>); rough when the weights are unusable.</summary>
    public int RollPrecision(Random rand) => MapPrecisionRoll.Roll(PrecisionWeights, rand);

    public int GravelPrice() => Round(Gravel);

    public int SettlementPrice() => Round(Settlement);

    public static int Round(double gears) => Math.Max(1, (int)Math.Round(gears, MidpointRounding.AwayFromZero));

    /// <summary>What is wrong with the table, for the loader to log.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        if (Share.Length != 3) problems.Add($"share has {Share.Length} entries, not one per precision 1–3");
        if (Share.Any(p => p <= 0)) problems.Add("a share of 0 or less");
        if (PrecisionWeights.Length != 3) problems.Add($"precisionWeights has {PrecisionWeights.Length} entries, not one per precision 1–3");
        if (PrecisionWeights.Any(w => w < 0 || !double.IsFinite(w))) problems.Add("a negative precision weight");
        else if (PrecisionWeights.Sum() <= 0) problems.Add("precisionWeights sum to 0");
        foreach (var (metal, f) in Scarcity)
            if (f <= 0) problems.Add($"scarcity '{metal}' is 0 or less");
        if (Underwater <= 0 || Underwater > 1) problems.Add("underwater is outside (0, 1]");
        if (Settlement <= 0) problems.Add("a settlement lead price of 0 or less");
        if (OreRadius <= 0 || GravelRadius <= 0) problems.Add("a radius of 0 or less");
        problems.AddRange(CampLeads.Problems());
        return problems;
    }
}
