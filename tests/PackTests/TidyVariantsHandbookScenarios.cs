using System.Reflection;
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
}
