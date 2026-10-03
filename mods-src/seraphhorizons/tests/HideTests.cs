using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class HideTests
{
    [Fact]
    public void ChestKeepsOneSide()
    {
        // vanilla chest: side <- abstract/horizontalorientation; if all four were listed, north stays.
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", ["east", "north", "south", "west"])));
        Assert.Equal(["game:chest-north"], r.Codes(Enumerable.Range(0, 4).Where(i => !r.IsHidden(i))));
        Assert.Equal(HideReason.Variant, r.HideReasonOf(r.Index("game:chest-east")));
    }

    [Fact]
    public void SingleListedOrientationIsHarmless()
    {
        // What the game really lists: only chest-east and torch-*-up.
        var r = Resolve([E("game:chest", EntryKind.Block, ("side", "east")),
            .. Product("game:torch", EntryKind.Block, ("type", ["crude", "basic"]), ("state", ["extinct", "lit"]), ("orientation", ["up"]))]);
        Assert.All(Enumerable.Range(0, r.Entries.Count), i => Assert.False(r.IsHidden(i)));
    }

    [Fact]
    public void OnePerCombinationOfTheOtherDimensions()
    {
        // plankstairs-{wood}-{verticalorientation}-{horizontalorientation}-{cover}
        var r = Resolve(Product("game:plankstairs", EntryKind.Block,
            ("wood", ["oak", "pine", "birch"]), ("verticalorientation", ["up", "down"]), ("horizontalorientation", Horizontal), ("cover", ["free", "snow"])));
        var visible = r.Codes(Enumerable.Range(0, r.Entries.Count).Where(i => !r.IsHidden(i)));
        Assert.Equal(
            ["game:plankstairs-oak-up-north-free", "game:plankstairs-oak-up-north-snow",
             "game:plankstairs-pine-up-north-free", "game:plankstairs-pine-up-north-snow",
             "game:plankstairs-birch-up-north-free", "game:plankstairs-birch-up-north-snow"], visible);
    }

    [Fact]
    public void DoorKeepsClosed()
    {
        // legacy door: state[closed,opened] plus orientation and part.
        var r = Resolve(Product("game:door", EntryKind.Block,
            ("material", ["plank"]), ("horizontalorientation", Horizontal), ("part", ["down", "up"]), ("state", ["opened", "closed"])));
        var visible = r.Codes(Enumerable.Range(0, r.Entries.Count).Where(i => !r.IsHidden(i)));
        Assert.Equal(["game:door-plank-north-up-closed"], visible);
    }

    [Fact]
    public void FallsBackToCreativeOrderWithoutCanonicalValue()
    {
        var r = Resolve(Product("game:thing", EntryKind.Block, ("rotation", ["90", "0", "180"])));
        Assert.Equal(["game:thing-90"], r.Codes(Enumerable.Range(0, 3).Where(i => !r.IsHidden(i))));
    }

    [Fact]
    public void AttributeStacksAreHiddenPerStack()
    {
        // Food Shelves style: per-side blocks, each with wood attribute stacks.
        var entries = new List<CreativeEntry>();
        foreach (var side in new[] { "east", "north" })
            foreach (var wood in new[] { "oak", "pine" })
                entries.Add(E("foodshelves:barshelf", EntryKind.Block, [("type", "normal"), ("side", side)], Stack("wood=" + wood, ("wood", wood))));
        var r = TidyEngine.Resolve(entries, WorldProperties);
        var visible = Enumerable.Range(0, 4).Where(i => !r.IsHidden(i)).Select(i => r.Entries[i].ToString()).ToArray();
        Assert.Equal(["foodshelves:barshelf-normal-north{wood=oak}", "foodshelves:barshelf-normal-north{wood=pine}"], visible);
    }

    [Fact]
    public void OverrideHideAndUnhide()
    {
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", Horizontal)).Append(E("game:clutter-junk", EntryKind.Block)),
            """
            { "rules": [
              { "match": { "domain": "game", "code": "chest-west" }, "hide": false },
              { "match": { "domain": "game", "code": "clutter-*" }, "hide": true },
            ] }
            """);
        Assert.False(r.IsHidden(r.Index("game:chest-north")));
        Assert.False(r.IsHidden(r.Index("game:chest-west")));
        Assert.True(r.IsHidden(r.Index("game:chest-east")));
        Assert.Equal(HideReason.Override, r.HideReasonOf(r.Index("game:clutter-junk")));
        // The unhidden orientation is filler: chest-north and chest-west share a group.
        Assert.Equal(r.GroupOf(r.Index("game:chest-north")), r.GroupOf(r.Index("game:chest-west")));
    }

    [Fact]
    public void ExplicitlyHiddenCanonicalPassesToTheNext()
    {
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", Horizontal)),
            """{ "rules": [ { "match": { "domain": "game", "code": "chest-north" }, "hide": true } ] }""");
        Assert.Equal(["game:chest-east"], r.Codes(Enumerable.Range(0, 4).Where(i => !r.IsHidden(i))));
    }

    [Fact]
    public void PreferredOrientationIsOverridable()
    {
        var r = Resolve(Product("game:plankslab", EntryKind.Block, ("wood", ["oak"]), ("rot", ["north", "east", "south", "west", "up", "down"])),
            """{ "preferred": { "orientation": ["down"] } }""");
        Assert.Equal(["game:plankslab-oak-down"], r.Codes(Enumerable.Range(0, 6).Where(i => !r.IsHidden(i))));
    }
}
