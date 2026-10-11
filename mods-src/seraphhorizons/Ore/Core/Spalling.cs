namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Spalling's figures (#747): <c>SpallingSettings</c> in ModConfig/seraphhorizons.json. Raw ore set
/// down one at a time on the ground is broken with a hammer where it lies, a left-click a blow, into
/// crushed ore of its grade, one for one (#847), with no loss.
/// </summary>
public class SpallingConfig
{
    /// <summary>Hammer blows a raw ore takes, any grade: every grade is the same lump of rock (#847),
    /// and crystallised ore the same.</summary>
    public int BlowsRawOre { get; set; } = 6;

    /// <summary>Durability the hammer loses a blow, as a blow on the anvil costs it one.</summary>
    public int HammerWearPerBlow { get; set; } = 1;

    public static readonly SpallingConfig Defaults = new();

    /// <summary>Blows an ore item of <paramref name="grade"/> takes; 0 for one that is not spalled.</summary>
    public int BlowsFor(string? grade) => OreProducts.IsGrade(grade) ? BlowsRawOre : 0;

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (BlowsRawOre is < 1 or > 1000)
            Fix(nameof(BlowsRawOre), BlowsRawOre, BlowsRawOre = Defaults.BlowsRawOre, fixes);
        if (HammerWearPerBlow is < 0 or > 1000)
            Fix(nameof(HammerWearPerBlow), HammerWearPerBlow, HammerWearPerBlow = Defaults.HammerWearPerBlow, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}

/// <summary>Spalling's rules apart from the figures: what spalls and how fast blows may come.</summary>
public static class Spalling
{
    /// <summary>The least time between two blows a player strikes (a hammer swing takes longer), so a
    /// client cannot strike faster than the swing.</summary>
    public const int BlowIntervalMs = 300;

    /// <summary>Whether an ore item code's first part and grade make it spallable: vanilla's graded
    /// <c>ore</c> and <c>crystalizedore</c> items of a grade.</summary>
    public static bool Spalls(string? firstPart, string? grade) =>
        firstPart is "ore" or "crystalizedore" && OreProducts.IsGrade(grade);

    /// <summary>
    /// One blow on an ore with <paramref name="before"/> blows struck: the blows after it, and whether
    /// it breaks the ore (the blows reached <paramref name="needed"/>).
    /// </summary>
    public static (int Blows, bool Breaks) Strike(int before, int needed)
    {
        int blows = Math.Max(0, before) + 1;
        return (blows, blows >= Math.Max(1, needed));
    }
}
