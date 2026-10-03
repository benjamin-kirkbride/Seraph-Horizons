using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

/// <summary>Shapes found in the asset review of the pack (vanilla 1.22.7 and the pack's mods).</summary>
public class AssetReviewTests
{
    static CreativeEntry WithShipped(CreativeEntry e, params string[] groupBy) =>
        new(e.Code, e.Kind, e.Variant, e.Tabs, e.Stack, e.PropertySources, groupBy);

    // ---- 1. Blocks and items sharing a code -------------------------------------------------

    [Fact]
    public void OverrideKindSelectsBlockOrItemOfTheSameCode()
    {
        List<CreativeEntry> entries =
        [
            .. Product("game:ore", EntryKind.Block, ("grade", ["poor", "rich"]), ("type", ["galena"]), ("rock", ["granite", "chalk"])),
            .. Product("game:ore", EntryKind.Item, ("grade", ["poor", "rich"]), ("ore", ["galena"]), ("rock", ["granite", "chalk"])),
        ];
        var r = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse(
            """{ "rules": [ { "match": { "domain": "game", "code": "ore-*", "kind": "item" }, "split": ["grade"] } ] }"""));
        // The block stays one group (type=galena); the item splits per grade.
        Assert.Equal(["auto:game:ore/type=galena", "auto:game:ore/grade=poor/ore=galena", "auto:game:ore/grade=rich/ore=galena"], r.Groups.Select(g => g.Id));
        Assert.All(r.Groups[0].Members, m => Assert.Equal(EntryKind.Block, r.Entries[m].Kind));
    }

    // ---- 2. Open/closed by value; `state` as a shape ----------------------------------------

    [Fact]
    public void ShuttersHideOpenedAndKeepShapes()
    {
        // slidingwoodenshutters 1x1woodenshutters: side × status[opened,closed] × state[left,half,...] × wood
        var r = Resolve(Product("slidingwoodenshutters:1x1woodenshutters", EntryKind.Block,
            ("side", ["north", "east"]), ("status", ["opened", "closed"]), ("state", ["left", "half", "right"]), ("wood", ["oak", "pine"])));
        var visible = Enumerable.Range(0, r.Entries.Count).Where(i => !r.IsHidden(i)).ToList();
        Assert.Equal(6, visible.Count);
        Assert.All(visible, i => Assert.StartsWith("slidingwoodenshutters:1x1woodenshutters-north-closed-", r.Entries[i].Code));
        Assert.Equal(["state=left", "state=half", "state=right"], r.Groups.Select(g => g.Id.Split('/')[1]));
        Assert.Equal(DimensionClass.OpenClosedState, r.Families[0].Dimensions[1].Class);
        Assert.Equal(DimensionClass.Meaningful, r.Families[0].Dimensions[2].Class);
    }

    [Fact]
    public void OverrideCanForceAClass()
    {
        var r = Resolve(Product("slidingwoodenshutters:woodenshutters", EntryKind.Block, ("state", ["left", "right"]), ("wood", ["oak", "pine"])),
            """{ "rules": [ { "match": { "domain": "slidingwoodenshutters", "code": "woodenshutters-*" }, "dimensions": { "state": "orientation" } } ] }""");
        Assert.Equal(2, Enumerable.Range(0, 4).Count(i => !r.IsHidden(i)));
        Assert.Single(r.Groups);
    }

    // ---- 3. Decorated material values -------------------------------------------------------

    [Fact]
    public void TankardWoodsWithTrailingDotsCollapse()
    {
        var r = Resolve(Product("tankardsandgoblets:tankard-woodtype", EntryKind.Block,
            ("wood", ["pine.", "oak.", "birch."]), ("binding", ["leather-plain", "leather-red"])));
        Assert.Equal(2, r.Groups.Count); // one per binding
        Assert.Equal("tankardsandgoblets:tankard-woodtype-oak.-leather-plain", r.Entries[r.Groups[0].Representative].Code);
    }

    // ---- 4. Clay colours in generic `type` --------------------------------------------------

    [Fact]
    public void ClayColorListAndPerTypeFiller()
    {
        // vanilla burnedbrick type[blue,fire,red]; Alchemy herbrackmold {clay}-{raw|fired}
        var o = """
            { "lists": { "claycolor": { "values": ["blue", "fire", "red", "black", "brown", "cream", "gray", "orange", "tan"], "preferred": ["red"] } },
              "rules": [ { "match": { "domain": "alchemy", "code": "herbrackmold-*" }, "dimensions": { "clay": "claycolor" } } ] }
            """;
        var r = Resolve([
            .. Product("game:burnedbrick", EntryKind.Item, ("type", ["blue", "fire"])),
            .. Product("alchemy:herbrackmold", EntryKind.Block, ("clay", ["blue", "weird"]), ("state", ["raw", "fired"])),
        ], o);
        var brick = r.GroupOf("game:burnedbrick-blue");
        Assert.Equal("claycolor", r.DimensionsOf(r.Index("game:burnedbrick-blue"))[0].List);
        Assert.Equal(2, brick.Members.Count);
        var mold = r.GroupOf("alchemy:herbrackmold-blue-raw");
        Assert.Equal(4, mold.Members.Count);
        Assert.Equal("claycolor", r.DimensionsOf(r.Index("alchemy:herbrackmold-weird-raw"))[0].List);
    }

    [Theory]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": "x" }, "dimensions": { "type": "claycolour" } } ] }""", "rules[0].dimensions.type: unknown class 'claycolour'")]
    [InlineData("""{ "lists": { "rock": { "values": ["a"] } } }""", "lists.rock: 'rock' is a built-in name")]
    [InlineData("""{ "lists": { "x": {} } }""", "lists.x: needs 'values' or 'properties'")]
    [InlineData("""{ "lists": { "x": { "values": ["a"], "prefered": [] } } }""", "lists.x: unknown field 'prefered'")]
    [InlineData("""{ "honorShippedGroupBy": "yes" }""", "honorShippedGroupBy: must be true or false")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": [] }, "ungroup": true } ] }""", "rules[0].match.code: must list at least one pattern")]
    [InlineData("""{ "rules": [ { "match": { "domain": "game", "code": ["a", "game:b"] }, "ungroup": true } ] }""", "rules[0].match.code: give the domain")]
    public void NewParseErrors(string json, string expected)
    {
        var ex = Assert.Throws<OverrideFormatException>(() => OverrideFile.Parse(json));
        Assert.Contains(ex.Errors, e => e.StartsWith(expected, StringComparison.Ordinal));
    }

    // ---- 5. Shipped handbook groupBy --------------------------------------------------------

    static List<CreativeEntry> BreadedVegetables() =>
        Product("expandedfoods:breadedvegetable", EntryKind.Item, ("type", ["flax", "rice"]), ("veggie", ["carrot", "onion", "turnip"]), ("state", ["raw", "cooked"]))
            .Select(e => WithShipped(e, "breadedvegetable-*")).ToList();

    [Fact]
    public void ShippedGroupByMergesAutomaticGroups()
    {
        // Auto derivation alone gives one group per type × veggie; EF's shipped groupBy says one page per code.
        var r = TidyEngine.Resolve(BreadedVegetables(), WorldProperties);
        var g = Assert.Single(r.Groups);
        Assert.Equal(GroupSource.Shipped, g.Source);
        Assert.True(g.IsAutomatic);
        Assert.Null(g.Title);
        Assert.Equal("breadedvegetable-*", g.ShippedPattern);
        Assert.Equal("groupby:item:expandedfoods:breadedvegetable-*", g.Id);
        Assert.Equal(12, g.Members.Count);
        Assert.Equal(Enumerable.Range(0, 12), g.Members);
        Assert.Equal("expandedfoods:breadedvegetable-flax-carrot-cooked", r.Entries[g.Representative].Code);

        var stats = TidyStats.Compute(r);
        Assert.Equal(1, stats.ShippedGroups);
        Assert.Equal("breadedvegetable-*", Assert.Single(stats.ShippedGroupList).ShippedPattern);
    }

    [Fact]
    public void ShippedGroupByIsSwitchable()
    {
        var off = TidyEngine.Resolve(BreadedVegetables(), WorldProperties, null, new ResolveOptions { HonorShippedGroupBy = false });
        Assert.Equal(6, off.Groups.Count);
        Assert.All(off.Groups, g => Assert.Equal(GroupSource.Automatic, g.Source));

        var offByFile = TidyEngine.Resolve(BreadedVegetables(), WorldProperties, OverrideFile.Parse("""{ "honorShippedGroupBy": false }"""));
        Assert.Equal(6, offByFile.Groups.Count);

        // Options beat the file.
        var on = TidyEngine.Resolve(BreadedVegetables(), WorldProperties, OverrideFile.Parse("""{ "honorShippedGroupBy": false }"""), new ResolveOptions { HonorShippedGroupBy = true });
        Assert.Single(on.Groups);
    }

    [Fact]
    public void OverridesBeatShippedGroupBy()
    {
        var r = TidyEngine.Resolve(BreadedVegetables(), WorldProperties, OverrideFile.Parse("""
            { "rules": [ { "match": { "domain": "expandedfoods", "code": "breadedvegetable-*-onion-*" }, "group": { "id": "onions" } } ] }
            """));
        Assert.Equal(["groupby:item:expandedfoods:breadedvegetable-*", "onions"], r.Groups.Select(g => g.Id));
        Assert.Equal(8, r.Groups[0].Members.Count);
        Assert.Equal(4, r.Groups[1].Members.Count);
    }

    [Fact]
    public void ShippedPatternThatAddsNothingKeepsTheAutomaticGroup()
    {
        // vanilla ore item: groupBy ["ore-*-{ore}-*"], the same split auto derivation makes.
        var r = TidyEngine.Resolve(Product("game:ore", EntryKind.Item, ("grade", ["poor", "rich"]), ("ore", ["galena", "hematite"]), ("rock", ["granite", "chalk"]))
            .Select(e => WithShipped(e, "ore-*-{ore}-*")).ToList(), WorldProperties);
        Assert.Equal(["auto:game:ore/ore=galena", "auto:game:ore/ore=hematite"], r.Groups.Select(g => g.Id));
        Assert.All(r.Groups, g => Assert.Equal(GroupSource.Automatic, g.Source));
    }

    [Fact]
    public void ShippedGroupByJoinsSingletonsAndOtherTypes()
    {
        // vanilla flowerpot: `flowerpot-{color}` plain and glazed `flowerpot-glazed-{color}` -- one shipped pattern
        // `flowerpot-*` on the plain ones also pulls the glazed family in. Meaningful-only entries become one group.
        List<CreativeEntry> entries =
        [
            .. Product("game:flowerpot", EntryKind.Block, ("type", ["burned", "raw"])).Select(e => WithShipped(e, "flowerpot-*")),
            .. Product("game:flowerpot-glazed", EntryKind.Block, ("glaze", ["celadon", "ochre"])),
            E("game:planter", EntryKind.Block),
        ];
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var g = Assert.Single(r.Groups);
        Assert.Equal(4, g.Members.Count);
        Assert.Null(g.Family);
    }

    [Fact]
    public void ShippedGroupByStaysWithinItsKind()
    {
        List<CreativeEntry> entries =
        [
            .. Product("game:dough", EntryKind.Item, ("type", ["spelt", "rye"])).Select(e => WithShipped(e, "dough-*")),
            .. Product("game:dough", EntryKind.Block, ("type", ["spelt", "rye"])),
        ];
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var g = Assert.Single(r.Groups);
        Assert.All(g.Members, m => Assert.Equal(EntryKind.Item, r.Entries[m].Kind));
    }

    [Fact]
    public void ShippedPatternsWithDomainAndHiddenEntries()
    {
        var entries = Product("game:chest", EntryKind.Block, ("side", Horizontal))
            .Concat(Product("game:trunk", EntryKind.Block, ("side", Horizontal)))
            .Select(e => WithShipped(e, "game:*-north")).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties);
        // Hidden orientations never join; the two canonical members do.
        var g = Assert.Single(r.Groups);
        Assert.Equal(["game:chest-north", "game:trunk-north"], r.Codes(g.Members));
    }

    // ---- 6. Cross-type and attribute-family overrides ---------------------------------------

    [Fact]
    public void CrossTypeGroupWithSeveralCodePatterns()
    {
        var entries = new[] { "amethyst", "clearquartz", "rosequartz", "smokyquartz", "olivine" }
            .SelectMany(t => Product("game:" + t, EntryKind.Block, ("rock", ["granite", "andesite"]))).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse("""
            { "rules": [ { "match": { "domain": "game", "code": ["amethyst-*", "clearquartz-*", "rosequartz-*", "smokyquartz-*"] },
                           "group": { "id": "quartz", "title": "seraphhorizons:tidyvariants-group-game-quartz" } } ] }
            """));
        Assert.Equal(["quartz", "auto:game:olivine"], r.Groups.Select(g => g.Id));
        Assert.Equal(8, r.Groups[0].Members.Count);
        Assert.Equal("game:amethyst-granite", r.Entries[r.Groups[0].Representative].Code);
    }

    static List<CreativeEntry> Clutter() =>
        new[] { "book-big-closed", "book-small-open", "bottle-1", "bottle-2", "crate", "army-tool" }
            .Select(t => new CreativeEntry("game:clutter", EntryKind.Block, null, ["decorative"], Stack("{\"type\":\"" + t + "\"}", ("type", t)))).ToList();

    [Fact]
    public void WholeAttributeFamilyInOneTile()
    {
        var r = TidyEngine.Resolve(Clutter(), WorldProperties, OverrideFile.Parse(
            """{ "rules": [ { "match": { "domain": "game", "code": "clutter", "kind": "block" }, "group": { "id": "clutter", "title": "seraphhorizons:tidyvariants-group-game-clutter" } } ] }"""));
        Assert.Equal(6, Assert.Single(r.Groups).Members.Count);
    }

    [Fact]
    public void AttributeFamilySplitByAttributeValue()
    {
        var r = TidyEngine.Resolve(Clutter(), WorldProperties, OverrideFile.Parse("""
            { "rules": [
              { "match": { "domain": "game", "code": "clutter", "attributes": { "type": "book-*" } }, "group": { "id": "clutter-books" } },
              { "match": { "domain": "game", "code": "clutter", "attributes": { "type": "bottle-*" } }, "group": { "id": "clutter-bottles" } },
              { "match": { "domain": "game", "code": "clutter" }, "group": { "id": "clutter-other" } },
            ] }
            """));
        Assert.Equal(["clutter-books", "clutter-bottles", "clutter-other"], r.Groups.Select(g => g.Id));
        Assert.All(r.Groups, g => Assert.Equal(2, g.Members.Count));
    }

    // ---- {base} placeholder ------------------------------------------------------------------

    [Fact]
    public void BasePlaceholderMakesOneGroupPerBaseCode()
    {
        // ExpandedFoods: one page per code, as its shipped groupBy says, from a single rule.
        List<CreativeEntry> entries =
        [
            .. Product("expandedfoods:breadedball", EntryKind.Item, ("type", ["flax", "rice"]), ("state", ["raw", "cooked"])),
            .. Product("expandedfoods:hardtack", EntryKind.Item, ("type", ["spelt", "rye"]), ("state", ["raw", "bake1"])),
            E("expandedfoods:lard", EntryKind.Item),
        ];
        var r = TidyEngine.Resolve(entries, WorldProperties, OverrideFile.Parse("""
            { "rules": [ { "match": { "domain": "expandedfoods", "code": "*" },
                           "group": { "id": "ef-{base}", "title": "seraphhorizons:tidyvariants-group-expandedfoods-{base}" } } ] }
            """));
        Assert.Equal(["ef-breadedball", "ef-hardtack"], r.Groups.Select(g => g.Id));
        Assert.Equal(["seraphhorizons:tidyvariants-group-expandedfoods-breadedball", "seraphhorizons:tidyvariants-group-expandedfoods-hardtack"], r.Groups.Select(g => g.Title));
        Assert.All(r.Groups, g => Assert.Equal(4, g.Members.Count));
        Assert.Equal(-1, r.GroupOf(r.Index("expandedfoods:lard")));
    }

    [Fact]
    public void BaseCombinesWithBy()
    {
        var r = Resolve(Product("game:shield", EntryKind.Item, ("construction", ["woodmetal", "metal"]), ("wood", ["oak", "pine"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "shield-*" }, "group": { "id": "{base}-{construction}", "by": ["construction"] } } ] }""");
        Assert.Equal(["shield-woodmetal", "shield-metal"], r.Groups.Select(g => g.Id));
    }

    [Fact]
    public void BaseNeedsNoBy()
    {
        Assert.Single(OverrideFile.Parse("""{ "rules": [ { "match": { "domain": "game", "code": "*" }, "group": { "id": "g-{base}" } } ] }""").Rules);
        var ex = Assert.Throws<OverrideFormatException>(() => OverrideFile.Parse("""{ "rules": [ { "match": { "domain": "game", "code": "*" }, "group": { "id": "g-{wood}" } } ] }"""));
        Assert.Contains(ex.Errors, e => e.Contains("placeholder '{wood}' must be listed in 'by'"));
    }
}
