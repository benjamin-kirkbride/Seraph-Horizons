namespace SeraphHorizons.SeraphTweaks.TidyVariants.Core;

/// <summary>
/// Plain <c>*</c> wildcards, matched ordinally against the whole string. <c>*</c> matches any run of
/// characters, dashes included, and may match nothing, as in Vintage Story's <c>WildcardUtil</c>.
/// There is no other special character.
/// </summary>
public static class Wildcard
{
    public static bool IsMatch(string pattern, string text) => IsMatch(pattern.AsSpan(), text.AsSpan());

    public static bool IsMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
            else if (p < pattern.Length && pattern[p] == text[t]) { p++; t++; }
            else if (star >= 0) { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>The literal text before the first <c>*</c> (the whole pattern if it has none).</summary>
    public static string LiteralPrefix(string pattern)
    {
        int i = pattern.IndexOf('*');
        return i < 0 ? pattern : pattern[..i];
    }
}

/// <summary>
/// A mirror of how the 1.22.7 handbook matches an <c>attributes.handbook.groupBy</c> pattern
/// (<c>SlideshowItemstackTextComponent</c> → <c>WildcardUtil.Match(AssetLocation, AssetLocation)</c> → <c>fastMatch</c>),
/// for the code path once the domains are known to be equal:
/// <list type="bullet">
/// <item>A pattern starting with <c>@</c> is a .NET regex: the rest is wrapped as <c>^…$</c> (so a top-level
/// alternation needs its own group), <c>RegexOptions.CultureInvariant</c> only, so it is <b>case-sensitive</b>,
/// matched against the path only, cached per pattern string for the session (1 s timeout).</item>
/// <item>Anything else is a <c>*</c> wildcard (no other special character), compared <b>ignoring ASCII case</b>.</item>
/// <item>An empty pattern matches nothing.</item>
/// </list>
/// The rest of the game's handling, not modelled here: a pattern with no <c>:</c> gets the head collectible's domain
/// (path kept verbatim); one with a <c>:</c> is split at the first <c>:</c> and lowercased whole (so a regex must not
/// contain <c>:</c>, e.g. <c>(?:</c>); a domain other than <c>*</c> must equal the stack's; an <c>IHandbookGrouping</c>
/// collectible (clutter, shields) first replaces <c>{name}</c> in the pattern with its stack's attributes and is matched
/// by <c>path-attr1-attr2</c> instead of its code. Block and item are not told apart: only the code is compared.
/// </summary>
public static class GroupByMatcher
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.RegularExpressions.Regex> cache = new(StringComparer.Ordinal);

    public static bool IsRegex(string pattern) => pattern.Length > 0 && pattern[0] == '@';

    public static bool IsMatch(string pattern, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern[0] == '@')
            return cache.GetOrAdd(pattern, p => new System.Text.RegularExpressions.Regex(
                string.Concat("^", p.AsSpan(1), "$"), System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).IsMatch(path);
        int pi = 0, t = 0, star = -1, mark = 0;
        while (t < path.Length)
        {
            if (pi < pattern.Length && pattern[pi] == '*') { star = pi++; mark = t; }
            else if (pi < pattern.Length && SameCharIgnoreCase(pattern[pi], path[t])) { pi++; t++; }
            else if (star >= 0) { pi = star + 1; t = ++mark; }
            else return false;
        }
        while (pi < pattern.Length && pattern[pi] == '*') pi++;
        return pi == pattern.Length;
    }

    /// <summary>The game's <c>WildcardUtil.SameCharIgnoreCase</c>.</summary>
    static bool SameCharIgnoreCase(char a, char b)
    {
        if (a == b) return true;
        if ((a | b) < 128) return (a ^ b) == 32 && (uint)((a & 0x5F) - 65) < 26u;
        return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }
}
