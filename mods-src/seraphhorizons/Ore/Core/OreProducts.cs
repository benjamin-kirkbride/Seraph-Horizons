namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The grain crushed ore comes out at: fine from poor ore, coarse from the rest (#686).</summary>
public enum OreGrain { Coarse, Fine }

/// <summary>A smelting rate the game can hold: <see cref="Output"/> items of the metal (an ingot or a
/// bloom, 100 units each) per <see cref="Ratio"/> items smelted, the game's <c>smeltedStack</c>
/// stack size and <c>smeltedRatio</c>.</summary>
public readonly record struct SmeltRate(int Output, int Ratio)
{
    /// <summary>Units of metal one smelted item gives.</summary>
    public double UnitsPerItem => 100.0 * Output / Ratio;
}

/// <summary>
/// Ore processing's items (#686, #687, #688; README "Ore processing: items, crushing and smelting"):
/// their codes, which ore gets which, and the rules the game side applies to vanilla's ore item and
/// to the new items. Game-independent; the figures (units an item holds, what share each form
/// smelts to) are <see cref="OreRecovery"/>'s.
/// <list type="bullet">
/// <item>Raw ore and chunks are vanilla's <c>game:ore-{grade}-{ore}-{rock}</c>: poor and medium are
/// raw ore (<see cref="RawStack"/>, no smelting), rich and bountiful chunks (<see cref="ChunkStack"/>,
/// half). Their <c>metalUnits</c> and the ore blocks' drops stay as vanilla's, which deposit
/// measurement reads.</item>
/// <item>Crushed ore is <c>game:crushed-{ore}-{grain}</c>, a second item type next to vanilla's
/// <c>game:crushed-{material}</c>; ground ore, concentrate, roasted concentrate (sulfides) and
/// amalgam (free gold and silver) are <c>seraphhorizons:{form}-{ore}</c>; litharge, one item.</item>
/// <item>Crushing yields the input's units in crushed items of <see cref="OreRecovery.ConcentrateUnits"/>
/// (5), so it loses nothing: every grade's units are a multiple of 5.</item>
/// </list>
/// </summary>
public static class OreProducts
{
    public const string Domain = "seraphhorizons";

    /// <summary>Raw ore (poor and medium) stacks to 4: heavy rock, carried by the cartload.</summary>
    public const int RawStack = 4;

    /// <summary>Chunks (rich and bountiful) stack to 16.</summary>
    public const int ChunkStack = 16;

    /// <summary>Crushed and ground ore stack to 16, concentrate and roasted concentrate and amalgam to
    /// 128, litharge to 64 (the item type files).</summary>
    public const int CrushedStack = 16, ConcentrateStack = 128, LithargeStack = 64;

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

    /// <summary>The form an ore item of <paramref name="grade"/> takes: raw ore for poor and medium,
    /// a chunk for rich and bountiful; null for anything else.</summary>
    public static OreForm? FormOfGrade(string? grade) => grade switch
    {
        "poor" or "medium" => OreForm.Raw,
        "rich" or "bountiful" => OreForm.Chunk,
        _ => null,
    };

    /// <summary>The stack size of an ore item of <paramref name="grade"/>, or null to leave it.</summary>
    public static int? StackOfGrade(string? grade) => FormOfGrade(grade) switch
    {
        OreForm.Raw => RawStack,
        OreForm.Chunk => ChunkStack,
        _ => null,
    };

    /// <summary>The grain an ore of <paramref name="grade"/> crushes to.</summary>
    public static OreGrain GrainOfGrade(string? grade) => OreFeed.IsFineGrained(grade) ? OreGrain.Fine : OreGrain.Coarse;

    public static string Key(OreGrain grain) => grain == OreGrain.Fine ? "fine" : "coarse";

    /// <summary>The crushed ore item: <c>game:crushed-{ore}-{grain}</c>.</summary>
    public static string CrushedCode(string ore, OreGrain grain) => $"game:crushed-{ore}-{Key(grain)}";

    public static string GroundCode(string ore) => $"{Domain}:groundore-{ore}";
    public static string ConcentrateCode(string ore) => $"{Domain}:concentrate-{ore}";
    public static string RoastedCode(string ore) => $"{Domain}:roastedconcentrate-{ore}";
    public static string AmalgamCode(string ore) => $"{Domain}:amalgam-{ore}";
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

    /// <summary>The ore a nugget crushes into crushed ore of: the ore of the same name, or for the
    /// placer metals the quartz ore (<c>nativegold</c>: <c>quartz_nativegold</c>); null if none.</summary>
    public static string? OreOfNugget(string nugget)
    {
        if (Ores.Contains(nugget)) return nugget;
        var quartz = "quartz_" + nugget;
        return Ores.Contains(quartz) ? quartz : null;
    }

    /// <summary>
    /// How many crushed items an input of <paramref name="units"/> crushes to: its units over a crushed
    /// item's, with no loss. An input whose units are not a whole number of crushed items (none in the
    /// pack: every grade is a multiple of 5) is rounded to the nearest, never below one.
    /// </summary>
    public static int CrushedCount(double units, double unitsPerCrushed)
    {
        if (!(unitsPerCrushed > 0)) throw new ArgumentOutOfRangeException(nameof(unitsPerCrushed));
        return Math.Max(1, (int)Math.Round(units / unitsPerCrushed, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The smelting rate that gives <paramref name="share"/> of <paramref name="unitsPerItem"/> per item:
    /// the smallest output stack (up to 20) for which the ratio is a whole number of items, so a half of a
    /// 35-unit chunk is 7 ingots per 40 chunks (17.5 units each), exact. Null for a share of 0 (does not
    /// smelt). If no stack up to 20 is exact, the nearest ratio for one output (never 0).
    /// </summary>
    public static SmeltRate? Rate(double unitsPerItem, double share)
    {
        double units = unitsPerItem * share;
        if (!(units > 0)) return null;
        for (int output = 1; output <= 20; output++)
        {
            double ratio = 100.0 * output / units;
            double whole = Math.Round(ratio);
            if (whole >= 1 && Math.Abs(ratio - whole) < 1e-9)
                return new SmeltRate(output, (int)whole);
        }
        return new SmeltRate(1, Math.Max(1, (int)Math.Round(100.0 / units)));
    }
}
