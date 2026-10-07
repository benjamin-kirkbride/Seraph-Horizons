namespace SeraphHorizons.Mod.Core;

/// <summary>
/// What felling a tree costs the axe under <c>FlatFellingWear</c>: one figure per tree, by the
/// width of its trunk, in place of the game's one durability per log block. Game-independent;
/// <c>FellingWear</c> applies it.
/// </summary>
public static class FellingWearRules
{
    /// <summary>The code path prefix of the game's two-by-two trunk blocks (<c>logsection-grown-redwood-ne-ud</c>).</summary>
    public const string LogSectionPrefix = "logsection-";

    /// <summary>Whether a tree is thick: any of its blocks (their code paths) is a log section.</summary>
    public static bool IsThick(IEnumerable<string> blockCodePaths) =>
        blockCodePaths.Any(path => path.StartsWith(LogSectionPrefix, StringComparison.Ordinal));

    /// <summary>The durability felling a tree costs.</summary>
    public static int Cost(bool thick, FellingWearConfig config) => thick ? config.ThickTree : config.ThinTree;
}
