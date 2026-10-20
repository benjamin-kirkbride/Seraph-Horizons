using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.CupellationFurnace.Core;

/// <summary>
/// The cupellation furnace's own settings (#740; <c>CupellationFurnaceSettings</c> in ModConfig/seraphhorizons.json).
/// Its recovery (95 %) and its rate (55 units of lead an hour) are ore processing's (<c>config/ore-processing.json</c>,
/// <c>machines.cupellationfurnace</c>); its blast (10 L/s of Air at 1.5 atm or more), its row's chimney and its test's
/// wear (15 charges) are the model's (<c>config/cupellationfurnace-rig.json</c>, <c>config/builds/cupellationfurnace.json</c>).
/// </summary>
public class CupellationFurnaceConfig
{
    /// <summary>The most metal units a charge holds, bars and lead together: the bone-ash cupel's limit (#722), the
    /// owner's figure for the test's 15 charges (about 3,000 units of lead to a test).</summary>
    public double ChargeUnits { get; set; } = 200;

    /// <summary>Coal (or coke, or charcoal) the fire burns an in-game hour while lit with the damper open (the owner's
    /// ruling of 2026-10-14: 0.7).</summary>
    public double CoalPerHour { get; set; } = 0.7;

    /// <summary>Lead units a charge needs to each unit of a bar whose metal is not lead (argentiferous copper,
    /// cupriferous silver), as the bone-ash cupel's <c>leadPerOreUnit</c>.</summary>
    public double LeadPerOtherUnit { get; set; } = 1;

    /// <summary>The share of the full draught (the pace of the work and the fire's burning) the damper half shut
    /// gives; open is the whole, shut none (the fire banked).</summary>
    public double DamperHalfDraught { get; set; } = 0.5;

    /// <summary>The share of the open rate the banked fire (the damper shut) still burns its coal at.</summary>
    public double BankedBurn { get; set; } = 0.25;

    public static readonly CupellationFurnaceConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        var d = Defaults;
        if (!(ChargeUnits is >= 100 and <= 1000))
        {
            fixes.Add($"ChargeUnits {ChargeUnits} is out of range (100 to 1000), using {d.ChargeUnits}");
            ChargeUnits = d.ChargeUnits;
        }
        if (!(CoalPerHour is > 0 and <= 10))
        {
            fixes.Add($"CoalPerHour {CoalPerHour} is out of range, using {d.CoalPerHour}");
            CoalPerHour = d.CoalPerHour;
        }
        if (!(LeadPerOtherUnit is >= 0 and <= 10))
        {
            fixes.Add($"LeadPerOtherUnit {LeadPerOtherUnit} is out of range, using {d.LeadPerOtherUnit}");
            LeadPerOtherUnit = d.LeadPerOtherUnit;
        }
        if (!(DamperHalfDraught is >= 0 and <= 1))
        {
            fixes.Add($"DamperHalfDraught {DamperHalfDraught} is out of range, using {d.DamperHalfDraught}");
            DamperHalfDraught = d.DamperHalfDraught;
        }
        if (!(BankedBurn is >= 0 and <= 1))
        {
            fixes.Add($"BankedBurn {BankedBurn} is out of range, using {d.BankedBurn}");
            BankedBurn = d.BankedBurn;
        }
        return fixes;
    }
}

/// <summary>One stack of the furnace's charge: a bar of an ore's base bullion (<see cref="Ore"/> set), a bar of the
/// Parkes kettle's rich lead (<see cref="RichLead"/>), lead added (a lead ingot: neither), or anything else
/// (<see cref="Foreign"/>), with the metal units it holds. <see cref="Whole"/>: the share of a bar's units off the
/// whole route (<see cref="Bullion.WholeShareAttribute"/>), made whole here when the furnace is the ore's final step.</summary>
public readonly record struct FurnaceCharge(string? Ore, double Units, bool RichLead = false, bool Foreign = false, double Whole = 0)
{
    public static FurnaceCharge Lead(double units) => new(null, units);
    public static FurnaceCharge Rich(double units) => new(null, units, RichLead: true);
    public static FurnaceCharge Bar(string ore, double units, double whole = 0) => new(ore, units, Whole: whole);
    public static FurnaceCharge Other => new(null, 0, Foreign: true);
}

/// <summary>Why the furnace refuses a charge, or will not work the one it holds; <see cref="None"/> when it will.
/// Each a lang entry <c>seraphhorizons:cupellationfurnace-error-{name}</c>.</summary>
public enum CupellingRefusal
{
    None,
    /// <summary>Nothing in the dish.</summary>
    Empty,
    /// <summary>Something it does not take: only silver-lead, argentiferous copper, cupriferous silver, rich lead
    /// and lead.</summary>
    Foreign,
    /// <summary>Only lead: nothing to part.</summary>
    NothingToPart,
    /// <summary>More than a charge.</summary>
    TooMuch,
    /// <summary>Argentiferous copper or cupriferous silver without its lead.</summary>
    TooLittleLead,
    /// <summary>A charge is being worked: nothing goes in or comes out until it is done.</summary>
    Working,
    /// <summary>The last charge's silver is still on the test.</summary>
    TakeOutFirst,
    /// <summary>The test is spent: it is relined with bone meal before the next charge.</summary>
    TestWorn,
    /// <summary>The pot holds no more litharge: empty it.</summary>
    PotFull,
}

/// <summary>What a charge gives: lead (into litharge) and the other metals (silver, copper), in metal units.</summary>
public sealed record CupellingYield(double LeadUnits, IReadOnlyList<MetalUnits> Metals)
{
    public double UnitsOf(string metal) => Metals.Where(m => m.Metal == metal).Sum(m => m.Units);
}

/// <summary>
/// What the cupellation furnace does (#740; README "Ore processing: the cupellation furnace"): an English test
/// furnace, batch, charged and emptied by hand through its working door. A charge of base bullion that cupellation
/// parts (<see cref="Bullion.TakenBy"/>: silver-lead, argentiferous copper and cupriferous silver, the last two with
/// as much lead added), the Parkes kettle's rich lead, and lead, up to <see cref="CupellationFurnaceConfig.ChargeUnits"/>,
/// is worked on the bone-ash test at ore processing's rate (55 units of lead an hour) while the fire burns, the
/// blast blows and the row's chimney draws:
/// <list type="bullet">
/// <item>the lead burns to litharge (5 units an item, as the cupel's) and runs into the pot;</item>
/// <item>each bar's by-product comes out at the furnace's 95 % (<see cref="OreRecovery.Smelted"/> at
/// <see cref="PartingStation.CupellationFurnace"/>), and for an ore whose final step the furnace is (argentiferous
/// galena, tetrahedrite, freibergite) the share of a bar off the whole route made whole, its main metal and
/// by-product both (<see cref="FurnaceCharge.Whole"/>); rich lead's silver whole (the Parkes kettle's 100 %): silver stays
/// on the test as the cake, copper (which oxidises with the lead) comes back beside it, both as metal bits, as the
/// bone-ash cupel gives them (#722);</item>
/// <item>the pace is the blast's share (air arriving ÷ the blast's rate, at most 1) times the damper's draught;
/// the fire burns its coal at <see cref="CupellationFurnaceConfig.CoalPerHour"/> times the damper's share.</item>
/// </list>
/// </summary>
public static class Cupelling
{
    /// <summary>Lead added to a charge.</summary>
    public const string LeadIngot = "game:ingot-lead";

    /// <summary>The fire's fuel, by hand at the fire door: coal, coke or charcoal (the owner's ruling).</summary>
    public static readonly IReadOnlyList<string> Fuels =
        ["game:ore-bituminouscoal", "game:ore-anthracite", "game:coke", "game:charcoal"];

    /// <summary>Units of metal in one litharge or metal bit.</summary>
    public const double UnitsPerItem = Cupellation.UnitsPerItem;

    /// <summary>Whether the furnace parts an ore's bullion.</summary>
    public static bool Takes(OreRecovery r, OreSpec ore) => Bullion.TakenBy(r, ore, PartingStation.CupellationFurnace);

    /// <summary>The lead in a charge: added, rich lead, and bars whose metal is lead.</summary>
    public static double LeadUnits(OreRecovery r, IEnumerable<FurnaceCharge> charge) =>
        charge.Where(c => !c.Foreign).Sum(c => c.Ore is not { } ore || r.Ore(ore).Metal == "lead" ? c.Units : 0);

    /// <summary>The units of bars whose metal is not lead, which want lead added.</summary>
    public static double OtherUnits(OreRecovery r, IEnumerable<FurnaceCharge> charge) =>
        charge.Where(c => !c.Foreign && c.Ore is { } ore && r.Ore(ore).Metal != "lead").Sum(c => c.Units);

    /// <summary>Whether the furnace would work the charge, and if not, why (its contents only).</summary>
    public static CupellingRefusal Check(OreRecovery r, CupellationFurnaceConfig cfg, IReadOnlyCollection<FurnaceCharge> charge)
    {
        if (charge.All(c => !c.Foreign && c.Units <= 0)) return CupellingRefusal.Empty;
        if (charge.Any(c => c.Foreign || c.Ore is { } ore && !Takes(r, r.Ore(ore)))) return CupellingRefusal.Foreign;
        if (!charge.Any(c => (c.Ore != null || c.RichLead) && c.Units > 0)) return CupellingRefusal.NothingToPart;
        if (charge.Sum(c => c.Units) > cfg.ChargeUnits + 1e-9) return CupellingRefusal.TooMuch;
        double lead = charge.Where(c => c.Ore == null).Sum(c => c.Units);
        return lead + 1e-9 < OtherUnits(r, charge) * cfg.LeadPerOtherUnit ? CupellingRefusal.TooLittleLead : CupellingRefusal.None;
    }

    /// <summary>Whether a stack of <paramref name="units"/> more fits a charge already holding
    /// <paramref name="held"/> units.</summary>
    public static bool Fits(CupellationFurnaceConfig cfg, double held, double units) => held + units <= cfg.ChargeUnits + 1e-9;

    /// <summary>What a charge the furnace works gives (<see cref="Check"/> first).</summary>
    public static CupellingYield Yield(OreRecovery r, IEnumerable<FurnaceCharge> charge)
    {
        double lead = 0;
        var metals = new SortedDictionary<string, double>(StringComparer.Ordinal);
        void Add(string metal, double units) => metals[metal] = metals.GetValueOrDefault(metal) + units;
        foreach (var c in charge)
        {
            if (c.Foreign || c.Units <= 0) continue;
            if (c.RichLead)
            {
                lead += c.Units;
                Add("silver", c.Units * r.RichLeadSilver);
                continue;
            }
            if (c.Ore is not { } ore)
            {
                lead += c.Units;
                continue;
            }
            foreach (var m in r.Smelted(r.Ore(ore), c.Units, PartingStation.CupellationFurnace, wholeShare: c.Whole))
            {
                if (m.Metal == "lead") lead += m.Units;
                else Add(m.Metal, m.Units);
            }
        }
        return new CupellingYield(lead, metals.Select(kv => new MetalUnits(kv.Key, kv.Value)).ToList());
    }

    /// <summary>The litharge a charge of <paramref name="leadUnits"/> puts in the pot, at most (a fraction rounds up).</summary>
    public static int LithargeFor(double leadUnits) => leadUnits > 0 ? (int)Math.Ceiling(leadUnits / UnitsPerItem - 1e-9) : 0;

    /// <summary>The damper's share of the full draught: the pace of the work and the fire's burning. None without a
    /// working chimney on the row.</summary>
    public static double Draught(Damper damper, bool rowDraws, CupellationFurnaceConfig cfg) =>
        !rowDraws ? 0 : damper switch
        {
            Damper.Open => 1,
            Damper.Half => cfg.DamperHalfDraught,
            _ => 0,
        };

    /// <summary>The share of the open rate the fire burns its coal at: the damper's draught, or banked
    /// (<see cref="CupellationFurnaceConfig.BankedBurn"/>) with it shut or no draught.</summary>
    public static double BurnShare(Damper damper, bool rowDraws, CupellationFurnaceConfig cfg) =>
        Math.Max(Draught(damper, rowDraws, cfg), cfg.BankedBurn);

    /// <summary>The blast's share: the air that arrived over the litres the work wants, 0 to 1 (the owner's ruling:
    /// pace × min(1, air ÷ 10 L/s)).</summary>
    public static double BlastShare(double litres, double wanted) => wanted > 0 ? Math.Clamp(litres / wanted, 0, 1) : 0;

    /// <summary>The work after <paramref name="hours"/> more at <paramref name="pace"/> (the blast's share × the
    /// draught): units of lead cupelled, at <paramref name="perHour"/>, and whether the charge's
    /// <paramref name="leadUnits"/> are done.</summary>
    public static (double Done, bool Finished) Advance(double done, double hours, double pace, double perHour, double leadUnits)
    {
        double next = Math.Max(0, done) + Math.Max(0, hours) * Math.Clamp(pace, 0, 1) * Math.Max(0, perHour);
        return next >= leadUnits - 1e-9 ? (leadUnits, true) : (next, false);
    }

    /// <summary>
    /// The fire after <paramref name="hours"/> more: it burns <paramref name="coalPerHour"/> × <paramref name="share"/>
    /// coal an hour, first the piece burning (<paramref name="ember"/>, 0 to 1 of a coal), then whole ones from the
    /// <paramref name="coal"/> at the fire door. Returns the ember left, the coal taken and whether it still burns.
    /// </summary>
    public static (double Ember, int Taken, bool Lit) Burn(double ember, int coal, double hours, double coalPerHour, double share)
    {
        double need = Math.Max(0, hours) * Math.Max(0, coalPerHour) * Math.Clamp(share, 0, 1);
        ember = Math.Clamp(ember, 0, 1);
        if (need <= ember)
            return (ember - need, 0, ember - need > 1e-12 || coal > 0);
        need -= ember;
        int whole = (int)Math.Ceiling(need - 1e-12);
        if (whole > coal)
            return (0, coal, false);
        return (whole - need, whole, true);
    }
}
