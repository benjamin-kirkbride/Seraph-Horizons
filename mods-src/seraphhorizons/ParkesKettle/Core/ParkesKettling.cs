using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.ParkesKettle.Core;

/// <summary>Where a kettle's charge stands.</summary>
public enum KettlePhase
{
    /// <summary>No lead in it.</summary>
    Empty,
    /// <summary>Silver-lead in, short of the zinc it takes: more lead or zinc may go in.</summary>
    Charging,
    /// <summary>The zinc in: the stirrer works the bath while the fire heats it.</summary>
    Stirring,
    /// <summary>The charge done: the crust to skim, then the desilverized lead to ladle out.</summary>
    Done,
}

/// <summary>Why a kettle refuses a click (each a lang entry <c>seraphhorizons:parkeskettle-error-{name}</c>).</summary>
public enum KettleRefusal
{
    None,
    /// <summary>The zinc is in: the charge is being stirred.</summary>
    Stirring,
    /// <summary>The last charge's crust and lead are still in it.</summary>
    Done,
    /// <summary>The charge is full.</summary>
    Full,
    /// <summary>Silver-lead of another ore than the bath's.</summary>
    OtherBullion,
    /// <summary>Bullion the kettle does not take (argentiferous galena's is richer than the crust: cupel it).</summary>
    NotTaken,
    /// <summary>Zinc goes in on the lead.</summary>
    NoLead,
    /// <summary>The zinc for the lead in it is in.</summary>
    ZincIn,
    /// <summary>The grate is full.</summary>
    GrateFull,
    /// <summary>Nothing on the grate to light.</summary>
    NoFuel,
    /// <summary>It is burning.</summary>
    Lit,
}

/// <summary>
/// The Parkes kettle's own settings (#927; <c>ParkesKettleSettings</c> in ModConfig/seraphhorizons.json). Its
/// recovery (100 %), its charge (2,400 units), its hours (5) and its zinc return (80 %) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.parkeskettle</c>); its fuel (6 coal a charge, the grate holding
/// 8) and its row's chimney are the rig's (<see cref="ParkesKettleRig"/>).
/// </summary>
public class ParkesKettleConfig
{
    /// <summary>Units of zinc a unit of lead takes (#927: 1 to 100).</summary>
    public double ZincPerLead { get; set; } = 0.01;

    /// <summary>The share of its heat (the stirring's pace) the damper half shut leaves.</summary>
    public double DamperHalfHeat { get; set; } = 0.5;

    /// <summary>The share of its open rate the fire burns at with the damper half shut.</summary>
    public double DamperHalfBurn { get; set; } = 0.5;

    /// <summary>The share of its open rate the fire burns at banked behind the shut damper (no heat), or with no
    /// draught.</summary>
    public double DamperShutBurn { get; set; } = 0.25;

    /// <summary>Ingots of desilverized lead a ladling takes out.</summary>
    public int LadleIngots { get; set; } = 6;

    public static readonly ParkesKettleConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        var d = Defaults;
        void Share(string name, double value, Action<double> set, double fallback, bool positive = false)
        {
            if (value is >= 0 and <= 1 && (!positive || value > 0))
                return;
            fixes.Add($"{name} {value} is out of range, using {fallback}");
            set(fallback);
        }
        Share(nameof(ZincPerLead), ZincPerLead, v => ZincPerLead = v, d.ZincPerLead, positive: true);
        Share(nameof(DamperHalfHeat), DamperHalfHeat, v => DamperHalfHeat = v, d.DamperHalfHeat);
        Share(nameof(DamperHalfBurn), DamperHalfBurn, v => DamperHalfBurn = v, d.DamperHalfBurn);
        Share(nameof(DamperShutBurn), DamperShutBurn, v => DamperShutBurn = v, d.DamperShutBurn);
        if (LadleIngots is < 1 or > 64)
        {
            fixes.Add($"LadleIngots {LadleIngots} is out of range, using {d.LadleIngots}");
            LadleIngots = d.LadleIngots;
        }
        return fixes;
    }
}

/// <summary>What a charge leaves: rich lead bars (the crust's, carrying its silver), desilverized lead ingots,
/// zinc bits back (the retort's work, as a rule), and what is carried over to the next charge short of a whole
/// item (silver, zinc and lead units: lead only when the whole route's restore made the charge's lead more than
/// whole bars).</summary>
public readonly record struct KettleYield(int RichLead, int Lead, int ZincBits, double SilverCarry, double ZincCarry, double LeadCarry = 0);

/// <summary>
/// What the Parkes kettle does (#927; README "Ore processing: the Parkes kettle"): a cast-iron kettle of silver-lead
/// over its own fire, with a powered stirrer. Silver-lead bars in by hand, then zinc (1 unit to 100 of lead); once
/// the zinc for the lead is in, the stirrer works the charge while the fire heats it, at the machine's 2,400 units in
/// 5 hours at the reference speed (in proportion to shaft speed and the damper's heat). Done, the crust is skimmed:
/// the charge's silver whole (the kettle is 100 %, ore-processing.json) as rich lead (<see cref="Bullion"/>), with
/// 80 % of the zinc back; the rest of the lead is ladled out desilverized. The kettle is plain galena's final step:
/// the share of its bars off the whole route is made whole, lead and silver (<see cref="Parted"/>). The stirrer's load follows how full the
/// kettle is (#927, one of the three loads that follow their job).
/// </summary>
public static class ParkesKettling
{
    /// <summary>Units of zinc in a zinc bit.</summary>
    public const double ZincUnitsPerBit = 5;

    public const string ZincBit = "game:metalbit-zinc";
    public const string Lead = "game:ingot-lead";

    /// <summary>What the grate burns: coal, coke or charcoal, by hand at the fire door.</summary>
    public static readonly IReadOnlyList<string> Fuels =
        ["game:ore-bituminouscoal", "game:ore-lignite", "game:ore-anthracite", "game:coke", "game:charcoal"];

    public static bool IsFuel(string code) => Fuels.Contains(code);

    /// <summary>Where a charge of <paramref name="leadUnits"/> with <paramref name="zincBits"/> stands.</summary>
    public static KettlePhase PhaseOf(double leadUnits, int zincBits, bool done, ParkesKettleConfig c) =>
        done ? KettlePhase.Done
        : leadUnits <= 0 ? KettlePhase.Empty
        : zincBits >= ZincBitsFor(leadUnits, c) ? KettlePhase.Stirring
        : KettlePhase.Charging;

    /// <summary>Zinc bits <paramref name="leadUnits"/> of lead take (rounded up to a whole bit).</summary>
    public static int ZincBitsFor(double leadUnits, ParkesKettleConfig c) =>
        leadUnits <= 0 ? 0 : (int)Math.Ceiling(leadUnits * c.ZincPerLead / ZincUnitsPerBit - 1e-9);

    /// <summary>How many of <paramref name="held"/> bars of <paramref name="unitsPerBar"/> go in, or why none.</summary>
    public static (int Take, KettleRefusal Why) AddLead(KettlePhase phase, double leadUnits, int held, double unitsPerBar, double chargeUnits)
    {
        if (phase == KettlePhase.Stirring) return (0, KettleRefusal.Stirring);
        if (phase == KettlePhase.Done) return (0, KettleRefusal.Done);
        int room = (int)Math.Floor((chargeUnits - leadUnits) / unitsPerBar + 1e-9);
        if (room <= 0) return (0, KettleRefusal.Full);
        return (Math.Min(room, held), KettleRefusal.None);
    }

    /// <summary>How many of <paramref name="held"/> zinc bits go in (only what the lead takes), or why none.</summary>
    public static (int Take, KettleRefusal Why) AddZinc(KettlePhase phase, double leadUnits, int zincBits, int held, ParkesKettleConfig c)
    {
        if (phase == KettlePhase.Done) return (0, KettleRefusal.Done);
        if (phase == KettlePhase.Empty) return (0, KettleRefusal.NoLead);
        int missing = ZincBitsFor(leadUnits, c) - zincBits;
        if (missing <= 0) return (0, KettleRefusal.ZincIn);
        return (Math.Min(missing, held), KettleRefusal.None);
    }

    /// <summary>How many of <paramref name="held"/> fuel go on a grate holding <paramref name="fuel"/> of
    /// <paramref name="grate"/>, or why none.</summary>
    public static (int Take, KettleRefusal Why) AddFuel(double fuel, int held, int grate)
    {
        int room = (int)Math.Floor(grate - fuel + 1e-9);
        return room <= 0 ? (0, KettleRefusal.GrateFull) : (Math.Min(room, held), KettleRefusal.None);
    }

    /// <summary>Why the fire cannot be lit, or none.</summary>
    public static KettleRefusal CannotLight(bool lit, double fuel) =>
        lit ? KettleRefusal.Lit : fuel < 1 - 1e-9 ? KettleRefusal.NoFuel : KettleRefusal.None;

    /// <summary>The share of its heat the fire gives the bath: none without a working row (no draught), else the
    /// damper's (open 1, half <see cref="ParkesKettleConfig.DamperHalfHeat"/>, shut none: banked).</summary>
    public static double Heat(bool lit, bool draught, Damper damper, ParkesKettleConfig c) =>
        !lit || !draught ? 0 : damper switch
        {
            Damper.Open => 1,
            Damper.Half => c.DamperHalfHeat,
            _ => 0,
        };

    /// <summary>The share of its open rate the fire burns at: the damper's, or banked with no draught.</summary>
    public static double BurnShare(bool draught, Damper damper, ParkesKettleConfig c) =>
        !draught ? c.DamperShutBurn : damper switch
        {
            Damper.Open => 1,
            Damper.Half => c.DamperHalfBurn,
            _ => c.DamperShutBurn,
        };

    /// <summary>The fuel left after <paramref name="hours"/> burning at <paramref name="perHour"/> × the burn share,
    /// and whether the fire went out.</summary>
    public static (double Fuel, bool Out) Burn(double fuel, double hours, double perHour, double share)
    {
        if (!(hours > 0)) return (fuel, fuel <= 1e-9);
        double left = Math.Max(0, fuel - hours * perHour * share);
        return (left, left <= 1e-9);
    }

    /// <summary>Shaft turns a charge of <paramref name="leadUnits"/> takes: the machine's turns a full charge, in
    /// proportion (its rate is 2,400 units in 5 hours at 0.6, whatever is in it).</summary>
    public static double TurnsFor(double leadUnits, double chargeUnits, double turnsPerCharge) =>
        chargeUnits > 0 ? turnsPerCharge * Math.Clamp(leadUnits / chargeUnits, 0, 1) : 0;

    /// <summary>How full the kettle is, 0 to 1: the stirrer's load follows it.</summary>
    public static double Fill(double bathUnits, double chargeUnits) =>
        chargeUnits > 0 ? Math.Clamp(bathUnits / chargeUnits, 0, 1) : 0;

    /// <summary>The bath's levels drawn (<c>lead1</c>.. up to this): ceil(fill × levels).</summary>
    public static int Levels(double bathUnits, double chargeUnits, int levels) =>
        (int)Math.Ceiling(Fill(bathUnits, chargeUnits) * levels - 1e-9);

    /// <summary>The grate's layers drawn: ceil(fuel / a layer's), at most <paramref name="layers"/>.</summary>
    public static int FuelLayers(double fuel, int perLayer, int layers) =>
        perLayer > 0 ? Math.Min(layers, (int)Math.Ceiling(fuel / perLayer - 1e-9)) : 0;

    /// <summary>
    /// What a charge of <paramref name="leadUnits"/> of <paramref name="ore"/>'s silver-lead parts into, lead and silver
    /// (<see cref="OreRecovery.Smelted(OreSpec, double, PartingStation?, double)"/> at the kettle):
    /// <paramref name="wholeUnits"/> of it came off the whole route (bars carrying <see cref="Bullion.WholeShareAttribute"/>),
    /// made whole when the kettle is the ore's final step (plain galena); the rest is the lead as it is and its silver at
    /// the kettle's 100 %.
    /// </summary>
    public static (double Lead, double Silver) Parted(OreRecovery r, OreSpec ore, double leadUnits, double wholeUnits)
    {
        if (!(leadUnits > 0)) return (0, 0);
        var metals = r.Smelted(ore, leadUnits, PartingStation.ParkesKettle, wholeShare: Math.Clamp(wholeUnits, 0, leadUnits) / leadUnits);
        return (metals.Where(m => m.Metal == ore.Metal).Sum(m => m.Units), metals.Where(m => m.Metal == "silver").Sum(m => m.Units));
    }

    /// <summary>
    /// A charge done: <paramref name="silverUnits"/> of silver (the charge's whole silver, at the kettle's figure)
    /// with what the last charges carried go into rich lead at <paramref name="richLeadSilver"/> silver a unit of
    /// lead, whole bars (<see cref="Bullion.RichLeadBars"/>), the rest of the silver carried on; each rich bar takes
    /// its 100 units of the lead, the rest is ladled out as desilverized ingots; the zinc put in comes back at
    /// <paramref name="zincReturn"/> as whole bits, the rest carried on. Lead short of a whole ingot (a charge made
    /// whole off the whole route holds more than its bars) is carried on too, with <paramref name="leadCarry"/> the
    /// last charges'.
    /// </summary>
    public static KettleYield Finish(double leadUnits, double silverUnits, double silverCarry, double richLeadSilver,
        int zincBits, double zincCarry, double zincReturn, double leadCarry = 0)
    {
        double silver = Math.Max(0, silverUnits) + Math.Max(0, silverCarry);
        double allLead = Math.Max(0, leadUnits) + Math.Max(0, leadCarry);
        int maxRich = (int)Math.Floor(allLead / Bullion.UnitsPerItem + 1e-9);
        int rich = richLeadSilver > 0 ? Math.Min(maxRich, (int)Math.Floor(Bullion.RichLeadBars(silver, richLeadSilver) + 1e-9)) : 0;
        double silverLeft = Math.Max(0, silver - rich * Bullion.UnitsPerItem * richLeadSilver);
        double leadLeft = Math.Max(0, allLead - rich * Bullion.UnitsPerItem);
        int lead = (int)Math.Floor(leadLeft / Bullion.UnitsPerItem + 1e-9);
        double leadRest = leadLeft - lead * Bullion.UnitsPerItem;
        double zinc = zincBits * ZincUnitsPerBit * Math.Clamp(zincReturn, 0, 1) + Math.Max(0, zincCarry);
        int bits = (int)Math.Floor(zinc / ZincUnitsPerBit + 1e-9);
        return new KettleYield(rich, Math.Max(0, lead), bits, silverLeft, Math.Max(0, zinc - bits * ZincUnitsPerBit),
            leadRest > 1e-9 ? leadRest : 0);
    }
}
