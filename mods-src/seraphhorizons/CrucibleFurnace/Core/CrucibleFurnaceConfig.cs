namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// The crucible furnace's figures: CrucibleFurnaceSettings in ModConfig/seraphhorizons.json (README
/// "Crucible furnace"). Temperatures in °C, times in game hours unless named in seconds. The
/// defaults melt stainless (1530 °C) from cold in about 7.6 hours on the shortest chimney (6 blocks)
/// and about 3.8 hours on the tallest that counts (11 blocks), on natural draught alone.
/// </summary>
public class CrucibleFurnaceConfig
{
    /// <summary>Metal units a melting pot holds: three ingots, so the furnace's two pots fill a
    /// 600-unit cylinder blank (#768).</summary>
    public int PotCapacityUnits { get; set; } = 300;

    /// <summary>Heats a melting pot lasts: it cracks when the last one is emptied (or broken out).</summary>
    public int PotHeats { get; set; } = 3;

    /// <summary>Real seconds a pulled pot of molten metal stays pourable before the metal freezes in
    /// it, from the furnace's temperature when it was pulled; a frozen pot is lost, its metal knocked
    /// out as bits.</summary>
    public double PourWindowSeconds { get; set; } = 20;

    /// <summary>Coke the furnace's one bed holds around its two pots: enough, at an hour a coke, to
    /// melt stainless from cold on the shortest chimney with four hours to spare.</summary>
    public int CokeCapacity { get; set; } = 12;

    /// <summary>Game hours a piece of coke burns with the ash-pit damper open.</summary>
    public double HoursPerCoke { get; set; } = 1;

    /// <summary>How fast a lit furnace heats under its shut lid on the shortest chimney
    /// (<see cref="MinChimneyHeight"/> blocks), °C an hour.</summary>
    public double ChimneyHeatingPerHour { get; set; } = 200;

    /// <summary>The hottest the shortest chimney takes a furnace.</summary>
    public double ChimneyMaxTemperature { get; set; } = 1600;

    /// <summary>°C an hour each chimney block above <see cref="MinChimneyHeight"/> adds to the heating,
    /// up to <see cref="MaxChimneyHeight"/>.</summary>
    public double ChimneyHeatingPerExtraBlock { get; set; } = 40;

    /// <summary>°C each chimney block above <see cref="MinChimneyHeight"/> adds to the hottest the
    /// furnace gets, up to <see cref="MaxChimneyHeight"/>.</summary>
    public double ChimneyMaxPerExtraBlock { get; set; } = 10;

    /// <summary>How fast a furnace cools with its lid shut and no fire (or no draught), °C an hour.</summary>
    public double CoolingPerHour { get; set; } = 300;

    /// <summary>How fast it cools with its lid open, fire or not, °C an hour.</summary>
    public double LidOpenCoolingPerHour { get; set; } = 600;

    /// <summary>Chimney blocks stood on the outlet, counted from it up, that the furnace needs to draw.</summary>
    public int MinChimneyHeight { get; set; } = 6;

    /// <summary>The tallest chimney that draws harder: blocks above it count no more.</summary>
    public int MaxChimneyHeight { get; set; } = 11;

    /// <summary>Furnaces side by side that one chimney serves.</summary>
    public int MaxFurnacesInRow { get; set; } = 5;

    /// <summary>The ash-pit damper half shut: this share of the chimney's heating rate.</summary>
    public double DamperHalfHeating { get; set; } = 0.5;

    /// <summary>The damper half shut: the coke burns at this share of its open rate.</summary>
    public double DamperHalfBurn { get; set; } = 0.6;

    /// <summary>The damper shut, the fire banked: the coke burns at this share of its open rate.</summary>
    public double DamperShutBurn { get; set; } = 0.25;

    /// <summary>The damper shut: the furnace, lid shut, cools at this share of
    /// <see cref="CoolingPerHour"/>, and does not heat.</summary>
    public double DamperShutCooling { get; set; } = 0.25;

    /// <summary>Blocks a chimney is built of (codes, <c>*</c> for any run of characters).</summary>
    public List<string> ChimneyBlocks { get; set; } =
    [
        "game:claybricks-*",
        "game:claybrickchimney-*",
        "game:refractorybricks-*",
        "game:stonebricks-*",
    ];

    /// <summary>Metals (the ingot codes a pot's melt is, <c>*</c> for any run of characters) that a
    /// pulled melting pot pours only into <see cref="LargeCastingMolds"/>: molten iron, so iron tools
    /// stay smithed (#768). The game's crucible is not affected.</summary>
    public List<string> LargeCastingMetals { get; set; } = ["game:ingot-iron"];

    /// <summary>The molds (block codes, <c>*</c> for any run of characters) a melting pot of a
    /// <see cref="LargeCastingMetals"/> metal pours into: the game's ingot molds and the cylinder mold.
    /// Every other mold refuses it.</summary>
    public List<string> LargeCastingMolds { get; set; } =
    [
        "game:ingotmold-*",
        "seraphhorizons:cylindermold-*",
    ];

    public static readonly CrucibleFurnaceConfig Defaults = new();

    /// <summary>Whether a melting pot of <paramref name="metal"/> (its ingot code) pours into the mold
    /// <paramref name="mold"/> (its block code): any mold, unless the metal is one of
    /// <see cref="LargeCastingMetals"/>, which pours only into <see cref="LargeCastingMolds"/>.</summary>
    public bool PourAllowed(string metal, string mold) =>
        !IsLargeCastingMetal(metal) || LargeCastingMolds.Any(m => PotRecipes.CodeMatches(m, mold));

    /// <summary>Whether <paramref name="metal"/> (an ingot code) pours only into large-casting molds.</summary>
    public bool IsLargeCastingMetal(string metal) => LargeCastingMetals.Any(m => PotRecipes.CodeMatches(m, metal));

    /// <summary>Whether <paramref name="block"/> (a block code) is a chimney block.</summary>
    public bool IsChimneyBlock(string? block) => block != null && ChimneyBlocks.Any(c => PotRecipes.CodeMatches(c, block));

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
        static bool Share(double v) => v is >= 0 and <= 1;
        Check(nameof(PotCapacityUnits), PotCapacityUnits, PotCapacityUnits is < 5 or > 10000, () => PotCapacityUnits = d.PotCapacityUnits, d.PotCapacityUnits);
        Check(nameof(PotHeats), PotHeats, PotHeats is < 1 or > 1000, () => PotHeats = d.PotHeats, d.PotHeats);
        Check(nameof(PourWindowSeconds), PourWindowSeconds, !(PourWindowSeconds is >= 1 and <= 3600), () => PourWindowSeconds = d.PourWindowSeconds, d.PourWindowSeconds);
        Check(nameof(CokeCapacity), CokeCapacity, CokeCapacity is < 1 or > 64, () => CokeCapacity = d.CokeCapacity, d.CokeCapacity);
        Check(nameof(HoursPerCoke), HoursPerCoke, !(HoursPerCoke is > 0 and <= 1000), () => HoursPerCoke = d.HoursPerCoke, d.HoursPerCoke);
        Check(nameof(ChimneyHeatingPerHour), ChimneyHeatingPerHour, !(ChimneyHeatingPerHour is > 0 and <= 100000), () => ChimneyHeatingPerHour = d.ChimneyHeatingPerHour, d.ChimneyHeatingPerHour);
        Check(nameof(ChimneyMaxTemperature), ChimneyMaxTemperature, !(ChimneyMaxTemperature is >= 100 and <= 5000), () => ChimneyMaxTemperature = d.ChimneyMaxTemperature, d.ChimneyMaxTemperature);
        Check(nameof(ChimneyHeatingPerExtraBlock), ChimneyHeatingPerExtraBlock, !(ChimneyHeatingPerExtraBlock is >= 0 and <= 100000), () => ChimneyHeatingPerExtraBlock = d.ChimneyHeatingPerExtraBlock, d.ChimneyHeatingPerExtraBlock);
        Check(nameof(ChimneyMaxPerExtraBlock), ChimneyMaxPerExtraBlock, !(ChimneyMaxPerExtraBlock is >= 0 and <= 5000), () => ChimneyMaxPerExtraBlock = d.ChimneyMaxPerExtraBlock, d.ChimneyMaxPerExtraBlock);
        Check(nameof(CoolingPerHour), CoolingPerHour, !(CoolingPerHour is > 0 and <= 100000), () => CoolingPerHour = d.CoolingPerHour, d.CoolingPerHour);
        Check(nameof(LidOpenCoolingPerHour), LidOpenCoolingPerHour, !(LidOpenCoolingPerHour is > 0 and <= 100000), () => LidOpenCoolingPerHour = d.LidOpenCoolingPerHour, d.LidOpenCoolingPerHour);
        Check(nameof(MinChimneyHeight), MinChimneyHeight, MinChimneyHeight is < 1 or > 64, () => MinChimneyHeight = d.MinChimneyHeight, d.MinChimneyHeight);
        Check(nameof(MaxChimneyHeight), MaxChimneyHeight, MaxChimneyHeight < MinChimneyHeight || MaxChimneyHeight > 64,
            () => MaxChimneyHeight = Math.Max(MinChimneyHeight, d.MaxChimneyHeight), Math.Max(MinChimneyHeight, d.MaxChimneyHeight));
        Check(nameof(MaxFurnacesInRow), MaxFurnacesInRow, MaxFurnacesInRow is < 1 or > 16, () => MaxFurnacesInRow = d.MaxFurnacesInRow, d.MaxFurnacesInRow);
        Check(nameof(DamperHalfHeating), DamperHalfHeating, !Share(DamperHalfHeating), () => DamperHalfHeating = d.DamperHalfHeating, d.DamperHalfHeating);
        Check(nameof(DamperHalfBurn), DamperHalfBurn, !(DamperHalfBurn is > 0 and <= 1), () => DamperHalfBurn = d.DamperHalfBurn, d.DamperHalfBurn);
        Check(nameof(DamperShutBurn), DamperShutBurn, !(DamperShutBurn is > 0 and <= 1), () => DamperShutBurn = d.DamperShutBurn, d.DamperShutBurn);
        Check(nameof(DamperShutCooling), DamperShutCooling, !Share(DamperShutCooling), () => DamperShutCooling = d.DamperShutCooling, d.DamperShutCooling);
        if (ChimneyBlocks == null || ChimneyBlocks.Count == 0 || ChimneyBlocks.Any(string.IsNullOrWhiteSpace))
        {
            ChimneyBlocks = [.. d.ChimneyBlocks];
            fixes.Add($"{nameof(ChimneyBlocks)} is empty or has a blank entry, using the defaults");
        }
        if (LargeCastingMetals == null || LargeCastingMetals.Any(string.IsNullOrWhiteSpace))
        {
            LargeCastingMetals = [.. d.LargeCastingMetals];
            fixes.Add($"{nameof(LargeCastingMetals)} is missing or has a blank entry, using the defaults");
        }
        if (LargeCastingMolds == null || LargeCastingMolds.Any(string.IsNullOrWhiteSpace))
        {
            LargeCastingMolds = [.. d.LargeCastingMolds];
            fixes.Add($"{nameof(LargeCastingMolds)} is missing or has a blank entry, using the defaults");
        }
        return fixes;
    }
}
