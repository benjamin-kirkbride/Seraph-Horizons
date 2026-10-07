namespace SeraphHorizons.Mod.Core;

/// <summary>
/// The flat felling wear's figures: FlatFellingWearSettings in ModConfig/seraphhorizons.json. A
/// value out of range falls back to its default with a warning (<see cref="Sanitise"/>); the
/// server's are used.
/// </summary>
public class FellingWearConfig
{
    /// <summary>Durability an axe loses felling a tree with a one-block-wide trunk (every vanilla
    /// tree but the redwood), whatever its height, in place of one per log.</summary>
    public int ThinTree { get; set; } = 4;

    /// <summary>Durability an axe loses felling a tree with a two-by-two trunk (the game's log
    /// sections: the redwood), in place of one per log.</summary>
    public int ThickTree { get; set; } = 8;

    public static readonly FellingWearConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        ThinTree = Check(nameof(ThinTree), ThinTree, Defaults.ThinTree, fixes);
        ThickTree = Check(nameof(ThickTree), ThickTree, Defaults.ThickTree, fixes);
        return fixes;
    }

    private static int Check(string name, int value, int fallback, List<string> fixes)
    {
        if (value >= 0 && value <= 10_000)
            return value;
        fixes.Add($"{name} {value} is out of range, using {fallback}");
        return fallback;
    }
}
