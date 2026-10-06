namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The kind of an Interesting Ore Gen district ore zone (its <c>OreZoneType</c>).</summary>
public enum DistrictZoneKind
{
    /// <summary>An ore shoot at a fault bend or widening: a lens along the fault, the whole world high.</summary>
    Shoot,
    /// <summary>A ladder vein: one-block slabs across a widened stretch of fault, the whole world high.</summary>
    Ladder,
    /// <summary>Horsetail lenses: a few small ellipsoids on a one-block fracture at a fault tip.</summary>
    Lens,
}

/// <summary>One ore of a zone: its metal group (null for gems, minerals and anything unmanaged),
/// its pick weight, its placement density (0 if its code resolves to no block in the district's
/// rock, so it never places anything) and the ingots one block of its poorest grade gives.</summary>
public readonly record struct DistrictOre(string? Metal, double Weight, double Density, double IngotsPerBlock);

/// <summary>
/// One district ore zone as the rules need it: its kind, its ores, the ore blocks it places per
/// layer of rock when the whole of it is ore at density 1 (<see cref="DistrictVeins"/> explains
/// the estimate), and its height range.
/// </summary>
public sealed record DistrictZone(DistrictZoneKind Kind, IReadOnlyList<DistrictOre> Ores, double BlocksPerLayer, int YMin, int YMax);

/// <summary>What to do with a zone: drop it, or keep it with these of its ores (the rest placing
/// nothing) and, for a vein, within this band of heights (inclusive; null keeps its range).</summary>
public sealed record DistrictZonePlan(bool Keep, IReadOnlyList<bool> OreKept, int? BandMin, int? BandMax)
{
    public static DistrictZonePlan Unchanged(int ores) => new(true, Enumerable.Repeat(true, ores).ToArray(), null, null);
}

/// <summary>The district rules' data (<c>config/ore-districts.json</c>).</summary>
/// <param name="GradeAllowance">Ore per block over the poorest grade's: away from a fault's rich
/// bands nine blocks in ten are the poorest grade (IOG's <c>ResolveOreCode</c>).</param>
public sealed record DistrictVeinSettings(int VeinsPerMetal, IReadOnlyCollection<string> KeepCount, double GradeAllowance,
    int BandFloor, int BandBelowSeaLevel);

/// <summary>
/// Hydrothermal district veins brought to the size and number of veins elsewhere (#435, under
/// <c>SmallerDeposits</c>). Interesting Ore Gen 2.3.8 builds each district's ore zones once, from
/// the seed, before any of its ore is placed (<c>HydrothermalDistrict.DetectOreZones</c>); a zone
/// holds one or two ores, and every block of it that is the district's fault rock becomes one of
/// them with chance <c>geometric density × ore density</c>. Ore shoots and ladder veins reach from
/// the mantle to the top of the world, so a vein is as big as the rock column under it (up to
/// 13,000 ingots, #445); horsetail lenses are a few hundred blocks at most.
/// <list type="bullet">
/// <item>Count: per district, at most <see cref="DistrictVeinSettings.VeinsPerMetal"/> veins
/// (shoots and ladders) hold each managed metal (one with a size target in
/// <c>ore-sizes.json</c>) that is not in <see cref="DistrictVeinSettings.KeepCount"/> (the metals
/// only districts provide). Which ones is a stable hash of the district and the zone's index.
/// The metal's ore in any further vein, and in every horsetail lens, places nothing; a zone left
/// with no ore that places anything is dropped.</item>
/// <item>Size: every kept vein holding a managed metal is cut to a band of heights. Its ore per
/// layer is <c>blocks per layer × weight share × density × ingots per block × grade allowance</c> for each such
/// ore; the band is the height that gives the ore the zone's drawn size
/// (<see cref="DrawSize"/>: small, typical and large are the 10th, 50th and 90th percentile, as
/// the epic reads them), the least over its ores, at a height from the hash between
/// <see cref="DistrictVeinSettings.BandFloor"/> and sea level less
/// <see cref="DistrictVeinSettings.BandBelowSeaLevel"/>, so that it lies in rock.</item>
/// </list>
/// Unmanaged ores (gems, minerals) keep their counts; in a vein that also holds a managed metal
/// they share its band.
/// </summary>
public sealed class DistrictVeins
{
    private readonly OreSizeTable _sizes;
    private readonly DistrictVeinSettings _settings;
    private readonly HashSet<string> _keep;

    public DistrictVeins(OreSizeTable sizes, DistrictVeinSettings settings)
    {
        _sizes = sizes;
        _settings = settings;
        _keep = new HashSet<string>(settings.KeepCount, StringComparer.Ordinal);
    }

    /// <summary>Whether the rules size this metal (it has small / typical / large targets).</summary>
    public bool Sized(string? metal) => metal != null && _sizes.TargetsFor(metal) is not null;

    /// <summary>Whether the rules cap this metal's count of veins per district.</summary>
    public bool Capped(string? metal) => Sized(metal) && !_keep.Contains(metal!);

    /// <summary>The plan for every zone of one district, in its order.</summary>
    /// <param name="districtSeed">A stable value per district (its centre and config).</param>
    public IReadOnlyList<DistrictZonePlan> Plan(long districtSeed, IReadOnlyList<DistrictZone> zones, int seaLevel)
    {
        var kept = zones.Select(z => z.Ores.Select(_ => true).ToArray()).ToArray();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = Enumerable.Range(0, zones.Count)
            .Where(i => zones[i].Kind != DistrictZoneKind.Lens)
            .OrderBy(i => Hash(districtSeed, "order", i))
            .ThenBy(i => i);
        foreach (int i in order)
        {
            var allowed = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int j = 0; j < zones[i].Ores.Count; j++)
            {
                if (zones[i].Ores[j] is not { Metal: { } metal, Density: > 0 } || !Capped(metal)) continue;
                if (!allowed.TryGetValue(metal, out bool ok))
                {
                    int n = counts.GetValueOrDefault(metal);
                    allowed[metal] = ok = n < _settings.VeinsPerMetal;
                    if (ok) counts[metal] = n + 1;
                }
                kept[i][j] = ok;
            }
        }
        var plans = new DistrictZonePlan[zones.Count];
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (zone.Kind == DistrictZoneKind.Lens)
                for (int j = 0; j < zone.Ores.Count; j++)
                    if (Capped(zone.Ores[j].Metal)) kept[i][j] = false;
            bool places = zone.Ores.Where((o, j) => kept[i][j] && o.Density > 0 && o.Weight > 0).Any();
            if (!places)
            {
                plans[i] = new DistrictZonePlan(false, kept[i], null, null);
                continue;
            }
            var (min, max) = zone.Kind == DistrictZoneKind.Lens ? (null, null) : Band(districtSeed, i, zone, kept[i], seaLevel);
            plans[i] = new DistrictZonePlan(true, kept[i], min, max);
        }
        return plans;
    }

    private (int?, int?) Band(long districtSeed, int index, DistrictZone zone, bool[] kept, int seaLevel)
    {
        double totalWeight = zone.Ores.Sum(o => o.Weight);
        if (totalWeight <= 0 || zone.BlocksPerLayer <= 0) return (null, null);
        double u = Unit(Hash(districtSeed, "size", index));
        double? height = null;
        for (int j = 0; j < zone.Ores.Count; j++)
        {
            var ore = zone.Ores[j];
            if (!kept[j] || ore.Metal is not { } metal || _sizes.TargetsFor(metal) is not { } targets) continue;
            double perLayer = zone.BlocksPerLayer * ore.Weight / totalWeight * ore.Density * ore.IngotsPerBlock * _settings.GradeAllowance;
            if (perLayer <= 0) continue;
            double h = DrawSize(targets, u) / perLayer;
            height = height is { } prev ? Math.Min(prev, h) : h;
        }
        if (height is not { } wanted) return (null, null);
        int lo = Math.Max(zone.YMin, _settings.BandFloor);
        int hi = Math.Min(zone.YMax, seaLevel - _settings.BandBelowSeaLevel);
        int band = Math.Max(1, (int)Math.Round(wanted));
        if (hi < lo || band >= hi - lo + 1) return (null, null);
        int start = lo + (int)(Unit(Hash(districtSeed, "height", index)) * (hi - lo + 2 - band));
        start = Math.Min(start, hi - band + 1);
        return (start, start + band - 1);
    }

    /// <summary>
    /// A deposit size for a quantile <paramref name="u"/> in [0, 1): log-linear through small at
    /// 0.1, typical at 0.5 and large at 0.9, and on at the same slopes to 0 and 1.
    /// </summary>
    public static double DrawSize(SizeTargets t, double u)
    {
        double ls = Math.Log(t.Small), lt = Math.Log(t.Typical), ll = Math.Log(t.Large);
        double l = u < 0.5
            ? lt + (u - 0.5) / 0.4 * (lt - ls)
            : lt + (u - 0.5) / 0.4 * (ll - lt);
        return Math.Exp(l);
    }

    /// <summary>The ore of an IOG district ore code (<c>ore-*-cassiterite-granite</c>,
    /// <c>ore-sulfur-travertine</c>, <c>game:ore-...</c>); null if it isn't an ore block.</summary>
    public static string? OreOfCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        int colon = code.IndexOf(':');
        string path = colon >= 0 ? code[(colon + 1)..] : code;
        if (!path.StartsWith("ore-", StringComparison.Ordinal)) return null;
        var parts = path.Split('-');
        if (parts.Length < 3) return null;
        return parts.Length >= 4 && (parts[1] == "*" || Grades.Contains(parts[1])) ? parts[2] : parts[1];
    }

    private static readonly HashSet<string> Grades = ["poor", "medium", "rich", "bountiful"];

    private static ulong Hash(long seed, string salt, int index) => OreCells.Hash(seed, OreCells.Fnv1a(salt), index, 0, 0);

    private static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));
}
