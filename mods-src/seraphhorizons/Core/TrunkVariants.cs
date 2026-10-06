namespace SeraphHorizons.Mod.Core;

/// <summary>A Logging Expanded tree trunk's block code path,
/// <c>treetrunk-{wood}-{size}-{branches}-{side}</c>, read from its end: the side, the branches state
/// and the size are one word each, and the wood is the rest.</summary>
public readonly record struct TrunkCode(string Wood, string Size, string Branches, string Side)
{
    public const string Prefix = "treetrunk-";

    public string Path => $"{Prefix}{Wood}-{Size}-{Branches}-{Side}";

    /// <summary>Null when <paramref name="path"/> is not a trunk's.</summary>
    public static TrunkCode? Parse(string? path)
    {
        if (path == null || !path.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        var parts = path[Prefix.Length..].Split('-');
        if (parts.Length < 4 || parts.Any(p => p.Length == 0))
            return null;
        int n = parts.Length;
        return new TrunkCode(string.Join("-", parts, 0, n - 3), parts[n - 3], parts[n - 2], parts[n - 1]);
    }

    public TrunkCode WithBranches(string branches) => this with { Branches = branches };

    public TrunkCode WithSize(string size) => this with { Size = size };

    /// <summary>Logging Expanded's size for a trunk of <paramref name="logs"/> logs
    /// (<c>BlockTreeTrunk.GetSizeClass</c>): xs up to 3, sm up to 8, md up to 15, lg up to 24, xl
    /// up to 35, xxl beyond.</summary>
    public static string SizeFor(int logs) =>
        logs <= 3 ? "xs" : logs <= 8 ? "sm" : logs <= 15 ? "md" : logs <= 24 ? "lg" : logs <= 35 ? "xl" : "xxl";
}

/// <summary>
/// The states of a trunk's <c>branches</c> variant. Logging Expanded has <c>yes</c> and <c>no</c>;
/// the Rosser switch's JSON patch adds <c>debarked</c>, a clean trunk with its bark off (the
/// rosser's output). Game-independent, so tests/ runs it without the game.
/// </summary>
public static class TrunkVariants
{
    public const string Group = "branches";
    public const string Branchy = "yes";
    public const string Clean = "no";
    public const string Debarked = "debarked";

    /// <summary>Branched as Logging Expanded judges it: the variant is <c>yes</c>, or branches are
    /// counted. A debarked trunk has neither.</summary>
    public static bool IsBranched(string? branches, int branchCount) => branches == Branchy || branchCount > 0;

    public static bool IsDebarked(string? branches) => branches == Debarked;

    /// <summary>The code path of the debarked trunk of the same wood, size and side; null when
    /// <paramref name="path"/> is not a trunk's.</summary>
    public static string? DebarkedPath(string? path) => TrunkCode.Parse(path)?.WithBranches(Debarked).Path;
}
