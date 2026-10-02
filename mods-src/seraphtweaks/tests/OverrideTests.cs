using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using static SeraphHorizons.SeraphTweaks.TidyVariants.Tests.Fx;

namespace SeraphHorizons.SeraphTweaks.TidyVariants.Tests;

public class OverrideTests
{
    /// <summary>A commented example of every rule kind; the same content as Core/README.md's example.</summary>
    const string Example = """
        {
          "version": 1,
          "note": "Seraph Horizons pack overrides for Tidy Variants",

          // Replace a default preferred list (first present value wins for the representative).
          "preferred": { "rock": ["granite", "basalt"], "process": ["cooked", "perfect", "fired", "lit"] },

          // Extend the material lists: more worldproperties files, more values.
          "materials": { "color": { "properties": ["doorvariants:block/door-bricks"], "values": ["seafoam"] } },

          "rules": [
            // Clutter: one block, many attribute stacks; group the books.
            { "match": { "domain": "game", "code": "clutter", "attributes": { "type": "book-*" } },
              "group": { "id": "clutter-books", "title": "seraphtweaks:tidyvariants-group-clutter-books",
                         "representative": { "code": "clutter", "attributes": { "type": "book-big-closed" } } } },

            // One group per shield construction, with a title per construction.
            { "match": { "domain": "game", "code": "shield-*" },
              "group": { "id": "shield-{construction}", "by": ["construction"], "title": "seraphtweaks:tidyvariants-group-shield-{construction}",
                         "representative": "shield-*-oak" } },

            // Never group these.
            { "match": { "domain": "game", "code": "rock-*", "kind": "block" }, "ungroup": true },

            // Classification overrides for a type pattern; `split` is sugar for "meaningful".
            { "match": { "domain": "expandedfoods", "code": "pasta-*" }, "dimensions": { "type": "filler" } },
            { "match": { "domain": "game", "code": "ore-*" }, "split": ["grade"] },

            // Hide (true) or never hide (false).
            { "match": { "domain": "game", "code": "chest-*" }, "hide": false },
            { "match": { "domain": "game", "code": "creativeonly-*" }, "hide": true },

            // Prefer as representative of whatever group the entry lands in.
            { "match": { "domain": "game", "code": "ingot-iron" }, "prefer": true, "note": "ported from Handbook Declutterer (MIT)" },
          ],
        }
        """;

    [Fact]
    public void ExampleParses()
    {
        var o = OverrideFile.Parse(Example);
        Assert.Equal(8, o.Rules.Count);
        Assert.Equal([RuleAction.Group, RuleAction.Group, RuleAction.Ungroup, RuleAction.Dimensions, RuleAction.Dimensions, RuleAction.Unhide, RuleAction.Hide, RuleAction.Prefer],
            o.Rules.Select(r => r.Action));
        Assert.Equal(["granite", "basalt"], o.Preferred["rock"]);
        Assert.Equal(DimensionClass.Meaningful, o.Rules[4].Dimensions!["grade"].Class);
        Assert.Equal(DimensionClass.Filler, o.Rules[3].Dimensions!["type"].Class);
        Assert.Equal("ported from Handbook Declutterer (MIT)", o.Rules[7].Note);
        Assert.Equal(EntryKind.Block, o.Rules[2].Match.Kind);
        Assert.Equal("*", o.Rules[1].Group!.Representative!.Domain);
    }

    [Theory]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "grop": {} } ] }""", "rules[0]: unknown field 'grop'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "cod": "x" }, "ungroup": true } ] }""", "rules[0].match: unknown field 'cod'")]
    [InlineData("""{ "rules": [ { "match": { "code": "x" }, "ungroup": true } ] }""", "rules[0].match: 'domain' is required")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game" }, "ungroup": true } ] }""", "rules[0].match: 'code' is required")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "game:x" }, "ungroup": true } ] }""", "rules[0].match.code: give the domain")]
    [InlineData("""{ "rules": [ { "ungroup": true } ] }""", "rules[0]: 'match' is required")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" } } ] }""", "rules[0]: needs exactly one of")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "ungroup": true, "hide": true } ] }""", "rules[0]: has ungroup and hide")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "ungroup": false } ] }""", "rules[0].ungroup: must be true")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "hide": "yes" } ] }""", "rules[0].hide: must be true")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x", "kind": "entity" }, "ungroup": true } ] }""", "rules[0].match.kind: must be 'block' or 'item'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "group": { "title": "t" } } ] }""", "rules[0].group: 'id' is required")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "group": { "id": "a-{wood}" } } ] }""", "rules[0].group.id: placeholder '{wood}' must be listed in 'by'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "group": { "id": "a", "by": ["wood"] } } ] }""", "rules[0].group.id: must contain '{wood}'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "group": { "id": "a", "colour": "red" } } ] }""", "rules[0].group: unknown field 'colour'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "dimensions": { "type": "fill" } } ] }""", "rules[0].dimensions.type: unknown class 'fill'")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "dimensions": {} } ] }""", "rules[0].dimensions: must name at least one dimension")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "split": [] } ] }""", "rules[0].split: must list at least one dimension")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "a" }, "group": { "id": "g" } }, { "match": { "domain": "game", "code": "b" }, "group": { "id": "g" } } ] }""", "rules[1]: group id 'g' is already used by rules[0]")]
    [InlineData("""{ "preferred": { "stone": ["granite"] } }""", "preferred: unknown key 'stone'")]
    [InlineData("""{ "materials": { "stone": {} } }""", "materials: unknown material 'stone'")]
    [InlineData("""{ "materials": { "rock": { "vals": [] } } }""", "materials.rock: unknown field 'vals'")]
    [InlineData("""{ "version": 2 }""", "version: must be 1")]
    [InlineData("""{ "groups": [] }""", "unknown field 'groups'")]
    [InlineData("""[]""", "the file must be a JSON object")]
    [InlineData("""{ "rules": [ """, "invalid JSON")]
    public void ParseErrors(string json, string expected)
    {
        var ex = Assert.Throws<OverrideFormatException>(() => OverrideFile.Parse(json));
        Assert.Contains(ex.Errors, e => e.StartsWith(expected, StringComparison.Ordinal) || e.Contains(expected));
    }

    [Fact]
    public void AllErrorsAreReportedAtOnce()
    {
        var ok = OverrideFile.TryParse("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "bad": 1 }, { "nope": 2 } ], "extra": 3 }""", out var file, out var errors);
        Assert.False(ok);
        Assert.Null(file);
        Assert.Contains(errors, e => e.StartsWith("rules[0]:"));
        Assert.Contains(errors, e => e.StartsWith("rules[1]:"));
        Assert.Contains(errors, e => e == "unknown field 'extra'");
    }

    [Fact]
    public void GroupRuleWithAttributesAndRepresentative()
    {
        var entries = new[] { "army-tool", "book-big-closed", "book-small-open", "book-pile", "bottle" }
            .Select(t => new CreativeEntry("game:clutter", EntryKind.Block, null, ["decorative"], Stack("type=" + t, ("type", t)))).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse("""
            { "rules": [ { "match": { "domain": "game", "code": "clutter", "attributes": { "type": "book-*" } },
                           "group": { "id": "clutter-books", "title": "seraphtweaks:tidyvariants-group-clutter-books",
                                      "representative": { "code": "clutter", "attributes": { "type": "book-pile" } } } } ] }
            """));
        // The other stacks (army-tool, bottle) form the automatic group of the collectible.
        Assert.Equal(["clutter-books", "auto:game:clutter"], r.Groups.Select(x => x.Id).OrderBy(x => x == "auto:game:clutter"));
        var g = r.Groups.Single(x => x.Id == "clutter-books");
        Assert.False(g.IsAutomatic);
        Assert.Equal(0, g.RuleIndex);
        Assert.Equal("seraphtweaks:tidyvariants-group-clutter-books", g.Title);
        Assert.Equal([1, 2, 3], g.Members);
        Assert.Equal(3, g.Representative);
        Assert.Same(g, r.GroupById("clutter-books"));
    }

    [Fact]
    public void GroupByCreatesOneGroupPerValueWithFilledTitle()
    {
        var r = Resolve(Product("game:shield", EntryKind.Item, ("construction", ["woodmetal", "metal"]), ("wood", ["oak", "pine"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "shield-*" }, "group": { "id": "shield-{construction}", "by": ["construction"], "title": "lang-{construction}", "representative": "shield-*-pine" } } ] }""");
        Assert.Equal(["shield-woodmetal", "shield-metal"], r.Groups.Select(g => g.Id));
        Assert.Equal(["lang-woodmetal", "lang-metal"], r.Groups.Select(g => g.Title));
        Assert.Equal("game:shield-metal-pine", r.Entries[r.Groups[1].Representative].Code);
    }

    [Fact]
    public void OverrideGroupsCanSpanFamilies()
    {
        var r = Resolve([.. Product("game:plankstairs", EntryKind.Block, ("wood", ["oak", "pine"])), .. Product("game:plankslab", EntryKind.Block, ("wood", ["oak", "pine"]))],
            """{ "rules": [ { "match": { "domain": "game", "code": "plank*" }, "group": { "id": "plank-shapes" } } ] }""");
        var g = Assert.Single(r.Groups);
        Assert.Equal(4, g.Members.Count);
        Assert.Null(g.Family);
    }

    [Fact]
    public void FirstMatchingGroupRuleWins()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["copper", "tin", "iron", "gold"])), """
            { "rules": [
              { "match": { "domain": "game", "code": "ingot-copper" }, "ungroup": true },
              { "match": { "domain": "game", "code": "ingot-*" }, "group": { "id": "all-ingots" } },
              { "match": { "domain": "game", "code": "ingot-tin" }, "group": { "id": "never" } },
            ] }
            """);
        Assert.Equal(-1, r.GroupOf(r.Index("game:ingot-copper")));
        Assert.Equal("all-ingots", Assert.Single(r.Groups).Id);
        Assert.Equal(3, r.Groups[0].Members.Count);
        Assert.Contains(r.Issues, i => i.Kind == "rule-unused" && i.Message.StartsWith("rules[2]"));
    }

    [Fact]
    public void UngroupMakesPlainEntries()
    {
        var r = Resolve(Product("game:rock", EntryKind.Block, ("rock", ["granite", "andesite"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "rock-*", "kind": "block" }, "ungroup": true } ] }""");
        Assert.Empty(r.Groups);
    }

    [Fact]
    public void KindRestrictsAMatch()
    {
        var r = Resolve(Product("game:rock", EntryKind.Block, ("rock", ["granite", "andesite"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "rock-*", "kind": "item" }, "ungroup": true } ] }""");
        Assert.Single(r.Groups);
    }

    [Fact]
    public void SplitMakesAFillerDimensionMeaningful()
    {
        var r = Resolve(Product("game:ore", EntryKind.Item, ("grade", ["poor", "rich"]), ("ore", ["galena"]), ("rock", ["granite", "chalk"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "ore-*" }, "split": ["grade"] } ] }""");
        Assert.Equal(["auto:game:ore/grade=poor/ore=galena", "auto:game:ore/grade=rich/ore=galena"], r.Groups.Select(g => g.Id));
        var dims = r.DimensionsOf(0);
        Assert.Equal(DimensionClass.Meaningful, dims[0].Class);
        Assert.Equal("override rules[0]", dims[0].Reason);
    }

    [Fact]
    public void DimensionsMakesAMeaningfulDimensionFiller()
    {
        // ExpandedFoods pasta type[dough,flat,cut,cooked,dried,charred] already is process state;
        // use foodoil type[flax,rice,...] which is meaningful by default.
        var r = Resolve(Product("expandedfoods:foodoilportion", EntryKind.Item, ("type", ["flax", "rice", "olive"])),
            """{ "rules": [ { "match": { "domain": "expandedfoods", "code": "foodoilportion-*" }, "dimensions": { "type": "filler" } } ] }""");
        Assert.Single(r.Groups);
        Assert.Equal(DimensionClass.Filler, r.DimensionsOf(0)[0].Class);
    }

    [Fact]
    public void DimensionsCanTargetAttributes()
    {
        var entries = new[] { "a", "b" }
            .Select(t => new CreativeEntry("game:clutter", EntryKind.Block, null, null, Stack("type=" + t, ("type", t)))).ToList();
        var o = OverrideFile.Parse("""{ "rules": [ { "match": { "domain": "game", "code": "clutter" }, "dimensions": { "attr:type": "filler" } } ] }""");
        Assert.Single(TidyEngine.Resolve(entries, WorldProperties, o).Groups);
        // A bare name finds the attribute too.
        var o2 = OverrideFile.Parse("""{ "rules": [ { "match": { "domain": "game", "code": "clutter" }, "dimensions": { "type": "filler" } } ] }""");
        Assert.Single(TidyEngine.Resolve(entries, WorldProperties, o2).Groups);
    }

    [Fact]
    public void DomainNotModid()
    {
        // woodenshuttersandmore ships its blocks under the slidingwoodenshutters: domain.
        var r = Resolve(Product("slidingwoodenshutters:shutter", EntryKind.Block, ("wood", ["oak", "pine"])),
            """{ "rules": [ { "match": { "domain": "woodenshuttersandmore", "code": "*" }, "ungroup": true } ] }""");
        Assert.Single(r.Groups);
        Assert.Contains(r.Issues, i => i.Kind == "rule-unused");
    }

    [Fact]
    public void UnmatchedRepresentativeIsReported()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["copper", "tin"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "ingot-*" }, "group": { "id": "ingots", "representative": "ingot-unobtainium" } } ] }""");
        Assert.Contains(r.Issues, i => i.Kind == "representative-unmatched");
        Assert.Equal("game:ingot-copper", r.Entries[r.Groups[0].Representative].Code);
    }

    [Fact]
    public void GroupRulesDoNotPullInHiddenEntries()
    {
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", Horizontal)).Append(E("game:chest-labeled", EntryKind.Block)),
            """{ "rules": [ { "match": { "domain": "game", "code": "chest-*" }, "group": { "id": "chests" } } ] }""");
        var g = Assert.Single(r.Groups);
        Assert.Equal(["game:chest-north", "game:chest-labeled"], r.Codes(g.Members));
    }
}
