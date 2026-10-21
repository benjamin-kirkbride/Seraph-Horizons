using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>The machines that need oil (<c>MachineOil</c>): the heavy mechanical power consumers.
/// The quern, the transmission parts and every other consumer are exempt.</summary>
public enum OilMachine
{
    /// <summary>The game's helve hammer (its load is on the wooden toggle that drives it).</summary>
    HelveHammer,
    /// <summary>The game's pulverizer.</summary>
    Pulverizer,
    /// <summary>Immersive Woodworking's plank sawmill.</summary>
    Sawmill,
    /// <summary>Immersive Woodworking's powered chopper.</summary>
    Chopper,
    /// <summary>This mod's bucking sawmill.</summary>
    BuckingMill,
    /// <summary>This mod's rosser.</summary>
    Rosser,
    /// <summary>This mod's gear cutter: its oil wears the cutter kit, never its shaft load.</summary>
    GearCutter,
    /// <summary>This mod's draw bench.</summary>
    DrawBench,
    /// <summary>This mod's cylinder borer: its oil wears the cutter head, never its shaft load, as the gear cutter's.</summary>
    CylinderBorer,
    /// <summary>This mod's jaw breaker.</summary>
    JawBreaker,
    /// <summary>This mod's stamp battery.</summary>
    StampBattery,
    /// <summary>This mod's arrastra.</summary>
    Arrastra,
    /// <summary>This mod's ball mill.</summary>
    BallMill,
    /// <summary>This mod's jig.</summary>
    Jig,
    /// <summary>This mod's shaking table.</summary>
    ShakingTable,
    /// <summary>This mod's vanner.</summary>
    Vanner,
    /// <summary>This mod's reverberatory roaster: its rabble's shaft.</summary>
    ReverberatoryRoaster,
    /// <summary>This mod's Parkes kettle: its stirrer's shaft.</summary>
    ParkesKettle,
    /// <summary>This mod's slitting mill.</summary>
    SlittingMill,
}

/// <summary>What a dry tank does to a machine (<c>BEMachine.DryEffect</c>): most turn harder, their load on
/// the shaft multiplied (<see cref="MachineOilConfig.DryResistanceMultiplier"/>); the gear cutter and the
/// cylinder borer wear their cutter faster instead, their load left alone.</summary>
public enum OilDryEffect
{
    /// <summary>The load on the shaft is multiplied while dry.</summary>
    Load,
    /// <summary>The load is left alone; the machine wears its cutter by its tank's fill instead.</summary>
    Wear,
}

/// <summary>
/// A machine's oil tank, in points: <see cref="PointsPerLitre"/> to the litre, so one item of a
/// 100-per-litre oil (the game's and Expanded Foods' oils) is one point, and a 10 litre bucket is
/// 1000. A machine starts with an empty tank, which is <see cref="Dry"/>; any oil at all is not.
/// The tank only fills and drains: what is in it cannot be taken back out, and it is lost with the
/// machine.
/// </summary>
public readonly record struct OilTank(double Points, double Capacity)
{
    public const double PointsPerLitre = 100;

    public static OilTank Empty(double capacity) => new(0, Math.Max(0, capacity));

    /// <summary>No oil left: the machine's load on its shaft is multiplied.</summary>
    public bool Dry => Points <= 0;

    /// <summary>Points that still fit.</summary>
    public double Room => Math.Max(0, Capacity - Points);

    /// <summary>Adds <paramref name="points"/>, up to the capacity.</summary>
    public OilTank Fill(double points) => this with { Points = Math.Min(Capacity, Points + Math.Max(0, points)) };

    /// <summary>Takes <paramref name="points"/> out, down to empty.</summary>
    public OilTank Drain(double points) => this with { Points = Math.Max(0, Points - Math.Max(0, points)) };

    /// <summary>The same oil in a tank of <paramref name="capacity"/> (a changed setting); what no
    /// longer fits is gone.</summary>
    public OilTank WithCapacity(double capacity) => new(Math.Clamp(Points, 0, Math.Max(0, capacity)), Math.Max(0, capacity));

    /// <summary>A shaft load as this tank leaves it: <paramref name="resistance"/> times
    /// <paramref name="dryMultiplier"/> while dry, as it is otherwise.</summary>
    public float Resistance(float resistance, float dryMultiplier) => Dry ? resistance * dryMultiplier : resistance;

    /// <summary>How many whole items of an oil worth <paramref name="pointsPerItem"/> each go in
    /// when <paramref name="available"/> are offered: as many as fit, never part of one.</summary>
    public int ItemsThatFit(double pointsPerItem, int available)
    {
        if (pointsPerItem <= 0 || available <= 0)
            return 0;
        // a hair of slack, so 1000 points of room takes 1000 items of 1 point despite rounding
        return (int)Math.Min(available, Math.Floor(Room / pointsPerItem + 1e-9));
    }

    /// <summary>Points one item of a liquid is worth, from its items per litre (its
    /// <c>waterTightContainerProps.itemsPerLitre</c>).</summary>
    public static double PointsPerItem(float itemsPerLitre) => itemsPerLitre > 0 ? PointsPerLitre / itemsPerLitre : 0;
}

/// <summary>What a finished job costs a machine in oil. Idle turning costs nothing.</summary>
public static class OilDrain
{
    /// <summary>The fill of <paramref name="tank"/>, 0..1 (0 for a tank of no capacity).</summary>
    public static double Fill(OilTank tank) => tank.Capacity > 0 ? Math.Clamp(tank.Points / tank.Capacity, 0, 1) : 0;

    /// <summary>A whole trunk's cost on the bucking mill or the rosser: <paramref name="perLog"/>
    /// per log stored in it, rounded up over the trunk (as the blade's and the heads' wear are).</summary>
    public static double PerTrunk(int storedLogs, double perLog) =>
        storedLogs <= 0 || perLog <= 0 ? 0 : Math.Ceiling(storedLogs * perLog - 1e-9);

    /// <summary>Jobs a full tank of <paramref name="capacity"/> lasts at <paramref name="perJob"/>
    /// each (for the README's arithmetic and the tests).</summary>
    public static double JobsPerTank(double capacity, double perJob) => perJob <= 0 ? double.PositiveInfinity : capacity / perJob;
}

/// <summary>Oil codes: <c>domain:path</c> patterns with <c>*</c> wildcards, as the settings list
/// them. A pattern without a domain is in <c>game</c>; case does not matter; blank ones are
/// ignored.</summary>
public sealed class OilCodes
{
    private readonly string[] _patterns;

    public OilCodes(IEnumerable<string?>? patterns)
    {
        _patterns = (patterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Normalize(p!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> Patterns => _patterns;

    public bool Matches(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        string c = Normalize(code);
        foreach (string pattern in _patterns)
            if (Wildcard.IsMatch(pattern, c))
                return true;
        return false;
    }

    public static string Normalize(string code)
    {
        code = code.Trim().ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }
}

/// <summary>One machine's tank size and what one job costs it (points).</summary>
public class OilMachineConfig
{
    public OilMachineConfig()
    {
    }

    public OilMachineConfig(float tank, float drainPerJob)
    {
        Tank = tank;
        DrainPerJob = drainPerJob;
    }

    /// <summary>Points the tank holds (100 to the litre).</summary>
    public float Tank { get; set; } = 1000;

    /// <summary>Points one job costs: a strike, an item, a log, or a log stored in a trunk.</summary>
    public float DrainPerJob { get; set; } = 1;
}

/// <summary>MachineOilSettings in ModConfig/seraphhorizons.json.</summary>
public class MachineOilConfig
{
    /// <summary>Liquids that oil a machine, poured from any liquid container: the game's oils
    /// (flax and olive, and melted rendered fat should a mod enable it), Expanded Foods' food oils
    /// (with Oils Resoaped's walnut) and its lard, liquid rendered fat, hardened or not.</summary>
    public string[] OilLiquids { get; set; } =
    [
        "game:oilportion-*",
        "expandedfoods:foodoilportion-*",
        "expandedfoods:lard",
        "expandedfoods:hardlardliquid",
    ];

    /// <summary>Solid oils, put in by hand: code pattern and the litres one item is worth. The
    /// game's tallow, rendered fat, is half a litre a lump (what its own melted form,
    /// <c>oilportion-fat</c>, holds: 2 to the litre).</summary>
    public Dictionary<string, float> OilLumps { get; set; } = new() { ["game:fat-rendered"] = 0.5f };

    /// <summary>What a dry machine's load on its shaft is multiplied by.</summary>
    public float DryResistanceMultiplier { get; set; } = 3f;

    /// <summary>Per strike of the hammer on an anvil with work on it.</summary>
    public OilMachineConfig HelveHammer { get; set; } = new(1000, 0.1f);

    /// <summary>Per item pulverized.</summary>
    public OilMachineConfig Pulverizer { get; set; } = new(1000, 0.5f);

    /// <summary>Per log sawn into planks.</summary>
    public OilMachineConfig Sawmill { get; set; } = new(1000, 2f);

    /// <summary>Per log chopped.</summary>
    public OilMachineConfig Chopper { get; set; } = new(1000, 1f);

    /// <summary>Per log stored in a cut trunk, rounded up over the trunk.</summary>
    public OilMachineConfig BuckingMill { get; set; } = new(1000, 2f);

    /// <summary>Per log stored in a debarked trunk, rounded up over the trunk.</summary>
    public OilMachineConfig Rosser { get; set; } = new(1000, 2f);

    /// <summary>Per small gear cut; a large gear drains double. The gear cutter's load on its shaft
    /// never changes with its oil: the oil wears its cutter kit instead (GearCutterSettings).</summary>
    public OilMachineConfig GearCutter { get; set; } = new(1000, 10f);

    /// <summary>Per pipe section drawn (four a hollow section).</summary>
    public OilMachineConfig DrawBench { get; set; } = new(1000, 2f);

    /// <summary>Per cylinder bored, either metal. As the gear cutter's, the borer's load on its shaft never
    /// changes with its oil: the oil wears its cutter head instead (CylinderBorerSettings).</summary>
    public OilMachineConfig CylinderBorer { get; set; } = new(1000, 10f);

    /// <summary>Per plate slit into nails and strips, whatever its metal (five knife strokes).</summary>
    public OilMachineConfig SlittingMill { get; set; } = new(1000, 5f);

    // The ore machines' tanks and drains are scaled by load (each machine's README, "Oil"): a tank of 5000
    // points a unit of working load, and 2 points a job for each unit of load times the shaft turns the job
    // takes at it, so every one runs about 44 hours at the reference speed on a full tank.

    /// <summary>Per rock broken.</summary>
    public OilMachineConfig JawBreaker { get; set; } = new(4000, 3.8f);

    /// <summary>Per rock stamped.</summary>
    public OilMachineConfig StampBattery { get; set; } = new(3750, 14.3f);

    /// <summary>Per rock of a charge ground, drained when the charge is.</summary>
    public OilMachineConfig Arrastra { get; set; } = new(500, 43f);

    /// <summary>Per rock ground.</summary>
    public OilMachineConfig BallMill { get; set; } = new(5500, 21f);

    /// <summary>Per rock jigged.</summary>
    public OilMachineConfig Jig { get; set; } = new(500, 1.4f);

    /// <summary>Per rock washed.</summary>
    public OilMachineConfig ShakingTable { get; set; } = new(500, 1.6f);

    /// <summary>Per rock vanned.</summary>
    public OilMachineConfig Vanner { get; set; } = new(400, 3.1f);

    /// <summary>Per item roasted, times its weight (a concentrate's 1).</summary>
    public OilMachineConfig ReverberatoryRoaster { get; set; } = new(2500, 0.75f);

    /// <summary>Per charge stirred, times its share of a full charge.</summary>
    public OilMachineConfig ParkesKettle { get; set; } = new(1000, 115f);

    public static readonly MachineOilConfig Defaults = new();

    /// <summary>The entry of <paramref name="machine"/>: the property of its name. A machine opts into oil
    /// with a value of <see cref="OilMachine"/> and a property of the same name here, with its defaults.</summary>
    public OilMachineConfig For(OilMachine machine) =>
        (OilMachineConfig)Entry(machine).GetValue(this)!;

    private static System.Reflection.PropertyInfo Entry(OilMachine machine) =>
        typeof(MachineOilConfig).GetProperty(machine.ToString()) is { PropertyType: var t } p && t == typeof(OilMachineConfig)
            ? p
            : throw new ArgumentOutOfRangeException(nameof(machine), machine, "MachineOilConfig has no entry of that name");

    /// <summary>The litres one lump of <paramref name="code"/> is worth, or 0 when it is not an oil
    /// lump.</summary>
    public float LumpLitres(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return 0;
        foreach (var (pattern, litres) in OilLumps)
            if (new OilCodes([pattern]).Matches(code))
                return litres;
        return 0;
    }

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        OilLiquids ??= [];
        OilLumps ??= [];
        foreach (var (code, litres) in OilLumps.ToList())
            if (!float.IsFinite(litres) || litres <= 0 || litres > 100)
            {
                fixes.Add($"OilLumps {code} {litres} is out of range, so it is not an oil");
                OilLumps.Remove(code);
            }
        if (!float.IsFinite(DryResistanceMultiplier) || DryResistanceMultiplier < 1 || DryResistanceMultiplier > 100)
        {
            fixes.Add($"DryResistanceMultiplier {DryResistanceMultiplier} is out of range, using {Defaults.DryResistanceMultiplier}");
            DryResistanceMultiplier = Defaults.DryResistanceMultiplier;
        }
        foreach (var machine in Enum.GetValues<OilMachine>())
        {
            var fallback = Defaults.For(machine);
            var value = For(machine);
            if (value == null)
            {
                fixes.Add($"{machine} is missing, using the defaults");
                Set(machine, new OilMachineConfig(fallback.Tank, fallback.DrainPerJob));
                continue;
            }
            if (!float.IsFinite(value.Tank) || value.Tank <= 0 || value.Tank > 1_000_000)
            {
                fixes.Add($"{machine}.Tank {value.Tank} is out of range, using {fallback.Tank}");
                value.Tank = fallback.Tank;
            }
            if (!float.IsFinite(value.DrainPerJob) || value.DrainPerJob < 0 || value.DrainPerJob > value.Tank)
            {
                fixes.Add($"{machine}.DrainPerJob {value.DrainPerJob} is out of range, using {fallback.DrainPerJob}");
                value.DrainPerJob = fallback.DrainPerJob;
            }
        }
        return fixes;
    }

    private void Set(OilMachine machine, OilMachineConfig value) => Entry(machine).SetValue(this, value);
}

/// <summary>How the block info writes oil figures.</summary>
public static class OilText
{
    /// <summary>Tank points: whole, rounded up, so a tank with any oil in it never reads 0, and an
    /// empty one reads 0, never -0.</summary>
    public static string Points(double points) => points <= 1e-9 ? "0" : Math.Ceiling(points - 1e-9).ToString("0");

    /// <summary>Kilonewtons to a unit of shaft load: the game's own readout of a power network's
    /// figures (the creative rotor's <c>mechanical-torque-kn</c>, "Torque: {0} kN", is its torque
    /// times 100; the windmill's sails line about the same), and a load is in the same units as a
    /// torque. A plank sawmill's 0.085 reads 8.5 kN.</summary>
    public const float KiloNewtonsPerUnit = 100f;

    /// <summary>A shaft load in kN (the lang string adds the unit), to one place: the game shows
    /// whole kN, but a machine's load is small enough that a place keeps 0.005 from reading 0.</summary>
    public static string Load(float load) => (load * KiloNewtonsPerUnit).ToString("0.#");
}
