using System.Text;

namespace SeraphHorizons.SeraphTweaks.TidyVariants.Core;

/// <summary>Why an entry is hidden.</summary>
public enum HideReason
{
    /// <summary>Not hidden.</summary>
    None,
    /// <summary>A non-canonical orientation or open/closed variant (automatic hide rule).</summary>
    Variant,
    /// <summary>An override <c>"hide": true</c> rule.</summary>
    Override,
}

/// <summary>
/// The entries of one collectible type: same domain, kind, base code and dimension names. A dimension is
/// classified once per family, from the values its entries take.
/// </summary>
public sealed class TidyFamily
{
    internal TidyFamily(int index, string key, string domain, string basePath, EntryKind kind, string[] dims, bool aligned)
    {
        Index = index; Key = key; Domain = domain; BasePath = basePath; Kind = kind; DimensionNames = dims; CodeAligned = aligned;
    }

    public int Index { get; }
    public string Key { get; }
    public string Domain { get; }
    /// <summary>The code path with the variant values removed (<c>ore</c> for <c>ore-poor-nativecopper-granite</c>).</summary>
    public string BasePath { get; }
    public EntryKind Kind { get; }
    /// <summary>Variant names in order, then <c>attr:&lt;key&gt;</c> names.</summary>
    public IReadOnlyList<string> DimensionNames { get; }
    /// <summary>Automatic classification, parallel to <see cref="DimensionNames"/>.</summary>
    public IReadOnlyList<DimensionInfo> Dimensions { get; internal set; } = [];
    /// <summary>False when a member's code isn't <c>base-v1-v2...</c>; no handbook pattern is built from positions then.</summary>
    public bool CodeAligned { get; internal set; }
    /// <summary>Entry indices in creative order.</summary>
    public IReadOnlyList<int> Members => members;
    internal readonly List<int> members = [];
    /// <summary>Number of variant (non-attribute) dimensions at the front of <see cref="DimensionNames"/>.</summary>
    public int VariantDimensionCount { get; internal set; }
}

/// <summary>Where a group comes from.</summary>
public enum GroupSource
{
    /// <summary>Derived from dimensions: a family plus its meaningful values.</summary>
    Automatic,
    /// <summary>Automatic groups merged because a shipped handbook <c>groupBy</c> already puts them on one page.</summary>
    Shipped,
    /// <summary>An override <c>group</c> rule.</summary>
    Override,
}

/// <summary>Resolution switches; null fields fall back to the override file, then to the default.</summary>
public sealed class ResolveOptions
{
    /// <summary>Merge automatic groups that a shipped <c>groupBy</c> joins (default true).</summary>
    public bool? HonorShippedGroupBy { get; init; }
}

/// <summary>A resolved group with at least two members. Groups are ordered by their first member.</summary>
public sealed class TidyGroup
{
    internal TidyGroup(int index, string id, GroupSource source, int ruleIndex, int[] members, string? title, string? shippedPattern)
    {
        Index = index; Id = id; Source = source; RuleIndex = ruleIndex; Members = members; Title = title; ShippedPattern = shippedPattern;
    }

    public int Index { get; }
    /// <summary>Stable id: the override's id, <c>auto:&lt;domain&gt;:&lt;base&gt;[/dim=value...]</c>, or
    /// <c>groupby:&lt;block|item&gt;:&lt;domain&gt;:&lt;pattern&gt;</c> for shipped-groupBy merges.</summary>
    public string Id { get; }
    public GroupSource Source { get; }
    /// <summary>Not from an override rule (automatic or shipped): no title, named after the representative.</summary>
    public bool IsAutomatic => Source != GroupSource.Override;
    /// <summary>The shipped groupBy pattern (filled, no domain) that merged it, for <see cref="GroupSource.Shipped"/>.</summary>
    public string? ShippedPattern { get; }
    /// <summary>The override rule that made it, or -1.</summary>
    public int RuleIndex { get; }
    /// <summary>Entry indices in creative order (hidden entries never are members).</summary>
    public IReadOnlyList<int> Members { get; }
    /// <summary>Lang key for the title, or null: the game layer then uses the representative's name.</summary>
    public string? Title { get; }
    /// <summary>The preferred member (entry index) when the whole group is shown.</summary>
    public int Representative { get; internal set; }
    /// <summary>The single family all members share, or null for override groups spanning families.</summary>
    public TidyFamily? Family { get; internal set; }

    public override string ToString() => $"{Id} ({Members.Count})";
}

/// <summary>
/// The engine's result: resolve once at load, then build display lists (<see cref="DisplayListBuilder"/>),
/// the handbook plan (<see cref="Handbook"/>) and stats (<see cref="TidyStats"/>) from it.
/// </summary>
public sealed class TidyResolution
{
    internal TidyResolution() { }

    public IReadOnlyList<CreativeEntry> Entries { get; internal set; } = [];
    public IReadOnlyList<TidyFamily> Families { get; internal set; } = [];
    public IReadOnlyList<TidyGroup> Groups { get; internal set; } = [];
    public IReadOnlyList<TidyIssue> Issues { get; internal set; } = [];
    public TidySettings Settings { get; internal set; } = null!;

    internal int[] groupOf = [];
    internal HideReason[] hidden = [];
    internal int[] repRank = [];
    internal int[] familyOf = [];
    internal DimensionInfo[][] dims = [];
    internal Dictionary<string, int> groupById = [];

    public bool IsHidden(int entry) => hidden[entry] != HideReason.None;
    public HideReason HideReasonOf(int entry) => hidden[entry];
    /// <summary>Group index, or -1 for a plain entry (singletons, ungrouped and hidden entries).</summary>
    public int GroupOf(int entry) => groupOf[entry];
    public TidyFamily FamilyOf(int entry) => Families[familyOf[entry]];
    /// <summary>Effective dimensions of an entry (family classification plus <c>dimensions</c> overrides), parallel to its family's names.</summary>
    public IReadOnlyList<DimensionInfo> DimensionsOf(int entry) => dims[entry];
    /// <summary>0 for the most preferred member of its group; lower wins when picking a representative among a subset.</summary>
    public int RepresentativeRank(int entry) => repRank[entry];
    public TidyGroup? GroupById(string id) => groupById.TryGetValue(id, out int g) ? Groups[g] : null;
    public int VisibleCount { get; internal set; }
}

/// <summary>The rule engine entry point.</summary>
public static class TidyEngine
{
    /// <summary>
    /// Resolves classification, the hide rule, groups and representatives. O(n log n) in the entries
    /// (plus rules × entries of the same domain for override matching).
    /// </summary>
    /// <param name="entries">The creative inventory, flat, in creative order.</param>
    /// <param name="worldProperties">See <see cref="TidySettings(IReadOnlyDictionary{string, IReadOnlyList{string}}?, OverrideFile?, List{TidyIssue}?)"/>.</param>
    public static TidyResolution Resolve(
        IReadOnlyList<CreativeEntry> entries,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? worldProperties,
        OverrideFile? overrides = null,
        ResolveOptions? options = null)
    {
        overrides ??= OverrideFile.Empty;
        bool honorShipped = options?.HonorShippedGroupBy ?? overrides.HonorShippedGroupBy ?? true;
        var issues = new List<TidyIssue>();
        var settings = new TidySettings(worldProperties, overrides, issues);
        int n = entries.Count;
        var res = new TidyResolution { Entries = entries, Settings = settings };
        var rules = new RuleIndex(overrides.Rules);

        // 1. Families.
        var families = new List<TidyFamily>();
        var familyByKey = new Dictionary<string, TidyFamily>(StringComparer.Ordinal);
        var familyOf = new int[n];
        var values = new string[n][];
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            int vc = e.Variant.Count, ac = e.Stack?.Values.Count ?? 0;
            var names = new string[vc + ac];
            var vals = new string[vc + ac];
            for (int d = 0; d < vc; d++) { names[d] = e.Variant[d].Key; vals[d] = e.Variant[d].Value; }
            for (int d = 0; d < ac; d++) { names[vc + d] = "attr:" + e.Stack!.Values[d].Key; vals[vc + d] = e.Stack.Values[d].Value; }
            values[i] = vals;

            bool aligned = true;
            string basePath = e.Path;
            if (vc > 0)
            {
                sb.Clear();
                for (int d = 0; d < vc; d++) sb.Append('-').Append(vals[d]);
                string suffix = sb.ToString();
                if (e.Path.Length > suffix.Length && e.Path.EndsWith(suffix, StringComparison.Ordinal))
                    basePath = e.Path[..^suffix.Length];
                else aligned = false;
            }

            sb.Clear();
            sb.Append(e.Domain).Append(':').Append(basePath).Append('|').Append(e.Kind == EntryKind.Block ? 'b' : 'i');
            foreach (var nm in names) sb.Append('|').Append(nm);
            if (!aligned) sb.Append("|!").Append(e.Path);
            string key = sb.ToString();
            if (!familyByKey.TryGetValue(key, out var fam))
            {
                fam = new TidyFamily(families.Count, key, e.Domain, basePath, e.Kind, names, aligned) { VariantDimensionCount = vc };
                families.Add(fam);
                familyByKey[key] = fam;
                if (!aligned)
                    issues.Add(new("variant-mismatch", $"{e.Code}: code does not end with its variant values; treated as its own family"));
            }
            fam.members.Add(i);
            familyOf[i] = fam.Index;
        }

        // 2. Classify each family's dimensions from the values its entries take.
        foreach (var fam in families)
        {
            var infos = new DimensionInfo[fam.DimensionNames.Count];
            for (int d = 0; d < infos.Length; d++)
            {
                var distinct = new HashSet<string>(StringComparer.Ordinal);
                string? hint = null;
                foreach (int m in fam.members)
                {
                    distinct.Add(values[m][d]);
                    hint ??= entries[m].PropertySources is { } ps && ps.TryGetValue(fam.DimensionNames[d], out var h) ? h : null;
                }
                infos[d] = Classifier.Classify(fam.DimensionNames[d], distinct, hint, settings);
            }
            fam.Dimensions = infos;
        }

        // 3. Effective dimensions per entry: `dimensions`/`split` overrides, first rule per dimension wins.
        var dims = new DimensionInfo[n][];
        for (int i = 0; i < n; i++)
        {
            var fam = families[familyOf[i]];
            var baseInfos = (DimensionInfo[])fam.Dimensions;
            DimensionInfo[]? own = null;
            foreach (var r in rules.For(entries[i].Domain, RuleAction.Dimensions))
            {
                if (!r.Match.Matches(entries[i])) continue;
                rules.Used(r);
                foreach (var (name, o) in r.Dimensions!)
                {
                    int d = IndexOfDim(fam.DimensionNames, name);
                    if (d < 0) continue;
                    own ??= (DimensionInfo[])baseInfos.Clone();
                    if (ReferenceEquals(own[d], baseInfos[d])) own[d] = Classifier.FromOverride(fam.DimensionNames[d], o, r.Index);
                }
            }
            dims[i] = own ?? baseInfos;
        }

        // 4. Hide rule.
        var hidden = new HideReason[n];
        var unhide = new bool[n];
        var canonical = new Dictionary<string, int>(StringComparer.Ordinal);
        var hideKey = new string?[n];
        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            foreach (var r in rules.For(e.Domain, RuleAction.Hide, RuleAction.Unhide))
            {
                if (!r.Match.Matches(e)) continue;
                rules.Used(r);
                if (r.Action == RuleAction.Hide) hidden[i] = HideReason.Override; else unhide[i] = true;
                break;
            }
            if (hidden[i] != HideReason.None) continue;
            var di = dims[i];
            bool any = false;
            sb.Clear();
            sb.Append(familyOf[i]);
            for (int d = 0; d < di.Length; d++)
            {
                if (di[d].IsHideable) { any = true; sb.Append("|*"); }
                else sb.Append('|').Append(values[i][d]);
            }
            if (!any) continue;
            if (e.Stack is not null) sb.Append('#').Append(e.Stack.Key);
            string k = sb.ToString();
            hideKey[i] = k;
            if (!canonical.TryGetValue(k, out int best) || CompareCanonical(i, best, dims, values, settings) < 0)
                canonical[k] = i;
        }
        for (int i = 0; i < n; i++)
            if (hideKey[i] is { } k && canonical[k] != i && !unhide[i]) hidden[i] = HideReason.Variant;

        // 5. Groups: first matching group/ungroup rule, else automatic by family + meaningful values.
        var groupOf = new int[n];
        Array.Fill(groupOf, -1);
        var buckets = new List<Bucket>();
        var entryBucket = new int[n];
        Array.Fill(entryBucket, -1);
        var bucketByKey = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            if (hidden[i] != HideReason.None) continue;
            var e = entries[i];
            var fam = families[familyOf[i]];
            OverrideRule? rule = null;
            foreach (var r in rules.For(e.Domain, RuleAction.Group, RuleAction.Ungroup))
                if (r.Match.Matches(e)) { rule = r; rules.Used(r); break; }
            if (rule?.Action == RuleAction.Ungroup) continue;

            sb.Clear();
            if (rule is not null)
            {
                sb.Append("rule:").Append(rule.Index);
                if (rule.Group!.UsesBase) sb.Append("|base=").Append(fam.Domain).Append(':').Append(fam.BasePath);
                foreach (var b in rule.Group!.By)
                {
                    var v = ValueOf(fam, values[i], e, b);
                    if (v is null)
                        issues.Add(new("placeholder-unresolved", $"rules[{rule.Index}]: {e} has no dimension '{b}'"));
                    sb.Append('|').Append(v);
                }
            }
            else
            {
                sb.Append("auto:").Append(fam.Index);
                var di = dims[i];
                for (int d = 0; d < di.Length; d++)
                    if (!di[d].IsFiller) sb.Append('|').Append(fam.DimensionNames[d]).Append('=').Append(values[i][d]);
                if (e.Stack is { Values.Count: 0 }) sb.Append('#').Append(e.Stack.Key);
            }
            string key = sb.ToString();
            if (!bucketByKey.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket(key, rule, i) { Index = buckets.Count };
                buckets.Add(bucket);
                bucketByKey[key] = bucket;
            }
            bucket.Members.Add(i);
            entryBucket[i] = bucket.Index;
        }

        // 5b. Shipped groupBy: automatic buckets whose entries a shipped pattern joins become one group.
        if (honorShipped) buckets = MergeShipped(entries, buckets, entryBucket, families, familyOf, values);

        // 6. Materialise groups of two or more, with ids, titles and representatives.
        var groups = new List<TidyGroup>();
        var groupById = new Dictionary<string, int>(StringComparer.Ordinal);
        var repRank = new int[n];
        var prefer = new bool[n];
        for (int i = 0; i < n; i++)
            foreach (var r in rules.For(entries[i].Domain, RuleAction.Prefer))
                if (r.Match.Matches(entries[i])) { prefer[i] = true; rules.Used(r); break; }

        foreach (var bucket in buckets)
        {
            if (bucket.Members.Count < 2) continue;
            int first = bucket.Members[0];
            var e0 = entries[first];
            var fam0 = families[familyOf[first]];
            string id;
            string? title = null;
            if (bucket.Rule is { } rule)
            {
                id = Fill(rule.Group!.Id, fam0, values[first], e0);
                if (rule.Group.Title is { } t) title = Fill(t, fam0, values[first], e0);
            }
            else if (bucket.Shipped is { } shipped)
                id = "groupby:" + (e0.Kind == EntryKind.Block ? "block:" : "item:") + e0.Domain + ":" + shipped;
            else
            {
                sb.Clear();
                sb.Append("auto:").Append(fam0.Domain).Append(':').Append(fam0.BasePath);
                var di = dims[first];
                for (int d = 0; d < di.Length; d++)
                    if (!di[d].IsFiller) sb.Append('/').Append(fam0.DimensionNames[d]).Append('=').Append(values[first][d]);
                if (e0.Stack is { Values.Count: 0 }) sb.Append('#').Append(e0.Stack.Key);
                id = sb.ToString();
            }
            if (groupById.ContainsKey(id))
            {
                int suffix = 2;
                while (groupById.ContainsKey(id + "#" + suffix)) suffix++;
                if (!bucket.IsAuto)
                    issues.Add(new("duplicate-group-id", $"group id '{id}' produced twice; the second is '{id}#{suffix}'"));
                id = id + "#" + suffix;
            }

            var members = bucket.Members.ToArray();
            var source = bucket.Rule is not null ? GroupSource.Override : bucket.Shipped is not null ? GroupSource.Shipped : GroupSource.Automatic;
            var g = new TidyGroup(groups.Count, id, source, bucket.Rule?.Index ?? -1, members, title, bucket.Shipped);
            int famIdx = familyOf[members[0]];
            g.Family = members.All(m => familyOf[m] == famIdx) ? families[famIdx] : null;

            int forced = -1;
            if (bucket.Rule?.Group?.Representative is { } repMatch)
            {
                foreach (int m in members) if (repMatch.Matches(entries[m])) { forced = m; break; }
                if (forced < 0)
                    issues.Add(new("representative-unmatched", $"group '{id}': representative {repMatch} matches no member"));
            }
            var order = (int[])members.Clone();
            Array.Sort(order, (a, b) =>
            {
                if (a == b) return 0;
                if (a == forced) return -1;
                if (b == forced) return 1;
                if (prefer[a] != prefer[b]) return prefer[a] ? -1 : 1;
                int c = ComparePreference(a, b, dims, values, settings);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int r = 0; r < order.Length; r++) repRank[order[r]] = r;
            g.Representative = order[0];

            foreach (int m in members) groupOf[m] = g.Index;
            groupById[id] = g.Index;
            groups.Add(g);
        }

        foreach (var r in rules.Unused())
            issues.Add(new("rule-unused", $"rules[{r.Index}] ({r.Action.ToString().ToLowerInvariant()} {r.Match}) matched no entry"));

        res.Families = families;
        res.Groups = groups;
        res.Issues = issues;
        res.groupOf = groupOf;
        res.hidden = hidden;
        res.repRank = repRank;
        res.familyOf = familyOf;
        res.dims = dims;
        res.groupById = groupById;
        res.VisibleCount = hidden.Count(h => h == HideReason.None);
        return res;
    }

    sealed class Bucket(string key, OverrideRule? rule, int first)
    {
        public string Key { get; } = key;
        public OverrideRule? Rule { get; } = rule;
        public int First { get; } = first;
        public bool IsAuto => Rule is null;
        public List<int> Members { get; } = [];
        public int Index { get; set; }
        /// <summary>Set when shipped groupBy merged several automatic buckets into this one.</summary>
        public string? Shipped { get; set; }
    }

    /// <summary>
    /// Union-find over automatic buckets: for each distinct shipped pattern (filled from the entry's variant, scoped
    /// to the entry's domain and kind), every automatic entry whose code path it matches joins one component.
    /// Override buckets never take part. Returns the buckets ordered by first member.
    /// </summary>
    static List<Bucket> MergeShipped(IReadOnlyList<CreativeEntry> entries, List<Bucket> buckets, int[] entryBucket,
        List<TidyFamily> families, int[] familyOf, string[][] values)
    {
        int nb = buckets.Count;
        var parent = new int[nb];
        for (int b = 0; b < nb; b++) parent[b] = b;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        // Automatic entries per (domain, kind), sorted by path, for prefix-range matching.
        var byScope = new Dictionary<(string, EntryKind), List<(string Path, int Entry)>>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entryBucket[i] < 0 || !buckets[entryBucket[i]].IsAuto) continue;
            var key = (entries[i].Domain, entries[i].Kind);
            if (!byScope.TryGetValue(key, out var list)) byScope[key] = list = [];
            list.Add((entries[i].Path, i));
        }
        foreach (var l in byScope.Values) l.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var label = new (int Entry, string Pattern)?[nb];
        var done = new HashSet<(string, EntryKind, string)>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.ShippedGroupBy.Count == 0 || entryBucket[i] < 0 || !buckets[entryBucket[i]].IsAuto) continue;
            foreach (var raw in e.ShippedGroupBy)
            {
                string pattern = Fill(raw, families[familyOf[i]], values[i], e);
                string domain = e.Domain;
                int colon = pattern.IndexOf(':');
                if (colon >= 0) { domain = pattern[..colon]; pattern = pattern[(colon + 1)..]; }
                if (!done.Add((domain, e.Kind, pattern)) || !byScope.TryGetValue((domain, e.Kind), out var scope)) continue;

                string prefix = Wildcard.LiteralPrefix(pattern);
                int lo = 0, hi = scope.Count;
                while (lo < hi) { int mid = (lo + hi) >>> 1; if (string.CompareOrdinal(scope[mid].Path, prefix) < 0) lo = mid + 1; else hi = mid; }
                int anchor = -1;
                for (int k = lo; k < scope.Count && scope[k].Path.StartsWith(prefix, StringComparison.Ordinal); k++)
                {
                    if (!Wildcard.IsMatch(pattern, scope[k].Path)) continue;
                    int b = Find(entryBucket[scope[k].Entry]);
                    if (anchor < 0) { anchor = b; continue; }
                    if (b == anchor) continue;
                    // Union; the lower bucket stays root so roots follow creative order.
                    int root = Math.Min(anchor, b), child = Math.Max(anchor, b);
                    parent[child] = root;
                    anchor = root;
                    // The component's label: the pattern of the earliest entry whose pattern merged anything into it.
                    (int Entry, string Pattern)? best = (i, pattern);
                    foreach (var l in new[] { label[root], label[child] })
                        if (l is { } x && x.Entry < best.Value.Entry) best = x;
                    label[root] = best;
                }
            }
        }

        var result = new List<Bucket>();
        var byRoot = new Dictionary<int, Bucket>();
        foreach (var b in buckets)
        {
            if (!b.IsAuto) { result.Add(b); continue; }
            int root = Find(b.Index);
            if (root == b.Index && !byRoot.ContainsKey(root))
            {
                byRoot[root] = b;
                result.Add(b);
                continue;
            }
            if (!byRoot.TryGetValue(root, out var target))
            {
                target = buckets[root];
                byRoot[root] = target;
                result.Add(target);
            }
            target.Members.AddRange(b.Members);
            target.Shipped = label[root]?.Pattern ?? "?";
        }
        foreach (var b in byRoot.Values) b.Members.Sort();
        result.Sort((a, b) => a.Members[0].CompareTo(b.Members[0]));
        return result;
    }

    static int IndexOfDim(IReadOnlyList<string> names, string name)
    {
        for (int d = 0; d < names.Count; d++) if (names[d] == name) return d;
        // A bare name also finds an attribute dimension.
        for (int d = 0; d < names.Count; d++) if (names[d].Length == name.Length + 5 && names[d].StartsWith("attr:", StringComparison.Ordinal) && names[d].EndsWith(name, StringComparison.Ordinal)) return d;
        return -1;
    }

    static string? ValueOf(TidyFamily fam, string[] vals, CreativeEntry e, string name)
    {
        int d = IndexOfDim(fam.DimensionNames, name);
        return d >= 0 ? vals[d] : null;
    }

    static string Fill(string template, TidyFamily fam, string[] vals, CreativeEntry e)
    {
        if (!template.Contains('{')) return template;
        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            int close = open < 0 ? -1 : template.IndexOf('}', open);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            string name = template[(open + 1)..close];
            sb.Append(name == "base" ? fam.BasePath : ValueOf(fam, vals, e, name) ?? "");
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Lexicographic by hideable dimension (canonical-value rank), then creative order.</summary>
    static int CompareCanonical(int a, int b, DimensionInfo[][] dims, string[][] values, TidySettings s)
    {
        var da = dims[a];
        for (int d = 0; d < da.Length; d++)
        {
            if (!da[d].IsHideable) continue;
            int ra = s.PreferenceRank(da[d].PreferenceKey, values[a][d]);
            int rb = s.PreferenceRank(dims[b][d].PreferenceKey, values[b][d]);
            if (ra != rb) return ra.CompareTo(rb);
        }
        return a.CompareTo(b);
    }

    /// <summary>Lexicographic over each entry's filler dimensions in order; members without a preferred value rank last.</summary>
    static int ComparePreference(int a, int b, DimensionInfo[][] dims, string[][] values, TidySettings s)
    {
        var da = dims[a]; var db = dims[b];
        int ia = 0, ib = 0;
        while (true)
        {
            while (ia < da.Length && !da[ia].IsFiller) ia++;
            while (ib < db.Length && !db[ib].IsFiller) ib++;
            if (ia >= da.Length || ib >= db.Length) return 0;
            int ra = s.PreferenceRank(da[ia].PreferenceKey, values[a][ia]);
            int rb = s.PreferenceRank(db[ib].PreferenceKey, values[b][ib]);
            if (ra != rb) return ra.CompareTo(rb);
            ia++; ib++;
        }
    }

    /// <summary>Rules bucketed by action and domain so each entry only tests rules that can match it, in file order.</summary>
    sealed class RuleIndex
    {
        readonly IReadOnlyList<OverrideRule> all;
        readonly Dictionary<(string, int), OverrideRule[]> cache = [];
        readonly bool[] used;

        public RuleIndex(IReadOnlyList<OverrideRule> rules)
        {
            all = rules;
            used = new bool[rules.Count == 0 ? 0 : rules.Max(r => r.Index) + 1];
        }

        public OverrideRule[] For(string domain, params RuleAction[] actions)
        {
            int mask = 0;
            foreach (var a in actions) mask |= 1 << (int)a;
            if (cache.TryGetValue((domain, mask), out var list)) return list;
            list = all.Where(r => (mask & (1 << (int)r.Action)) != 0 && Wildcard.IsMatch(r.Match.Domain, domain)).ToArray();
            cache[(domain, mask)] = list;
            return list;
        }

        public void Used(OverrideRule r) => used[r.Index] = true;

        public IEnumerable<OverrideRule> Unused() => all.Where(r => !used[r.Index]);
    }
}
