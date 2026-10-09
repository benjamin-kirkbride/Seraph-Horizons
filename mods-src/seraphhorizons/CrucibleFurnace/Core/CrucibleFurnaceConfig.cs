namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// The crucible furnace's figures: CrucibleFurnaceSettings in ModConfig/seraphhorizons.json (README
/// "Crucible furnace"). Temperatures in °C, times in game hours unless named in seconds. The
/// defaults melt stainless (1530 °C) from cold in about 3.8 hours with forced air and 7.6 hours on
/// the chimney's draft alone.
/// </summary>
public class CrucibleFurnaceConfig
{
    /// <summary>Metal units a melting pot holds: two ingots.</summary>
    public int PotCapacityUnits { get; set; } = 200;

    /// <summary>Heats a melting pot lasts: it cracks when the last one is emptied (or broken out).</summary>
    public int PotHeats { get; set; } = 3;

    /// <summary>Real seconds a pulled pot of molten metal stays pourable before the metal freezes in
    /// it, from the furnace's temperature when it was pulled; a frozen pot is lost, its metal knocked
    /// out as bits.</summary>
    public double PourWindowSeconds { get; set; } = 20;

    /// <summary>Coke a hole holds around its pot.</summary>
    public int CokeCapacity { get; set; } = 6;

    /// <summary>Game hours a piece of coke burns.</summary>
    public double HoursPerCoke { get; set; } = 1;

    /// <summary>How fast a lit hole heats under its closed lid on the chimney's draft alone, °C an hour.</summary>
    public double ChimneyHeatingPerHour { get; set; } = 200;

    /// <summary>How fast it heats with forced air piped in, °C an hour.</summary>
    public double ForcedHeatingPerHour { get; set; } = 400;

    /// <summary>The hottest the chimney's draft takes a hole.</summary>
    public double ChimneyMaxTemperature { get; set; } = 1600;

    /// <summary>The hottest forced air takes a hole.</summary>
    public double ForcedMaxTemperature { get; set; } = 1650;

    /// <summary>How fast a hole cools with its lid closed and no fire (or no draft), °C an hour.</summary>
    public double CoolingPerHour { get; set; } = 300;

    /// <summary>How fast it cools with its lid open, fire or not, °C an hour.</summary>
    public double LidOpenCoolingPerHour { get; set; } = 600;

    /// <summary>Blocks of chimney the row's stack needs, counted up from the hole's level.</summary>
    public int MinChimneyHeight { get; set; } = 6;

    /// <summary>Holes a row may have on one stack.</summary>
    public int MaxHolesInRow { get; set; } = 4;

    /// <summary>Air a lit hole draws from a pipe next to its row for forced air, litres a real second
    /// (smex's twin-tub blower gives up to 110). It runs on forced air while it gets half of it.</summary>
    public double AirLitresPerSecond { get; set; } = 10;

    /// <summary>Blocks a chimney is built of (codes, <c>*</c> for any run of characters).</summary>
    public List<string> ChimneyBlocks { get; set; } =
    [
        "game:claybricks-*",
        "game:claybrickchimney-*",
        "game:refractorybricks-*",
        "game:stonebricks-*",
        "smex:smokestack-*",
    ];

    public static readonly CrucibleFurnaceConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        void Check<T>(string name, T value, bool bad, Action reset, T fallback)
        {
            if (!bad)
                return;
            reset();
            fixes.Add($"{name} {value} is out of range, using {fallback}");
        }
        var d = Defaults;
        Check(nameof(PotCapacityUnits), PotCapacityUnits, PotCapacityUnits is < 5 or > 10000, () => PotCapacityUnits = d.PotCapacityUnits, d.PotCapacityUnits);
        Check(nameof(PotHeats), PotHeats, PotHeats is < 1 or > 1000, () => PotHeats = d.PotHeats, d.PotHeats);
        Check(nameof(PourWindowSeconds), PourWindowSeconds, !(PourWindowSeconds is >= 1 and <= 3600), () => PourWindowSeconds = d.PourWindowSeconds, d.PourWindowSeconds);
        Check(nameof(CokeCapacity), CokeCapacity, CokeCapacity is < 1 or > 64, () => CokeCapacity = d.CokeCapacity, d.CokeCapacity);
        Check(nameof(HoursPerCoke), HoursPerCoke, !(HoursPerCoke is > 0 and <= 1000), () => HoursPerCoke = d.HoursPerCoke, d.HoursPerCoke);
        Check(nameof(ChimneyHeatingPerHour), ChimneyHeatingPerHour, !(ChimneyHeatingPerHour is > 0 and <= 100000), () => ChimneyHeatingPerHour = d.ChimneyHeatingPerHour, d.ChimneyHeatingPerHour);
        Check(nameof(ForcedHeatingPerHour), ForcedHeatingPerHour, !(ForcedHeatingPerHour is > 0 and <= 100000), () => ForcedHeatingPerHour = d.ForcedHeatingPerHour, d.ForcedHeatingPerHour);
        Check(nameof(ChimneyMaxTemperature), ChimneyMaxTemperature, !(ChimneyMaxTemperature is >= 100 and <= 5000), () => ChimneyMaxTemperature = d.ChimneyMaxTemperature, d.ChimneyMaxTemperature);
        Check(nameof(ForcedMaxTemperature), ForcedMaxTemperature, !(ForcedMaxTemperature is >= 100 and <= 5000), () => ForcedMaxTemperature = d.ForcedMaxTemperature, d.ForcedMaxTemperature);
        Check(nameof(CoolingPerHour), CoolingPerHour, !(CoolingPerHour is > 0 and <= 100000), () => CoolingPerHour = d.CoolingPerHour, d.CoolingPerHour);
        Check(nameof(LidOpenCoolingPerHour), LidOpenCoolingPerHour, !(LidOpenCoolingPerHour is > 0 and <= 100000), () => LidOpenCoolingPerHour = d.LidOpenCoolingPerHour, d.LidOpenCoolingPerHour);
        Check(nameof(MinChimneyHeight), MinChimneyHeight, MinChimneyHeight is < 1 or > 64, () => MinChimneyHeight = d.MinChimneyHeight, d.MinChimneyHeight);
        Check(nameof(MaxHolesInRow), MaxHolesInRow, MaxHolesInRow is < 1 or > 16, () => MaxHolesInRow = d.MaxHolesInRow, d.MaxHolesInRow);
        Check(nameof(AirLitresPerSecond), AirLitresPerSecond, !(AirLitresPerSecond is >= 0 and <= 10000), () => AirLitresPerSecond = d.AirLitresPerSecond, d.AirLitresPerSecond);
        if (ChimneyBlocks == null || ChimneyBlocks.Count == 0 || ChimneyBlocks.Any(string.IsNullOrWhiteSpace))
        {
            ChimneyBlocks = [.. d.ChimneyBlocks];
            fixes.Add($"{nameof(ChimneyBlocks)} is empty or has a blank entry, using the defaults");
        }
        return fixes;
    }
}
