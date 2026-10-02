namespace SeraphHorizons.TidyVariants.Core;

/// <summary>What to write on one collectible's <c>attributes.handbook</c>.</summary>
/// <param name="Code">Collectible code, <c>domain:path</c> (one per collectible, even with several attribute stacks).</param>
/// <param name="Kind">Block or item: a block and an item may share a code (vanilla <c>ore-*</c>), and they are different collectibles.</param>
/// <param name="GroupBy">The single <c>groupBy</c> pattern (a <c>*</c> wildcard over the code path, no domain), or null to leave it alone.</param>
/// <param name="Exclude">True when every creative entry of this code is hidden: write <c>exclude: true</c>.</param>
/// <param name="Group">The group the pattern stands for, or -1.</param>
public sealed record HandbookEntry(string Code, EntryKind Kind, string? GroupBy, bool Exclude, int Group);

public sealed class HandbookPlan
{
    internal HandbookPlan(IReadOnlyList<HandbookEntry> entries, IReadOnlyDictionary<int, string> patterns, IReadOnlyList<TidyIssue> issues)
    {
        Entries = entries; PatternByGroup = patterns; Issues = issues;
    }

    /// <summary>One per distinct collectible code that needs something written (a pattern or exclude), in creative order.</summary>
    public IReadOnlyList<HandbookEntry> Entries { get; }
    /// <summary>Exact pattern per group index, for groups that have one.</summary>
    public IReadOnlyDictionary<int, string> PatternByGroup { get; }
    /// <summary><c>groupby-inexact</c> (no pattern matches exactly the group's codes) and <c>groupby-conflict</c>
    /// (one code's stacks sit in different groups, so one pattern per item can't express it).</summary>
    public IReadOnlyList<TidyIssue> Issues { get; }
}

/// <summary>Derives handbook <c>groupBy</c> patterns and <c>exclude</c> flags from a resolution.</summary>
public static class Handbook
{
    /// <param name="verifyAcrossKinds">When false (default) a pattern only has to be exact among visible collectibles of
    /// its own kind, assuming the handbook groups block and item pages separately (vanilla ships the same
    /// <c>ore-*-{x}-*</c> pattern on the ore block and the ore item). When true, same-domain codes of the other kind count too.</param>
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
            var memberSet = new HashSet<string>(codes.Select(c => c[..2] + Split(c).Path), StringComparer.Ordinal);
            var visible = visibleByDomain[verifyAcrossKinds ? e0.Domain : KindTag(e0.Kind) + e0.Domain];

            string? found = null, offender = null;
            foreach (var candidate in Candidates(res, g, paths))
            {
                offender = FirstNonMember(candidate, visible, memberSet, paths);
                if (offender is null) { found = candidate; break; }
            }
            if (found is null)
            {
                issues.Add(new("groupby-inexact", $"group '{g.Id}': no exact pattern ({offender} would also match)"));
                continue;
            }
            patterns[g.Index] = found;
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
        return new HandbookPlan(result, patterns, issues);
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
    static IEnumerable<string> Candidates(TidyResolution res, TidyGroup g, List<string> paths)
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
            yield return fam.BasePath + string.Concat(parts.Select(p => "-" + (p ?? "*")));
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
        yield return first[..pre] + "*" + first[(first.Length - suf)..];
    }

    static int CommonPrefix(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    static int CommonSuffix(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[^(i + 1)] == b[^(i + 1)]) i++;
        return i;
    }

    /// <summary>A visible non-member the pattern matches, a member it misses, or null when exact.</summary>
    /// <summary>A member the pattern misses, a visible non-member it matches, or null when exact.
    /// <paramref name="members"/> holds kind tag + path.</summary>
    static string? FirstNonMember(string pattern, List<(string Path, string Kind)> sorted, HashSet<string> members, List<string> memberPaths)
    {
        foreach (var m in memberPaths) if (!Wildcard.IsMatch(pattern, m)) return m + " (a member) would not";
        string prefix = Wildcard.LiteralPrefix(pattern);
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (string.CompareOrdinal(sorted[mid].Path, prefix) < 0) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < sorted.Count && sorted[i].Path.StartsWith(prefix, StringComparison.Ordinal); i++)
            if (!members.Contains(sorted[i].Kind + sorted[i].Path) && Wildcard.IsMatch(pattern, sorted[i].Path))
                return sorted[i].Path + (sorted[i].Kind == "i|" ? " (item)" : " (block)");
        return null;
    }
}
