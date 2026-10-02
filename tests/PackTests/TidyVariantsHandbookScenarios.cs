using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/tidyvariants, handbook side (#257, docs/variant-grouping/handbook.md): the handbook layout computed
/// from the server's resolution of the whole pack. The handbook itself is client only, so pages are approximated
/// by every engine entry's page code (vanilla <c>PageCodeForStack</c>) and every page is taken to be able to
/// represent its group; the client checks that per page at runtime.
/// </summary>
[AtlasWorld]
public class TidyVariantsHandbookScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private dynamic Bridge()
    {
        var system = World.Api.ModLoader.GetMod("tidyvariants").Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.TidyVariants.TidyVariantsModSystem");
        object? bridge = system.GetType().GetProperty("Bridge")!.GetValue(system);
        Assert.True(bridge is not null, "the server resolution is null (see the log for the error)");
        return bridge!;
    }

    private static int Count(object collection) => ((System.Collections.ICollection)collection).Count;

    [AtlasScenario]
    public void Handbook_client_system_does_not_load_on_the_server()
    {
        const string name = "SeraphHorizons.TidyVariants.TidyHandbookSystem";
        // Mod.Systems lists every system of the mod; the loader only enables those whose ShouldLoad(side) is true.
        Assert.Contains(World.Api.ModLoader.GetMod("tidyvariants").Systems, s => s.GetType().FullName == name);
        Assert.False(World.Api.ModLoader.IsModSystemEnabled(name), "the handbook system is enabled on the server");
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public void Every_group_keeps_one_listed_page_and_one_groupBy_per_collectible()
    {
        dynamic bridge = Bridge();
        object res = bridge.Resolution;
        var asm = res.GetType().Assembly;
        var layoutType = asm.GetType("SeraphHorizons.TidyVariants.Core.HandbookLayout")!;
        var build = layoutType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;
        dynamic layout = build.Invoke(null, [res, true])!;
        dynamic layoutSameKind = build.Invoke(null, [res, false])!;

        int n = (int)bridge.Count;
        var pageOfEntry = new string[n];
        for (int i = 0; i < n; i++) pageOfEntry[i] = GuiHandbookItemStackPage.PageCodeForStack((ItemStack)bridge.StackOf(i));
        System.Func<int, string?> pageOf = e => pageOfEntry[e];
        System.Func<string, bool> canRepresent = _ => true;
        object collapse = layoutType.GetMethod("Collapse")!.Invoke((object)layout, [pageOf, canRepresent])!;
        dynamic c = collapse;

        // One groupBy (at most) per collectible: Attributes.Entries is keyed by kind + code.
        var attrEntries = ((IEnumerable<object>)layout.Attributes.Entries).Select(e => (dynamic)e).ToList();
        var keys = attrEntries.Select(e => ((int)e.Kind) + "|" + (string)e.Code).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        int withGroupBy = attrEntries.Count(e => e.GroupBy is not null), excluded = attrEntries.Count(e => (bool)e.Exclude);

        var duplicates = (IReadOnlySet<string>)c.DuplicatePages;
        var linkedFrom = new Dictionary<string, string>();
        int collapsedGroups = 0;
        foreach (dynamic g in (IEnumerable<object>)c.Groups)
        {
            collapsedGroups++;
            string rep = g.RepresentativePage;
            Assert.DoesNotContain(rep, duplicates);
            foreach (var (_, page) in (IEnumerable<(int, string)>)g.MemberPages) linkedFrom.TryAdd(page, rep);
        }

        var visiblePages = new HashSet<string>();
        for (int i = 0; i < n; i++) if (!(bool)((dynamic)res).IsHidden(i)) visiblePages.Add(pageOfEntry[i]);
        var allPages = pageOfEntry.ToHashSet();
        foreach (var p in visiblePages)
            if (duplicates.Contains(p)) Assert.True(linkedFrom.ContainsKey(p), $"{p} left the list but no listed page links it");
        int listedAfter = allPages.Count(p => !duplicates.Contains(p));

        int Issues(dynamic l, string kind) => ((IEnumerable<object>)l.Attributes.Issues).Count(i => (string)((dynamic)i).Kind == kind);
        var lines = new[]
        {
            $"entries {n}, distinct pages {allPages.Count} (visible entries' pages {visiblePages.Count})",
            $"list: {allPages.Count} -> {listedAfter} pages; {collapsedGroups} groups collapsed (of {Count(layout.Groups)} with 2+ members), {duplicates.Count} pages leave the list, {c.NoRepresentative} without a representative",
            $"attributes: groupBy on {withGroupBy} collectibles, exclude on {excluded}",
            $"verifyAcrossKinds=true: {Issues(layout, "groupby-inexact")} groupby-inexact, {Issues(layout, "groupby-conflict")} groupby-conflict, {Count(layout.Attributes.PatternByGroup)} patterns",
            $"verifyAcrossKinds=false: {Issues(layoutSameKind, "groupby-inexact")} groupby-inexact, {Issues(layoutSameKind, "groupby-conflict")} groupby-conflict, {Count(layoutSameKind.Attributes.PatternByGroup)} patterns",
        };
        foreach (var l in lines) output.WriteLine(l);

        Assert.True(collapsedGroups > 0);
        Assert.True(listedAfter < allPages.Count);
    }

    /// <summary>
    /// The proof that every written <c>groupBy</c> is exact, with the game's own matcher: each pattern is resolved the
    /// way <c>SlideshowItemstackTextComponent</c> does (head stack's <c>IHandbookGrouping</c> placeholders, the head's
    /// domain when there is no <c>:</c>) and run through <c>WildcardUtil.Match(AssetLocation, AssetLocation)</c> against
    /// every visible entry's handbook grouping code. It must match exactly the group's members, except a collectible of the
    /// other kind with a member's very code, which no pattern can tell apart (reported as <c>groupby-shared-code</c>).
    /// </summary>
    [AtlasScenario(TimeoutMs = 300_000)]
    public void Every_groupBy_pattern_is_exact_with_the_games_matcher()
    {
        dynamic bridge = Bridge();
        object res = bridge.Resolution;
        var asm = res.GetType().Assembly;
        var sw = Stopwatch.StartNew();
        dynamic plan = asm.GetType("SeraphHorizons.TidyVariants.Core.Handbook")!
            .GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [res, true])!;
        var planTime = sw.Elapsed;

        var issues = ((IEnumerable<object>)plan.Issues).Select(i => (Kind: (string)((dynamic)i).Kind, Message: (string)((dynamic)i).Message)).ToList();
        foreach (var i in issues) output.WriteLine($"{i.Kind}: {i.Message}");
        var inexact = issues.Where(i => i.Kind == "groupby-inexact").ToList();
        var shared = issues.Where(i => i.Kind == "groupby-shared-code").ToList();

        int n = (int)bridge.Count;
        dynamic r = res;
        var entries = (IReadOnlyList<object>)r.Entries;
        var hidden = new bool[n];
        var group = new int[n];
        var kindCode = new string[n];
        var path = new string[n];
        var grouping = new AssetLocation[n];
        for (int i = 0; i < n; i++)
        {
            hidden[i] = (bool)r.IsHidden(i);
            group[i] = (int)r.GroupOf(i);
            dynamic e = entries[i];
            kindCode[i] = (int)e.Kind + "|" + (string)e.Code;
            path[i] = e.Path;
            var coll = (CollectibleObject)bridge.CollectibleOf(i);
            var stack = (ItemStack)bridge.StackOf(i);
            grouping[i] = coll.GetCollectibleInterface<IHandbookGrouping>()?.GetCodeForHandbookGrouping(stack) ?? coll.Code;
        }

        var patterns = new List<(int Group, string Pattern)>();
        foreach (var kv in (IEnumerable<KeyValuePair<int, string>>)plan.PatternByGroup) patterns.Add((kv.Key, kv.Value));
        var groups = (IReadOnlyList<object>)r.Groups;

        sw.Restart();
        long matches = 0;
        var failures = new List<string>();
        int sharedHits = 0;
        foreach (var (g, pattern) in patterns)
        {
            var members = ((IReadOnlyList<int>)((dynamic)groups[g]).Members).ToList();
            var memberKeys = members.Select(m => kindCode[m]).ToHashSet();
            var memberPaths = members.Select(m => grouping[m].Path).ToHashSet();
            int head = members[0];
            var headColl = (CollectibleObject)bridge.CollectibleOf(head);
            string text = headColl.GetCollectibleInterface<IHandbookGrouping>()?.GetWildcardForHandbookGrouping(pattern, (ItemStack)bridge.StackOf(head)) ?? pattern;
            var needle = text.Contains(':') ? new AssetLocation(text) : new AssetLocation(headColl.Code.Domain, text);
            for (int i = 0; i < n; i++)
            {
                if (hidden[i]) continue;
                bool hit = WildcardUtil.Match(needle, grouping[i]);
                matches++;
                bool member = memberKeys.Contains(kindCode[i]);
                if (hit == member) continue;
                if (hit && memberPaths.Contains(grouping[i].Path)) { sharedHits++; continue; }
                if (failures.Count < 50) failures.Add($"{pattern} ({((dynamic)groups[g]).Id}): {(hit ? "matches non-member" : "misses member")} {kindCode[i]}");
            }
        }
        var matchTime = sw.Elapsed;

        var kinds = new Dictionary<int, string>();
        foreach (dynamic kv in (System.Collections.IEnumerable)plan.PatternKindByGroup) kinds[(int)kv.Key] = kv.Value.ToString();
        var regex = patterns.Where(p => p.Pattern.StartsWith('@')).ToList();
        var lengths = patterns.Select(p => p.Pattern.Length).OrderBy(x => x).ToList();
        int Pct(double q) => lengths[(int)Math.Min(lengths.Count - 1, Math.Floor(q * lengths.Count))];
        var longest = patterns.OrderByDescending(p => p.Pattern.Length).First();
        output.WriteLine($"patterns {patterns.Count}: " + string.Join(", ", kinds.Values.GroupBy(k => k).OrderBy(k => k.Key).Select(k => $"{k.Count()} {k.Key}")));
        output.WriteLine($"length: median {Pct(0.5)}, p90 {Pct(0.9)}, p99 {Pct(0.99)}, max {longest.Pattern.Length} ({((dynamic)groups[longest.Group]).Id}: {longest.Pattern})");
        foreach (var (g, p) in regex.OrderBy(x => x.Pattern, StringComparer.Ordinal)) output.WriteLine($"{kinds[g]} {((dynamic)groups[g]).Id}: {p}");
        output.WriteLine($"plan {planTime.TotalMilliseconds:F0} ms; game matcher: {matches:N0} matches in {matchTime.TotalMilliseconds:F0} ms; " +
                         $"{inexact.Count} groupby-inexact, {shared.Count} groupby-shared-code ({sharedHits} cross-kind same-code hits)");

        // Cost in the game's matcher: a slideshow tests its head's pattern against every handbook stack once.
        var visibleCodes = Enumerable.Range(0, n).Where(i => !hidden[i]).Select(i => grouping[i]).ToArray();
        void Time(string what, List<(int Group, string Pattern)> set)
        {
            if (set.Count == 0) return;
            var needles = set.Select(x => new AssetLocation(((CollectibleObject)bridge.CollectibleOf(((IReadOnlyList<int>)((dynamic)groups[x.Group]).Members)[0])).Code.Domain, x.Pattern)).ToList();
            foreach (var nd in needles) WildcardUtil.Match(nd, visibleCodes[0]); // compile the regexes outside the timing
            var t = Stopwatch.StartNew();
            foreach (var nd in needles) foreach (var c in visibleCodes) WildcardUtil.Match(nd, c);
            double ms = t.Elapsed.TotalMilliseconds;
            output.WriteLine($"{what}: {ms * 1e6 / ((long)needles.Count * visibleCodes.Length):F0} ns per stack, {ms / needles.Count:F2} ms per slideshow over {visibleCodes.Length:N0} stacks");
        }
        Time("regex patterns", regex);
        Time("wildcard patterns", patterns.Where(p => !p.Pattern.StartsWith('@')).ToList());

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Empty(inexact);
        Assert.Equal(0, issues.Count(i => i.Kind == "groupby-conflict"));
        // The inherent residue (docs/variant-grouping/handbook.md): vanilla's ore blocks and ore items share codes.
        var sharedIds = shared.Select(i => i.Message.Split('\'')[1]).ToList();
        Assert.All(sharedIds, id => Assert.Matches("^game-ore(chunk)?-", id));
    }
}
