namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Base bullion and rich lead: what the parting machines take (#740, the owner's ruling of 2026-10-13 on
/// #740, #927, #929 and #931; README "Ore processing: base bullion"). Smelting any form of a by-product ore
/// (one with <see cref="OreSpec.ByProducts"/>) gives its bullion rather than its plain metal: a bar of the
/// ore's main metal that still carries the by-product, which only a parting machine wins.
/// <list type="bullet">
/// <item>One bullion item per ore, <c>seraphhorizons:bullion-{ore}-{metal}</c> (<see cref="Code"/>), the
/// metal its main metal. A crucible holds a smelted stack with no attributes, so the ore, and with it the
/// by-product's share, is in the code; the metal is last because the game's crucible and ingot mold draw
/// the molten metal and the cast bar by the code's last part. It casts in the ingot mold, which keeps the
/// poured stack itself.</item>
/// <item>A bar holds <see cref="UnitsPerItem"/> units of the main metal and the by-product at the ore's
/// share of them (galena 3 silver, argentiferous galena 38, tetrahedrite 5 silver, freibergite 30 copper,
/// teallite 40 lead, franckeite 30 lead, gold quartz 15 silver): silver-lead, argentiferous copper,
/// cupriferous silver, tin-lead and doré.</item>
/// <item>Smelted again, plainly, a bar gives its main metal at the ore's <c>unparted</c> share (gold quartz
/// 85 %, the rest whole) and the by-product is lost.</item>
/// <item>Rich lead (<see cref="RichLeadCode"/>) is the Parkes kettle's (#927): a bar of 100 units of lead
/// carrying <see cref="OreRecovery.RichLeadSilver"/> of it as silver, which the cupellation furnace parts
/// whole. Smelted plainly it gives lead.</item>
/// </list>
/// What a parting machine gets from a charge of bars is <see cref="OreRecovery.Smelted"/> on
/// <see cref="UnitsPerItem"/> units a bar at that machine's <see cref="PartingStation"/>.
/// <para>The whole route survives the crucible: a bar carries the share of its units smelted from
/// concentrate (or roasted concentrate) marked off the ore's whole ideal route (<see cref="WholeShareAttribute"/>,
/// <see cref="WholeShare"/>), bars of one ore merging at their weighted share (<see cref="MergeWhole"/>), and the
/// parting machine that is the ore's final step makes that share whole
/// (<see cref="OreRecovery.Smelted(OreSpec, double, PartingStation?, double)"/>).</para>
/// </summary>
public static class Bullion
{
    /// <summary>Units of the main metal a bar holds: an ingot's.</summary>
    public const double UnitsPerItem = 100;

    /// <summary>The Parkes kettle's rich lead (#927).</summary>
    public const string RichLeadCode = OreProducts.Domain + ":richlead";

    private const string Prefix = OreProducts.Domain + ":bullion-";

    /// <summary>The bar's stack attribute holding the share of its units that came off the whole route (0 to 1;
    /// absent for none).</summary>
    public const string WholeShareAttribute = "oreWholeShare";

    /// <summary>
    /// The share of a smelt's units that came off the whole route: the units of the marked parts over all the parts'
    /// (each part a stack in the crucible, its units what it smelts to). 0 with nothing marked or nothing in it.
    /// </summary>
    public static double WholeShare(IEnumerable<(double Units, bool Whole)> parts)
    {
        double all = 0, whole = 0;
        foreach (var (units, marked) in parts)
        {
            if (!(units > 0)) continue;
            all += units;
            if (marked) whole += units;
        }
        return all > 0 ? Clamp(whole / all) : 0;
    }

    /// <summary>The share of a stack of <paramref name="count"/> bars at <paramref name="share"/> with
    /// <paramref name="otherCount"/> more at <paramref name="otherShare"/> merged in: weighted by bars.</summary>
    public static double MergeWhole(double share, double count, double otherShare, double otherCount)
    {
        count = Math.Max(0, count);
        otherCount = Math.Max(0, otherCount);
        return count + otherCount > 0 ? Clamp((Clamp(share) * count + Clamp(otherShare) * otherCount) / (count + otherCount)) : 0;
    }

    /// <summary>A share held to 0 to 1, float noise at either end shaved off (so a bar of all-marked units stacks
    /// with another).</summary>
    public static double Clamp(double share) =>
        !(share > 1e-9) ? 0 : share > 1 - 1e-9 ? 1 : share;

    /// <summary>Whether an ore smelts to bullion: it has a by-product, won only by parting.</summary>
    public static bool Makes(OreSpec ore) => ore.ByProducts.Count > 0;

    /// <summary>An ore's bullion: <c>seraphhorizons:bullion-{ore}-{metal}</c>.</summary>
    public static string Code(OreSpec ore) => $"{Prefix}{ore.Ore}-{ore.Metal}";

    /// <summary>The ore a bullion code is of; null for a code that is not bullion.</summary>
    public static string? OreOf(string code)
    {
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string rest = code[Prefix.Length..];
        int dash = rest.LastIndexOf('-');
        return dash > 0 ? rest[..dash] : null;
    }

    /// <summary>How an ore's by-product is parted; null for an ore with none.</summary>
    public static PartingMethod? MethodOf(OreSpec ore) => ore.ByProducts.Count > 0 ? ore.ByProducts[0].PartedBy : null;

    /// <summary>
    /// Whether a parting machine takes an ore's bullion: the cupellation furnace every cupellation ore's
    /// (silver-lead, argentiferous copper, cupriferous silver), the liquation furnace tin-lead, the acid
    /// parting vessel doré. The Parkes kettle takes silver-lead leaner than its own rich lead
    /// (<see cref="OreRecovery.RichLeadSilver"/>): plain galena's, not argentiferous galena's, which is
    /// richer than the crust already and is cupelled directly. The hand stations take roasted concentrate,
    /// not bullion.
    /// </summary>
    public static bool TakenBy(OreRecovery r, OreSpec ore, PartingStation s)
    {
        if (MethodOf(ore) is not { } method || method != OreRecovery.MethodOf(s)) return false;
        return s switch
        {
            PartingStation.CupellationFurnace or PartingStation.LiquationFurnace or PartingStation.AcidParting => true,
            PartingStation.ParkesKettle => ore.Metal == "lead"
                                           && ore.ByProducts.Any(b => b.Metal == "silver")
                                           && ore.ByProducts.Where(b => b.Metal == "silver").Sum(b => b.Share) < r.RichLeadSilver,
            _ => false,
        };
    }

    /// <summary>The share of its main metal a bar gives smelted plainly: the ore's <c>unparted</c>.</summary>
    public static double PlainShare(OreSpec ore) => ore.Unparted;

    /// <summary>Silver units in one rich lead bar.</summary>
    public static double RichLeadSilverUnits(OreRecovery r) => UnitsPerItem * r.RichLeadSilver;

    /// <summary>Rich lead bars (fractional) that carry <paramref name="silverUnits"/> of silver at
    /// <paramref name="richLeadSilver"/>; 0 for none or a share that is not positive.</summary>
    public static double RichLeadBars(double silverUnits, double richLeadSilver) =>
        silverUnits > 0 && richLeadSilver > 0 ? silverUnits / (UnitsPerItem * richLeadSilver) : 0;
}
