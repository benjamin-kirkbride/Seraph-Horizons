namespace SeraphHorizons.Mod.TidyVariants.Core;

/// <summary>What to write on one collectible's <c>attributes.handbook</c>.</summary>
/// <param name="Code">Collectible code, <c>domain:path</c> (one per collectible, even with several attribute stacks).</param>
/// <param name="Kind">Block or item: a block and an item may share a code (vanilla <c>ore-*</c>), and they are different collectibles.</param>
/// <param name="GroupBy">The single <c>groupBy</c> pattern (a <c>*</c> wildcard over the code path, or an <c>@</c> regex; no domain), or null to leave it alone.</param>
/// <param name="Exclude">True when every creative entry of this code is hidden: write <c>exclude: true</c>.</param>
/// <param name="Group">The group the pattern stands for, or -1.</param>
public sealed record HandbookEntry(string Code, EntryKind Kind, string? GroupBy, bool Exclude, int Group);

public sealed class HandbookPlan
{
    internal HandbookPlan(IReadOnlyList<HandbookEntry> entries, IReadOnlyDictionary<int, string> patterns,
        IReadOnlyDictionary<int, GroupByPatternKind> kinds, IReadOnlyList<TidyIssue> issues)
    {
        Entries = entries; PatternByGroup = patterns; PatternKindByGroup = kinds; Issues = issues;
    }

    /// <summary>One per distinct collectible code that needs something written (a pattern or exclude), in creative order.</summary>
    public IReadOnlyList<HandbookEntry> Entries { get; }
    /// <summary>Exact pattern per group index, for groups that have one.</summary>
    public IReadOnlyDictionary<int, string> PatternByGroup { get; }
    /// <summary>How each pattern in <see cref="PatternByGroup"/> was built.</summary>
    public IReadOnlyDictionary<int, GroupByPatternKind> PatternKindByGroup { get; }
    /// <summary><c>groupby-inexact</c> (no pattern matches exactly the group's codes), <c>groupby-conflict</c>
    /// (one code's stacks sit in different groups, so one pattern per item can't express it) and <c>groupby-shared-code</c>
    /// (the pattern is written, but also matches a collectible of the other kind with a member's very code).</summary>
    public IReadOnlyList<TidyIssue> Issues { get; }
}

/// <summary>Derives handbook <c>groupBy</c> patterns and <c>exclude</c> flags from a resolution.</summary>
public static class Handbook
{
    /// <summary>
    /// Per group, the first exact candidate: the positional wildcard, the common prefix*suffix wildcard, then the shorter
    /// exact one of <see cref="GroupByRegex"/>'s regexes (not for groups with attribute stacks). Exact means: with the
    /// game's matcher (<see cref="GroupByMatcher"/>), it matches every member's code path and no other visible code path
    /// of the domain. A pattern can only be compared with codes, so a non-member of the other kind with a member's very
    /// code (vanilla's ore block and ore item) can't be excluded: it is tolerated and reported (<c>groupby-shared-code</c>).
    /// </summary>
    /// <param name="verifyAcrossKinds">When false (default) a pattern only has to be exact among visible collectibles of
    /// its own kind. When true, same-domain codes of the other kind count too, which is what the handbook's slideshow
    /// does (it matches every stack's code, blocks and items alike); the client uses true.</param>
    public static HandbookPlan Build(TidyResolution res, bool verifyAcrossKinds = false)
    {
        var entries = res.Entries;
        var issues = new List<TidyIssue>();

        // Per code: its group (-1 none, -2 conflicting), whether every entry is hidden, first entry index.
        var byCode = new Dictionary<string, CodeInfo>(StringComparer.Ordinal);
        var codeOrder = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string ck = Key(e);
            if (!byCode.TryGetValue(ck, out var ci))
            {
                ci = new CodeInfo { Group = int.MinValue, AllHidden = true };
                codeOrder.Add(ck);
            }
            if (!res.IsHidden(i))
            {
                ci.AllHidden = false;
                int g = res.GroupOf(i);
                if (ci.Group == int.MinValue) ci.Group = g;
                else if (ci.Group != g) ci.Group = -2;
            }
            byCode[ck] = ci;
        }

        // Visible codes per domain, sorted, for prefix-range verification.
        var visibleByDomain = new Dictionary<string, List<(string Path, string Kind)>>(StringComparer.Ordinal);
        foreach (var ck in codeOrder)
        {
            if (byCode[ck].AllHidden) continue;
            var (kind, domain, path) = Split(ck);
            string scope = verifyAcrossKinds ? domain : kind + domain;
            if (!visibleByDomain.TryGetValue(scope, out var list)) visibleByDomain[scope] = list = [];
            list.Add((path, kind));
        }
        foreach (var l in visibleByDomain.Values) l.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var patterns = new Dictionary<int, string>();
        var kinds = new Dictionary<int, GroupByPatternKind>();
        var conflicted = new HashSet<int>();
        foreach (var ck in codeOrder)
            if (byCode[ck].Group == -2)
                issues.Add(new("groupby-conflict", $"{Display(ck)}: its attribute stacks are in different groups (or some ungrouped); no groupBy written for their groups"));
        for (int i = 0; i < entries.Count; i++)
            if (res.GroupOf(i) >= 0 && byCode[Key(entries[i])].Group == -2) conflicted.Add(res.GroupOf(i));

        foreach (var g in res.Groups)
        {
            if (conflicted.Contains(g.Index)) continue;
            var codes = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (int m in g.Members) if (seen.Add(Key(entries[m]))) codes.Add(Key(entries[m]));
            if (codes.Count < 2) continue; // stacks of one collectible: nothing for the handbook to group

            var e0 = entries[g.Members[0]];
            if (g.Members.Any(m => entries[m].Domain != e0.Domain || entries[m].Kind != e0.Kind))
            {
                issues.Add(new("groupby-inexact", $"group '{g.Id}' spans asset domains or block/item; no single groupBy pattern"));
                continue;
            }
            var paths = codes.Select(c => Split(c).Path).Distinct().ToList();
            paths.Sort(StringComparer.Ordinal);
            var memberSet = new HashSet<string>(codes.Select(c => c[..2] + Split(c).Path), StringComparer.Ordinal);
            var memberPaths = new HashSet<string>(paths, StringComparer.Ordinal);
            var visible = visibleByDomain[verifyAcrossKinds ? e0.Domain : KindTag(e0.Kind) + e0.Domain];

            string? offender = null;
            string? Check(Candidate c, List<string>? shared = null)
            {
                var off = FirstNonMember(c, visible, memberSet, memberPaths, paths, shared ?? []);
                offender ??= off;
                return off;
            }

            Candidate? found = null;
            foreach (var candidate in Candidates(res, g, paths))
                if (Check(candidate) is null) { found = candidate; break; }
            // No exact wildcard: a regex. Not for attribute stacks (an IHandbookGrouping collectible is matched by
            // path-attr1-attr2, which only a wildcard's '*' reaches).
            if (found is null && !g.Members.Any(m => entries[m].Stack is not null))
                found = GroupByRegex.Build(paths, c => Check(c) is null);
            if (found is not { } f)
            {
                issues.Add(new("groupby-inexact", $"group '{g.Id}': no exact pattern ({offender} would also match)"));
                continue;
            }
            var shared = new List<string>();
            Check(f, shared);
            if (shared.Count > 0)
                issues.Add(new("groupby-shared-code", $"group '{g.Id}': {f.Pattern} also matches {string.Join(", ", shared.Take(3))}{(shared.Count > 3 ? $" and {shared.Count - 3} more" : "")}: same code as a member, other kind (the handbook can't tell them apart)"));
            patterns[g.Index] = f.Pattern;
            kinds[g.Index] = f.Kind;
        }

        var result = new List<HandbookEntry>();
        foreach (var ck in codeOrder)
        {
            var ci = byCode[ck];
            var (kind, domain, path) = Split(ck);
            var ek = kind == "b|" ? EntryKind.Block : EntryKind.Item;
            if (ci.AllHidden) { result.Add(new HandbookEntry(domain + ":" + path, ek, null, true, -1)); continue; }
            if (ci.Group >= 0 && !conflicted.Contains(ci.Group) && patterns.TryGetValue(ci.Group, out var p))
                result.Add(new HandbookEntry(domain + ":" + path, ek, p, false, ci.Group));
        }
        return new HandbookPlan(result, patterns, kinds, issues);
    }

    static string KindTag(EntryKind k) => k == EntryKind.Block ? "b|" : "i|";
    static string Key(CreativeEntry e) => KindTag(e.Kind) + e.Code;
    static (string Kind, string Domain, string Path) Split(string key)
    {
        int colon = key.IndexOf(':');
        return (key[..2], key[2..colon], key[(colon + 1)..]);
    }
    static string Display(string key) => key[2..] + (key[0] == 'i' ? " (item)" : " (block)");

    struct CodeInfo
    {
        public int Group;
        public bool AllHidden;
    }

    /// <summary>Positional pattern (base with varying variant positions as <c>*</c>) first, then common prefix*suffix.</summary>
    static IEnumerable<Candidate> Candidates(TidyResolution res, TidyGroup g, List<string> paths)
    {
        if (g.Family is { CodeAligned: true } fam && fam.VariantDimensionCount > 0)
        {
            var e0 = res.Entries[g.Members[0]];
            var parts = new string?[fam.VariantDimensionCount];
            for (int d = 0; d < parts.Length; d++) parts[d] = e0.Variant[d].Value;
            foreach (int m in g.Members)
            {
                var v = res.Entries[m].Variant;
                for (int d = 0; d < parts.Length; d++)
                    if (parts[d] is not null && parts[d] != v[d].Value) parts[d] = null;
            }
            yield return Candidate.Wildcard(fam.BasePath + string.Concat(parts.Select(p => "-" + (p ?? "*"))));
        }

        string first = paths[0];
        int pre = first.Length, suf = first.Length;
        foreach (var p in paths)
        {
            pre = Math.Min(pre, CommonPrefix(first, p));
            suf = Math.Min(suf, CommonSuffix(first, p));
        }
        int minLen = paths.Min(p => p.Length);
        if (pre + suf > minLen) suf = minLen - pre;
        yield return Candidate.Wildcard(first[..pre] + "*" + first[(first.Length - suf)..]);
    }

    internal static int CommonPrefix(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    internal static int CommonSuffix(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[^(i + 1)] == b[^(i + 1)]) i++;
        return i;
    }

    /// <summary>A member the pattern misses, a visible non-member it matches, or null when exact, with the game's
    /// matcher (<see cref="GroupByMatcher"/>). <paramref name="members"/> holds kind tag + path. A non-member of the other
    /// kind with a member's very code is unavoidable (the handbook compares codes only): it goes to <paramref name="shared"/>.</summary>
    static string? FirstNonMember(Candidate c, List<(string Path, string Kind)> sorted, HashSet<string> members, HashSet<string> memberPaths,
        List<string> paths, List<string> shared)
    {
        foreach (var m in paths) if (!GroupByMatcher.IsMatch(c.Pattern, m)) return m + " (a member) would not";
        string prefix = c.Prefix;
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (string.CompareOrdinal(sorted[mid].Path, prefix) < 0) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < sorted.Count && sorted[i].Path.StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            var (path, kind) = sorted[i];
            if (members.Contains(kind + path) || !GroupByMatcher.IsMatch(c.Pattern, path)) continue;
            string shown = path + (kind == "i|" ? " (item)" : " (block)");
            if (memberPaths.Contains(path)) { shared.Add(shown); continue; }
            return shown;
        }
        return null;
    }
}

/// <summary>A <c>groupBy</c> candidate and a literal every code it matches starts with (to scan only that range of the
/// sorted codes). Wildcards are compared ignoring case by the game, so <see cref="Prefix"/> is only a fast filter for
/// lowercase codes, which is what asset codes are.</summary>
readonly record struct Candidate(string Pattern, string Prefix, GroupByPatternKind Kind)
{
    public static Candidate Wildcard(string pattern) => new(pattern, Core.Wildcard.LiteralPrefix(pattern), GroupByPatternKind.Wildcard);
}

/// <summary>How a group's <c>groupBy</c> was built.</summary>
public enum GroupByPatternKind
{
    /// <summary>A plain <c>*</c> wildcard.</summary>
    Wildcard,
    /// <summary>An <c>@</c> regex: per-position alternations of the values that occur, some relaxed to <c>[^-]*</c>.</summary>
    Structured,
    /// <summary>An <c>@</c> regex spelling out exactly the members' codes (<see cref="GroupByRegex.Enumerated"/>).</summary>
    Enumerated,
}

/// <summary>
/// <c>@</c> regex <c>groupBy</c> patterns (see <see cref="GroupByMatcher"/>) for groups no <c>*</c> wildcard matches exactly.
/// Output never contains <c>:</c> (the game would split the pattern there as a domain) nor <c>{</c> (<c>IHandbookGrouping</c>
/// placeholders), and has no top-level <c>|</c> (the game wraps it in <c>^…$</c>).
/// </summary>
public static class GroupByRegex
{
    /// <summary>
    /// The shortest exact one of: the <b>structured</b> regex (codes split at <c>-</c>; per token count, each position is
    /// its literal value or an alternation of the values members have there, then varying positions are relaxed to
    /// <c>[^-]*</c> left to right while still exact, the first one excepted), and the members' codes <b>enumerated</b> as a trie (<see cref="Enumerated"/>). Ties go to the structured one. <paramref name="exact"/> is the caller's check against every
    /// visible code. Null when neither is exact (only possible when <paramref name="exact"/> sees what the game can't
    /// tell apart).
    /// </summary>
    /// <param name="paths">The members' code paths, distinct, sorted ordinally.</param>
    internal static Candidate? Build(IReadOnlyList<string> paths, Func<Candidate, bool> exact)
    {
        if (paths.Count < 2 || paths.Any(p => p.Contains(':') || p.Contains('{'))) return null;
        Candidate? structured = Structured(paths, exact);
        var alt = Enumerated(paths);
        Candidate? alternation = exact(alt) ? alt : null;
        if (structured is { } s && (alternation is not { } a || s.Pattern.Length <= a.Pattern.Length)) return s;
        return alternation;
    }

    /// <summary>
    /// The members' codes spelled out, exact by construction: a trie over the <c>-</c> tokens where siblings with the
    /// same continuation share one alternation, so <c>ore-poor-x-a, ore-poor-x-b, ore-rich-x-a, ore-rich-x-b</c> becomes
    /// <c>@ore-(poor|rich)-x-(a|b)</c>. Its prefix is the members' common prefix (it matches nothing else).
    /// </summary>
    internal static Candidate Enumerated(IReadOnlyList<string> paths)
    {
        var root = new TrieNode();
        foreach (var p in paths)
        {
            var n = root;
            foreach (var t in p.Split('-'))
            {
                if (!n.Children.TryGetValue(t, out var c)) n.Children[t] = c = new TrieNode();
                n = c;
            }
            n.Terminal = true;
        }
        var (pre, _, _) = Factor(paths);
        var alts = Classes(root).Select(c => Tokens(c.Tokens) + c.Tail).ToList();
        return new("@" + (alts.Count == 1 ? alts[0] : "(" + string.Join("|", alts) + ")"), pre, GroupByPatternKind.Enumerated);
    }

    sealed class TrieNode
    {
        public readonly SortedDictionary<string, TrieNode> Children = new(StringComparer.Ordinal);
        public bool Terminal;
    }

    /// <summary>A node's children grouped by identical continuation (each continuation starts with its <c>-</c>).</summary>
    static List<(List<string> Tokens, string Tail)> Classes(TrieNode n)
    {
        var byRest = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (t, c) in n.Children)
        {
            string rest = Continuation(c);
            if (!byRest.TryGetValue(rest, out var l)) { byRest[rest] = l = []; order.Add(rest); }
            l.Add(t);
        }
        return order.Select(r => (byRest[r], r)).ToList();
    }

    /// <summary>What may follow a node's token: "" at a leaf, else "-x…", "(-x…|-y…)", optional when the node ends a code too.</summary>
    static string Continuation(TrieNode n)
    {
        if (n.Children.Count == 0) return "";
        var alts = Classes(n).Select(c => "-" + Tokens(c.Tokens) + c.Tail).ToList();
        if (alts.Count == 1 && !n.Terminal) return alts[0];
        return "(" + string.Join("|", alts) + ")" + (n.Terminal ? "?" : "");
    }

    static string Tokens(List<string> tokens)
    {
        if (tokens.Count == 1) return Esc(tokens[0]);
        var (pre, mid, suf) = Factor(tokens);
        return Esc(pre) + "(" + string.Join("|", mid.Select(Esc)) + ")" + Esc(suf);
    }

    /// <summary>Distinct values as their common prefix, the sorted distinct middles, and their common suffix.</summary>
    static (string Prefix, List<string> Middles, string Suffix) Factor(IReadOnlyList<string> values)
    {
        string first = values[0];
        int pre = first.Length, suf = first.Length, min = first.Length;
        foreach (var p in values)
        {
            pre = Math.Min(pre, Handbook.CommonPrefix(first, p));
            suf = Math.Min(suf, Handbook.CommonSuffix(first, p));
            min = Math.Min(min, p.Length);
        }
        if (pre + suf > min) suf = min - pre;
        var middles = values.Select(p => p[pre..(p.Length - suf)]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        return (first[..pre], middles, first[(first.Length - suf)..]);
    }

    static Candidate? Structured(IReadOnlyList<string> paths, Func<Candidate, bool> exact)
    {
        var shapes = paths.Select(p => p.Split('-')).GroupBy(t => t.Length).OrderBy(k => k.Key).ToList();
        // Per shape: per position, the distinct values (sorted).
        var slots = shapes.Select(sh => Enumerable.Range(0, sh.Key)
            .Select(i => sh.Select(t => t[i]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()).ToArray()).ToList();
        var relaxed = slots.Select(sh => new bool[sh.Length]).ToList();

        Candidate Make()
        {
            var bodies = new List<string>();
            string? prefix = null;
            for (int s = 0; s < slots.Count; s++)
            {
                var lit = new System.Text.StringBuilder();
                bool open = true; // still collecting the literal every match of this shape starts with
                var parts = new string[slots[s].Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    var v = slots[s][i];
                    if (relaxed[s][i]) { parts[i] = "[^-]*"; open = false; continue; }
                    if (v.Length == 1)
                    {
                        parts[i] = Esc(v[0]);
                        if (open) lit.Append(v[0]).Append(i + 1 < parts.Length ? "-" : "");
                        continue;
                    }
                    var (pre, alt, suf) = Factor(v);
                    parts[i] = Esc(pre) + "(" + string.Join("|", alt.Select(Esc)) + ")" + Esc(suf);
                    if (open) lit.Append(pre);
                    open = false;
                }
                bodies.Add(string.Join("-", parts));
                string l = lit.ToString();
                prefix = prefix is null ? l : prefix[..Handbook.CommonPrefix(prefix, l)];
            }
            return new(bodies.Count == 1 ? "@" + bodies[0] : "@(" + string.Join("|", bodies) + ")", prefix!, GroupByPatternKind.Structured);
        }

        var c = Make();
        if (!exact(c)) return null;
        // Position 0 (the base code) always stays spelled out: it keeps the pattern readable and its prefix scan short.
        for (int s = 0; s < slots.Count; s++)
            for (int i = 1; i < slots[s].Length; i++)
            {
                if (slots[s][i].Length == 1) continue;
                relaxed[s][i] = true;
                var r = Make();
                if (exact(r)) c = r; else relaxed[s][i] = false;
            }
        return c;
    }

    static string Esc(string s) => System.Text.RegularExpressions.Regex.Escape(s);
}
