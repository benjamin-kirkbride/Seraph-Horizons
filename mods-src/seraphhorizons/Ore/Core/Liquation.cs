namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The clay liquation pan's figures (<c>liquation</c> in <c>config/ore-processing.json</c>, #724).</summary>
/// <param name="CapacityUnits">The most metal units a charge holds.</param>
/// <param name="TinPoint">The heat (°C) at which the tin sweats out of the charge: the pan's melting
/// point, where the firepit or the forge starts the run.</param>
/// <param name="LeadPoint">The heat (°C) over which the lead melts too and runs with the tin, so the
/// charge loses its lead.</param>
/// <param name="SecondsPerIngot">Seconds at the tin point per 100 units of charge.</param>
public sealed record LiquationSettings(double CapacityUnits, double TinPoint, double LeadPoint, double SecondsPerIngot);

/// <summary>One stack of a pan's charge: a roasted concentrate of an ore liquation parts
/// (<see cref="Ore"/> set) with the metal units it holds, or anything else (<see cref="Foreign"/>).</summary>
public readonly record struct LiquationCharge(string? Ore, double Units, bool Foreign = false)
{
    public static LiquationCharge Other => new(null, 0, true);
}

/// <summary>Why a pan won't work its charge; <see cref="None"/> when it will.</summary>
public enum LiquationRefusal { None, Empty, Foreign, TooMuch }

/// <summary>What a charge gives: the main metal that runs off (tin), poured from the pan, and what stays
/// in it (lead), in metal units.</summary>
public sealed record LiquationYield(string Metal, double Units, IReadOnlyList<MetalUnits> Residue)
{
    public double ResidueOf(string metal) => Residue.Where(m => m.Metal == metal).Sum(m => m.Units);
}

/// <summary>
/// Liquation in a clay pan, in a firepit or crucibulum's forge (#724, hand and tier 1; README
/// "Liquation"). Tin melts at 232 °C and lead at 327 °C: heated gently, roasted teallite or franckeite
/// concentrate sweats its tin out first and leaves its lead behind.
/// <list type="bullet">
/// <item>The charge is roasted concentrate of ores whose by-product liquation parts, all of one main
/// metal (teallite and franckeite: tin), up to <see cref="LiquationSettings.CapacityUnits"/>.</item>
/// <item>Held at the tin point, the tin runs off: all of it, and the lead stays in the pan at its share
/// × 85 % (<see cref="OreRecovery.Smelted"/> at the hand tier: teallite 40 %, franckeite 30 %).</item>
/// <item>A charge over the lead point when it is done ran the lead with the tin: it gives the tin as
/// smelting it unparted would (<see cref="OreRecovery.Smelted"/> with no parting), and no lead.</item>
/// </list>
/// </summary>
public static class Liquation
{
    /// <summary>The tier the pan parts at (85 %, as tier 1).</summary>
    public const OreTier Tier = OreTier.Hand;

    /// <summary>Whether a pan takes an ore's roasted concentrate: a sulfide with a by-product liquation
    /// parts.</summary>
    public static bool Liquates(OreSpec ore) =>
        ore.IsSulfide && ore.ByProducts.Any(b => b.PartedBy == PartingMethod.Liquation);

    /// <summary>Whether the pan works the charge, and if not, why.</summary>
    public static LiquationRefusal Check(OreRecovery r, IReadOnlyCollection<LiquationCharge> charge)
    {
        if (charge.All(c => !c.Foreign && c.Units <= 0)) return LiquationRefusal.Empty;
        if (charge.Any(c => c.Foreign || c.Ore is not { } ore || !Liquates(r.Ore(ore)))) return LiquationRefusal.Foreign;
        // One main metal runs off into one pour.
        if (charge.Select(c => r.Ore(c.Ore!).Metal).Distinct().Count() > 1) return LiquationRefusal.Foreign;
        return charge.Sum(c => c.Units) > r.Liquation.CapacityUnits + 1e-9 ? LiquationRefusal.TooMuch : LiquationRefusal.None;
    }

    /// <summary>Whether a charge at <paramref name="temperature"/> is over the lead point.</summary>
    public static bool Overheated(LiquationSettings s, double temperature) => temperature > s.LeadPoint;

    /// <summary>What a charge the pan works gives (<see cref="Check"/> first); <paramref name="overheated"/>:
    /// the lead ran with the tin.</summary>
    public static LiquationYield Yield(OreRecovery r, IEnumerable<LiquationCharge> charge, bool overheated)
    {
        string? main = null;
        double units = 0;
        var residue = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var c in charge)
        {
            if (c.Foreign || c.Ore is not { } ore || c.Units <= 0) continue;
            var spec = r.Ore(ore);
            main ??= spec.Metal;
            foreach (var m in r.Smelted(spec, c.Units, overheated ? null : Tier))
            {
                if (m.Metal == spec.Metal) units += m.Units;
                else residue[m.Metal] = residue.GetValueOrDefault(m.Metal) + m.Units;
            }
        }
        return new LiquationYield(main ?? "", units, residue.Select(kv => new MetalUnits(kv.Key, kv.Value)).ToList());
    }

    /// <summary>The seconds at the tin point a charge of <paramref name="units"/> takes.</summary>
    public static double Seconds(LiquationSettings s, double units) => s.SecondsPerIngot * units / 100;
}
