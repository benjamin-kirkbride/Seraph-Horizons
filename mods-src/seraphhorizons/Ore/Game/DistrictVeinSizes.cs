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
/// after its faults and before its zones are indexed by chunk). With ore processing's quarter units
/// (#863) a kept vein of a sized metal is also widened four times in ore per layer (<see cref="Widen"/>),
/// so it holds the ingots it did in the height it had. District ore never goes through
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
    private static double _blocks = 1;
    private static HashSet<string> _countryRocks = new(StringComparer.Ordinal);
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
            || AccessTools.Field(AccessTools.TypeByName("InterestingOreGen.Generators.OreShootSampler"), "_halfAcross") == null
            || AccessTools.Field(AccessTools.TypeByName("InterestingOreGen.Generators.LadderVeinRegion"), "SlabSpacings") == null
            || AccessTools.Field(AccessTools.TypeByName("InterestingOreGen.Generators.LadderVeinSampler"), "_region") == null)
            return "the ore shoot or ladder vein sampler changed";
        if (AccessTools.Field(LensSamplerType, "_lenses") == null || AccessTools.Method(LensSamplerType, "GetTightBoundingBox") == null
            || AccessTools.Inner(LensSamplerType, "LensEntry") is not { } lens
            || new[] { "RadiusH", "RadiusV" }.Any(f => AccessTools.Field(lens, f)?.FieldType != typeof(float)))
            return "the horsetail lens sampler changed";
        return null;
    }

    /// <param name="blocks">How many times the ore per layer a sized vein is made
    /// (<see cref="DistrictVeinSettings.Blocks"/>): <see cref="OreSystem.GradedOreBlocks"/>.</param>
    public static void Bind(ICoreServerAPI api, Harmony harmony, OreSizeTable sizes, double blocks)
    {
        var s = api.Assets.Get(SettingsAsset).ToObject<SettingsFile>();
        _blocks = blocks;
        _rules = new DistrictVeins(sizes, new DistrictVeinSettings(s.VeinsPerMetal, s.KeepCount ?? [], s.GradeAllowance, s.BandFloor,
            s.BandBelowSeaLevel, blocks));
        _sapi = api;
        _countryRocks = CountryRocks(api);
        DistrictsDone = 0;
        harmony.Patch(DetectMethod, postfix: new HarmonyMethod(typeof(DistrictVeinSizes), nameof(DetectOreZonesPostfix)));
        api.Logger.Notification("[seraphhorizons] Smaller deposits: hydrothermal district veins sized, at most {0} veins per metal per district (all for {1})",
            s.VeinsPerMetal, string.Join(", ", s.KeepCount ?? []));
    }

    public static void Unbind()
    {
        _rules = null;
        _blocks = 1;
        _countryRocks = new(StringComparer.Ordinal);
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

        bool countryHost = DistrictVeins.HostIsCountryRock(host, _countryRocks);
        var views = new List<DistrictZone>(zones.Count);
        var richness = new Dictionary<object, float[]?>(ReferenceEqualityComparer.Instance);
        foreach (var zone in zones) views.Add(View(zone, host, countryHost, richness, sapi.World.BlockAccessor.MapSizeY));
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
            if (plan.Widen)
                Widen(zone, views[i].Kind, countryHost);
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
        sapi.Logger.Notification("[seraphhorizons] Smaller deposits: district '{0}' at ({1}, {2}), host {6}{7}: {3} of {4} ore zones kept; veins per metal {5}",
            code, centre.X, centre.Y, kept.Count, views.Count,
            string.Join(", ", after.Select(kv => $"{kv.Key} {kv.Value} (was {before.GetValueOrDefault(kv.Key)})")),
            host, countryHost ? " (a country rock: shoots counted whole)" : "");
    }

    /// <summary>The block codes the world's country rock can be: every rock stratum
    /// (<c>worldgen/rockstrata.json</c>, as the geology mods patch it) and the default rock under
    /// them (<c>worldgen/global.json</c>). A district whose host rock is one of them is sized as if
    /// all its zones were host rock (<see cref="DistrictVeins.HostIsCountryRock"/>, #943).</summary>
    private static HashSet<string> CountryRocks(ICoreServerAPI api)
    {
        var rocks = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var strata = api.Assets.TryGet(new AssetLocation("game", "worldgen/rockstrata.json"))?.ToObject<StrataFile>();
            foreach (var v in strata?.Variants ?? []) if (!string.IsNullOrEmpty(v.BlockCode)) rocks.Add(v.BlockCode);
            var global = api.Assets.TryGet(new AssetLocation("game", "worldgen/global.json"))?.ToObject<GlobalFile>();
            if (!string.IsNullOrEmpty(global?.DefaultRockCode)) rocks.Add(global.DefaultRockCode);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Smaller deposits: could not read the rock strata, district shoots are counted as cut to their faults: {0}", e.Message);
        }
        return rocks;
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

    private static DistrictZone View(object zone, string host, bool countryHost, Dictionary<object, float[]?> richness, int mapHeight)
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
                resolved ? tr.Field("Density").GetValue<float>() : 0, ingots, GradeIngots(oreCode, host, variants)));
        }
        var k = kind switch
        {
            "OreShoot" => DistrictZoneKind.Shoot,
            "LadderVein" => DistrictZoneKind.Ladder,
            _ => DistrictZoneKind.Lens,
        };
        double perLayer = k switch
        {
            DistrictZoneKind.Shoot => ShootBlocksPerLayer(zone, countryHost),
            DistrictZoneKind.Ladder => LadderBlocksPerLayer(zone),
            _ => 0,
        };
        double widening = k switch
        {
            DistrictZoneKind.Shoot => _blocks,
            DistrictZoneKind.Ladder => DistrictVeins.CloserSlabs(LadderSpacings(zone), _blocks).Gain,
            _ => _blocks,
        };
        float[]? rich = null;
        if (k != DistrictZoneKind.Lens && AccessTools.Field(type, "ParentFault").GetValue(zone) is { } fault)
        {
            if (!richness.TryGetValue(fault, out rich))
                richness[fault] = rich = Richness(fault, mapHeight);
        }
        return new DistrictZone(k, ores, perLayer, (int)Math.Floor(box.Y1), (int)Math.Ceiling(box.Y2), widening, rich);
    }

    /// <summary>A fault's richness at each height (IOG's <c>HydrothermalFaultSpline.RichnessAt</c>,
    /// from its rich bands, built before the zones), or null if that is gone: then the grade
    /// allowance stands in.</summary>
    private static float[]? Richness(object fault, int mapHeight)
    {
        if (AccessTools.Method(fault.GetType(), "RichnessAt", [typeof(int)]) is not { } method || method.ReturnType != typeof(float)) return null;
        var rich = new float[mapHeight];
        for (int y = 0; y < mapHeight; y++) rich[y] = (float)method.Invoke(fault, [y])!;
        return rich;
    }

    private static Type? LensSamplerType => AccessTools.TypeByName("InterestingOreGen.Generators.HorsetailLensSampler");

    private static int[] LadderSpacings(object zone) =>
        Traverse.Create(AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)).Field("_region").Field("SlabSpacings").GetValue<int[]>() ?? [];

    /// <summary>
    /// Widens a kept vein to about <see cref="_blocks"/> times its ore per layer (#863). An ore shoot is
    /// made that much longer along the fault (its sampler's half strike only, so the ellipse's area
    /// and its length of fault both grow that many times, whether it is cut to the fault or whole,
    /// #943), its box grown to match before IOG indexes the zones by chunk; a ladder vein gets its
    /// slabs closer (<see cref="DistrictVeins.CloserSlabs"/>), keeping its width, so the fault rock
    /// IOG lays is as it was. A horsetail lens's lenses grow (<see cref="DistrictVeins.LensRadii"/>),
    /// its box set to the sampler's new tight box.
    /// </summary>
    private static void Widen(object zone, DistrictZoneKind kind, bool countryHost)
    {
        var sampler = AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!;
        if (kind == DistrictZoneKind.Shoot)
        {
            var strike = AccessTools.Field(sampler.GetType(), "_halfStrike");
            float r = (float)strike.GetValue(sampler)!;
            strike.SetValue(sampler, (float)(r * _blocks));
            var box = (Cuboidd)AccessTools.Field(zone.GetType(), "BoundingBox").GetValue(zone)!;
            double grow = r * (_blocks - 1);
            box.X1 -= grow;
            box.X2 += grow;
            box.Z1 -= grow;
            box.Z2 += grow;
        }
        else if (kind == DistrictZoneKind.Ladder)
        {
            var region = Traverse.Create(sampler).Field("_region").GetValue()!;
            var field = AccessTools.Field(region.GetType(), "SlabSpacings");
            field.SetValue(region, DistrictVeins.CloserSlabs((int[]?)field.GetValue(region) ?? [], _blocks).Spacings);
        }
        else
        {
            var lenses = (IList)AccessTools.Field(sampler.GetType(), "_lenses").GetValue(sampler)!;
            var entry = AccessTools.Inner(sampler.GetType(), "LensEntry");
            var h = AccessTools.Field(entry, "RadiusH");
            var v = AccessTools.Field(entry, "RadiusV");
            for (int i = 0; i < lenses.Count; i++)
            {
                // A struct: set the fields on the boxed copy and put it back.
                var lens = lenses[i]!;
                var (rh, rv) = DistrictVeins.LensRadii((float)h.GetValue(lens)!, (float)v.GetValue(lens)!, _blocks, countryHost);
                h.SetValue(lens, rh);
                v.SetValue(lens, rv);
                lenses[i] = lens;
            }
            var tight = (Cuboidd)AccessTools.Method(sampler.GetType(), "GetTightBoundingBox").Invoke(sampler, null)!;
            var box = (Cuboidd)AccessTools.Field(zone.GetType(), "BoundingBox").GetValue(zone)!;
            (box.X1, box.Y1, box.Z1) = (tight.X1, tight.Y1, tight.Z1);
            (box.X2, box.Y2, box.Z2) = (tight.X2, tight.Y2, tight.Z2);
        }
    }

    // An ore shoot is an ellipse along the fault, half as wide as it is long, with density
    // 1 - r² (OreShootSampler): cut to the fault rock, or whole where the host is a country rock
    // (DistrictVeins.ShootBlocksPerLayer).
    private static double ShootBlocksPerLayer(object zone, bool countryHost)
    {
        var sampler = AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!;
        var st = Traverse.Create(sampler);
        float r = st.Field("_halfStrike").GetValue<float>();
        float across = st.Field("_halfAcross").GetValue<float>();
        double cx = st.Field("_centreX").GetValue<double>(), cz = st.Field("_centreZ").GetValue<double>();
        var fault = Traverse.Create(AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone));
        double halfWidth = fault.Property("AverageWidthHalf").GetValue<float>();
        if (fault.Property("Segments").GetValue() is IList { Count: > 0 } segments
            && fault.Method("FindNearestSegmentIndex", new Vec3d(cx, 0, cz)).GetValue() is int i and >= 0 && i < segments.Count)
            halfWidth = Traverse.Create(segments[i]).Field("WidthHalf").GetValue<float>();
        return DistrictVeins.ShootBlocksPerLayer(r, across, halfWidth, countryHost);
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
    public static (bool Resolved, double Ingots) IngotsPerBlock(string oreCode, string host, Dictionary<string, List<string>>? variants)
    {
        string? code = oreCode;
        if (oreCode.Contains('*'))
        {
            code = variants != null && variants.TryGetValue(host, out var grades) && grades is { Count: > 0 }
                ? oreCode.Replace("*", grades.OrderBy(g => Array.IndexOf(GradeOrder, g.ToLowerInvariant()) is var n and >= 0 ? n : 999).First())
                : null;
        }
        if (code == null) return (false, 0);
        return BlockIngots(code);
    }

    /// <summary>The ingots a block of each grade the district's rock allows gives, in IOG's order
    /// (poorest first; a grade whose block is missing places nothing there, so 0), or null for an
    /// ungraded ore or one the rock allows no grade of.</summary>
    public static double[]? GradeIngots(string oreCode, string host, Dictionary<string, List<string>>? variants)
    {
        if (!oreCode.Contains('*') || variants == null || !variants.TryGetValue(host, out var grades) || grades is not { Count: > 0 })
            return null;
        return grades.OrderBy(g => Array.IndexOf(GradeOrder, g.ToLowerInvariant()) is var n and >= 0 ? n : 999)
            .Select(g => BlockIngots(oreCode.Replace("*", g)).Ingots).ToArray();
    }

    private static (bool Resolved, double Ingots) BlockIngots(string code)
    {
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

    private sealed class StrataFile
    {
        public StrataVariant[]? Variants { get; set; }
    }

    private sealed class StrataVariant
    {
        public string? BlockCode { get; set; }
    }

    private sealed class GlobalFile
    {
        public string? DefaultRockCode { get; set; }
    }

    private sealed class SettingsFile
    {
        public int VeinsPerMetal { get; set; } = 8;
        public string[]? KeepCount { get; set; }
        public double GradeAllowance { get; set; } = 1.25;
        public int BandFloor { get; set; } = 4;
        public int BandBelowSeaLevel { get; set; } = 6;
    }
}
