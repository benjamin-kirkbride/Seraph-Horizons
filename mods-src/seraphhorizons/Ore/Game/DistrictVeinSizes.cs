using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Smaller deposits in hydrothermal districts (#435, under <c>SmallerDeposits</c>): applies
/// <see cref="DistrictVeins"/> to each district Interesting Ore Gen 2.3.8 builds, in a postfix on
/// <c>HydrothermalDistrict.DetectOreZones</c> (private, run once in the district's constructor,
/// after its faults and before its zones are indexed by chunk). District ore never goes through
/// the game's <c>GenDeposits</c> or IOG's vein generator, so neither the cell rule nor
/// <see cref="DepositSizes"/> reaches it: <c>HydrothermalDistrictSystem</c> places it itself, in
/// its own <c>TerrainFeatures</c> pass, into the fault rock it has just laid. Each zone keeps its
/// own list of ores, so an ore the rules drop is swapped there for a copy that places nothing
/// (density 0, the same weight, so the others' shares stay); a vein's band is its bounding box's
/// height, which <c>GetOreBlocksInChunk</c> iterates (the chunk index uses only X and Z). IOG
/// rebuilds districts from the seed after every restart, so the rules see the same district each
/// time and decide the same.
/// </summary>
public static class DistrictVeinSizes
{
    public const string DistrictTypeName = "InterestingOreGen.Generators.HydrothermalDistrict";
    public const string AssignmentTypeName = "InterestingOreGen.ModConfig.HydrothermalOreAssignment";
    public static readonly AssetLocation SettingsAsset = new("seraphhorizons", "config/ore-districts.json");

    private static DistrictVeins? _rules;
    private static ICoreServerAPI? _sapi;
    private static readonly ConcurrentDictionary<string, (bool Resolved, double Ingots)> IngotsByCode = new(StringComparer.Ordinal);

    /// <summary>Districts the rules have been applied to this run (the Atlas scenario reads it).</summary>
    public static int DistrictsDone;

    private static Type? DistrictType => AccessTools.TypeByName(DistrictTypeName);
    private static Type? AssignmentType => AccessTools.TypeByName(AssignmentTypeName);
    private static MethodInfo? DetectMethod => DistrictType is { } t ? AccessTools.Method(t, "DetectOreZones") : null;

    /// <summary>Why it can't bind here, or null if it can.</summary>
    public static string? Unsupported(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(OreCellPlacement.IogModId)) return "Interesting Ore Gen is not loaded";
        if (DistrictType is not { } district) return $"{DistrictTypeName} is gone";
        if (DetectMethod == null) return "HydrothermalDistrict.DetectOreZones is gone";
        if (AccessTools.Property(district, "OreZones") == null || AccessTools.Property(district, "Centre") == null
            || AccessTools.Property(district, "Config") == null)
            return "HydrothermalDistrict's OreZones, Centre or Config is gone";
        var zone = AccessTools.TypeByName("InterestingOreGen.Generators.OreZone");
        if (zone == null || new[] { "ZoneType", "EligibleOres", "BoundingBox", "Sampler", "ParentFault" }.Any(f => AccessTools.Field(zone, f) == null))
            return "OreZone's fields changed";
        if (AssignmentType is not { } a || new[] { "OreCode", "Weight", "Density", "AllowedVariantsByInBlock" }.Any(f => AccessTools.Field(a, f) == null))
            return "HydrothermalOreAssignment's fields changed";
        if (AccessTools.Field(AccessTools.TypeByName("InterestingOreGen.Generators.OreShootSampler"), "_halfStrike") == null
            || AccessTools.Field(AccessTools.TypeByName("InterestingOreGen.Generators.LadderVeinSampler"), "_region") == null)
            return "the ore shoot or ladder vein sampler changed";
        return null;
    }

    public static void Bind(ICoreServerAPI api, Harmony harmony, OreSizeTable sizes)
    {
        var s = api.Assets.Get(SettingsAsset).ToObject<SettingsFile>();
        _rules = new DistrictVeins(sizes, new DistrictVeinSettings(s.VeinsPerMetal, s.KeepCount ?? [], s.GradeAllowance, s.BandFloor, s.BandBelowSeaLevel));
        _sapi = api;
        DistrictsDone = 0;
        harmony.Patch(DetectMethod, postfix: new HarmonyMethod(typeof(DistrictVeinSizes), nameof(DetectOreZonesPostfix)));
        api.Logger.Notification("[seraphhorizons] Smaller deposits: hydrothermal district veins sized, at most {0} veins per metal per district (all for {1})",
            s.VeinsPerMetal, string.Join(", ", s.KeepCount ?? []));
    }

    public static void Unbind()
    {
        _rules = null;
        _sapi = null;
        IngotsByCode.Clear();
    }

    private static void DetectOreZonesPostfix(object __instance)
    {
        if (_rules is not { } rules || _sapi is not { } sapi) return;
        try
        {
            Apply(rules, sapi, __instance);
        }
        catch (Exception e)
        {
            sapi.Logger.Error("[seraphhorizons] Smaller deposits: could not size a hydrothermal district, left as IOG made it: {0}", e);
        }
    }

    private static void Apply(DistrictVeins rules, ICoreServerAPI sapi, object district)
    {
        var t = Traverse.Create(district);
        var zones = (IList)t.Property("OreZones").GetValue();
        var centre = (Vec2i)t.Property("Centre").GetValue();
        var config = t.Property("Config").GetValue();
        string code = Traverse.Create(config).Field("Code").GetValue<string>() ?? "";
        string host = Traverse.Create(config).Field("HostMaterialCode").GetValue<string>() ?? "";
        long seed = ((long)centre.X << 32) ^ (uint)centre.Y ^ (long)OreCells.Fnv1a(code);

        var views = new List<DistrictZone>(zones.Count);
        foreach (var zone in zones) views.Add(View(zone, host));
        var plans = rules.Plan(seed, views, sapi.World.SeaLevel);

        var assignmentType = AssignmentType!;
        var kept = new List<object>(zones.Count);
        var before = Tally(rules, views, null);
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i]!;
            var plan = plans[i];
            if (!plan.Keep) continue;
            var ores = (IList)AccessTools.Field(zone.GetType(), "EligibleOres").GetValue(zone)!;
            for (int j = 0; j < ores.Count; j++)
            {
                if (plan.OreKept[j]) continue;
                var none = Activator.CreateInstance(assignmentType)!;
                var tr = Traverse.Create(none);
                tr.Field("OreCode").SetValue("");
                tr.Field("Weight").SetValue(Traverse.Create(ores[j]).Field("Weight").GetValue<float>());
                tr.Field("Density").SetValue(0f);
                ores[j] = none;
            }
            if (plan.BandMin is { } min && plan.BandMax is { } max)
            {
                var box = (Cuboidd)AccessTools.Field(zone.GetType(), "BoundingBox").GetValue(zone)!;
                // GetOreBlocksInChunk tests block centres (y + 0.5) against the box.
                box.Y1 = min;
                box.Y2 = max + 1;
            }
            kept.Add(zone);
        }
        zones.Clear();
        foreach (var z in kept) zones.Add(z);
        Interlocked.Increment(ref DistrictsDone);
        var after = Tally(rules, views, plans);
        sapi.Logger.Notification("[seraphhorizons] Smaller deposits: district '{0}' at ({1}, {2}): {3} of {4} ore zones kept; veins per metal {5}",
            code, centre.X, centre.Y, kept.Count, views.Count,
            string.Join(", ", after.Select(kv => $"{kv.Key} {kv.Value} (was {before.GetValueOrDefault(kv.Key)})")));
    }

    /// <summary>Veins (shoots and ladders) holding each sized metal, before (no plans) or after.</summary>
    private static SortedDictionary<string, int> Tally(DistrictVeins rules, List<DistrictZone> zones, IReadOnlyList<DistrictZonePlan>? plans)
    {
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Kind == DistrictZoneKind.Lens || plans is { } p && !p[i].Keep) continue;
            var metals = zones[i].Ores
                .Where((o, j) => o.Density > 0 && rules.Sized(o.Metal) && (plans == null || plans[i].OreKept[j]))
                .Select(o => o.Metal!).Distinct();
            foreach (var m in metals) tally[m] = tally.GetValueOrDefault(m) + 1;
        }
        return tally;
    }

    private static DistrictZone View(object zone, string host)
    {
        var type = zone.GetType();
        string kind = AccessTools.Field(type, "ZoneType").GetValue(zone)!.ToString()!;
        var box = (Cuboidd)AccessTools.Field(type, "BoundingBox").GetValue(zone)!;
        var ores = new List<DistrictOre>();
        foreach (var a in (IList)AccessTools.Field(type, "EligibleOres").GetValue(zone)!)
        {
            var tr = Traverse.Create(a);
            string oreCode = tr.Field("OreCode").GetValue<string>() ?? "";
            var variants = tr.Field("AllowedVariantsByInBlock").GetValue<Dictionary<string, List<string>>>();
            var (resolved, ingots) = IngotsPerBlock(oreCode, host, variants);
            ores.Add(new DistrictOre(OreMetals.WorldgenMetalOf(DistrictVeins.OreOfCode(oreCode)), tr.Field("Weight").GetValue<float>(),
                resolved ? tr.Field("Density").GetValue<float>() : 0, ingots));
        }
        var k = kind switch
        {
            "OreShoot" => DistrictZoneKind.Shoot,
            "LadderVein" => DistrictZoneKind.Ladder,
            _ => DistrictZoneKind.Lens,
        };
        double perLayer = k switch
        {
            DistrictZoneKind.Shoot => ShootBlocksPerLayer(zone),
            DistrictZoneKind.Ladder => LadderBlocksPerLayer(zone),
            _ => 0,
        };
        return new DistrictZone(k, ores, perLayer, (int)Math.Floor(box.Y1), (int)Math.Ceiling(box.Y2));
    }

    // An ore shoot is an ellipse along the fault, half as wide as it is long, with density
    // 1 - r² (OreShootSampler), cut to the fault rock: per layer about the fault's width times
    // the integral of 1 - (s/r)² along it, 4r/3.
    private static double ShootBlocksPerLayer(object zone)
    {
        var sampler = AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!;
        var st = Traverse.Create(sampler);
        float r = st.Field("_halfStrike").GetValue<float>();
        double cx = st.Field("_centreX").GetValue<double>(), cz = st.Field("_centreZ").GetValue<double>();
        var fault = Traverse.Create(AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone));
        double halfWidth = fault.Property("AverageWidthHalf").GetValue<float>();
        if (fault.Property("Segments").GetValue() is IList { Count: > 0 } segments
            && fault.Method("FindNearestSegmentIndex", new Vec3d(cx, 0, cz)).GetValue() is int i and >= 0 && i < segments.Count)
            halfWidth = Traverse.Create(segments[i]).Field("WidthHalf").GetValue<float>();
        return Math.Max(1, 2 * halfWidth) * 4.0 / 3.0 * r;
    }

    // A ladder vein is one-block slabs, every SlabSpacings apart, across its stretch of fault
    // widened by WideningFactor and tapered over 50 blocks at each end (LadderVeinRegion).
    private static double LadderBlocksPerLayer(object zone)
    {
        var sampler = AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!;
        var region = Traverse.Create(sampler).Field("_region");
        float start = region.Field("ArcStart").GetValue<float>(), end = region.Field("ArcEnd").GetValue<float>();
        float widening = region.Field("WideningFactor").GetValue<float>();
        var spacings = region.Field("SlabSpacings").GetValue<int[]>() ?? [];
        double spacing = spacings.Length > 0 ? Math.Max(1, spacings.Average()) : 4;
        var fault = Traverse.Create(AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone));
        double halfWidth = fault.Property("AverageWidthHalf").GetValue<float>();
        return Math.Max(0, end - start - 50) * Math.Max(1, 2 * halfWidth * widening) / spacing;
    }

    /// <summary>Whether the ore's code resolves to a block in the district's rock (IOG never places
    /// it otherwise: the geology add-on ores of the granite districts name basalt), and the ingots
    /// in one block of its poorest grade there.</summary>
    private static (bool Resolved, double Ingots) IngotsPerBlock(string oreCode, string host, Dictionary<string, List<string>>? variants)
    {
        string? code = oreCode;
        if (oreCode.Contains('*'))
        {
            code = variants != null && variants.TryGetValue(host, out var grades) && grades is { Count: > 0 }
                ? oreCode.Replace("*", grades.OrderBy(g => Array.IndexOf(GradeOrder, g.ToLowerInvariant()) is var n and >= 0 ? n : 999).First())
                : null;
        }
        if (code == null) return (false, 0);
        return IngotsByCode.GetOrAdd(code, c =>
        {
            var block = _sapi?.World.GetBlock(new AssetLocation(c.Contains(':') ? c : "game:" + c));
            if (block == null) return (false, 0);
            if (block.Drops == null) return (true, 0);
            double units = 0;
            foreach (var drop in block.Drops)
                units += (drop.Quantity?.avg ?? 0) * (drop.ResolvedItemstack?.Collectible?.Attributes?["metalUnits"].AsInt(0) ?? 0);
            return (true, DepositSizing.Ingots(units));
        });
    }

    private static readonly string[] GradeOrder = ["poor", "medium", "rich", "bountiful"];

    private sealed class SettingsFile
    {
        public int VeinsPerMetal { get; set; } = 8;
        public string[]? KeepCount { get; set; }
        public double GradeAllowance { get; set; } = 1.15;
        public int BandFloor { get; set; } = 4;
        public int BandBelowSeaLevel { get; set; } = 6;
    }
}
