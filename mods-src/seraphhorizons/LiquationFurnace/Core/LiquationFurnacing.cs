using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.LiquationFurnace.Core;

/// <summary>Where a liquation furnace's plate stands.</summary>
public enum LiquationPhase
{
    /// <summary>Nothing on the plate, nothing in the pot.</summary>
    Empty,
    /// <summary>Tin-lead bars on the plate, not yet sweated through.</summary>
    Charged,
    /// <summary>Sweated: the tin in the pot and the lead on the plate, to take out.</summary>
    Sweated,
}

/// <summary>Why a liquation furnace refuses a click (each a lang entry <c>seraphhorizons:liquationfurnace-error-{name}</c>).</summary>
public enum LiquationFurnaceRefusal
{
    None,
    /// <summary>Not tin-lead bullion the furnace parts.</summary>
    NotTinLead,
    /// <summary>The plate holds its charge.</summary>
    Full,
    /// <summary>The last charge's tin and lead are still to be taken out.</summary>
    Sweated,
    /// <summary>Fuel goes in through the open fire door.</summary>
    DoorShut,
    /// <summary>The grate holds all the fuel it takes.</summary>
    GrateFull,
    /// <summary>The grate holds another fuel.</summary>
    OtherFuel,
    /// <summary>Nothing on the grate to light.</summary>
    NoFuel,
    /// <summary>It is burning already.</summary>
    Lit,
}

/// <summary>
/// The liquation furnace's own settings (#929; <c>LiquationFurnaceSettings</c> in ModConfig/seraphhorizons.json). Its
/// recovery (95 %) and its pace (100 units of tin an hour) are ore processing's (<c>config/ore-processing.json</c>,
/// <c>machines.liquationfurnace</c>), as every ore machine's figures are; its row's chimney (6 blocks from the outlet
/// up, 5 furnaces to a chimney) is the rig's (<see cref="LiquationFurnaceRig"/>).
/// </summary>
public class LiquationFurnaceConfig
{
    /// <summary>Tin-lead bars the plate takes a charge (the model draws four cakes).</summary>
    public int ChargeBars { get; set; } = 4;

    /// <summary>Fuel (coal, coke or charcoal) the grate holds.</summary>
    public int GrateFuel { get; set; } = 8;

    /// <summary>Fuel the fire burns an in-game hour with the damper open (the owner's ruling on #929: 0.15 t of coal a
    /// ton of tin-lead at the furnace's 6 t/d is 0.36 an hour).</summary>
    public double FuelPerHour { get; set; } = 0.36;

    /// <summary>The damper half shut: the share of the open pace the charge sweats at.</summary>
    public double DamperHalfPace { get; set; } = 0.5;

    /// <summary>The damper half shut: the share of the open rate the fuel burns at.</summary>
    public double DamperHalfBurn { get; set; } = 0.6;

    /// <summary>The damper shut, the fire banked: the share of the open rate the fuel burns at (nothing sweats).</summary>
    public double DamperShutBurn { get; set; } = 0.25;

    /// <summary>The fire door left open: the share of its pace the charge sweats at, the cold air drawn over the fire.</summary>
    public double DoorOpenPace { get; set; } = 0.5;

    public static readonly LiquationFurnaceConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        var d = Defaults;
        void Fix<T>(string name, T value, bool bad, Action reset, T def)
        {
            if (!bad) return;
            fixes.Add($"{name} {value} is out of range, using {def}");
            reset();
        }
        static bool Share(double v) => v is >= 0 and <= 1;
        Fix(nameof(ChargeBars), ChargeBars, ChargeBars is < 1 or > 64, () => ChargeBars = d.ChargeBars, d.ChargeBars);
        Fix(nameof(GrateFuel), GrateFuel, GrateFuel is < 1 or > 64, () => GrateFuel = d.GrateFuel, d.GrateFuel);
        Fix(nameof(FuelPerHour), FuelPerHour, !(FuelPerHour is > 0 and <= 64), () => FuelPerHour = d.FuelPerHour, d.FuelPerHour);
        Fix(nameof(DamperHalfPace), DamperHalfPace, !Share(DamperHalfPace), () => DamperHalfPace = d.DamperHalfPace, d.DamperHalfPace);
        Fix(nameof(DamperHalfBurn), DamperHalfBurn, !Share(DamperHalfBurn), () => DamperHalfBurn = d.DamperHalfBurn, d.DamperHalfBurn);
        Fix(nameof(DamperShutBurn), DamperShutBurn, !Share(DamperShutBurn), () => DamperShutBurn = d.DamperShutBurn, d.DamperShutBurn);
        Fix(nameof(DoorOpenPace), DoorOpenPace, !Share(DoorOpenPace), () => DoorOpenPace = d.DoorOpenPace, d.DoorOpenPace);
        return fixes;
    }
}

/// <summary>One kind of bar on the plate: its ore (<c>teallite</c>), how many, and the share of their units off the
/// whole route (<see cref="Bullion.WholeShareAttribute"/>), which the furnace, the ore's final step, makes whole.</summary>
public readonly record struct LiquationBars(string Ore, int Count, double Whole = 0);

/// <summary>What a sweated charge gives, in metal units: the tin run into the pot and the lead left on the plate.</summary>
public readonly record struct LiquationFurnaceYield(string Metal, double MetalUnits, string ByProduct, double ByProductUnits);

/// <summary>
/// What the liquation furnace does (#929; README "Ore processing: the liquation furnace"): a sloping cast-iron hearth
/// plate over a fire, batch, loaded and emptied by hand. Tin-lead bullion bars (smelted from teallite or franckeite,
/// <see cref="Bullion"/>) are laid on the plate; with the fire lit and the row's chimney drawing, the charge sweats at
/// ore processing's pace (100 units of tin an hour) slowed by the damper and an open fire door
/// (<see cref="Pace"/>). Done, all its tin has run into the pot and its lead is left on the plate at the ore's share ×
/// the furnace's 95 % (<see cref="OreRecovery.Smelted"/> with <see cref="PartingStation.LiquationFurnace"/>). The fire
/// burns its fuel whether there is a charge or not (<see cref="Burn"/>), and goes out when the grate is empty.
/// </summary>
public static class LiquationFurnacing
{
    public const PartingStation Station = PartingStation.LiquationFurnace;

    /// <summary>The fuels the grate takes: the game's coals, coke and charcoal.</summary>
    public static readonly IReadOnlyList<string> Fuels =
        ["game:ore-lignite", "game:ore-bituminouscoal", "game:ore-anthracite", "game:coke", "game:charcoal"];

    /// <summary>Units of metal in one of the game's metal bits, and in an ingot.</summary>
    public const double UnitsPerBit = 5, UnitsPerIngot = 100;

    public static bool IsFuel(string? code) => code != null && Fuels.Contains(code);

    /// <summary>The ore of a bar the furnace takes (tin-lead from teallite or franckeite), or null.</summary>
    public static string? OreOf(OreRecovery r, string? code)
    {
        if (code == null || Bullion.OreOf(code) is not { } ore) return null;
        try
        {
            return Bullion.TakenBy(r, r.Ore(ore), Station) ? ore : null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The phase of a furnace holding this much: <paramref name="bars"/> on the plate, whole metal in the pot
    /// or on the plate (<paramref name="productWaiting"/>).</summary>
    public static LiquationPhase PhaseOf(int bars, bool productWaiting) =>
        productWaiting ? LiquationPhase.Sweated : bars > 0 ? LiquationPhase.Charged : LiquationPhase.Empty;

    /// <summary>Charging: how many bars a click with <paramref name="held"/> puts on the plate, or why none.</summary>
    public static (int Take, LiquationFurnaceRefusal Refusal) Charge(LiquationPhase phase, int bars, int held, int capacity)
    {
        if (phase == LiquationPhase.Sweated) return (0, LiquationFurnaceRefusal.Sweated);
        int room = capacity - bars;
        if (room <= 0) return (0, LiquationFurnaceRefusal.Full);
        return (Math.Min(room, Math.Max(0, held)), LiquationFurnaceRefusal.None);
    }

    /// <summary>Stoking: how much fuel a click with <paramref name="held"/> of <paramref name="code"/> puts on the grate,
    /// or why none.</summary>
    public static (int Take, LiquationFurnaceRefusal Refusal) Stoke(bool doorShut, string? onGrate, int fuel, string code, int held, int grate)
    {
        if (doorShut) return (0, LiquationFurnaceRefusal.DoorShut);
        if (fuel > 0 && onGrate != null && onGrate != code) return (0, LiquationFurnaceRefusal.OtherFuel);
        int room = grate - fuel;
        if (room <= 0) return (0, LiquationFurnaceRefusal.GrateFull);
        return (Math.Min(room, Math.Max(0, held)), LiquationFurnaceRefusal.None);
    }

    /// <summary>Why the furnace cannot be lit now, or <see cref="LiquationFurnaceRefusal.None"/>.</summary>
    public static LiquationFurnaceRefusal CannotLight(bool lit, int fuel) =>
        lit ? LiquationFurnaceRefusal.Lit : fuel <= 0 ? LiquationFurnaceRefusal.NoFuel : LiquationFurnaceRefusal.None;

    /// <summary>The share of its full pace the charge sweats at: none unlit or without a working chimney on its row,
    /// else by the damper (open 1, half, shut 0) and the fire door (shut 1, open).</summary>
    public static double Pace(bool lit, bool rowDraws, Damper damper, bool doorShut, LiquationFurnaceConfig c) =>
        !lit || !rowDraws ? 0
        : damper switch
        {
            Damper.Open => 1,
            Damper.Half => c.DamperHalfPace,
            _ => 0,
        } * (doorShut ? 1 : c.DoorOpenPace);

    /// <summary>The share of its open rate the fire burns its fuel at, by the damper.</summary>
    public static double BurnShare(Damper damper, LiquationFurnaceConfig c) => damper switch
    {
        Damper.Open => 1,
        Damper.Half => c.DamperHalfBurn,
        _ => c.DamperShutBurn,
    };

    /// <summary>
    /// The fire after <paramref name="hours"/> more: the fuel it burnt through (<paramref name="burnt"/>, a piece's
    /// fraction carried) at <see cref="LiquationFurnaceConfig.FuelPerHour"/> × <see cref="BurnShare"/>, as whole pieces
    /// taken off the grate; the hours it burnt (fewer when the fuel ran out), and whether it is still lit.
    /// </summary>
    public static (double Burnt, int Pieces, double Hours, bool Lit) Burn(double burnt, int fuel, double hours, Damper damper, LiquationFurnaceConfig c)
    {
        double rate = c.FuelPerHour * BurnShare(damper, c);
        hours = Math.Max(0, hours);
        if (rate <= 0) return (burnt, 0, hours, fuel > 0);
        // the fuel left, counting the piece burning as partly gone
        double left = fuel - burnt;
        if (left <= 0) return (0, Math.Max(0, fuel), 0, false);
        double want = hours * rate;
        if (want >= left - 1e-9)
            return (0, fuel, left / rate, false);
        double total = burnt + want;
        int pieces = (int)Math.Floor(total + 1e-9);
        return (Math.Max(0, total - pieces), pieces, hours, true);
    }

    /// <summary>The sweating after <paramref name="hours"/> more at <paramref name="pace"/>: the units sweated
    /// (<paramref name="perHour"/> at full pace) and whether the charge of <paramref name="chargeUnits"/> is done.</summary>
    public static (double Sweated, bool Done) Advance(double sweated, double hours, double pace, double perHour, double chargeUnits)
    {
        double next = sweated + Math.Max(0, hours) * Math.Clamp(pace, 0, 1) * Math.Max(0, perHour);
        return chargeUnits > 0 && next >= chargeUnits - 1e-9 ? (chargeUnits, true) : (Math.Max(0, next), chargeUnits <= 0);
    }

    /// <summary>The units of tin a charge of bars holds.</summary>
    public static double ChargeUnits(IEnumerable<LiquationBars> bars) => bars.Sum(b => Math.Max(0, b.Count)) * Bullion.UnitsPerItem;

    /// <summary>What a charge gives: every bar's tin whole, and its lead at the ore's share × the furnace's recovery; the
    /// share of a bar off the whole route made whole, tin and lead (<see cref="LiquationBars.Whole"/>).</summary>
    public static LiquationFurnaceYield Yield(OreRecovery r, IEnumerable<LiquationBars> bars)
    {
        string metal = "", byProduct = "";
        double main = 0, by = 0;
        foreach (var b in bars)
        {
            if (b.Count <= 0) continue;
            var spec = r.Ore(b.Ore);
            foreach (var m in r.Smelted(spec, b.Count * Bullion.UnitsPerItem, Station, wholeShare: b.Whole))
                if (m.Metal == spec.Metal)
                {
                    metal = m.Metal;
                    main += m.Units;
                }
                else
                {
                    byProduct = m.Metal;
                    by += m.Units;
                }
        }
        return new LiquationFurnaceYield(metal, main, byProduct, by);
    }

    /// <summary>Metal units as whole ingots and bits, the rest (under a bit) carried in the furnace.</summary>
    public static (int Ingots, int Bits, double Carried) Whole(double units)
    {
        units = Math.Max(0, units);
        int ingots = (int)Math.Floor(units / UnitsPerIngot + 1e-9);
        double rest = Math.Max(0, units - ingots * UnitsPerIngot);
        int bits = (int)Math.Floor(rest / UnitsPerBit + 1e-9);
        return (ingots, bits, Math.Max(0, rest - bits * UnitsPerBit));
    }

    /// <summary>Whether metal enough for a bit is waiting.</summary>
    public static bool Waiting(double units) => units >= UnitsPerBit - 1e-9;

    /// <summary>Fuel layers drawn on the grate (<c>fuel1..</c>, <c>fire1..</c>) for <paramref name="fuel"/> of
    /// <paramref name="grate"/>: ceil(fuel / per layer), at most <paramref name="layers"/>.</summary>
    public static int FuelLayers(int fuel, int grate, int layers)
    {
        if (fuel <= 0 || grate <= 0 || layers <= 0) return 0;
        int per = Math.Max(1, (grate + layers - 1) / layers);
        return Math.Min(layers, (fuel + per - 1) / per);
    }

    /// <summary>The damper's next place, as a click cycles it.</summary>
    public static Damper NextDamper(Damper d) => d switch
    {
        Damper.Open => Damper.Half,
        Damper.Half => Damper.Shut,
        _ => Damper.Open,
    };
}
