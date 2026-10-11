namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The bone-ash cupel's figures (<c>cupel</c> in <c>config/ore-processing.json</c>, #722).</summary>
/// <param name="CapacityUnits">The most metal units a charge holds, ore and lead added together.</param>
/// <param name="LeadPerOreUnit">Lead units a charge needs per unit of an ore whose main metal is not
/// lead (tetrahedrite, freibergite).</param>
/// <param name="MeltingPoint">The heat (°C) the forge must hold the cupel at.</param>
/// <param name="SecondsPerIngot">Seconds per 100 units of charge with the blast gate open.</param>
public sealed record CupelSettings(double CapacityUnits, double LeadPerOreUnit, double MeltingPoint, double SecondsPerIngot);

/// <summary>One stack of a cupel's charge: a roasted concentrate of a cupellable ore
/// (<see cref="Ore"/> set), lead added (<see cref="Ore"/> null), or anything else
/// (<see cref="Foreign"/>), with the metal units it holds.</summary>
public readonly record struct CupelCharge(string? Ore, double Units, bool Foreign = false)
{
    public static CupelCharge Lead(double units) => new(null, units);
    public static CupelCharge Other => new(null, 0, true);
}

/// <summary>Why a cupel won't work its charge; <see cref="None"/> when it will.</summary>
public enum CupelRefusal { None, Empty, Foreign, NothingToPart, TooMuch, TooLittleLead }

/// <summary>What a charge gives: lead (into litharge, the spent cupel) and the other metals (silver,
/// copper), in metal units.</summary>
public sealed record CupelYield(double LeadUnits, IReadOnlyList<MetalUnits> Metals)
{
    public double UnitsOf(string metal) => Metals.Where(m => m.Metal == metal).Sum(m => m.Units);
}

/// <summary>
/// Cupellation in a bone-ash cupel at the forge (#722, hand and tier 1; README "Cupellation"). The
/// charge is roasted concentrate of an ore whose silver cupellation parts (galena, argentiferous
/// galena, tetrahedrite, freibergite), with lead added for an ore whose main metal is not lead (any
/// lead the crucible would take: lead bits, roasted galena, a galena nugget; never litharge, which is
/// lead already spent). The lead soaks into the cupel as litharge; what is left is the bead.
/// <list type="bullet">
/// <item>Each ore's metal comes out as <see cref="OreRecovery.Smelted"/> has it at the hand tier: all
/// of the main metal and each cupellation by-product at its share × 85 % (galena: its lead and 3 %
/// silver; argentiferous galena 38 %; tetrahedrite its copper and 5 % silver; freibergite its silver and
/// 30 % copper). Lead, the ore's and the added, goes into the litharge.</item>
/// <item>Copper does not stay in the bead: it oxidises with the lead. The pack gives it back as copper
/// bits beside the litharge, at #685's share, rather than lose it in the cupel.</item>
/// <item>Whole items are made from units by <see cref="Whole"/>: the fraction left over is one more
/// item at that chance, drawn once when the cupel is done, so a charge yields its units on average.</item>
/// </list>
/// </summary>
public static class Cupellation
{
    /// <summary>The tier the cupel parts at (85 %, as tier 1).</summary>
    public const OreTier Tier = OreTier.Hand;

    /// <summary>Units of metal in one bit, nugget or litharge.</summary>
    public const double UnitsPerItem = 5;

    /// <summary>Whether a cupel takes an ore's roasted concentrate: a sulfide whose silver cupellation
    /// parts, as its main metal or a by-product.</summary>
    public static bool Cupels(OreSpec ore) =>
        ore.IsSulfide && (ore.Metal == "silver"
                          || ore.ByProducts.Any(b => b.Metal == "silver" && b.PartedBy == PartingMethod.Cupellation));

    /// <summary>Lead the charge holds: added, and the ores' whose main metal is lead.</summary>
    public static double LeadUnits(OreRecovery r, IEnumerable<CupelCharge> charge) =>
        charge.Where(c => !c.Foreign).Sum(c => c.Ore is not { } ore || r.Ore(ore).Metal == "lead" ? c.Units : 0);

    /// <summary>Whether the cupel works the charge, and if not, why.</summary>
    public static CupelRefusal Check(OreRecovery r, IReadOnlyCollection<CupelCharge> charge)
    {
        if (charge.All(c => !c.Foreign && c.Units <= 0)) return CupelRefusal.Empty;
        if (charge.Any(c => c.Foreign || c.Ore is { } ore && !Cupels(r.Ore(ore)))) return CupelRefusal.Foreign;
        if (!charge.Any(c => c.Ore != null && c.Units > 0)) return CupelRefusal.NothingToPart;
        if (charge.Sum(c => c.Units) > r.Cupel.CapacityUnits + 1e-9) return CupelRefusal.TooMuch;
        double needs = charge.Where(c => c.Ore is { } ore && r.Ore(ore).Metal != "lead").Sum(c => c.Units) * r.Cupel.LeadPerOreUnit;
        return LeadUnits(r, charge) + 1e-9 < needs ? CupelRefusal.TooLittleLead : CupelRefusal.None;
    }

    /// <summary>What a charge the cupel works gives (<see cref="Check"/> first).</summary>
    public static CupelYield Yield(OreRecovery r, IEnumerable<CupelCharge> charge)
    {
        double lead = 0;
        var metals = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var c in charge)
        {
            if (c.Foreign || c.Units <= 0) continue;
            if (c.Ore is not { } ore)
            {
                lead += c.Units;
                continue;
            }
            foreach (var m in r.Smelted(r.Ore(ore), c.Units, Tier))
            {
                if (m.Metal == "lead") lead += m.Units;
                else metals[m.Metal] = metals.GetValueOrDefault(m.Metal) + m.Units;
            }
        }
        return new CupelYield(lead, metals.Select(kv => new MetalUnits(kv.Key, kv.Value)).ToList());
    }

    /// <summary>The seconds a charge of <paramref name="units"/> takes at the gate's
    /// <paramref name="air"/> (open 1, half 0.85, quarter 0.7); null with no air (the gate shut).</summary>
    public static double? Seconds(CupelSettings s, double units, double air) =>
        air > 0 ? s.SecondsPerIngot * units / 100 / air : null;

    /// <summary>Whole items of <paramref name="perItem"/> units from <paramref name="units"/>: the whole
    /// ones, and one more if <paramref name="draw"/> (uniform in [0, 1)) falls under the fraction left.</summary>
    public static int Whole(double units, double perItem, double draw)
    {
        if (!(units > 0) || !(perItem > 0)) return 0;
        double items = units / perItem;
        // Shave float noise so an exact 40 is 40, not 39 and a near-certain 40th.
        double floor = Math.Floor(items + 1e-9);
        double rest = Math.Max(0, items - floor);
        return (int)floor + (draw < rest ? 1 : 0);
    }
}
