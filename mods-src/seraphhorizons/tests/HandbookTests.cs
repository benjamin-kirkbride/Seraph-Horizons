using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class HandbookTests
{
    /// <summary>Every group with a pattern matches exactly its members among the visible codes of its domain, with
    /// the game's matcher. With <paramref name="acrossKinds"/>, both kinds count (a same-code collectible of the other
    /// kind is tolerated); otherwise only the group's kind.</summary>
    static void AssertExact(TidyResolution r, HandbookPlan plan, bool acrossKinds = false)
    {
        foreach (var (g, pattern) in plan.PatternByGroup)
        {
            var group = r.Groups[g];
            var e0 = r.Entries[group.Members[0]];
            var members = group.Members.Select(m => (r.Entries[m].Kind, r.Entries[m].Code)).ToHashSet();
            var memberCodes = members.Select(m => m.Code).ToHashSet();
            for (int i = 0; i < r.Entries.Count; i++)
            {
                var e = r.Entries[i];
                if (r.IsHidden(i) || e.Domain != e0.Domain || (!acrossKinds && e.Kind != e0.Kind)) continue;
                bool hit = GroupByMatcher.IsMatch(pattern, e.Path);
                if (hit && !members.Contains((e.Kind, e.Code)) && memberCodes.Contains(e.Code)) continue;
                Assert.True(members.Contains((e.Kind, e.Code)) == hit, $"{pattern} vs {e.Kind} {e.Code}");
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
    public void NoExactWildcardGivesARegex()
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
        Assert.Empty(plan.Issues);
        Assert.Equal("@thing-a-[^-]*", plan.PatternByGroup[0]);
        Assert.Equal(GroupByPatternKind.Structured, plan.PatternKindByGroup[0]);
        Assert.Equal("thing-a-b-*", plan.PatternByGroup[1]);
        Assert.Equal(GroupByPatternKind.Wildcard, plan.PatternKindByGroup[1]);
        Assert.Equal("@thing-a-[^-]*", plan.Entries.Single(e => e.Code == "game:thing-a-copper").GroupBy);
        AssertExact(r, plan);
    }

    [Fact]
    public void AttributeStackGroupsGetNoRegex()
    {
        // The handbook matches an IHandbookGrouping stack by path-attr..., which a code regex would miss.
        var r = TidyEngine.Resolve([
            new CreativeEntry("game:thing-a-copper", EntryKind.Item, [KeyValuePair.Create("kind", "a"), KeyValuePair.Create("metal", "copper")], null, Stack("t=1", ("t", "1"))),
            new CreativeEntry("game:thing-a-iron", EntryKind.Item, [KeyValuePair.Create("kind", "a"), KeyValuePair.Create("metal", "iron")], null, Stack("t=1", ("t", "1"))),
            E("game:thing", EntryKind.Item, ("kind", "a-b"), ("metal", "copper")),
        ], WorldProperties);
        var plan = Handbook.Build(r);
        var g = r.GroupOf(r.Index("game:thing-a-copper"));
        Assert.False(plan.PatternByGroup.ContainsKey(g));
        Assert.Contains(plan.Issues, i => i.Kind == "groupby-inexact" && i.Message.Contains("thing-a-b-copper"));
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

        // A visible non-member under the same prefix rules out the wildcard: a regex keeps it out.
        var r2 = Resolve(shapes.Append(E("game:planksaw", EntryKind.Block)), rules);
        var plan2 = Handbook.Build(r2);
        Assert.Equal("@planks(lab|tairs)-[^-]*", plan2.PatternByGroup[r2.GroupById("shapes")!.Index]);
        Assert.Empty(plan2.Issues);
        AssertExact(r2, plan2);
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

        // Verified across kinds: the handbook compares codes only, so no pattern can tell the block from the item with
        // the same code. That residue is tolerated (the pattern is still written) and reported.
        var strict = Handbook.Build(r, verifyAcrossKinds: true);
        Assert.DoesNotContain(strict.Issues, i => i.Kind == "groupby-inexact");
        Assert.Equal(4, strict.Issues.Count(i => i.Kind == "groupby-shared-code"));
        Assert.All(strict.PatternByGroup.Values, p => Assert.Matches("^ore-\\*-(galena|hematite)-\\*$", p));
        AssertExact(r, strict, acrossKinds: true);
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

/// <summary>
/// <see cref="GroupByMatcher"/> against the decompiled 1.22.7 <c>WildcardUtil.fastMatch</c>, and <see cref="GroupByRegex"/>
/// on the shapes of real groups that no <c>*</c> wildcard matched exactly (the Atlas scenario
/// <c>TidyVariantsHandbookScenarios</c> checks the real pack with the real matcher).
/// </summary>
public class GroupByRegexTests
{
    [Theory]
    // '*' wildcard: spans dashes, may match nothing, no other special character, ASCII case ignored.
    [InlineData("ore-*-galena-*", "ore-poor-galena-granite", true)]
    [InlineData("door-*", "door-sleek-windowed-oak", true)]
    [InlineData("ore-*", "ore-", true)]
    [InlineData("*", "", true)]
    [InlineData("ore-?", "ore-x", false)]
    [InlineData("a.b", "axb", false)]
    [InlineData("ORE-*", "ore-x", true)]
    [InlineData("", "", false)]
    // '@': a regex wrapped as ^…$, case-sensitive, so a top-level alternation binds to the anchors.
    [InlineData("@ore-.*", "ore-x", true)]
    [InlineData("@ore-.*", "xore-x", false)]
    [InlineData("@ore", "ore-x", false)]
    [InlineData("@ORE-.*", "ore-x", false)]
    [InlineData("@a|b", "a-anything", true)]
    [InlineData("@a|b", "anything-b", true)]
    [InlineData("@(a|b)", "a-anything", false)]
    [InlineData("@", "", true)]
    [InlineData("@hide-raw-[^-]*", "hide-raw-bear-black-complete", false)]
    [InlineData("@hide-raw-[^-]*", "hide-raw-small", true)]
    public void MatcherMirrorsTheGame(string pattern, string path, bool expected) =>
        Assert.Equal(expected, GroupByMatcher.IsMatch(pattern, path));

    /// <summary>Builds from sorted members, checked like <see cref="Handbook"/> does: members and the other visible codes.</summary>
    static Candidate? Build(string[] members, params string[] others)
    {
        var sorted = members.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var c = GroupByRegex.Build(sorted, c => members.All(m => GroupByMatcher.IsMatch(c.Pattern, m))
                                              && !others.Any(o => GroupByMatcher.IsMatch(c.Pattern, o)));
        if (c is { } x)
        {
            Assert.DoesNotContain(':', x.Pattern);
            Assert.DoesNotContain('{', x.Pattern);
            Assert.False(TopLevelBar(x.Pattern), x.Pattern);
            Assert.All(members.Concat(others), p => Assert.True(!GroupByMatcher.IsMatch(x.Pattern, p) || p.StartsWith(x.Prefix, StringComparison.Ordinal), $"{x.Prefix} for {p}"));
            // Deterministic: the same members in another order give the same pattern.
            var again = GroupByRegex.Build(sorted.AsEnumerable().Reverse().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                c => members.All(m => GroupByMatcher.IsMatch(c.Pattern, m)) && !others.Any(o => GroupByMatcher.IsMatch(c.Pattern, o)));
            Assert.Equal(x, again);
        }
        return c;
    }

    static bool TopLevelBar(string p)
    {
        int depth = 0;
        for (int i = 1; i < p.Length; i++)
        {
            if (p[i] == '\\') { i++; continue; }
            if (p[i] == '(') depth++;
            else if (p[i] == ')') depth--;
            else if (p[i] == '|' && depth == 0) return true;
        }
        return false;
    }

    [Fact]
    public void DisplayCasesAcrossTwoTypes()
    {
        // Shipped *displaycase-aged* on displaycase-aged{n} and talldisplaycase-aged{n}: their prefix*suffix is '*'.
        var c = Build([.. Enumerable.Range(1, 5).SelectMany(n => new[] { $"displaycase-aged{n}", $"talldisplaycase-aged{n}" })],
            "agedfirewood", "agedstonebricks", "displaycase-oak1", "talldisplaycase-oak1");
        Assert.Equal("@(|tall)displaycase-aged(1|2|3|4|5)", c!.Value.Pattern);
    }

    [Fact]
    public void QuartzCrystalsCrossTypeGroup()
    {
        var c = Build(["amethyst", "clearquartz", "rosequartz", "smokyquartz"], "agedfirewood", "amethystbrick", "quartz");
        Assert.Equal("@(amethyst|clearquartz|rosequartz|smokyquartz)", c!.Value.Pattern);
    }

    [Fact]
    public void HidesOfOneTypeExcludeTheLongerAnimalHides()
    {
        // hide-raw-{size} vs hide-raw-{animal}-{color}-{part}: hide-raw-* catches both, [^-]* only the sizes.
        var c = Build(["hide-raw-small", "hide-raw-medium", "hide-raw-large", "hide-raw-huge"],
            "hide-raw-bear-black-complete", "hide-soaked-small", "hide-raw");
        Assert.Equal("@hide-raw-[^-]*", c!.Value.Pattern);
        Assert.Equal("hide-raw-", c.Value.Prefix);
        Assert.Equal(GroupByPatternKind.Structured, c.Value.Kind);
    }

    [Fact]
    public void ChickenEggsKeepTheirAlternationWhenSiblingsShareTheShape()
    {
        var c = Build(["egg-chicken-1", "egg-chicken-2", "egg-chicken-3", "egg-chicken-broken"], "egg-chicken-boiled", "egg-chicken-raw");
        Assert.Equal("@egg-chicken-(1|2|3|broken)", c!.Value.Pattern);
    }

    [Fact]
    public void UngradedOreItemsExcludeTheOreBlocks()
    {
        var c = Build(["ore-alum", "ore-borax", "ore-sulfur"], "ore-alum-arkose", "ore-poor-galena-granite");
        Assert.Equal("@ore-[^-]*", c!.Value.Pattern);
    }

    [Fact]
    public void SparseProductIsEnumeratedAsATrie()
    {
        // Ilmenite blocks: not every grade in every rock, and the ore item has the missing codes.
        var c = Build(["ore-poor-ilmenite-a", "ore-poor-ilmenite-b", "ore-medium-ilmenite-a", "ore-medium-ilmenite-b", "ore-rich-ilmenite-a"],
            "ore-rich-ilmenite-b");
        Assert.Equal("@ore(-(medium|poor)-ilmenite-(a|b)|-rich-ilmenite-a)", c!.Value.Pattern);
        Assert.Equal(GroupByPatternKind.Enumerated, c.Value.Kind);
    }

    [Fact]
    public void EnumeratedIsExactByConstruction()
    {
        string[] members = ["a.b-1", "a.b-2", "a.b", "x-y-z", "x-y", "c(d)-e"];
        var c = GroupByRegex.Enumerated(members.OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.All(members, m => Assert.True(GroupByMatcher.IsMatch(c.Pattern, m), $"{c.Pattern} vs {m}"));
        foreach (var o in new[] { "axb-1", "a.b-", "a.b-3", "x", "x-y-z-w", "x-z", "c(d)", "cd-e", "" })
            Assert.False(GroupByMatcher.IsMatch(c.Pattern, o), $"{c.Pattern} vs {o}");
    }

    [Fact]
    public void NoRegexWhenNothingIsExact()
    {
        // Only possible when the check sees a difference the matcher can't (here: the same code twice).
        Assert.Null(Build(["a-1", "a-2"], "a-1"));
    }
}
