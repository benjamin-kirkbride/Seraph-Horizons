using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class RepresentativeTests
{
    static string Rep(TidyResolution r, string memberCode) => r.Entries[r.GroupOf(memberCode).Representative].Code;

    [Fact]
    public void PrefersPackWideValues()
    {
        var r = Resolve(Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ["nativecopper"]), ("rock", ["andesite", "chalk", "granite"])));
        Assert.Equal("game:ore-medium-nativecopper-granite", Rep(r, "game:ore-poor-nativecopper-andesite"));

        var doors = Resolve(Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid"]), ("material", ["andesite", "granite"]), ("wood", ["aged", "pine", "oak"])));
        Assert.Equal("doorvariants:cobbledoor-solid-granite-oak", Rep(doors, "doorvariants:cobbledoor-solid-andesite-aged"));

        var ingots = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "zinc", "copper", "iron"])));
        Assert.Equal("game:ingot-copper", Rep(ingots, "game:ingot-tin"));

        var food = Resolve(Product("expandedfoods:dumpling", EntryKind.Item, ("type", ["raw", "partbaked", "cooked", "charred"])));
        Assert.Equal("expandedfoods:dumpling-cooked", Rep(food, "expandedfoods:dumpling-raw"));
    }

    [Fact]
    public void FirstInCreativeOrderWithoutAPreferredValue()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "zinc", "lead"])));
        Assert.Equal("game:ingot-tin", Rep(r, "game:ingot-zinc"));
    }

    [Fact]
    public void EarlierFillerDimensionDecidesFirst()
    {
        // rock before wood: granite-pine beats andesite-oak.
        var r = Resolve(Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid"]), ("material", ["andesite", "granite"]), ("wood", ["oak", "pine"])));
        Assert.Equal("doorvariants:cobbledoor-solid-granite-oak", Rep(r, "doorvariants:cobbledoor-solid-andesite-oak"));
        var r2 = Resolve(Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid"]), ("material", ["andesite", "granite"]), ("wood", ["pine", "maple"])));
        Assert.Equal("doorvariants:cobbledoor-solid-granite-pine", Rep(r2, "doorvariants:cobbledoor-solid-andesite-pine"));
    }

    [Fact]
    public void PreferredListsAreOverridable()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron"])), """{ "preferred": { "metal": ["iron"] } }""");
        Assert.Equal("game:ingot-iron", Rep(r, "game:ingot-tin"));
    }

    [Fact]
    public void PreferRuleWinsOverPreferredValues()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron"])),
            """{ "rules": [ { "match": { "domain": "game", "code": "ingot-tin" }, "prefer": true } ] }""");
        Assert.Equal("game:ingot-tin", Rep(r, "game:ingot-iron"));
    }

    [Fact]
    public void RanksOrderEveryMember()
    {
        var r = Resolve(Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron", "zinc"])));
        var ranks = r.Groups[0].Members.ToDictionary(m => r.Entries[m].Code, r.RepresentativeRank);
        // copper (preferred #0), iron (#1), then tin and zinc in creative order.
        Assert.Equal(0, ranks["game:ingot-copper"]);
        Assert.Equal(1, ranks["game:ingot-iron"]);
        Assert.Equal(2, ranks["game:ingot-tin"]);
        Assert.Equal(3, ranks["game:ingot-zinc"]);
    }
}
