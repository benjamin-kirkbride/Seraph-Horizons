using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class GroupingTests
{
    static IEnumerable<CreativeEntry> Ores() =>
        Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ["nativecopper", "galena", "cassiterite"]), ("rock", ["andesite", "granite", "chalk"]));

    [Fact]
    public void OneGroupPerOreTypeOverRockAndGrade()
    {
        var r = Resolve(Ores());
        Assert.Equal(3, r.Groups.Count);
        var copper = r.GroupOf("game:ore-poor-nativecopper-andesite");
        Assert.Equal("auto:game:ore/ore=nativecopper", copper.Id);
        Assert.Equal(12, copper.Members.Count);
        Assert.All(copper.Members, m => Assert.Contains("-nativecopper-", r.Entries[m].Code));
        Assert.True(copper.IsAutomatic);
        Assert.Null(copper.Title);
        Assert.Equal(new[] { "auto:game:ore/ore=nativecopper", "auto:game:ore/ore=galena", "auto:game:ore/ore=cassiterite" }, r.Groups.Select(g => g.Id));
    }

    [Fact]
    public void OneGroupPerDoorStyleOverRockAndWood()
    {
        // DoorVariants cobbledoor / rounddoor: style × material<-block/rock × wood[aged,veryaged,...]
        var r = Resolve([
            .. Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid3x1cobblestone"]), ("material", ["granite", "andesite", "basalt"]), ("wood", ["aged", "veryaged", "oak", "pine"])),
            .. Product("doorvariants:rounddoor", EntryKind.Block, ("style", ["round2x1cobblestone", "round2x2cobblestone"]), ("material", ["granite", "andesite", "basalt"]), ("wood", ["aged", "veryaged", "oak", "pine"])),
        ]);
        Assert.Equal(["auto:doorvariants:cobbledoor/style=solid3x1cobblestone", "auto:doorvariants:rounddoor/style=round2x1cobblestone", "auto:doorvariants:rounddoor/style=round2x2cobblestone"],
            r.Groups.Select(g => g.Id));
        Assert.All(r.Groups, g => Assert.Equal(12, g.Members.Count));
    }

    [Fact]
    public void MaterialOnlyTypeCollapsesToOneGroup()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["copper", "tinbronze", "iron", "gold"])));
        var g = Assert.Single(r.Groups);
        Assert.Equal("auto:game:ingot", g.Id);
    }

    [Fact]
    public void FoodCollapsesCookingStatesKeepsIngredient()
    {
        // ExpandedFoods breadedball: type[flax,rice,...] × state[raw,partbaked,cooked,charred,...]
        var r = Resolve(Product("expandedfoods:breadedball", EntryKind.Item,
            ("type", ["flax", "rice", "rye"]), ("state", ["raw", "partbaked", "cooked", "charred", "oiled"])));
        Assert.Equal(3, r.Groups.Count);
        Assert.Equal("auto:expandedfoods:breadedball/type=rice", r.GroupOf("expandedfoods:breadedball-rice-charred").Id);
    }

    [Fact]
    public void MoreRoadsRockTypedRoadsCollapseSnowCoverToo()
    {
        var r = Resolve(Product("moreroads:ancientroad", EntryKind.Block,
            ("style", ["basic", "brick"]), ("type", ["andesite", "basalt", "granite", "obsidian"]), ("cover", ["free", "snow"])));
        Assert.Equal(["auto:moreroads:ancientroad/style=basic", "auto:moreroads:ancientroad/style=brick"], r.Groups.Select(g => g.Id));
        Assert.All(r.Groups, g => Assert.Equal(8, g.Members.Count));
    }

    [Fact]
    public void MeaningfulOnlyTypeStaysPlain()
    {
        // vanilla shield construction[crude,blackguard]: every entry its own (singleton) group = plain.
        var r = Resolve(Product("game:shield", EntryKind.Item, ("construction", ["crude", "blackguard"])));
        Assert.Empty(r.Groups);
        Assert.Equal(-1, r.GroupOf(0));
        Assert.Equal(-1, r.GroupOf(1));
    }

    [Fact]
    public void TorchesGroupByTypeOverState()
    {
        var r = Resolve(Product("game:torch", EntryKind.Block, ("type", ["crude", "basic", "cloth"]), ("state", ["extinct", "burnedout", "lit"]), ("orientation", ["up"])));
        Assert.Equal(3, r.Groups.Count);
        Assert.Equal("game:torch-basic-lit-up", r.Entries[r.GroupOf("game:torch-basic-extinct-up").Representative].Code);
    }

    [Fact]
    public void SameBaseDifferentDimensionsAreDifferentFamilies()
    {
        // vanilla lore: type[book] × color, plus type[add] paper, scroll (no color).
        var r = Resolve([
            .. Product("game:lore", EntryKind.Item, ("type", ["book"]), ("color", ["aged-orange", "aged-darkgreen"])),
            .. Product("game:lore", EntryKind.Item, ("type", ["paper", "scroll"])),
        ]);
        Assert.Equal(2, r.Families.Count);
        Assert.Empty(r.Groups); // aged-* colors aren't in the color list: each book is meaningful
        Assert.Equal(2, r.Families[0].Members.Count);
    }

    [Fact]
    public void BlocksAndItemsWithTheSameCodeAreDifferentFamilies()
    {
        var r = Resolve([E("game:thing", EntryKind.Block, ("metal", "copper")), E("game:thing", EntryKind.Item, ("metal", "iron"))]);
        Assert.Equal(2, r.Families.Count);
        Assert.Empty(r.Groups);
    }

    [Fact]
    public void AttributeStacksWithoutValuesAreOneTile()
    {
        // vanilla clutter: one block, many attribute stacks. The handbook can only group whole collectibles, so a
        // collectible's stacks are one tile unless an override splits them.
        var entries = new[] { "book-big-closed", "bottle", "crate" }
            .Select(t => new CreativeEntry("game:clutter", EntryKind.Block, null, ["decorative"], Stack("{\"type\":\"" + t + "\"}"))).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties);
        Assert.Equal("auto:game:clutter", Assert.Single(r.Groups).Id);
        Assert.Empty(Handbook.Build(r).Issues);
    }

    [Fact]
    public void AttributeStacksWithMaterialValuesCollapse()
    {
        // Food Shelves: per-wood attribute stacks of one block.
        var entries = new[] { "oak", "pine", "birch", "acacia" }
            .Select(w => new CreativeEntry("foodshelves:barshelf-normal-east", EntryKind.Block,
                [KeyValuePair.Create("type", "normal"), KeyValuePair.Create("side", "east")], ["foodshelves"], Stack("wood=" + w, ("wood", w)))).ToList();
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var g = Assert.Single(r.Groups);
        Assert.Equal(4, g.Members.Count);
        Assert.Equal("attr:wood", r.Families[0].DimensionNames[2]);
        Assert.Equal(MaterialKind.Wood, r.Families[0].Dimensions[2].Material);
        Assert.Equal("oak", r.Entries[g.Representative].Stack!.Get("wood"));
    }

    [Fact]
    public void EveryVisibleEntryInExactlyOneGroupOrPlain()
    {
        var r = Resolve([.. Ores(), .. Product("game:chest", EntryKind.Block, ("side", Horizontal))]);
        var counts = new int[r.Entries.Count];
        foreach (var g in r.Groups) foreach (int m in g.Members) counts[m]++;
        for (int i = 0; i < r.Entries.Count; i++)
        {
            if (r.IsHidden(i)) { Assert.Equal(0, counts[i]); Assert.Equal(-1, r.GroupOf(i)); }
            else Assert.Equal(r.GroupOf(i) >= 0 ? 1 : 0, counts[i]);
        }
    }

    [Fact]
    public void VariantMismatchIsItsOwnFamilyAndReported()
    {
        var r = TidyEngine.Resolve([new CreativeEntry("game:odd-name", EntryKind.Item, [KeyValuePair.Create("metal", "copper")])], WorldProperties);
        Assert.Contains(r.Issues, i => i.Kind == "variant-mismatch");
        Assert.False(r.Families[0].CodeAligned);
    }
}
