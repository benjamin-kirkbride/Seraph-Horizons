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
/// (density 0, the same weight, so the others' shares stay), and an ore whose code names no block
/// in the district's rock, which IOG draws but never places, is taken out of it (its share goes to
/// the ores that place, #946); a vein's band is its bounding box's
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
    private static int[] _surfaces = [];
    private static RockStrataPrediction? _strata;
    private static bool _strataTried;
    private static readonly object StrataLock = new();
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
        if (zone == null || new[] { "ZoneType", "EligibleOres", "BoundingBox", "Sampler", "ParentFault", "TotalOreWeight" }.Any(f => AccessTools.Field(zone, f) == null))
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
        _surfaces = s.SurfaceAboveSeaLevel is { Length: > 0 } heights ? heights : [0];
        _strata = null;
        _strataTried = false;
        DistrictsDone = 0;
        harmony.Patch(DetectMethod, postfix: new HarmonyMethod(typeof(DistrictVeinSizes), nameof(DetectOreZonesPostfix)));
        api.Logger.Notification("[seraphhorizons] Smaller deposits: hydrothermal district veins sized, at most {0} veins per metal per district (all for {1})",
            s.VeinsPerMetal, string.Join(", ", s.KeepCount ?? []));
    }

    public static void Unbind()
    {
        _rules = null;
        _blocks = 1;
        _surfaces = [];
        _strata = null;
        _strataTried = false;
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var t = Traverse.Create(district);
        var zones = (IList)t.Property("OreZones").GetValue();
        var centre = (Vec2i)t.Property("Centre").GetValue();
        var config = t.Property("Config").GetValue();
        string code = Traverse.Create(config).Field("Code").GetValue<string>() ?? "";
        string host = Traverse.Create(config).Field("HostMaterialCode").GetValue<string>() ?? "";
        long seed = ((long)centre.X << 32) ^ (uint)centre.Y ^ (long)OreCells.Fnv1a(code);

        var strata = Strata(sapi);
        int hostId = sapi.World.GetBlock(new AssetLocation(host))?.BlockId ?? 0;
        var hostRock = strata != null && hostId != 0 ? new HostRock(strata, hostId, _surfaces, sapi.World.SeaLevel) : null;
        var views = new List<DistrictZone>(zones.Count);
        var richness = new Dictionary<object, float[]?>(ReferenceEqualityComparer.Instance);
        var traces = new Dictionary<object, FaultTrace?>(ReferenceEqualityComparer.Instance);
        WiderShapes.Clear();
        foreach (var zone in zones) views.Add(View(rules, zone, host, hostRock, richness, traces, sapi.World.BlockAccessor.MapSizeY, false));
        // Which veins keep which ores does not depend on their sizes: plan once to count, then
        // measure only the shoots that keep a sized metal (a few dozen of a district's hundreds).
        var counted = rules.Plan(seed, views, sapi.World.SeaLevel);
        for (int i = 0; i < zones.Count; i++)
            if (views[i].Kind == DistrictZoneKind.Shoot && counted[i].Keep
                && views[i].Ores.Where((o, j) => counted[i].OreKept[j] && o.Density > 0 && rules.Sized(o.Metal)).Any())
                views[i] = View(rules, zones[i]!, host, hostRock, richness, traces, sapi.World.BlockAccessor.MapSizeY, true);
        var plans = rules.Plan(seed, views, sapi.World.SeaLevel);

        var assignmentType = AssignmentType!;
        var kept = new List<object>(zones.Count);
        var before = Tally(rules, views, null);
        int removed = 0, inHost = 0, lensesWidened = 0, thinned = 0;
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i]!;
            var plan = plans[i];
            if (!plan.Keep) continue;
            var ores = (IList)AccessTools.Field(zone.GetType(), "EligibleOres").GetValue(zone)!;
            var placing = new List<object>(ores.Count);
            for (int j = 0; j < ores.Count; j++)
            {
                // IOG draws an ore whose code names no block in the host rock and places nothing:
                // out, so the ores that place share its blocks (#946).
                if (views[i].Ores[j].Density <= 0)
                {
                    removed++;
                    continue;
                }
                if (plan.OreKept[j])
                {
                    // The richer metal of a two-metal vein, thinned to its own size (#946): a copy
                    // (IOG shares an ore's assignment between its zones) at the cut density.
                    if (plan.Thin is { } thin && thin[j] < 1)
                    {
                        var copy = Activator.CreateInstance(assignmentType)!;
                        foreach (var f in AccessTools.GetDeclaredFields(assignmentType).Where(f => !f.IsStatic))
                            f.SetValue(copy, f.GetValue(ores[j]));
                        var d = Traverse.Create(copy).Field("Density");
                        d.SetValue((float)(d.GetValue<float>() * thin[j]));
                        placing.Add(copy);
                        thinned++;
                    }
                    else placing.Add(ores[j]!);
                    continue;
                }
                var none = Activator.CreateInstance(assignmentType)!;
                var tr = Traverse.Create(none);
                tr.Field("OreCode").SetValue("");
                tr.Field("Weight").SetValue(Traverse.Create(ores[j]).Field("Weight").GetValue<float>());
                tr.Field("Density").SetValue(0f);
                placing.Add(none);
            }
            ores.Clear();
            foreach (var o in placing) ores.Add(o);
            if (placing.Count != views[i].Ores.Count)
            {
                // IOG picks a block's ore by weight out of this total, cached when the zone was made.
                float total = 0;
                foreach (var o in placing) total += Traverse.Create(o).Field("Weight").GetValue<float>();
                AccessTools.Field(zone.GetType(), "TotalOreWeight").SetValue(zone, total);
            }
            if (plan.Widen)
            {
                bool whole = Widen(zone, views[i].Kind, hostRock, plan.Wider > 0 ? WiderShapes[(zone, plan.Wider - 1)] : null);
                if (views[i].Kind == DistrictZoneKind.Lens)
                {
                    lensesWidened++;
                    if (whole) inHost++;
                }
            }
            if (plan.Drawn > 0 && plan.BandMin is { } y0 && plan.BandMax is { } y1)
            {
                var v = views[i];
                var (f, c) = plan.Wider > 0 && v.Wider is { } w ? w[plan.Wider - 1] : (v.BlocksPerLayer, v.CountryBlocks);
                double share = v.CountryShare is { } cs ? Enumerable.Range(y0, y1 - y0 + 1).Average(y => cs(y)) : 0;
                sapi.Logger.VerboseDebug("[seraphhorizons] Smaller deposits: district '{0}' at ({1}, {2}) zone {3} {4}: y {5}..{6}, shape {7} ({8:0} fault and {9:0} country blocks a layer at density 1, {10:P0} of it host rock), drawn {11:0} ingots, the band holds {12:0} by the estimate",
                    code, centre.X, centre.Y, kept.Count, v.Kind, y0, y1, plan.Wider, f * v.Widening, c * v.Widening, share, plan.Drawn, plan.Held);
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
        var banded = plans.Where(p => p.Keep && p.Drawn > 0).ToList();
        int roomed = banded.Count(p => p.Held < p.Drawn * 0.9);
        var ratios = banded.Select(p => p.Held / p.Drawn).Order().ToList();
        string sizing = banded.Count == 0 ? "no vein banded"
            : $"{banded.Count} veins banded, {banded.Count(p => p.Wider > 0)} of them wider, {thinned} ores thinned to their metal's size, {roomed} cut to the room under their drawn size (median holds {ratios[ratios.Count / 2]:P0} of drawn); sized in {clock.ElapsedMilliseconds} ms";
        sapi.Logger.Notification("[seraphhorizons] Smaller deposits: district '{0}' at ({1}, {2}), host {6}: {3} of {4} ore zones kept, {7} ores that place nothing in {6} taken out; veins per metal {5}; {9}; {8}",
            code, centre.X, centre.Y, kept.Count, views.Count,
            string.Join(", ", after.Select(kv => $"{kv.Key} {kv.Value} (was {before.GetValueOrDefault(kv.Key)})")),
            host, removed,
            hostRock == null ? "strata not predicted, shoots counted as their fault rock" : $"{inHost} of {lensesWidened} widened lenses in {host} country rock",
            sizing);
    }

    /// <summary>The strata prediction, made once the world's generators are initialised (the first
    /// district is built after that); null, with a warning, if it can't be.</summary>
    private static RockStrataPrediction? Strata(ICoreServerAPI api)
    {
        lock (StrataLock)
        {
            if (_strataTried) return _strata;
            _strataTried = true;
            _strata = RockStrataPrediction.TryCreate(api, out var why);
            if (_strata == null)
                api.Logger.Warning("[seraphhorizons] Smaller deposits: cannot predict the rock strata ({0}); district shoots are counted as their fault rock only", why ?? "");
            return _strata;
        }
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

    private static DistrictZone View(DistrictVeins rules, object zone, string host, HostRock? hostRock, Dictionary<object, float[]?> richness,
        Dictionary<object, FaultTrace?> traces, int mapHeight, bool measure)
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
        // A shoot's blocks are measured only if asked (it keeps a sized metal that places).
        bool sized = measure && ores.Any(o => o.Density > 0 && rules.Sized(o.Metal));
        ShootShape? shape = null;
        double perLayer = 0, country = 0;
        IReadOnlyList<(double Fault, double Country)>? wider = null;
        if (k == DistrictZoneKind.Shoot && sized)
        {
            (perLayer, country, shape) = ShootBlocks(zone, traces);
            // Measured only for a shoot that needs one (most fit their own shape).
            var own = shape;
            wider = new LazyShapes(WiderCount, () =>
            {
                var list = new List<(double, double)>();
                foreach (var (i, w) in Wider(zone, own, traces).Select((w, i) => (i, w)))
                {
                    WiderShapes[(zone, i)] = (w.Strike, w.Across);
                    list.Add((w.Fault, w.Country));
                }
                return list;
            });
        }
        else if (k == DistrictZoneKind.Ladder) perLayer = LadderBlocksPerLayer(zone);
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
        // The share of a shoot's country rock that is host rock, by height: predicted only if the
        // rules ask (a kept vein of a sized metal), over the shoot as widened.
        System.Func<int, double>? share = null;
        if (shape != null && country > 0 && hostRock != null)
        {
            var st = Traverse.Create(AccessTools.Field(type, "Sampler").GetValue(zone));
            float dip = st.Field("_dipShift").GetValue<float>();
            int height = st.Field("_worldHeight").GetValue<int>();
            var lazy = new Lazy<double[]>(() => hostRock.ShootShare(shape.Samples(), shape.AcrossX, shape.AcrossZ, dip, height),
                LazyThreadSafetyMode.ExecutionAndPublication);
            share = y => y >= 0 && y < lazy.Value.Length ? lazy.Value[y] : 0;
        }
        return new DistrictZone(k, ores, perLayer, (int)Math.Floor(box.Y1), (int)Math.Ceiling(box.Y2), widening, rich, country, share, wider);
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
    /// IOG lays is as it was. A horsetail lens's lenses grow (<see cref="DistrictVeins.LensRadii"/>):
    /// across only where the predicted strata at the lens are mostly host rock, so the whole
    /// spheroid is ore, both ways otherwise (#946); its box set to the sampler's new tight box.
    /// Returns whether a lens zone's lenses were taken as in host rock (most of them).
    /// </summary>
    private static bool Widen(object zone, DistrictZoneKind kind, HostRock? hostRock, (double Strike, double Across)? wider = null)
    {
        var sampler = AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!;
        if (kind == DistrictZoneKind.Shoot)
        {
            var strike = AccessTools.Field(sampler.GetType(), "_halfStrike");
            var across = AccessTools.Field(sampler.GetType(), "_halfAcross");
            float r = (float)strike.GetValue(sampler)!;
            float b = (float)across.GetValue(sampler)!;
            var (ks, ka) = wider ?? (_blocks, 1);
            strike.SetValue(sampler, (float)(r * ks));
            across.SetValue(sampler, (float)(b * ka));
            // IOG's box is the centre ± (strike + 2) each way; a wider shape's is its own (ShootShape).
            double half = wider == null ? r * ks + 2 : WiderBox(sampler, r * ks, b * ka);
            var box = (Cuboidd)AccessTools.Field(zone.GetType(), "BoundingBox").GetValue(zone)!;
            double grow = half - (r + 2);
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
            var st = Traverse.Create(sampler);
            double ax = st.Field("_acrossX").GetValue<double>(), az = st.Field("_acrossZ").GetValue<double>();
            float dip = st.Field("_dipShift").GetValue<float>();
            int height = st.Field("_worldHeight").GetValue<int>();
            int whole = 0;
            for (int i = 0; i < lenses.Count; i++)
            {
                // A struct: set the fields on the boxed copy and put it back.
                var lens = lenses[i]!;
                var le = Traverse.Create(lens);
                float rh0 = (float)h.GetValue(lens)!, rv0 = (float)v.GetValue(lens)!;
                int cy = le.Field("CentreY").GetValue<int>();
                double shift = dip * (double)(height - cy);
                bool inHost = hostRock != null
                              && hostRock.Share(le.Field("CentreX").GetValue<double>() + ax * shift, le.Field("CentreZ").GetValue<double>() + az * shift,
                                  cy - (int)rv0, cy + (int)rv0) >= 0.5;
                if (inHost) whole++;
                var (rh, rv) = DistrictVeins.LensRadii(rh0, rv0, _blocks, inHost);
                h.SetValue(lens, rh);
                v.SetValue(lens, rv);
                lenses[i] = lens;
            }
            var tight = (Cuboidd)AccessTools.Method(sampler.GetType(), "GetTightBoundingBox").Invoke(sampler, null)!;
            var box = (Cuboidd)AccessTools.Field(zone.GetType(), "BoundingBox").GetValue(zone)!;
            (box.X1, box.Y1, box.Z1) = (tight.X1, tight.Y1, tight.Z1);
            (box.X2, box.Y2, box.Z2) = (tight.X2, tight.Y2, tight.Z2);
            return 2 * whole > lenses.Count;
        }
        return false;
    }

    /// <summary>
    /// An ore shoot's blocks per layer at density 1 as widened (<see cref="_blocks"/> times its strike),
    /// divided by that widening (the rules multiply it back): the fault rock IOG lays through it, its
    /// own blocks counted from the fault's spline (<see cref="FaultTrace"/>), and the rest of it, in
    /// country rock (<see cref="ShootShape"/>, #946).
    /// </summary>
    private static (double Fault, double Country, ShootShape Shape) ShootBlocks(object zone, Dictionary<object, FaultTrace?> traces)
    {
        var st = Traverse.Create(AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!);
        double strike = st.Field("_halfStrike").GetValue<float>() * _blocks;
        var shape = new ShootShape(st.Field("_centreX").GetValue<double>(), st.Field("_centreZ").GetValue<double>(),
            st.Field("_tangentX").GetValue<double>(), st.Field("_tangentZ").GetValue<double>(),
            st.Field("_acrossX").GetValue<double>(), st.Field("_acrossZ").GetValue<double>(),
            strike, st.Field("_halfAcross").GetValue<float>(), strike + 2);
        double whole = shape.Whole();
        var fault = AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone);
        double faultRock;
        if (fault != null && Trace(fault, traces) is { } trace)
            faultRock = trace.LayersNear(shape.CentreX, shape.CentreZ, shape.BoxHalf).Average(shape.Over);
        else
            // The fault's spline can't be read: its width times 4r/3, as before #946.
            faultRock = Math.Min(whole, Math.Max(1, 2 * ShootHalfWidth(zone)) * 4.0 / 3.0 * strike);
        faultRock = Math.Min(faultRock, whole);
        return (faultRock / _blocks, (whole - faultRock) / _blocks, shape);
    }

    private const int WiderCount = 4;

    /// <summary>A list of a known length whose items are measured when first read.</summary>
    private sealed class LazyShapes(int count, Func<List<(double, double)>> measure) : IReadOnlyList<(double Fault, double Country)>
    {
        private readonly Lazy<List<(double, double)>> _items = new(measure, LazyThreadSafetyMode.ExecutionAndPublication);
        public int Count => count;
        public (double Fault, double Country) this[int index] => _items.Value[index];
        public IEnumerator<(double Fault, double Country)> GetEnumerator() => _items.Value.Select(t => ((double, double))t).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The wider shapes offered for each sized shoot of the district being sized, by zone and
    /// index: how many times IOG's strike and across it takes (the plan picks one, #946).</summary>
    [ThreadStatic] private static Dictionary<(object, int), (double Strike, double Across)>? _widerShapes;

    private static Dictionary<(object, int), (double Strike, double Across)> WiderShapes =>
        _widerShapes ??= new Dictionary<(object, int), (double, double)>(new ZoneIndexComparer());

    private sealed class ZoneIndexComparer : IEqualityComparer<(object, int)>
    {
        public bool Equals((object, int) a, (object, int) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;
        public int GetHashCode((object, int) k) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Item1), k.Item2);
    }

    /// <summary>
    /// Wider shapes for a shoot whose band would not fit the room (#946): two, four, eight and sixteen times the
    /// area of its own widened shape (<see cref="_blocks"/> times IOG's), each split between strike and
    /// across (strike at most sixteen times IOG's, across eight) the way that holds the most of its fault's rock: a
    /// longer strike gains little where the fault turns away from the segment's tangent, a wider
    /// ellipse follows it further. Its box is what the sheared ellipse needs, so none of it is cut
    /// off, and its whole is π · strike · across / 2 over the shear (<c>sqrt(1 − (tangent · across)²)</c>).
    /// Per layer in the rules' units (divided by <see cref="_blocks"/>, which they multiply back).
    /// </summary>
    private static IEnumerable<(double Strike, double Across, double Fault, double Country)> Wider(object zone, ShootShape own, Dictionary<object, FaultTrace?> traces)
    {
        var st = Traverse.Create(AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!);
        double r = st.Field("_halfStrike").GetValue<float>(), b = st.Field("_halfAcross").GetValue<float>();
        double c = Math.Abs(own.TangentX * own.AcrossX + own.TangentZ * own.AcrossZ);
        double shear = Math.Sqrt(Math.Max(1e-6, 1 - c * c));
        var fault = AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone);
        var trace = fault == null ? null : Trace(fault, traces);
        double reach = Math.Max(r * 16, b * 8) / Math.Sqrt(Math.Max(1e-6, 1 - c)) + 2;
        var rock = trace?.LayersNear(own.CentreX, own.CentreZ, reach);
        for (int i = 1; i <= WiderCount; i++)
        {
            double area = _blocks * (1 << i);
            (double Ks, double Ka, double Fault, double Whole)? best = null;
            for (double ka = 1; ka <= 8 && ka <= area; ka *= 2)
            {
                double ks = area / ka;
                if (ks > 16) continue;
                var shape = own with { HalfStrike = r * ks, HalfAcross = b * ka, BoxHalf = Box(r * ks, b * ka, c) };
                double whole = Math.PI * shape.HalfStrike * shape.HalfAcross / 2 / shear;
                double f = rock != null ? rock.Average(shape.Over) : Math.Max(1, 2 * ShootHalfWidth(zone)) * 4.0 / 3.0 * Math.Min(shape.HalfStrike, shape.HalfAcross / Math.Max(c, 1e-3));
                f = Math.Min(f, whole);
                if (best == null || f > best.Value.Fault) best = (ks, ka, f, whole);
            }
            var (bks, bka, bf, bw) = best!.Value;
            yield return (bks, bka, bf / _blocks, (bw - bf) / _blocks);
        }
    }

    // The box half size a sheared ellipse needs: its larger semi-axis over the smaller singular value
    // of (tangent, across), sqrt(1 - |tangent · across|), and IOG's 2 blocks.
    private static double Box(double strike, double across, double c) => Math.Max(strike, across) / Math.Sqrt(Math.Max(1e-6, 1 - c)) + 2;

    private static double WiderBox(object sampler, double strike, double across)
    {
        var st = Traverse.Create(sampler);
        double c = Math.Abs(st.Field("_tangentX").GetValue<double>() * st.Field("_acrossX").GetValue<double>()
                            + st.Field("_tangentZ").GetValue<double>() * st.Field("_acrossZ").GetValue<double>());
        return Box(strike, across, c);
    }

    private static FaultTrace? Trace(object fault, Dictionary<object, FaultTrace?> traces)
    {
        if (!traces.TryGetValue(fault, out var trace)) traces[fault] = trace = FaultTrace.Of(fault);
        return trace;
    }

    /// <summary>
    /// The plan of the fault rock IOG lays along a fault (<c>HydrothermalFaultSpline.PlaceInChunk</c>,
    /// at the top of the fault, where its dip has not shifted it): at every step of one block along
    /// its spline, the blocks across it (the fault's global across direction) within its half width
    /// there, which is its average times the width factor at that step, tapered over its first and
    /// last 50 blocks and widened in a ladder vein's stretch.
    /// </summary>
    private sealed class FaultTrace
    {
        private readonly double[] _x, _z;
        private readonly float[] _half;
        private readonly double _ax, _az, _dip;

        private FaultTrace(double[] x, double[] z, float[] half, double ax, double az, double dip)
        {
            (_x, _z, _half, _ax, _az, _dip) = (x, z, half, ax, az, dip);
        }

        /// <summary>The depths below the fault's top the fault rock is counted at, averaged: the
        /// band's (y 4 to 104 under a top of 255) and enough of them to even out where its rays
        /// cross from one column to the next.</summary>
        private static readonly int[] Depths = [150, 151, 152, 153];

        /// <summary>
        /// The fault rock near a point, a layer at each of <see cref="Depths"/>, as IOG lays it
        /// (<c>PlaceInChunk</c>): every block-wide ray down the fault from (its spline's point + w
        /// across) steps <c>dip</c> across a layer, and where it moves into a new column it also lays
        /// the column it leaves, at the same height, which fills the gaps of a fault running at a
        /// slant to the block grid. Each block once a layer, as its centre undipped (moved back up
        /// the dip to the fault's top), where a shoot's density is read.
        /// </summary>
        public List<List<(double X, double Z)>> LayersNear(double x, double z, double reach)
        {
            var layers = new List<List<(double, double)>>(Depths.Length);
            foreach (int k in Depths)
            {
                var seen = new HashSet<(int, int)>();
                double sx = _ax * _dip * k, sz = _az * _dip * k, px = _ax * _dip * (k - 1), pz = _az * _dip * (k - 1);
                for (int i = 0; i < _x.Length; i++)
                {
                    double r = reach + _half[i] + 2 + Math.Abs(sx) + Math.Abs(sz);
                    if (Math.Abs(_x[i] - x) > r || Math.Abs(_z[i] - z) > r) continue;
                    int w = (int)Math.Floor(_half[i]);
                    for (int j = -w; j <= w; j++)
                    {
                        double ox = _x[i] + _ax * j, oz = _z[i] + _az * j;
                        int bx = (int)Math.Floor(ox + sx), bz = (int)Math.Floor(oz + sz);
                        seen.Add((bx, bz));
                        int qx = (int)Math.Floor(ox + px), qz = (int)Math.Floor(oz + pz);
                        if (qx != bx || qz != bz) seen.Add((qx, qz));
                    }
                }
                double ux = _ax * _dip * (k - 0.5), uz = _az * _dip * (k - 0.5);
                layers.Add(seen.Select(b => (b.Item1 + 0.5 - ux, b.Item2 + 0.5 - uz)).ToList());
            }
            return layers;
        }

        public static FaultTrace? Of(object fault)
        {
            var t = fault.GetType();
            var centreAt = AccessTools.Method(t, "EvaluateCentreAt", [typeof(float)]);
            var widthAt = AccessTools.Method(t, "EvaluateWidthFactorAt", [typeof(float)]);
            if (centreAt == null || widthAt == null || centreAt.ReturnType != typeof(Vec3d) || widthAt.ReturnType != typeof(float)) return null;
            var tr = Traverse.Create(fault);
            float length = tr.Property("ParameterLength").GetValue<float>();
            float avg = tr.Property("AverageWidthHalf").GetValue<float>();
            double ax = tr.Property("GlobalAcrossX").GetValue<double>(), az = tr.Property("GlobalAcrossZ").GetValue<double>();
            double dip = tr.Property("DipShiftPerBlock").GetValue<float>();
            var ladders = new List<(float Start, float End, float Widening, MethodInfo Taper, object Region)>();
            if (tr.Property("LadderVeinRegions").GetValue() is IList regions)
                foreach (var r in regions)
                {
                    var rt = Traverse.Create(r);
                    if (AccessTools.Method(r!.GetType(), "TaperAt", [typeof(float)]) is { } taper)
                        ladders.Add((rt.Field("ArcStart").GetValue<float>(), rt.Field("ArcEnd").GetValue<float>(), rt.Field("WideningFactor").GetValue<float>(), taper, r));
                }
            if (!(length > 0)) return null;
            int n = (int)Math.Ceiling(length) + 1;
            var xs = new double[n];
            var zs = new double[n];
            var half = new float[n];
            for (int i = 0; i < n; i++)
            {
                float at = Math.Min(i, length);
                var c = (Vec3d)centreAt.Invoke(fault, [at])!;
                float w = avg * (float)widthAt.Invoke(fault, [at])!;
                if (at < 50f) w *= at / 50f;
                else if (at > length - 50f) w *= (length - at) / 50f;
                foreach (var l in ladders)
                    if (at >= l.Start && at <= l.End)
                    {
                        w *= 1f + (l.Widening - 1f) * (float)l.Taper.Invoke(l.Region, [at])!;
                        break;
                    }
                (xs[i], zs[i], half[i]) = (c.X, c.Z, w);
            }
            return new FaultTrace(xs, zs, half, ax, az, dip);
        }
    }

    // The half width of the fault segment nearest the shoot's centre (its average if that is gone).
    private static double ShootHalfWidth(object zone)
    {
        var st = Traverse.Create(AccessTools.Field(zone.GetType(), "Sampler").GetValue(zone)!);
        double cx = st.Field("_centreX").GetValue<double>(), cz = st.Field("_centreZ").GetValue<double>();
        var fault = Traverse.Create(AccessTools.Field(zone.GetType(), "ParentFault").GetValue(zone));
        double halfWidth = fault.Property("AverageWidthHalf").GetValue<float>();
        if (fault.Property("Segments").GetValue() is IList { Count: > 0 } segments
            && fault.Method("FindNearestSegmentIndex", new Vec3d(cx, 0, cz)).GetValue() is int i and >= 0 && i < segments.Count)
            halfWidth = Traverse.Create(segments[i]).Field("WidthHalf").GetValue<float>();
        return halfWidth;
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

    private sealed class SettingsFile
    {
        public int VeinsPerMetal { get; set; } = 8;
        public string[]? KeepCount { get; set; }
        public double GradeAllowance { get; set; } = 1.25;
        public int BandFloor { get; set; } = 4;
        public int BandBelowSeaLevel { get; set; } = 6;
        public int[]? SurfaceAboveSeaLevel { get; set; }
    }
}
