using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class DisplayListTests
{
    // apple(plain), ingot×4 (group), chest×4 (3 hidden), ore copper×4 (group), ore galena×4 (group), bread (plain)
    static TidyResolution Sample() => Resolve([
        E("game:apple", EntryKind.Item),
        .. Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron", "zinc"])),
        .. Product("game:chest", EntryKind.Block, ("side", ["east", "north", "south", "west"])),
        .. Product("game:ore", EntryKind.Item, ("grade", ["poor", "medium"]), ("ore", ["nativecopper", "galena"]), ("rock", ["andesite", "granite"])),
        E("game:bread", EntryKind.Item),
    ]);

    static string Show(TidyResolution r, DisplayListBuilder b) => string.Join(" ", b.Items.Select(it =>
    {
        string code = r.Entries[it.Entry].Code[5..];
        if (it.IsTile) return $"[{code}x{it.MembersCount}]";
        if (it.IsExpandedMember)
            return ((it.Flags & DisplayFlags.GroupStart) != 0 ? "<" : "") + code + ((it.Flags & DisplayFlags.GroupEnd) != 0 ? ">" : "");
        return code;
    }));

    static int[] All(TidyResolution r) => Enumerable.Range(0, r.Entries.Count).ToArray();

    [Fact]
    public void FullListCollapsesGroupsAndDropsHidden()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        b.Build(All(r));
        Assert.Equal("apple [ingot-copperx4] chest-north [ore-medium-nativecopper-granitex4] [ore-medium-galena-granitex4] bread", Show(r, b));
        Assert.Equal(-1, b.AutoExpandedGroup);
    }

    [Fact]
    public void TileHoldsOnlyMatchingMembersAndPrefersAmongThem()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        // A search for "tin|zinc|ore-poor": the ingot tile without copper/iron, ore tiles with poor only.
        int[] hits = All(r).Where(i => r.Entries[i].Code is var c && (c.EndsWith("-tin") || c.EndsWith("-zinc") || c.Contains("ore-poor"))).ToArray();
        b.Build(hits);
        Assert.Equal("[ingot-tinx2] [ore-poor-nativecopper-granitex2] [ore-poor-galena-granitex2]", Show(r, b));
        var tile = b.Items[0];
        Assert.Equal(["game:ingot-tin", "game:ingot-zinc"], r.Codes(b.MembersOf(tile).ToArray()));
        Assert.Equal(r.GroupOf(r.Index("game:ingot-tin")), tile.Group);
    }

    [Fact]
    public void GroupSitsAtItsFirstMatchingMember()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        // Search results in a ranked order: bread first, then an ore, then an ingot, then another ore.
        int[] hits = [r.Index("game:bread"), r.Index("game:ore-poor-galena-andesite"), r.Index("game:ingot-iron"), r.Index("game:ore-medium-galena-granite"), r.Index("game:ingot-tin")];
        b.Build(hits);
        Assert.Equal("bread [ore-medium-galena-granitex2] [ingot-ironx2]", Show(r, b));
    }

    [Fact]
    public void ExpandedGroupsAreInlineWithMarkers()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        b.Build(All(r), new HashSet<int> { ingots });
        Assert.Equal("apple <ingot-tin ingot-copper ingot-iron ingot-zinc> chest-north [ore-medium-nativecopper-granitex4] [ore-medium-galena-granitex4] bread", Show(r, b));
        Assert.All(b.Items.Where(i => i.IsExpandedMember), i => { Assert.Equal(ingots, i.Group); Assert.Equal(4, i.MembersCount); });
        Assert.DoesNotContain(b.Items, i => (i.Flags & DisplayFlags.AutoExpanded) != 0);
    }

    [Fact]
    public void OnlyGroupLeftAutoExpands()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        int[] hits = All(r).Where(i => r.Entries[i].Code.Contains("galena") || r.Entries[i].Code == "game:apple").ToArray();
        b.Build(hits);
        Assert.Equal("apple <ore-poor-galena-andesite ore-poor-galena-granite ore-medium-galena-andesite ore-medium-galena-granite>", Show(r, b));
        Assert.Equal(r.GroupOf(r.Index("game:ore-poor-galena-andesite")), b.AutoExpandedGroup);
        Assert.All(b.Items.Skip(1), i => Assert.True((i.Flags & DisplayFlags.AutoExpanded) != 0));
    }

    [Fact]
    public void GroupWithOneSurvivorIsPlain()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        b.Build([r.Index("game:ingot-iron"), r.Index("game:ore-poor-galena-andesite"), r.Index("game:ore-poor-nativecopper-andesite")]);
        Assert.Equal("ingot-iron ore-poor-galena-andesite ore-poor-nativecopper-andesite", Show(r, b));
        Assert.All(b.Items, i => Assert.Equal(-1, i.Group));
    }

    [Fact]
    public void HiddenEntriesInTheSearchSubsetAreDropped()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        b.Build(All(r).Where(i => r.Entries[i].Code.StartsWith("game:chest")).ToArray());
        Assert.Equal("chest-north", Show(r, b));
    }

    [Fact]
    public void EmptyAndRepeatedBuildsReuseState()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        b.Build([]);
        Assert.Empty(b.Items);
        b.Build(All(r));
        string first = Show(r, b);
        b.Build([r.Index("game:ingot-tin"), r.Index("game:ingot-zinc")]);
        b.Build(All(r));
        Assert.Equal(first, Show(r, b));
    }

    [Fact]
    public void ExpandedButFilteredToOneIsPlain()
    {
        var r = Sample();
        var b = new DisplayListBuilder(r);
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        b.Build([r.Index("game:apple"), r.Index("game:ingot-tin")], new HashSet<int> { ingots });
        Assert.Equal("apple ingot-tin", Show(r, b));
    }
}
