using System.Globalization;
using System.Text;

namespace SeraphHorizons.TidyVariants.Core;

/// <summary>
/// Derives a group title from its members' display names when the group has no lang title: the words
/// (nearly) every distinct name shares, in the order of one of the names, with the varying words removed
/// ("Andesite gravel", "Basalt gravel" → "Gravel"; "Plain stone oven heat source (Andesite)", ... →
/// "Plain stone oven heat source"). Returns null when that common part is no good title, and the caller
/// falls back to the representative's name:
/// <list type="bullet">
/// <item>nothing (or only connectives such as "of", "the", punctuation, digits) is shared;</item>
/// <item>the shared words are only modifiers of a varying head noun: in a head-last language (English,
/// German, ...) a varying word right after the last kept word in the same clause ("Dead clownfish",
/// "Dead haddock (adult)" → "Dead" is no title); mirrored for head-first languages (French, Spanish, ...).
/// Text in brackets or after a separator (<c>:</c>, <c>,</c>, a lone dash) is a qualifier and never the head.</item>
/// </list>
/// Works on whitespace/punctuation tokens with case-insensitive comparison; no dictionary beyond a short
/// connective list. Names that look like untranslated asset codes (<c>domain:block-foo-bar</c>) are ignored.
/// Linear in the total length of the names.
/// </summary>
public static class TitleDeriver
{
    /// <summary>Share of the distinct names a word must appear in to be kept: "nearly all".</summary>
    public const double Threshold = 0.8;

    // Short function words in the languages the game ships: trimmed when a dropped word leaves one at an edge, they end
    // the head scan ("Walking stick with ..."), and they are never a title alone.
    static readonly HashSet<string> Connectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "the", "a", "an", "and", "or", "in", "on", "with", "for", "from", "to", "at", "by", "into",
        "von", "der", "die", "das", "des", "dem", "den", "und", "mit", "aus", "im", "zum", "zur",
        "de", "du", "la", "le", "les", "l'", "d'", "et", "en", "au", "aux", "à", "avec",
        "del", "el", "los", "las", "y", "e", "o", "da", "do", "dos", "di", "della", "il", "lo", "con",
        "van", "het", "een", "på", "och", "av", "med", "z", "w", "i", "na", "ze", "и", "из", "с", "в", "со",
    };

    // Head-first languages (adjectives follow the noun) by two-letter code.
    static readonly HashSet<string> HeadFirstLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "fr", "es", "it", "pt", "ro", "ca", "gl", "oc", "vi", "id", "ms", "th", "he", "ar", "fa" };

    /// <summary>Whether titles in <paramref name="locale"/> (a game language code such as <c>en</c>,
    /// <c>pt-br</c>) put the head noun last. Unknown codes count as head-last.</summary>
    public static bool IsHeadLast(string? locale)
    {
        if (string.IsNullOrEmpty(locale)) return true;
        int dash = locale.IndexOfAny(['-', '_']);
        return !HeadFirstLanguages.Contains(dash < 0 ? locale : locale[..dash]);
    }

    enum Kind { Word, Open, Close, Sep }

    readonly record struct Token(Kind Kind, string Text, bool SpaceBefore)
    {
        public string Key => Text.ToLowerInvariant();
    }

    // A word inside brackets plays a different part than the same word outside ("Dead Acmon Blue (female)",
    // "Dead Aega Morpho (blue female)"), so it is counted apart.
    static string[] Keys(List<Token> toks)
    {
        var keys = new string[toks.Count];
        int d = 0;
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i].Kind == Kind.Open) d++;
            else if (toks[i].Kind == Kind.Close && d > 0) d--;
            keys[i] = toks[i].Kind != Kind.Word ? "" : d > 0 ? "(" + toks[i].Key : toks[i].Key;
        }
        return keys;
    }

    /// <summary>
    /// The title the names have in common, or null (use the representative's name).
    /// </summary>
    /// <param name="names">Display names of the group's members (creative order; duplicates are fine).</param>
    /// <param name="representativeName">The representative's name: its word order wins when it contains every kept word.</param>
    /// <param name="headLast">See <see cref="IsHeadLast"/>.</param>
    public static string? Derive(IEnumerable<string?> names, string? representativeName = null, bool headLast = true)
    {
        // Distinct usable names, representative first.
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? n)
        {
            if (n is null) return;
            n = n.Trim();
            if (n.Length == 0 || LooksLikeCode(n) || !seen.Add(n)) return;
            distinct.Add(n);
        }
        Add(representativeName);
        foreach (var n in names) Add(n);
        if (distinct.Count == 0) return null;

        var tokenized = new List<Token>[distinct.Count];
        var keys = new string[distinct.Count][];
        for (int i = 0; i < distinct.Count; i++) keys[i] = Keys(tokenized[i] = Tokenize(distinct[i]));

        // Words by the number of distinct names they occur in.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var perName = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ks in keys)
        {
            perName.Clear();
            foreach (var k in ks)
                if (k.Length > 0 && perName.Add(k)) counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        int need = (int)Math.Ceiling(Threshold * distinct.Count - 1e-9);
        if (need < 1) need = 1;

        // Source of the word order: the name containing the most kept words; the representative, then creative order, on ties.
        int best = -1, bestKept = -1;
        for (int i = 0; i < tokenized.Length; i++)
        {
            perName.Clear();
            int kept = 0;
            foreach (var k in keys[i])
                if (k.Length > 0 && counts[k] >= need && perName.Add(k)) kept++;
            if (kept > bestKept) { best = i; bestKept = kept; }
        }
        if (bestKept <= 0) return null;
        return Build(tokenized[best], keys[best], counts, need, headLast);
    }

    static string? Build(List<Token> src, string[] keys, Dictionary<string, int> counts, int need, bool headLast)
    {
        int n = src.Count;
        var keep = new bool[n];
        var depth = new int[n];
        var clause = new int[n];   // depth-0 clause number: separators and brackets start a new one
        int d = 0, c = 0;
        for (int i = 0; i < n; i++)
        {
            var t = src[i];
            if (t.Kind == Kind.Open) { c++; d++; }
            depth[i] = d;
            clause[i] = c;
            if (t.Kind == Kind.Close) { if (d > 0) d--; c++; }
            if (t.Kind == Kind.Sep && d == 0) c++;
            if (t.Kind == Kind.Word) keep[i] = counts[keys[i]] >= need;
        }

        // A varying word next to the kept head side, in the same depth-0 clause: the kept words only modify it.
        // A connective ends the scan: what follows "with", "of", "aus" is a qualifier, not the head.
        int firstKept = -1, lastKept = -1;
        for (int i = 0; i < n; i++)
            if (keep[i] && depth[i] == 0 && !Connectives.Contains(src[i].Text)) { if (firstKept < 0) firstKept = i; lastKept = i; }
        if (firstKept < 0) return null;   // only bracketed or connective words in common: a qualifier, not a title
        if (headLast)
        {
            for (int i = lastKept + 1; i < n && clause[i] == clause[lastKept] && depth[i] == 0 && !IsConnective(src[i]); i++)
                if (src[i].Kind == Kind.Word && !keep[i]) return null;
        }
        else
        {
            for (int i = firstKept - 1; i >= 0 && clause[i] == clause[firstKept] && depth[i] == 0 && !IsConnective(src[i]); i--)
                if (src[i].Kind == Kind.Word && !keep[i]) return null;
        }

        // A bracket survives only when every word in it is kept ("(Polished)"); otherwise all of it goes
        // ("(Gold, Plain cloth)" must not become "(cloth)").
        var stack = new Stack<int>();
        for (int i = 0; i < n; i++)
        {
            if (src[i].Kind == Kind.Open) { stack.Push(i); continue; }
            if (src[i].Kind != Kind.Close || stack.Count == 0) continue;
            int o = stack.Pop();
            bool any = false, all = true;
            for (int j = o + 1; j < i; j++)
                if (src[j].Kind == Kind.Word) { any = true; all &= keep[j]; }
            bool k = any && all;
            if (!k) for (int j = o + 1; j < i; j++) keep[j] = false;
            keep[o] = keep[i] = k;
        }
        for (int i = 0; i < n; i++) if (src[i].Kind == Kind.Sep) keep[i] = true;

        // Tidy: separators at an edge or next to another separator go; so do connectives left dangling by a
        // dropped neighbour ("Meteoric Stone in snowball" → "in snowball" → "snowball"), but never one that
        // starts or ends the source name itself ("Die Cast").
        bool changed = true;
        while (changed)
        {
            changed = false;
            int prev = -1;
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                int next = i + 1;
                while (next < n && !keep[next]) next++;
                bool startEdge = prev < 0 || src[prev].Kind is Kind.Open or Kind.Sep;
                bool endEdge = next >= n || src[next].Kind is Kind.Close or Kind.Sep;
                bool drop = src[i].Kind switch
                {
                    Kind.Sep => startEdge || endEdge,
                    Kind.Word when Connectives.Contains(src[i].Text) =>
                        (startEdge && i > 0 && !keep[i - 1] && src[i - 1].Kind == Kind.Word) ||
                        (endEdge && i + 1 < n && !keep[i + 1] && src[i + 1].Kind == Kind.Word),
                    _ => false,
                };
                if (drop) { keep[i] = false; changed = true; continue; }
                prev = i;
            }
        }

        // Rebuild with the source's spacing: a space before a kept token if the source had one anywhere since
        // the previous kept token; never right after an opening bracket or before a closing one or a comma.
        var outTok = new List<Token>();
        int last = -1;
        for (int i = 0; i < n; i++)
        {
            if (!keep[i]) continue;
            bool space = src[i].SpaceBefore;
            if (src[i].Kind is not (Kind.Close or Kind.Sep) || IsDash(src[i].Text))
                for (int j = last + 1; j < i; j++) space |= src[j].SpaceBefore;
            if (last >= 0 && src[last].Kind == Kind.Open) space = false;
            outTok.Add(src[i] with { SpaceBefore = space });
            last = i;
        }

        if (!outTok.Any(t => t.Kind == Kind.Word && t.Text.Any(char.IsLetter) && !Connectives.Contains(t.Text))) return null;

        var sb = new StringBuilder();
        foreach (var t in outTok)
        {
            if (sb.Length > 0 && t.SpaceBefore) sb.Append(' ');
            sb.Append(t.Text);
        }
        return Capitalise(sb.ToString());
    }

    /// <summary>Upper-cases the first character if it is a lower-case letter ("gravel" → "Gravel"; "3-tall door" stays).</summary>
    static string Capitalise(string s) =>
        s.Length > 0 && char.IsLower(s[0]) ? char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..] : s;

    /// <summary>An untranslated lang key or asset code (<c>vinteng:block-vesheetmetal-temporalsteel</c>): no spaces, a domain colon.</summary>
    static bool LooksLikeCode(string s)
    {
        int colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1) return false;
        foreach (char ch in s) if (char.IsWhiteSpace(ch) || char.IsUpper(ch)) return false;
        return true;
    }

    static bool IsConnective(Token t) => t.Kind == Kind.Word && Connectives.Contains(t.Text);

    static bool IsSepChar(char ch) => ch is ',' or ';' or ':' or '|' or '/' or '·' or '•';
    static bool IsDash(string s) => s is "-" or "–" or "—" or "--";
    static bool IsOpen(char ch) => ch is '(' or '[' or '{' or '«';
    static bool IsClose(char ch) => ch is ')' or ']' or '}' or '»';

    static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = 0;
        bool space = false;
        while (i < s.Length)
        {
            char ch = s[i];
            if (char.IsWhiteSpace(ch)) { space = true; i++; continue; }
            if (IsOpen(ch)) { list.Add(new Token(Kind.Open, ch.ToString(), space)); space = false; i++; continue; }
            if (IsClose(ch)) { list.Add(new Token(Kind.Close, ch.ToString(), space)); space = false; i++; continue; }
            if (IsSepChar(ch)) { list.Add(new Token(Kind.Sep, ch.ToString(), space)); space = false; i++; continue; }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && !IsOpen(s[i]) && !IsClose(s[i]) && !IsSepChar(s[i])) i++;
            // A separator character between two non-space characters is part of the word ("1:5", "and/or").
            while (i < s.Length - 1 && IsSepChar(s[i]) && s[i] != ',' && s[i] != ';' && !char.IsWhiteSpace(s[i + 1]) && !IsOpen(s[i + 1]) && !IsClose(s[i + 1]))
            {
                i++;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && !IsOpen(s[i]) && !IsClose(s[i]) && !IsSepChar(s[i])) i++;
            }
            string w = s[start..i];
            list.Add(new Token(IsDash(w) ? Kind.Sep : Kind.Word, w, space));
            space = false;
        }
        return list;
    }
}
