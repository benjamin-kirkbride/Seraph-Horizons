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
/// rock, so it never places anything: the game side takes such an ore out of its zone, so it holds
/// no share of the zone's blocks, #946), the ingots one block of its poorest grade gives, and the
/// ingots a block of each grade the district's rock allows gives, poorest first (null for an
/// ungraded ore, or if unknown: then the poorest grade's times the grade allowance is used).</summary>
public readonly record struct DistrictOre(string? Metal, double Weight, double Density, double IngotsPerBlock,
    IReadOnlyList<double>? GradeIngots = null);

/// <summary>
/// One district ore zone as the rules need it: its kind, its ores, the blocks of it per layer that
/// are the fault rock IOG lays (the district's host rock) at density 1 (<see cref="DistrictVeins"/>
/// explains the estimate), its height range, how many times its ore per layer the game side can
/// make it if the rules widen it (<see cref="DistrictZonePlan.Widen"/>; 1: it can't), its fault's
/// richness at each height, indexed by y (IOG's <c>RichnessAt</c>, 0 to 1; null if unknown), and
/// for an ore shoot the blocks per layer of it off the fault rock, in country rock, at density 1
/// (<paramref name="CountryBlocks"/>) with the share of them at a height that is the host rock too
/// (<paramref name="CountryShare"/>, from the predicted strata, #946; null: none is). A shoot may
/// also offer wider shapes (<paramref name="Wider"/>, each its fault rock and country rock per layer
/// in the same units, larger in turn) for a vein whose band would not fit the room otherwise.
/// </summary>
public sealed record DistrictZone(DistrictZoneKind Kind, IReadOnlyList<DistrictOre> Ores, double BlocksPerLayer, int YMin, int YMax,
    double Widening = 1, IReadOnlyList<float>? Richness = null, double CountryBlocks = 0, Func<int, double>? CountryShare = null,
    IReadOnlyList<(double Fault, double Country)>? Wider = null)
{
    /// <summary>The blocks per layer at height <paramref name="y"/> that are host rock, at density 1:
    /// the fault rock and the share of the country rock that is host rock too.</summary>
    public double HostBlocksAt(int y) =>
        BlocksPerLayer + (CountryBlocks > 0 && CountryShare is { } share ? CountryBlocks * Math.Clamp(share(y), 0, 1) : 0);
}

/// <summary>What to do with a zone: drop it, or keep it with these of its ores (the rest placing
/// nothing) and, for a vein, within this band of heights (inclusive; null keeps its range), widened
/// to its <see cref="DistrictZone.Widening"/> if <paramref name="Widen"/>. A banded vein also
/// carries the size drawn for it and the ingots its band holds by the estimate (the ore that
/// reached its size first; less than drawn where the band is the whole room), and which of the
/// zone's <see cref="DistrictZone.Wider"/> shapes it takes (0: none, its own widening; i: the i-th),
/// and, for a vein of two sized metals, the factor each ore's density is cut by so the richer metal
/// holds its own size while the band grows to the poorer's (null: none cut).</summary>
public sealed record DistrictZonePlan(bool Keep, IReadOnlyList<bool> OreKept, int? BandMin, int? BandMax, bool Widen = false,
    double Drawn = 0, double Held = 0, int Wider = 0, IReadOnlyList<double>? Thin = null)
{
    public static DistrictZonePlan Unchanged(int ores) => new(true, Enumerable.Repeat(true, ores).ToArray(), null, null);
}

/// <summary>The district rules' data (<c>config/ore-districts.json</c>).</summary>
/// <param name="GradeAllowance">Ore per block over the poorest grade's, for an ore or zone whose
/// grades or fault richness are unknown (otherwise each layer's grades are reckoned, <see
/// cref="DistrictVeins.ExpectedIngots"/>): away from a fault's rich bands nine blocks in ten are
/// the poorest grade (IOG's <c>ResolveOreCode</c>).</param>
/// <param name="Blocks">How many times the ore per layer a sized vein is made (#863): <see
/// cref="OreProducts.DepositBlocks"/> while an ore item holds about a quarter of vanilla's units,
/// so a vein holds the ingots it did; 1 leaves the veins' width as IOG makes it.</param>
public sealed record DistrictVeinSettings(int VeinsPerMetal, IReadOnlyCollection<string> KeepCount, double GradeAllowance,
    int BandFloor, int BandBelowSeaLevel, double Blocks = 1);

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
/// <item>Width (#863): with <see cref="DistrictVeinSettings.Blocks"/> over 1, every kept vein and
/// horsetail lens holding a managed metal is widened (<see cref="DistrictZonePlan.Widen"/>) to as near that many times its ore
/// per layer as the game side can make it (<see cref="DistrictZone.Widening"/>): an ore item holds
/// about a quarter of vanilla's units, so the vein needs four times the blocks to hold its ingots
/// in the height of rock it had. Without it a vein needed four to five times the height, which most
/// columns don't have. A lens, which has no band, is grown four times in ore (<see cref="LensRadii"/>)
/// so it holds the ingots it did. A shoot whose band would still be the whole room under sea level
/// and short of its size takes the first of its wider shapes (<see cref="DistrictZone.Wider"/>,
/// #946) whose band holds it, or the widest (<see cref="DistrictZonePlan.Wider"/>).</item>
/// <item>Size: every kept vein holding a managed metal is cut to a band of heights. Its ore at a
/// height is <c>host blocks per layer × widening × weight share × density × ingots per block</c> for
/// each such ore. Its host blocks at a height (<see cref="DistrictZone.HostBlocksAt"/>) are the
/// fault rock IOG lays through it and, for an ore shoot, the share of the rest of its ellipse whose
/// country rock is the host rock as well, from the strata predicted there (#946): IOG turns a zone
/// block into ore only where it is host rock. The weight share is over the ores that place
/// anything. The ingots per block are the mean of its grades at its fault's richness there
/// (<see cref="ExpectedIngots"/>, #943: a fault's rich bands give two to three times the ore of
/// the poorest grade), or the poorest grade's times the grade allowance where either is unknown.
/// The band starts at a height from the hash between <see cref="DistrictVeinSettings.BandFloor"/>
/// and sea level less <see cref="DistrictVeinSettings.BandBelowSeaLevel"/>, so that it lies in
/// rock, room left for the most height it can need, and grows a layer at a time until each of its
/// metals (its ores of that metal together) holds the zone's drawn size, the richer of two then
/// thinned to its size (<see cref="DistrictZonePlan.Thin"/>, #946) (<see cref="DrawSize"/>: small, typical and large are the 10th,
/// 50th and 90th percentile, as the epic reads them). A band taller than that room is the room
/// (#943): the vein holds less than drawn.</item>
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
            bool widen = zone.Widening > 1 && _settings.Blocks > 1
                         && zone.Ores.Where((o, j) => kept[i][j] && o.Density > 0 && Sized(o.Metal)).Any();
            if (zone.Kind == DistrictZoneKind.Lens)
            {
                plans[i] = new DistrictZonePlan(true, kept[i], null, null, widen);
                continue;
            }
            double w = widen ? zone.Widening : 1;
            var band = Band(districtSeed, i, zone, kept[i], seaLevel, w);
            int wider = 0;
            // A widened shoot whose band would be the whole room and still short of its drawn size
            // takes the first wider shape whose band holds it, or the widest (#946).
            if (widen && !band.Full && band.Min != null && zone.Wider is { Count: > 0 } shapes)
                for (int o = 0; o < shapes.Count; o++)
                {
                    band = Band(districtSeed, i, zone with { BlocksPerLayer = shapes[o].Fault, CountryBlocks = shapes[o].Country }, kept[i], seaLevel, w);
                    wider = o + 1;
                    if (band.Full) break;
                }
            plans[i] = new DistrictZonePlan(true, kept[i], band.Min, band.Max, widen, band.Drawn, band.Held, wider, band.Thin);
        }
        return plans;
    }

    private (int? Min, int? Max, double Drawn, double Held, bool Full, double[]? Thin) Band(long districtSeed, int index, DistrictZone zone, bool[] kept, int seaLevel, double widening)
    {
        // An ore that places nothing is taken out of the zone (#946): the rest share its blocks.
        double totalWeight = zone.Ores.Where(o => o.Density > 0).Sum(o => o.Weight);
        if (totalWeight <= 0 || zone.BlocksPerLayer <= 0 && zone.CountryBlocks <= 0) return (null, null, 0, 0, true, null);
        double u = Unit(Hash(districtSeed, "size", index));
                int lo = Math.Max(zone.YMin, _settings.BandFloor);
        int hi = Math.Min(zone.YMax, seaLevel - _settings.BandBelowSeaLevel);
        if (hi < lo) (lo, hi) = (zone.YMin, zone.YMax);
        // Each sized metal: its drawn size and the ingots a layer of the vein holds of it, the sum
        // over its ores (a zone can draw two ores of one metal, magnetite and pyrite, which together
        // are the vein's iron, #946).
        var layers = new Dictionary<string, (double Size, List<Func<int, double>> Ores, List<int> Index)>(StringComparer.Ordinal);
        for (int j = 0; j < zone.Ores.Count; j++)
        {
            var ore = zone.Ores[j];
            if (!kept[j] || ore.Metal is not { } metal || _sizes.TargetsFor(metal) is not { } targets) continue;
            double share = widening * ore.Weight / totalWeight * ore.Density;
            if (share <= 0) continue;
            Func<int, double> ingots = ore.GradeIngots is { Count: > 0 } grades && zone.Richness is { Count: > 0 } richness
                ? y => ExpectedIngots(grades, richness[Math.Clamp(y, 0, richness.Count - 1)])
                : _ => ore.IngotsPerBlock * _settings.GradeAllowance;
            if (!layers.TryGetValue(metal, out var entry)) layers[metal] = entry = (DrawSize(targets, u), [], []);
            entry.Ores.Add(y => zone.HostBlocksAt(y) * share * ingots(y));
            entry.Index.Add(j);
        }
        var sized = new List<(double Size, Func<int, double> Layer, double Least, List<int> Index)>();
        foreach (var (size, ores, idx) in layers.Values)
        {
            Func<int, double> layer = y => ores.Sum(o => o(y));
            double least = LeastLayer(layer, lo, hi);
            if (least > 0) sized.Add((size, layer, least, idx));
        }
        if (sized.Count == 0) return (null, null, 0, 0, true, null);
        // Placed by the height it needs at its poorest layer (away from any rich band, IOG's grades
        // at richness 0, and where the least of its country rock is host rock), which is the most it
        // can need; then grown layer by layer from there, by each layer's host rock and expected
        // grade, until an ore reaches its drawn size (#943, #946). A band taller than the room is
        // the room: the vein holds less than drawn, never IOG's whole column.
        double most = sized.Max(o => o.Size / o.Least);
        int room = hi - lo + 1;
        int placed = Math.Clamp((int)Math.Round(most), 1, room);
        int start = lo + (int)(Unit(Hash(districtSeed, "height", index)) * (room + 1 - placed));
        start = Math.Min(start, hi - placed + 1);
        var held = new double[sized.Count];
        int end = start;
        bool full = false;
        for (int y = start; y <= hi; y++)
        {
            end = y;
            // A vein of two sized metals (tin and bismuth) grows until the poorer holds its size
            // too; the richer one's ore is then thinned to its own (#946). Rounded to the nearest
            // layer, as a height of size / ingots per layer would be.
            bool all = true;
            for (int k = 0; k < sized.Count; k++)
            {
                double layer = sized[k].Layer(y);
                held[k] += layer;
                if (held[k] < sized[k].Size - layer / 2) all = false;
            }
            if (all)
            {
                full = true;
                break;
            }
        }
        // Each metal over its size has its ores' density cut to hold just that: IOG then places that
        // share of the blocks it drew for them, and the rest stay rock.
        double[]? thin = null;
        for (int k = 0; k < sized.Count; k++)
        {
            if (held[k] <= sized[k].Size * 1.05) continue;
            thin ??= Enumerable.Repeat(1.0, zone.Ores.Count).ToArray();
            foreach (int j in sized[k].Index) thin[j] = sized[k].Size / held[k];
            held[k] = sized[k].Size;
        }
        // Reported as the metals' geometric means: the size drawn and the ingots held.
        double drawn = Math.Exp(sized.Average(o => Math.Log(o.Size)));
        double holds = Math.Exp(Enumerable.Range(0, sized.Count).Average(k => Math.Log(Math.Max(1e-12, held[k]))));
        return (start, end, drawn, holds, full, thin);
    }

    // The least ingots a layer of an ore holds over the room: at the room's least richness and
    // least host rock.
    private static double LeastLayer(Func<int, double> layer, int lo, int hi)
    {
        double least = double.MaxValue;
        for (int y = lo; y <= hi; y++) least = Math.Min(least, layer(y));
        return least;
    }

    /// <summary>
    /// The ingots a block of a graded district ore gives on average at a fault richness (IOG's
    /// <c>HydrothermalDistrictSystem.ResolveOreCode</c>): with <c>n</c> grades, poorest first, grade
    /// <c>i</c> is drawn with weight <c>max(0, 1 − |i − richness (n − 1)| / 1.5)²</c>. At richness 0
    /// that is nine poorest blocks to one of the next; in a rich band (up to 1) most are the richest
    /// grades, which for cassiterite (1 / 3 / 4 / 6 units) is about three times the ore.
    /// </summary>
    public static double ExpectedIngots(IReadOnlyList<double> gradeIngots, double richness)
    {
        int n = gradeIngots.Count;
        if (n == 0) return 0;
        if (n == 1) return gradeIngots[0];
        double centre = (float)richness * (n - 1);
        double sum = 0, total = 0;
        for (int i = 0; i < n; i++)
        {
            double w = Math.Max(0, 1 - Math.Abs(i - centre) / 1.5);
            w *= w;
            sum += w * gradeIngots[i];
            total += w;
        }
        return total <= 1e-6 ? gradeIngots[0] : sum / total;
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

    /// <summary>
    /// A ladder vein's slab spacings brought closer to give about <paramref name="blocks"/> times its
    /// slabs (#863): each spacing divided by it, rounded, at least 1 (IOG's 2–10 become 1–3), and the
    /// gain that makes, the old mean spacing over the new, which is under <paramref name="blocks"/>
    /// where spacings hit 1. A ladder vein is widened this way and not by its width, which would widen
    /// the fault rock IOG lays along it.
    /// </summary>
    public static (int[] Spacings, double Gain) CloserSlabs(IReadOnlyList<int> spacings, double blocks)
    {
        if (spacings.Count == 0 || !(blocks > 1)) return (spacings.ToArray(), 1);
        var closer = spacings.Select(s => Math.Max(1, (int)Math.Round(s / blocks, MidpointRounding.AwayFromZero))).ToArray();
        return (closer, spacings.Average() / closer.Average());
    }

    /// <summary>
    /// A horsetail lens's radii grown to give <paramref name="blocks"/> times its ore (#863). IOG's lens
    /// is a spheroid (<paramref name="radiusH"/> both ways across, <paramref name="radiusV"/> up), but
    /// where the host is a true fault rock its ore is only where it crosses the splay's one-block
    /// fracture, a disc of the two radii in the fracture's plane, so both radii take the square root:
    /// four times the ore is twice each (4–12 by 3–8 become 8–24 by 6–16). Where the lens lies in
    /// country rock that is mostly host rock too (<paramref name="inHostRock"/>: the predicted strata
    /// there, #946) the whole spheroid can be ore (#943), so only the horizontal radius grows, by the
    /// square root: four times the volume.
    /// </summary>
    public static (float H, float V) LensRadii(float radiusH, float radiusV, double blocks, bool inHostRock = false)
    {
        if (!(blocks > 1)) return (radiusH, radiusV);
        float k = (float)Math.Sqrt(blocks);
        return inHostRock ? (radiusH * k, radiusV) : (radiusH * k, radiusV * k);
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
