using SeraphHorizons.TidyVariants.Core;
using static SeraphHorizons.TidyVariants.Tests.Fx;

namespace SeraphHorizons.TidyVariants.Tests;

public class HandbookTests
{
    /// <summary>Every group with a pattern matches exactly its members among the visible codes of its domain.</summary>
    static void AssertExact(TidyResolution r, HandbookPlan plan)
    {
        foreach (var (g, pattern) in plan.PatternByGroup)
        {
            var group = r.Groups[g];
            var members = group.Members.Select(m => r.Entries[m].Code).ToHashSet();
            string domain = r.Entries[group.Members[0]].Domain;
            for (int i = 0; i < r.Entries.Count; i++)
            {
                var e = r.Entries[i];
                if (r.IsHidden(i) || e.Domain != domain) continue;
                Assert.Equal(members.Contains(e.Code), Wildcard.IsMatch(pattern, e.Path));
            }
        }
    }

    [Fact]
    public void OrePatternMatchesVanillasShape()
    {
        var r = Resolve(Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ["nativecopper", "galena", "quartz_nativegold"]), ("rock", ["andesite", "granite"])));
        var plan = Handbook.Build(r);
        Assert.Equal("ore-*-nativecopper-*", plan.PatternByGroup[r.GroupOf(r.Index("game:ore-poor-nativecopper-andesite"))]);
        Assert.Equal("ore-*-quartz_nativegold-*", plan.PatternByGroup[r.GroupOf(r.Index("game:ore-poor-quartz_nativegold-granite"))]);
        AssertExact(r, plan);
        Assert.Empty(plan.Issues);
        var e = plan.Entries.Single(x => x.Code == "game:ore-rich-galena-granite");
        Assert.Equal("ore-*-galena-*", e.GroupBy);
        Assert.False(e.Exclude);
    }

    [Fact]
    public void DoorPatternKeepsStyleLiteral()
    {
        var r = Resolve([
            .. Product("game:door", EntryKind.Block, ("style", ["solid", "sleek-windowed", "2x2gate"]), ("wood", ["oak", "pine", "aged"])),
        ]);
        var plan = Handbook.Build(r);
        Assert.Equal(["door-solid-*", "door-sleek-windowed-*", "door-2x2gate-*"], r.Groups.Select(g => plan.PatternByGroup[g.Index]));
        AssertExact(r, plan);
    }

    [Fact]
    public void HiddenEntriesAreExcludedAndIgnoredForExactness()
    {
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", Horizontal)));
        var plan = Handbook.Build(r);
        Assert.Equal(["game:chest-east", "game:chest-south", "game:chest-west"], plan.Entries.Where(e => e.Exclude).Select(e => e.Code));
        Assert.DoesNotContain(plan.Entries, e => e.Code == "game:chest-north");
    }

    [Fact]
    public void InexactPatternIsReportedNotThrown()
    {
        // A meaningful value that is a prefix of another with a dash: thing-a-* also matches thing-a-b-x.
        var r = TidyEngine.Resolve([
            E("game:thing", EntryKind.Item, ("kind", "a"), ("metal", "copper")),
            E("game:thing", EntryKind.Item, ("kind", "a"), ("metal", "iron")),
            E("game:thing", EntryKind.Item, ("kind", "a-b"), ("metal", "copper")),
            E("game:thing", EntryKind.Item, ("kind", "a-b"), ("metal", "iron")),
        ], WorldProperties);
        var plan = Handbook.Build(r);
        Assert.Equal(2, r.Groups.Count);
        Assert.Contains(plan.Issues, i => i.Kind == "groupby-inexact" && i.Message.Contains("thing/kind=a'"));
        Assert.Equal("thing-a-b-*", plan.PatternByGroup[1]);
        Assert.False(plan.PatternByGroup.ContainsKey(0));
        Assert.DoesNotContain(plan.Entries, e => e.Code == "game:thing-a-copper");
        AssertExact(r, plan);
    }

    [Fact]
    public void CrossFamilyGroupUsesPrefixSuffix()
    {
        const string rules = """{ "rules": [ { "match": { "domain": "game", "code": "plank*-*" }, "group": { "id": "shapes" } } ] }""";
        IEnumerable<CreativeEntry> shapes = [.. Product("game:plankstairs", EntryKind.Block, ("wood", ["oak", "pine"])), .. Product("game:plankslab", EntryKind.Block, ("wood", ["oak", "pine"]))];
        var r = Resolve(shapes, rules);
        var plan = Handbook.Build(r);
        Assert.Equal("planks*", plan.PatternByGroup[r.GroupById("shapes")!.Index]);
        AssertExact(r, plan);

        // A visible non-member under the same prefix makes it inexact.
        var r2 = Resolve(shapes.Append(E("game:planksaw", EntryKind.Block)), rules);
        var plan2 = Handbook.Build(r2);
        Assert.Empty(plan2.PatternByGroup);
        Assert.Contains(plan2.Issues, i => i.Kind == "groupby-inexact" && i.Message.Contains("planksaw"));
    }

    [Fact]
    public void AttributeStackGroupsOfOneCodeNeedNoPattern()
    {
        var entries = new[] { "oak", "pine" }
            .Select(w => new CreativeEntry("foodshelves:barshelf-normal-east", EntryKind.Block,
                [KeyValuePair.Create("type", "normal"), KeyValuePair.Create("side", "east")], null, Stack("wood=" + w, ("wood", w)))).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var plan = Handbook.Build(r);
        Assert.Single(r.Groups);
        Assert.Empty(plan.PatternByGroup);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void StacksOfOneCodeInDifferentGroupsConflict()
    {
        var entries = new List<CreativeEntry>();
        foreach (var code in new[] { "game:clutter-a", "game:clutter-b" })
            foreach (var t in new[] { "book-1", "bottle-1" })
                entries.Add(new CreativeEntry(code, EntryKind.Block, null, null, Stack(t, ("type", t))));
        var o = OverrideFile.Parse("""
            { "rules": [
              { "match": { "domain": "game", "code": "clutter-*", "attributes": { "type": "book-*" } }, "group": { "id": "books" } },
              { "match": { "domain": "game", "code": "clutter-*", "attributes": { "type": "bottle-*" } }, "group": { "id": "bottles" } },
            ] }
            """);
        var r = TidyEngine.Resolve(entries, WorldProperties, o);
        var plan = Handbook.Build(r);
        Assert.Equal(2, r.Groups.Count);
        Assert.Contains(plan.Issues, i => i.Kind == "groupby-conflict" && i.Message.StartsWith("game:clutter-a"));
        Assert.Empty(plan.PatternByGroup);
    }

    [Fact]
    public void BlockAndItemSharingACodeAreSeparateCollectibles()
    {
        // 1.22.7: block ore-{grade}-{type}-{rock} and item ore-{grade}-{ore}-{rock} share codes.
        var r = Resolve([
            .. Product("game:ore", EntryKind.Block, ("grade", ["poor", "rich"]), ("type", ["galena", "hematite"]), ("rock", ["granite", "chalk"])),
            .. Product("game:ore", EntryKind.Item, ("grade", ["poor", "rich"]), ("ore", ["galena", "hematite"]), ("rock", ["granite", "chalk"])),
        ]);
        Assert.Equal(4, r.Groups.Count);
        Assert.Equal(2, r.Families.Count(f => f.BasePath == "ore"));
        var plan = Handbook.Build(r);
        Assert.Empty(plan.Issues);
        var both = plan.Entries.Where(e => e.Code == "game:ore-poor-galena-granite").ToList();
        Assert.Equal([EntryKind.Block, EntryKind.Item], both.Select(e => e.Kind));
        Assert.All(both, e => Assert.Equal("ore-*-galena-*", e.GroupBy));
        Assert.NotEqual(both[0].Group, both[1].Group);

        // Verified across kinds, the identical patterns are no longer exact.
        var strict = Handbook.Build(r, verifyAcrossKinds: true);
        Assert.Contains(strict.Issues, i => i.Kind == "groupby-inexact");
    }

    [Fact]
    public void ShippedGroupsGetOnePattern()
    {
        var r = Resolve(new[] {
            E("game:dough", EntryKind.Item, [("type", "spelt")], null),
            E("game:dough", EntryKind.Item, [("type", "rye")], null),
        }.Select(e => new CreativeEntry(e.Code, e.Kind, e.Variant, e.Tabs, shippedGroupBy: ["dough-*"])));
        var plan = Handbook.Build(r);
        Assert.Equal("dough-*", plan.PatternByGroup[0]);
    }

    [Fact]
    public void RealisticMixIsExact()
    {
        var r = Resolve([
            .. Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", OreGraded), ("rock", Rock[..6])),
            .. Product("game:crystalizedore", EntryKind.Item, ("grade", Grades), ("ore", OreGraded[..3]), ("rock", Rock[..4])),
            .. Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid3x1cobblestone"]), ("material", Rock), ("wood", Wood)),
            .. Product("game:plankstairs", EntryKind.Block, ("wood", Wood), ("verticalorientation", ["up", "down"]), ("horizontalorientation", Horizontal), ("cover", ["free", "snow"])),
            .. Product("expandedfoods:breadedball", EntryKind.Item, ("type", ["flax", "rice", "rye"]), ("state", ["raw", "partbaked", "cooked", "charred"])),
        ]);
        var plan = Handbook.Build(r);
        Assert.Empty(plan.Issues);
        Assert.Equal(r.Groups.Count, plan.PatternByGroup.Count);
        AssertExact(r, plan);
        Assert.Equal("plankstairs-*-up-north-*", plan.PatternByGroup[r.GroupOf(r.Index("game:plankstairs-oak-up-north-free"))]);
    }
}
