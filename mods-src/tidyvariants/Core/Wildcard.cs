namespace SeraphHorizons.TidyVariants.Core;

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
