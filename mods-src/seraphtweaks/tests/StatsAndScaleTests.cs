using System.Diagnostics;
using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using static SeraphHorizons.SeraphTweaks.TidyVariants.Tests.Fx;

namespace SeraphHorizons.SeraphTweaks.TidyVariants.Tests;

public class StatsAndScaleTests
{
    [Fact]
    public void StatsCountBeforeAfterHiddenPerDomainAndTab()
    {
        var entries = new List<CreativeEntry>
        {
            E("game:apple", EntryKind.Item, [], null, "general", "food"),
        };
        entries.AddRange(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron"])));
        entries.AddRange(Product("game:chest", EntryKind.Block, ("side", Horizontal)));
        entries.AddRange(Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["a", "b"]), ("material", ["granite", "andesite"])));
        entries.Add(E("game:junk", EntryKind.Item));
        var r = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse(
            """{ "rules": [ { "match": { "domain": "game", "code": "junk" }, "hide": true }, { "match": { "domain": "doorvariants", "code": "*-b-*" }, "group": { "id": "b-doors", "title": "lang-b" } } ] }"""));
        var s = TidyStats.Compute(r);

        Assert.Equal(13, s.EntriesBefore);
        Assert.Equal(4, s.Hidden);
        Assert.Equal(3, s.HiddenByVariantRule);
        Assert.Equal(1, s.HiddenByOverride);
        Assert.Equal(3, s.Groups);
        Assert.Equal(2, s.AutomaticGroups);
        Assert.Equal(1, s.OverrideGroups);
        Assert.Equal(7, s.GroupedEntries);
        // apple, ingot tile, chest-north, door a tile, door b tile.
        Assert.Equal(5, s.EntriesAfter);

        var game = s.PerDomain.Single(d => d.Key == "game");
        Assert.Equal(new CountStat("game", 9, 4, 3), game);
        Assert.Equal(new CountStat("doorvariants", 4, 0, 2), s.PerDomain.Single(d => d.Key == "doorvariants"));
        Assert.Equal(new CountStat("food", 1, 0, 1), s.PerTab.Single(t => t.Key == "food"));
        Assert.Equal(new CountStat("general", 13, 4, 5), s.PerTab.Single(t => t.Key == "general"));

        Assert.Equal("auto:game:ingot", s.LargestGroups[0].Id);
        Assert.Equal("game:ingot-copper", s.LargestGroups[0].RepresentativeCode);
        Assert.Equal(["auto:game:ingot", "auto:doorvariants:cobbledoor/style=a"], s.UntitledGroups.Select(g => g.Id));
    }

    /// <summary>A synthetic pack of ~27,000 entries shaped like the real one.</summary>
    static List<CreativeEntry> BigPack()
    {
        var list = new List<CreativeEntry>();
        string[] moreRocks = [.. RockWithDeposit, "arkose", "dolostone", "marl", "mudstone", "serpentinite", "gneiss", "schist", "quartzite", "rhyolite", "dacite", "diorite", "gabbro", "komatiite", "anorthosite", "pumice", "jaspillite", "lignite2", "coquina", "breccia", "tillite", "migmatite", "hornfels", "skarn", "eclogite"];
        string[] ores = [.. OreGraded, "bismuthinite", "magnetite", "hematite", "pentlandite", "uranium", "wolframite", "rhodochrosite", "quartz_nativesilver", "galena_nativesilver"];
        list.AddRange(Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ores), ("rock", moreRocks[..40]))); // 2880
        list.AddRange(Product("game:crystalizedore", EntryKind.Item, ("grade", Grades), ("ore", ores), ("rock", moreRocks[..40]))); // 2880
        list.AddRange(Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid3x1cobblestone", "round2x1", "round2x2"]), ("material", moreRocks[..36]), ("wood", [.. Wood, "aged", "veryaged"]))); // 1512
        for (int f = 0; f < 300; f++)
            list.AddRange(Product($"expandedfoods:food{f}", EntryKind.Item, ("type", ["a", "b", "c"]), ("state", ["raw", "partbaked", "cooked", "charred"]))); // 3600
        for (int f = 0; f < 200; f++)
            list.AddRange(Product($"game:block{f}", EntryKind.Block, ("rock", Rock), ("cover", ["free", "snow"]))); // 5600
        for (int f = 0; f < 100; f++)
            list.AddRange(Product($"game:furniture{f}", EntryKind.Block, ("wood", Wood), ("side", ["north", "east"]))); // 2400
        for (int i = 0; i < 7600; i++)
            list.Add(E($"mod{i % 40}:thing{i}", EntryKind.Item));
        return list;
    }

    [Fact]
    public void FullScaleResolveIsFastAndDeterministic()
    {
        var entries = BigPack();
        Assert.InRange(entries.Count, 26_000, 28_000);

        var sw = Stopwatch.StartNew();
        var a = TidyEngine.Resolve(entries, WorldProperties);
        var resolveMs = sw.ElapsedMilliseconds;
        var b = TidyEngine.Resolve(entries, WorldProperties);

        Assert.Equal(a.Groups.Select(g => (g.Id, g.Representative, string.Join(",", g.Members))), b.Groups.Select(g => (g.Id, g.Representative, string.Join(",", g.Members))));
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(a.IsHidden(i), b.IsHidden(i));
            Assert.Equal(a.GroupOf(i), b.GroupOf(i));
        }
        Assert.True(resolveMs < 5000, $"resolve took {resolveMs} ms");

        var plan = Handbook.Build(a);
        Assert.Empty(plan.Issues);
        var stats = TidyStats.Compute(a);
        // 18 + 18 ore tiles, 3 door styles, 900 foods, 200 rock blocks, 100 furniture (side hidden), 7,600 plain things.
        Assert.Equal(18 + 18 + 3 + 900 + 200 + 100 + 7600, stats.EntriesAfter);
        Assert.Equal(1200, stats.HiddenByVariantRule);
    }

    [Fact]
    public void DisplayListPerKeystrokeIsFast()
    {
        var entries = BigPack();
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var builder = new DisplayListBuilder(r);
        int[] all = Enumerable.Range(0, entries.Count).ToArray();
        int[] some = all.Where(i => i % 3 == 0).ToArray();
        builder.Build(all); // warm
        builder.Build(some);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int runs = 50;
        for (int k = 0; k < runs; k++) builder.Build(k % 2 == 0 ? all : some);
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sw.Elapsed.TotalMilliseconds / runs < 20, $"{sw.Elapsed.TotalMilliseconds / runs:F2} ms per build");
        Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated over {runs} builds");
    }

    [Fact]
    public void ResultDoesNotDependOnOverrideFileFormatting()
    {
        var entries = Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper"])).ToList();
        var a = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse("""{"rules":[{"match":{"domain":"game","code":"ingot-*"},"group":{"id":"x"}}]}"""));
        var b = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse("""
            // same rules, commented and spaced
            { "rules": [ { "match": { "code": "ingot-*", "domain": "game" }, "group": { "id": "x" } }, ] }
            """));
        Assert.Equal(a.Groups.Select(g => g.Id), b.Groups.Select(g => g.Id));
    }

    [Fact]
    public void WildcardSemantics()
    {
        Assert.True(Wildcard.IsMatch("ore-*-galena-*", "ore-poor-galena-granite"));
        Assert.True(Wildcard.IsMatch("ore-*", "ore-"));
        Assert.True(Wildcard.IsMatch("*", ""));
        Assert.True(Wildcard.IsMatch("a*b*c", "aXXbYYbc"));
        Assert.False(Wildcard.IsMatch("ore-*-galena-*", "ore-poor-galena"));
        Assert.False(Wildcard.IsMatch("ore", "ore-x"));
        Assert.True(Wildcard.IsMatch("door-*", "door-sleek-windowed-oak")); // * spans dashes
        Assert.Equal("ore-", Wildcard.LiteralPrefix("ore-*-x"));
    }
}
