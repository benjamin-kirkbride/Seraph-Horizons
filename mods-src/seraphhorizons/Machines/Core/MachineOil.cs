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

    public static readonly MachineOilConfig Defaults = new();

    public OilMachineConfig For(OilMachine machine) => machine switch
    {
        OilMachine.HelveHammer => HelveHammer,
        OilMachine.Pulverizer => Pulverizer,
        OilMachine.Sawmill => Sawmill,
        OilMachine.Chopper => Chopper,
        OilMachine.BuckingMill => BuckingMill,
        OilMachine.Rosser => Rosser,
        OilMachine.GearCutter => GearCutter,
        _ => throw new ArgumentOutOfRangeException(nameof(machine)),
    };

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

    private void Set(OilMachine machine, OilMachineConfig value)
    {
        switch (machine)
        {
            case OilMachine.HelveHammer: HelveHammer = value; break;
            case OilMachine.Pulverizer: Pulverizer = value; break;
            case OilMachine.Sawmill: Sawmill = value; break;
            case OilMachine.Chopper: Chopper = value; break;
            case OilMachine.BuckingMill: BuckingMill = value; break;
            case OilMachine.Rosser: Rosser = value; break;
            case OilMachine.GearCutter: GearCutter = value; break;
        }
    }
}
