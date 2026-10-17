namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>A smelting rate the game can hold: <see cref="Output"/> items of the metal (an ingot or a
/// bloom, 100 units each) per <see cref="Ratio"/> items smelted, the game's <c>smeltedStack</c>
/// stack size and <c>smeltedRatio</c>.</summary>
public readonly record struct SmeltRate(int Output, int Ratio)
{
    /// <summary>Units of metal one smelted item gives.</summary>
    public double UnitsPerItem => 100.0 * Output / Ratio;
}

/// <summary>
/// Ore processing's items (#686, #687, #688, #847; README "Ore processing: items, crushing and
/// smelting"): their codes, which ore gets which, and the rules the game side applies to vanilla's ore
/// item and to the new items. Game-independent; the figures (units a concentrate holds, what share each
/// form smelts to) are <see cref="OreRecovery"/>'s.
/// <list type="bullet">
/// <item>Raw ore is vanilla's <c>game:ore-{grade}-{ore}-{rock}</c>, every grade alike: 16 a stack
/// (<see cref="RockStack"/>), no chunks. Its <c>metalUnits</c> are the pack's (<see cref="RawUnits"/>,
/// about a quarter of vanilla's, #863), set by patch on vanilla's item, which deposit measurement
/// reads; the ore blocks' drops stay as vanilla's.</item>
/// <item>The other rock forms are the pack's, by ore and grade: crushed ore
/// (<c>seraphhorizons:crushedore-{ore}-{grade}</c>), ground ore (<c>groundore-{ore}-{grade}</c>) and a
/// sulfide's roasted rock (<c>roastedore-{ore}-{grade}</c>), each 16 a stack and holding the raw ore's
/// units (<see cref="RawUnits"/>), so one raw ore crushes to one crushed, which grinds to one ground
/// (#847, #863). Concentrate, roasted concentrate (sulfides), amalgam and sponge (free gold and silver) are
/// <c>seraphhorizons:{form}-{ore}</c>, 5 units each; litharge, one item.</item>
/// </list>
/// </summary>
public static class OreProducts
{
    public const string Domain = "seraphhorizons";

    /// <summary>Every rock form stacks to 16, as rock and earth blocks do (#863): raw, crushed and
    /// ground ore and roasted rock are rock at any grade, and a slot holds a fixed amount of it (#847).</summary>
    public const int RockStack = 16;

    /// <summary>
    /// How many times vanilla's blocks a deposit of a graded ore holds (#863): each ore item carries
    /// about a quarter of vanilla's units (<see cref="RawUnits"/>), so deposits are four times the
    /// volume and hold about the metal they did. <see cref="OreSizeTable.VolumeFor"/> applies it.
    /// </summary>
    public const double DepositBlocks = 4;

    /// <summary>Concentrate, roasted concentrate, amalgam and sponge stack to 128, litharge to 64 (the
    /// item type files).</summary>
    public const int ConcentrateStack = 128, LithargeStack = 64;

    /// <summary>Litharge holds a nugget's worth of lead and smelts back to it at a small loss:
    /// 21 litharge make an ingot (4.76 units each, 95 %).</summary>
    public static readonly SmeltRate LithargeRate = new(1, 21);

    /// <summary>The ore's grades, poor to bountiful.</summary>
    public static readonly IReadOnlyList<string> Grades = ["poor", "medium", "rich", "bountiful"];

    /// <summary>The ores ore processing makes items for: every graded metal ore in the pack (vanilla,
    /// GeologyAdditions, MaterialNeedsGeology), as the item type files list them. Not wolframite,
    /// which has no ore item.</summary>
    public static readonly IReadOnlyList<string> Ores =
    [
        "azurite", "bismuthinite", "cassiterite", "cerussite", "chalcocite", "chalcopyrite", "chromite",
        "franckeite", "freibergite", "galena", "galena_nativesilver", "hematite", "hemimorphite", "ilmenite",
        "limonite", "magnetite", "malachite", "nativecopper", "nativeplatinum", "pentlandite", "pyrite",
        "quartz_nativegold", "quartz_nativesilver", "rhodochrosite", "smithsonite", "sperrylite", "sphalerite",
        "teallite", "tetrahedrite", "uranium", "vanadinite", "wulfenite",
    ];

    /// <summary>Whether <paramref name="grade"/> is one of the ore's grades.</summary>
    public static bool IsGrade(string? grade) => grade != null && Grades.Contains(grade);

    /// <summary>
    /// The metal units raw ore of <paramref name="ore"/> and <paramref name="grade"/> holds (#863): 3, 5,
    /// 7 and 10 by grade, about a quarter of vanilla's <c>ore-graded.json</c> <c>metalUnitsByType</c>
    /// (15, 20, 25, 35), so a slot of 16 holds about the metal 4 did. Vanilla's own exceptions keep their
    /// ratio to its default, rounded half up (<see cref="Quartered"/>): hematite 4, 6, 8, 11 (vanilla 20,
    /// 25, 30, 40), silver quartz 2, 4, 6, 7 (10, 15, 20, 25), cassiterite and gold quartz 1, 3, 4, 6 (5,
    /// 10, 15, 20). <c>patches/oreprocessing-ore.json</c> sets these on vanilla's ore item and twice them
    /// on crystallised ore, the other rock forms' type files repeat them, and the pack tests hold all
    /// three to this. Null for a grade that is not one.
    /// </summary>
    public static int? RawUnits(string ore, string? grade)
    {
        int i = grade == null ? -1 : Grades.ToList().IndexOf(grade);
        if (i < 0) return null;
        int[] units = ore switch
        {
            "cassiterite" or "quartz_nativegold" => [1, 3, 4, 6],
            "hematite" => [4, 6, 8, 11],
            "quartz_nativesilver" => [2, 4, 6, 7],
            _ => DefaultUnits,
        };
        return units[i];
    }

    /// <summary>Raw ore's units by grade for every ore but vanilla's exceptions (#863).</summary>
    private static readonly int[] DefaultUnits = [3, 5, 7, 10];

    /// <summary>Vanilla's default units by grade (<c>ore-graded.json</c>), which its exceptions are
    /// measured against.</summary>
    public static readonly IReadOnlyList<int> VanillaDefaultUnits = [15, 20, 25, 35];

    /// <summary>
    /// The pack's units for an ore vanilla gives <paramref name="vanillaUnits"/> at
    /// <paramref name="grade"/> (#863): the same ratio to the pack's default (3, 5, 7, 10) as to
    /// vanilla's (15, 20, 25, 35), rounded half up, at least 1. How <see cref="RawUnits"/>'
    /// exceptions were made.
    /// </summary>
    public static int Quartered(int vanillaUnits, string grade)
    {
        int i = Grades.ToList().IndexOf(grade);
        if (i < 0) throw new ArgumentOutOfRangeException(nameof(grade), grade, "not a grade");
        double units = (double)vanillaUnits * DefaultUnits[i] / VanillaDefaultUnits[i];
        return Math.Max(1, (int)Math.Floor(units + 0.5));
    }

    /// <summary>The crushed ore item: <c>seraphhorizons:crushedore-{ore}-{grade}</c>.</summary>
    public static string CrushedCode(string ore, string grade) => $"{Domain}:crushedore-{ore}-{grade}";

    /// <summary>The ground ore item: <c>seraphhorizons:groundore-{ore}-{grade}</c>.</summary>
    public static string GroundCode(string ore, string grade) => $"{Domain}:groundore-{ore}-{grade}";

    /// <summary>A sulfide's roasted rock (raw or crushed ore roasted in the firepit):
    /// <c>seraphhorizons:roastedore-{ore}-{grade}</c>.</summary>
    public static string RoastedRockCode(string ore, string grade) => $"{Domain}:roastedore-{ore}-{grade}";

    /// <summary>A sulfide's rock roasted in the stall roaster (#925), which smelts at the stall's 92 %, not the
    /// firepit's 85 %: <c>seraphhorizons:stallroastedore-{ore}-{grade}</c>.</summary>
    public static string StallRoastedRockCode(string ore, string grade) => $"{Domain}:stallroastedore-{ore}-{grade}";

    public static string ConcentrateCode(string ore) => $"{Domain}:concentrate-{ore}";
    public static string RoastedCode(string ore) => $"{Domain}:roastedconcentrate-{ore}";
    public static string AmalgamCode(string ore) => $"{Domain}:amalgam-{ore}";
    /// <summary>Retorted sponge: the gold or silver an amalgam leaves in the still (#726).</summary>
    public static string SpongeCode(string ore) => $"{Domain}:sponge-{ore}";
    public const string LithargeCode = Domain + ":litharge";

    /// <summary>Whether an ore has a roasted concentrate: a sulfide.</summary>
    public static bool Roasts(OreSpec ore) => ore.IsSulfide;

    /// <summary>Whether an ore has an amalgam: free gold or silver.</summary>
    public static bool Amalgamates(OreSpec ore) => ore.Free;

    /// <summary>
    /// The nugget an ore's metal is read from (<c>nugget-{this}</c>), as vanilla's ore item names it:
    /// the ore's code without <c>quartz_</c> (<c>quartz_nativegold</c>: <c>nativegold</c>), except
    /// argentiferous galena, a lead ore (#690), whose nugget is galena's (vanilla hammers it into native
    /// silver). What a nugget smelts to is what every form of the ore smelts to.
    /// </summary>
    public static string NuggetOf(string ore) => ore == Argentiferous ? "galena" : ore.Replace("quartz_", "");

    /// <summary>Argentiferous galena (vanilla's silver galena), a lead ore carrying silver (#690).</summary>
    public const string Argentiferous = "galena_nativesilver";

    /// <summary>
    /// How many rock items of <paramref name="unitsPerItem"/> an input of <paramref name="units"/>
    /// crushes to, with no loss: one for raw ore, whose units the crushed ore holds (#847), two for
    /// crystallised ore, which holds twice its grade's. Rounded to the nearest, never below one.
    /// </summary>
    public static int CrushedCount(double units, double unitsPerItem)
    {
        if (!(unitsPerItem > 0)) throw new ArgumentOutOfRangeException(nameof(unitsPerItem));
        return Math.Max(1, (int)Math.Round(units / unitsPerItem, MidpointRounding.AwayFromZero));
    }

    /// <summary>The most output a smelting rate may name: enough for every share of every grade's units
    /// to be exact.</summary>
    public const int MaxRateOutput = 10_000;

    /// <summary>
    /// The smelting rate that gives <paramref name="share"/> of <paramref name="unitsPerItem"/> per item:
    /// the smallest output stack for which the ratio is a whole number of items, so a 7-unit rich ore
    /// at 40 % (2.8 units) is 7 ingots per 250, exact. The game's crucible and alloy maths count a
    /// charge as a fraction of the rate, so a rate larger than a charge costs nothing. Null for a share
    /// of 0 (does not smelt). If no output up to <see cref="MaxRateOutput"/> is exact, the nearest ratio
    /// for one output (never 0).
    /// </summary>
    public static SmeltRate? Rate(double unitsPerItem, double share)
    {
        double units = unitsPerItem * share;
        if (!(units > 0)) return null;
        for (int output = 1; output <= MaxRateOutput; output++)
        {
            double ratio = 100.0 * output / units;
            double whole = Math.Round(ratio);
            if (whole >= 1 && Math.Abs(ratio - whole) < 1e-9 * Math.Max(1, ratio))
                return new SmeltRate(output, (int)whole);
        }
        return new SmeltRate(1, Math.Max(1, (int)Math.Round(100.0 / units)));
    }
}
