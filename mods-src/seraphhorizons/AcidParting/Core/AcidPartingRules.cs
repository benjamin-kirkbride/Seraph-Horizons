using SeraphHorizons.Mod.CrucibleFurnace.Core;

namespace SeraphHorizons.Mod.AcidParting.Core;

/// <summary>Where a vessel's charge stands.</summary>
public enum VesselPhase
{
    /// <summary>No dore in it, no gold to take out (weak acid may stand in it).</summary>
    Empty,
    /// <summary>Dore in, not parting yet: it wants acid enough for the charge, wash water, copper in the tub,
    /// the fire and the row's draught.</summary>
    Charged,
    /// <summary>Parting: the acid taking the silver up, drawn off into the tub, the gold left.</summary>
    Parting,
    /// <summary>The whole charge parted: the gold, washed, to take out.</summary>
    Parted,
}

/// <summary>Why a vessel refuses a click (each a lang entry <c>seraphhorizons:acidparting-error-{name}</c>).</summary>
public enum VesselRefusal
{
    None,
    /// <summary>A charge is parting: nothing goes in or comes out of the vessel until it is done.</summary>
    Parting,
    /// <summary>The last charge's gold is still in the vessel.</summary>
    GoldWaiting,
    /// <summary>The vessel holds a full charge of dore.</summary>
    Full,
    /// <summary>The vessel holds all the acid it takes.</summary>
    AcidFull,
    /// <summary>The wash water is full.</summary>
    WaterFull,
    /// <summary>The fire holds all the fuel it takes.</summary>
    FuelFull,
    /// <summary>Nothing to light: no fuel laid.</summary>
    NoFuel,
    /// <summary>It is burning.</summary>
    Burning,
    /// <summary>The gold is taken out once the charge is parted.</summary>
    NotParted,
    /// <summary>Nothing in the tub to draw.</summary>
    NoSilver,
    /// <summary>The held liquid is not the vessel's: not nitric acid or fresh water.</summary>
    WrongLiquid,
}

/// <summary>
/// The acid parting vessel's own settings (#931; <c>AcidPartingSettings</c> in ModConfig/seraphhorizons.json). Its
/// recovery (95 %), its rate (10 units an hour) and its wash water (1 L a 10 units) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.acidparting</c>), as every ore machine's figures are; its row's
/// chimney (6 blocks, 6 vessels to a chimney) is the rig's (<c>parting</c>). Every figure here is a starting value.
/// </summary>
public class AcidPartingConfig
{
    /// <summary>Bars of dore (100 units of gold each) a charge holds.</summary>
    public int ChargeBars { get; set; } = 2;

    /// <summary>Litres of nitric acid (aqua fortis) a bar of dore needs in the vessel to part: about 2.5 times what
    /// its silver takes up, as the period boiled it.</summary>
    public double AcidLitresPerBar { get; set; } = 2;

    /// <summary>The share of a charge's acid spent with it (the owner's ruling, 2026-10-14: 40 %); the rest stays
    /// in the vessel as weak acid for the next charge.</summary>
    public double AcidSpentShare { get; set; } = 0.4;

    /// <summary>Firepit burn seconds of fuel (charcoal 40, firewood 24) the sand bath's small fire burns an in-game
    /// hour at full draught: charcoal lasts 4 hours, firewood 2.4.</summary>
    public double FuelBurnSecondsPerHour { get; set; } = 10;

    /// <summary>Fuel items the fire door takes at once.</summary>
    public int FuelCapacity { get; set; } = 8;

    /// <summary>The lowest burn temperature (°C) of a fuel the fire takes.</summary>
    public int MinFuelTemperature { get; set; } = 500;

    /// <summary>The share of its draught a half-open damper gives (the parting's and the fire's pace); open is the
    /// full draught, shut banks the fire (nothing parts, no fuel burns).</summary>
    public double HalfDamperDraught { get; set; } = 0.5;

    public static readonly AcidPartingConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        var d = Defaults;
        void Fix(string name, object bad, object good) => fixes.Add($"{name} {bad} is out of range, using {good}");
        if (ChargeBars is < 1 or > 16) { Fix(nameof(ChargeBars), ChargeBars, d.ChargeBars); ChargeBars = d.ChargeBars; }
        if (!(AcidLitresPerBar is > 0 and <= 100)) { Fix(nameof(AcidLitresPerBar), AcidLitresPerBar, d.AcidLitresPerBar); AcidLitresPerBar = d.AcidLitresPerBar; }
        if (!(AcidSpentShare is >= 0 and <= 1)) { Fix(nameof(AcidSpentShare), AcidSpentShare, d.AcidSpentShare); AcidSpentShare = d.AcidSpentShare; }
        if (!(FuelBurnSecondsPerHour is > 0 and <= 1000)) { Fix(nameof(FuelBurnSecondsPerHour), FuelBurnSecondsPerHour, d.FuelBurnSecondsPerHour); FuelBurnSecondsPerHour = d.FuelBurnSecondsPerHour; }
        if (FuelCapacity is < 1 or > 64) { Fix(nameof(FuelCapacity), FuelCapacity, d.FuelCapacity); FuelCapacity = d.FuelCapacity; }
        if (MinFuelTemperature is < 0 or > 3000) { Fix(nameof(MinFuelTemperature), MinFuelTemperature, d.MinFuelTemperature); MinFuelTemperature = d.MinFuelTemperature; }
        if (!(HalfDamperDraught is >= 0 and <= 1)) { Fix(nameof(HalfDamperDraught), HalfDamperDraught, d.HalfDamperDraught); HalfDamperDraught = d.HalfDamperDraught; }
        return fixes;
    }
}

/// <summary>What a stretch of parting did (<see cref="AcidPartingRules.Part"/>).</summary>
/// <param name="Units">Dore units parted (gold units; the silver they carry is the ore's share of them).</param>
/// <param name="Water">Wash water spent, litres.</param>
/// <param name="Acid">Acid spent, litres.</param>
/// <param name="Silver">Silver units thrown down in the tub, which wear the copper.</param>
/// <param name="Gold">Gold units left in the vessel.</param>
public readonly record struct PartingStep(double Units, double Water, double Acid, double Silver, double Gold);

/// <summary>
/// What the acid parting vessel does (#931; README "Ore processing: the acid parting vessel"): nitric acid parting,
/// batch, loaded and emptied by hand. Dore (bullion of gold quartz: a bar holds 100 units of gold carrying the ore's
/// 15 % silver, #740's bullion) goes into the vessel with nitric acid enough for the charge; with wash water poured
/// in from a bucket, copper plates in the tub, the sand bath's fire burning and the row's chimney drawing, it parts
/// at ore processing's rate (10 units an hour at full draught): the silver goes into the acid, is siphoned off into
/// the tub and thrown down on the copper as sludge (silver bits, drawn by hand at any time); the gold stays in the
/// vessel and is taken out washed once the whole charge is parted. Gold comes out whole, the silver at the vessel's
/// 95 % (<see cref="Ore.Core.OreRecovery.Smelted"/> with <see cref="Ore.Core.PartingStation.AcidParting"/>). A
/// charge spends 40 % of its acid (the rest stays as weak acid for the next), 1 L of wash water a 10 units, and the
/// copper at 0.29 of the silver's weight.
/// </summary>
public static class AcidPartingRules
{
    /// <summary>The game's nitric acid, Expanded Matter's aqua fortis (a liquid, 100 portions a litre).</summary>
    public const string Acid = "game:acid-full-nitric";

    /// <summary>The copper plate the tub's copper is (a wearing part, renewed by right-click).</summary>
    public const string CopperPlate = "game:metalplate-copper";

    /// <summary>Units of metal in a gold or silver bit, the vessel's and the tub's outputs.</summary>
    public const double UnitsPerBit = 5;

    /// <summary>The phase of a vessel: <paramref name="started"/> once a charge of <paramref name="chargeUnits"/> began
    /// parting (its bars dissolved into it), <paramref name="parted"/> of them done; before that, <paramref name="bars"/>
    /// of dore put in.</summary>
    public static VesselPhase PhaseOf(int bars, double chargeUnits, double parted, bool started) =>
        started ? parted + 1e-9 < chargeUnits ? VesselPhase.Parting : VesselPhase.Parted
        : bars > 0 ? VesselPhase.Charged
        : VesselPhase.Empty;

    /// <summary>Whether a charged vessel starts parting now: acid enough in it for its bars, and the rest of what it
    /// needs (water, copper, the fire and the draught) the caller's.</summary>
    public static bool AcidReady(int bars, double acid, AcidPartingConfig c) => bars > 0 && acid + 1e-9 >= AcidFor(bars, c);

    /// <summary>Litres of acid a charge of <paramref name="bars"/> needs in the vessel to start.</summary>
    public static double AcidFor(double bars, AcidPartingConfig c) => Math.Max(0, bars) * c.AcidLitresPerBar;

    /// <summary>Litres of acid the vessel holds at most: a full charge's.</summary>
    public static double AcidCapacity(AcidPartingConfig c) => AcidFor(c.ChargeBars, c);

    /// <summary>Charging: how many bars a click with <paramref name="held"/> puts in, or why none.</summary>
    public static (int Take, VesselRefusal Refusal) Charge(VesselPhase phase, int bars, int held, AcidPartingConfig c)
    {
        if (phase == VesselPhase.Parting) return (0, VesselRefusal.Parting);
        if (phase == VesselPhase.Parted) return (0, VesselRefusal.GoldWaiting);
        int room = c.ChargeBars - bars;
        if (room <= 0) return (0, VesselRefusal.Full);
        return (Math.Min(room, Math.Max(0, held)), VesselRefusal.None);
    }

    /// <summary>Pouring acid: the litres taken from <paramref name="held"/>, or why none. Acid goes in before or between
    /// charges, never into one parting (it was boiled with what it had).</summary>
    public static (double Take, VesselRefusal Refusal) PourAcid(VesselPhase phase, double acid, double held, AcidPartingConfig c)
    {
        if (phase == VesselPhase.Parting) return (0, VesselRefusal.Parting);
        double room = AcidCapacity(c) - acid;
        if (room <= 1e-9) return (0, VesselRefusal.AcidFull);
        return (Math.Min(room, Math.Max(0, held)), VesselRefusal.None);
    }

    /// <summary>The share of the full draught the vessel's fire burns and parts at: none without a working chimney
    /// on its row or with the damper shut (banked), half with it half open.</summary>
    public static double Draught(Damper damper, bool rowDraws, AcidPartingConfig c) =>
        !rowDraws ? 0 : damper switch
        {
            Damper.Open => 1,
            Damper.Half => c.HalfDamperDraught,
            _ => 0,
        };

    /// <summary>The next place of the damper, a click on the ash mouth: open, half, shut, open.</summary>
    public static Damper Next(Damper d) => d switch
    {
        Damper.Open => Damper.Half,
        Damper.Half => Damper.Shut,
        _ => Damper.Open,
    };

    /// <summary>Whether a fuel of this burn temperature and duration (the game's combustible properties) burns in the
    /// sand bath's fire.</summary>
    public static bool IsFuel(int burnTemperature, float burnDuration, AcidPartingConfig c) =>
        burnDuration > 0 && burnTemperature >= c.MinFuelTemperature;

    /// <summary>The in-game hours one fuel item of <paramref name="burnDuration"/> firepit seconds keeps the fire at
    /// full draught.</summary>
    public static double FuelHours(float burnDuration, AcidPartingConfig c) => Math.Max(0, burnDuration) / c.FuelBurnSecondsPerHour;

    /// <summary>
    /// Parting for <paramref name="hours"/> at <paramref name="draught"/>: at most the rate's units, no more than the
    /// charge has left, the wash water allows (<paramref name="waterPerUnit"/>) or the tub's copper can throw down
    /// (<paramref name="copperSilver"/>, silver units left on the plates). The silver a unit carries is
    /// <paramref name="silverPerUnit"/> (the ore's share × the vessel's recovery); the acid a unit spends is the
    /// charge's need × the spent share over its units.
    /// </summary>
    public static PartingStep Part(double hours, double draught, double perHour, double left, double water, double waterPerUnit,
        double copperSilver, double silverPerUnit, double acidPerUnit)
    {
        double units = Math.Max(0, hours) * Math.Clamp(draught, 0, 1) * Math.Max(0, perHour);
        units = Math.Min(units, Math.Max(0, left));
        if (waterPerUnit > 0)
            units = Math.Min(units, Math.Max(0, water) / waterPerUnit);
        if (silverPerUnit > 0)
            units = Math.Min(units, Math.Max(0, copperSilver) / silverPerUnit);
        if (units <= 1e-12)
            return default;
        return new PartingStep(units, units * Math.Max(0, waterPerUnit), units * Math.Max(0, acidPerUnit), units * Math.Max(0, silverPerUnit), units);
    }

    /// <summary>The acid a unit of a charge spends: a bar's acid × the spent share over its <paramref name="unitsPerBar"/>.</summary>
    public static double AcidPerUnit(AcidPartingConfig c, double unitsPerBar) =>
        unitsPerBar > 0 ? c.AcidLitresPerBar * c.AcidSpentShare / unitsPerBar : 0;
}
