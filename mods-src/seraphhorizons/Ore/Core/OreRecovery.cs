namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The hand tier and the machines' tiers 1-4 (epic #684). How a machine is powered
/// doesn't matter, only its tier.</summary>
public enum OreTier { Hand, Tier1, Tier2, Tier3, Tier4 }

/// <summary>Gravity concentrators: the gold pan and rocker by hand, then the concentrator machine's
/// tiers (long-tom sluice, jig, shaking table, table and slime vanner).</summary>
public enum Concentrator { Pan, Rocker, Sluice, Jig, Table, TableAndVanner }

/// <summary>Sulfide roasting: the vanilla firepit (hand and tier 1), the stall roaster (tier 2), the
/// reverberatory (tiers 3 and 4).</summary>
public enum Roaster { Firepit, Stall, Reverberatory }

/// <summary>How a by-product is parted from the main metal: cupellation (silver from lead, and
/// from copper or silver ores with lead added), liquation (tin sweats out first, leaving the lead),
/// acid parting (silver from gold).</summary>
public enum PartingMethod { Cupellation, Liquation, AcidParting }

/// <summary>The design's ore classes: the path an ore takes.</summary>
public enum OreClass { Oxide, Sulfide, Native, Placer }

/// <summary>The forms ore takes on the line. Raw ore and chunks hold their grade's units; the rest
/// hold <see cref="OreRecovery.ConcentrateUnits"/>. A sponge is the gold or silver amalgam leaves in
/// the still once its mercury is driven off (#726).</summary>
public enum OreForm { Raw, Chunk, Crushed, Ground, Concentrate, RoastedConcentrate, Sponge }

/// <summary>What has been done to the feed reaching a concentrator.</summary>
/// <param name="FineGrained">Poor ore: fine-grained, it loses heavily unless ground.</param>
/// <param name="Classified">Through a riddle or classifier.</param>
/// <param name="Ground">Through a grinder (tier 2 and up).</param>
/// <param name="Amalgamated">Free gold and silver caught by amalgamation at the same tier.</param>
public readonly record struct OreFeed(bool FineGrained, bool Classified, bool Ground, bool Amalgamated)
{
    /// <summary>Whether ore of a grade (<c>poor</c>, <c>medium</c>, ...) is fine-grained: only poor.</summary>
    public static bool IsFineGrained(string? grade) => grade == "poor";
}

/// <summary>A by-product of an ore, won only at a parting step.</summary>
/// <param name="Share">Units of it per unit of the main metal recovered, in vein ore.</param>
/// <param name="DistrictShare">The same in ore from a hydrothermal district, when it differs.</param>
public sealed record ByProduct(string Metal, double Share, double? DistrictShare, PartingMethod PartedBy)
{
    public double ShareIn(bool district) => district && DistrictShare is { } d ? d : Share;
}

/// <summary>One ore's processing properties (<c>ores</c> in the config).</summary>
/// <param name="Ore">The ore part of its code (<c>galena</c>).</param>
/// <param name="Metal">Its main metal (<see cref="OreMetals"/>' group, or the ore itself).</param>
/// <param name="Free">Free gold or silver, which wants amalgamation.</param>
/// <param name="Unparted">The share of the main metal a smelt gives without parting.</param>
public sealed record OreSpec(string Ore, string Metal, OreClass Class, double Density, bool Free, double Unparted,
    IReadOnlyList<ByProduct> ByProducts)
{
    public bool IsSulfide => Class == OreClass.Sulfide;
}

/// <summary>A metal and its units out of a smelt (fractional; <see cref="UnitCarry"/> makes items).</summary>
public readonly record struct MetalUnits(string Metal, double Units);

/// <summary>The devices of one line through the stages, for working out a whole line's recovery.</summary>
/// <param name="Roaster">Null when the concentrate is not roasted (a sulfide then doesn't smelt).</param>
public sealed record OreLine(Concentrator Concentrator, bool Classified, bool Ground, bool Amalgamated, Roaster? Roaster)
{
    /// <summary>The standard line at a tier: classified, ground from tier 2, amalgamated, roasted, with
    /// that tier's concentrator and roaster (the hand tier's concentrator is the rocker).</summary>
    public static OreLine AtTier(OreTier tier) =>
        new(OreTiers.ConcentratorAt(tier), true, OreTiers.GrindsAt(tier), true, OreTiers.RoasterAt(tier));
}

/// <summary>Which device each stage has at each tier (the design's stage table). Fixed, not config:
/// the figures are config, the machines are code.</summary>
public static class OreTiers
{
    /// <summary>The concentrator a tier has; the hand tier's best, the rocker (the pan is 45 %).</summary>
    public static Concentrator ConcentratorAt(OreTier tier) => tier switch
    {
        OreTier.Hand => Concentrator.Rocker,
        OreTier.Tier1 => Concentrator.Sluice,
        OreTier.Tier2 => Concentrator.Jig,
        OreTier.Tier3 => Concentrator.Table,
        _ => Concentrator.TableAndVanner,
    };

    public static Roaster RoasterAt(OreTier tier) => tier switch
    {
        OreTier.Hand or OreTier.Tier1 => Roaster.Firepit,
        OreTier.Tier2 => Roaster.Stall,
        _ => Roaster.Reverberatory,
    };

    /// <summary>Grinding arrives at tier 2 (the arrastra).</summary>
    public static bool GrindsAt(OreTier tier) => tier >= OreTier.Tier2;
}

/// <summary>
/// The recovery model (#685): every station and machine asks it how much metal a stage keeps.
/// Overall recovery is the product of the stage multipliers; crushing and grinding keep it all.
/// Recoveries are fractions of metal units; turning units into items, with the fraction carried
/// over to the next item, is <see cref="UnitCarry"/>'s job.
/// </summary>
public sealed class OreRecovery
{
    private readonly OreProcessingConfig _config;
    private readonly Dictionary<string, double> _concentrators;
    private readonly Dictionary<string, double> _roasters;
    private readonly Dictionary<string, double> _smelt;
    private readonly Dictionary<string, Dictionary<string, double>> _parting;
    private readonly Dictionary<string, OreSpec> _ores = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];

    public OreRecovery(OreProcessingConfig config)
    {
        _config = config;
        _concentrators = new(config.Concentrators ?? new(), StringComparer.OrdinalIgnoreCase);
        _roasters = new(config.Roasters ?? new(), StringComparer.OrdinalIgnoreCase);
        _smelt = new(config.Smelt ?? new(), StringComparer.OrdinalIgnoreCase);
        _parting = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (method, byTier) in config.Parting ?? new())
            _parting[method] = new(byTier ?? new(), StringComparer.OrdinalIgnoreCase);

        if (!(config.ConcentrateUnits > 0)) _problems.Add($"concentrateUnits {config.ConcentrateUnits} is not positive");
        foreach (var c in Enum.GetValues<Concentrator>()) CheckShare("concentrators", _concentrators, Key(c));
        foreach (var r in Enum.GetValues<Roaster>()) CheckShare("roasters", _roasters, Key(r));
        foreach (var f in Enum.GetValues<OreForm>()) CheckShare("smelt", _smelt, Key(f));
        CheckShare("unclassified", config.Unclassified);
        CheckShare("fineUnground", config.FineUnground);
        CheckShare("freeUnamalgamated", config.FreeUnamalgamated);
        foreach (var (method, byTier) in _parting)
        {
            if (!TryParse<PartingMethod>(method, out _)) _problems.Add($"parting: unknown method '{method}'");
            foreach (var (tier, v) in byTier)
            {
                if (!TryParse<OreTier>(tier, out _)) _problems.Add($"parting.{method}: unknown tier '{tier}'");
                CheckShare($"parting.{method}.{tier}", v);
            }
        }
        foreach (var (ore, entry) in config.Ores ?? new())
            _ores[ore] = Spec(ore, entry ?? new());
        var retort = config.Retort ?? new();
        CheckShare("retort.mercuryReturn", retort.MercuryReturn);
        if (string.IsNullOrWhiteSpace(retort.Mercury)) _problems.Add("retort.mercury: no mercury item");
        if (!(retort.AmalgamMercury * retort.MercuryReturn >= 1))
            _problems.Add($"retort: an amalgam returns {retort.AmalgamMercury * retort.MercuryReturn} portions of mercury, under one");
        foreach (var (code, portions) in retort.Cinnabar ?? new())
            if (!(portions >= 1)) _problems.Add($"retort.cinnabar.{code}: {portions} portions is under one");
        if (!(retort.PortionsPerSecond > 0)) _problems.Add($"retort.portionsPerSecond {retort.PortionsPerSecond} is not positive");
    }

    /// <summary>The still's retort (#726): the mercury, what an amalgam holds and returns, what
    /// cinnabar gives, the pace. <see cref="MercuryRetort"/> reads it.</summary>
    public OreProcessingConfig.RetortEntry Retort => _config.Retort ?? new();

    /// <summary>What is wrong with the config, for the server log; empty when it is sound. Missing
    /// figures read as 0 (a device that recovers nothing), so they show in play too.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Units of metal in one crushed, ground or concentrate item.</summary>
    public double ConcentrateUnits => _config.ConcentrateUnits;

    /// <summary>An ore's properties; an ore the config doesn't list is an oxide at density 1.</summary>
    public OreSpec Ore(string ore) =>
        _ores.TryGetValue(ore, out var spec) ? spec : new OreSpec(ore, OreMetals.MetalOf(ore) ?? ore, OreClass.Oxide, 1, false, 1, []);

    /// <summary>The ores the config lists.</summary>
    public IEnumerable<OreSpec> Ores => _ores.Values;

    public double ConcentratorBase(Concentrator c) => _concentrators.GetValueOrDefault(Key(c));

    /// <summary>
    /// The share of the feed's metal a concentrator puts into concentrate: its base, × unclassified
    /// if the feed skipped classifying, × fineUnground for fine-grained feed not ground,
    /// × freeUnamalgamated for free gold or silver not amalgamated, × the ore's density; capped at 1.
    /// </summary>
    public double Concentration(OreSpec ore, Concentrator concentrator, OreFeed feed)
    {
        double r = ConcentratorBase(concentrator) * ore.Density;
        if (!feed.Classified) r *= _config.Unclassified;
        if (feed.FineGrained && !feed.Ground) r *= _config.FineUnground;
        if (ore.Free && !feed.Amalgamated) r *= _config.FreeUnamalgamated;
        return Math.Clamp(r, 0, 1);
    }

    /// <summary>The share roasting keeps: 1 for an ore that isn't a sulfide, the roaster's figure for
    /// one that is, and 0 for a sulfide not roasted (it doesn't smelt).</summary>
    public double Roasting(OreSpec ore, Roaster? roaster) =>
        !ore.IsSulfide ? 1 : roaster is { } r ? _roasters.GetValueOrDefault(Key(r)) : 0;

    /// <summary>A parting method's recovery of the by-product at a tier, or null if that tier has no
    /// station for it (acid parting by hand or at tier 1).</summary>
    public double? Parting(PartingMethod method, OreTier tier) =>
        _parting.TryGetValue(Key(method), out var byTier) && byTier.TryGetValue(Key(tier), out var v) ? v : null;

    /// <summary>The share of its units a form gives in a furnace: concentrate 1, crushed ore and
    /// chunks a half, raw and ground ore nothing (config <c>smelt</c>); sulfide concentrate nothing until
    /// roasted (#720). Crushed sulfide and sulfide chunks take the half like any other.</summary>
    public double SmeltShare(OreSpec ore, OreForm form)
    {
        if (ore.IsSulfide && form == OreForm.Concentrate) return 0;
        return _smelt.GetValueOrDefault(Key(form));
    }

    /// <summary>A whole line's recovery of the main metal, into smeltable concentrate: concentration
    /// times roasting (crushing and grinding lose nothing).</summary>
    public double Overall(OreSpec ore, OreLine line, bool fineGrained) =>
        Concentration(ore, line.Concentrator, new OreFeed(fineGrained, line.Classified, line.Ground, line.Amalgamated))
        * Roasting(ore, line.Roaster);

    /// <summary>
    /// What smelting gives from concentrate holding <paramref name="mainUnits"/> of the main metal.
    /// Unparted (<paramref name="partingTier"/> null, or no station at that tier for the ore's
    /// by-products): the main metal × the ore's <c>unparted</c> share, by-products lost. Parted at a
    /// tier: all of the main metal, plus each by-product whose method has a station there, at its
    /// share × that station's recovery. <paramref name="district"/> picks the district share.
    /// </summary>
    public IReadOnlyList<MetalUnits> Smelted(OreSpec ore, double mainUnits, OreTier? partingTier, bool district = false)
    {
        var parted = new List<MetalUnits>();
        if (partingTier is { } tier)
            foreach (var bp in ore.ByProducts)
                if (Parting(bp.PartedBy, tier) is { } p)
                    parted.Add(new MetalUnits(bp.Metal, mainUnits * bp.ShareIn(district) * p));
        if (parted.Count == 0) return [new MetalUnits(ore.Metal, mainUnits * ore.Unparted)];
        return [new MetalUnits(ore.Metal, mainUnits), .. parted];
    }

    /// <summary>The config key of an enum value: its name in lower case (<c>tableandvanner</c>).</summary>
    public static string Key<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static bool TryParse<T>(string s, out T value) where T : struct, Enum =>
        Enum.TryParse(s, ignoreCase: true, out value) && Enum.IsDefined(value) && !int.TryParse(s, out _);

    private OreSpec Spec(string ore, OreProcessingConfig.OreEntry e)
    {
        var cls = OreClass.Oxide;
        if (e.Class is { } c && !TryParse(c, out cls)) _problems.Add($"ores.{ore}: unknown class '{c}'");
        if (!(e.Density > 0)) _problems.Add($"ores.{ore}: density {e.Density} is not positive");
        CheckShare($"ores.{ore}.unparted", e.Unparted);
        var byProducts = new List<ByProduct>();
        foreach (var b in e.ByProducts ?? [])
        {
            if (!TryParse(b.PartedBy, out PartingMethod method))
            {
                _problems.Add($"ores.{ore}: by-product {b.Metal} has unknown partedBy '{b.PartedBy}'");
                continue;
            }
            if (!(b.Share >= 0) || b.DistrictShare is < 0) _problems.Add($"ores.{ore}: by-product {b.Metal} has a negative share");
            byProducts.Add(new ByProduct(b.Metal, b.Share, b.DistrictShare, method));
        }
        return new OreSpec(ore, OreMetals.MetalOf(ore) ?? ore, cls, e.Density, e.Free, e.Unparted, byProducts);
    }

    private void CheckShare(string table, Dictionary<string, double> values, string key)
    {
        if (!values.TryGetValue(key, out var v)) _problems.Add($"{table}: no figure for '{key}'");
        else CheckShare($"{table}.{key}", v);
    }

    private void CheckShare(string name, double v)
    {
        if (!(v >= 0 && v <= 1)) _problems.Add($"{name}: {v} is not between 0 and 1");
    }
}
