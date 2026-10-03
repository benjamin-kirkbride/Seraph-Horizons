using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

/// <summary>The creative grid at slot-id level (Core/CreativeView.cs), as the client GUI patches use it (#256).</summary>
public class CreativeViewTests
{
    // apple(plain), ingot×4 (group), chest×4 (3 hidden), ore copper×4 (group), ore galena×4 (group), bread (plain)
    static TidyResolution Sample() => Resolve([
        E("game:apple", EntryKind.Item),
        .. Product("game:ingot", EntryKind.Item, ("metal", ["tin", "copper", "iron", "zinc"])),
        .. Product("game:chest", EntryKind.Block, ("side", ["east", "north", "south", "west"])),
        .. Product("game:ore", EntryKind.Item, ("grade", ["poor", "medium"]), ("ore", ["nativecopper", "galena"]), ("rock", ["andesite", "granite"])),
        E("game:bread", EntryKind.Item),
    ]);

    // Slot ids deliberately differ from entry indices: slot id = 1000 + entry; unknown slots are 900+.
    const int Offset = 1000;

    static (int[] Slots, int[] Entries) Tab(TidyResolution r, IEnumerable<int> entries)
    {
        var e = entries.ToArray();
        return (e.Select(i => i < 0 ? 900 - i : Offset + i).ToArray(), e);
    }

    static string Show(TidyResolution r, CreativeView v) => string.Join(" ", v.Slots.Select(s =>
    {
        string code = s.Entry < 0 ? $"?{s.SlotId}" : r.Entries[s.Entry].Code[5..];
        if (s.Entry >= 0) Assert.Equal(Offset + s.Entry, s.SlotId);
        if (s.IsTile) return $"[{code}x{s.MemberCount}]";
        if (s.IsExpandedMember)
            return ((s.Flags & DisplayFlags.GroupStart) != 0 ? "<" : "") + code + ((s.Flags & DisplayFlags.GroupEnd) != 0 ? ">" : "");
        return code;
    }));

    static int[] All(TidyResolution r) => Enumerable.Range(0, r.Entries.Count).ToArray();

    [Fact]
    public void TilesAreTheRepresentativesRealSlot()
    {
        var r = Sample();
        var v = new CreativeView(r);
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries);
        Assert.Equal("apple [ingot-copperx4] chest-north [ore-medium-nativecopper-granitex4] [ore-medium-galena-granitex4] bread", Show(r, v));
        var tile = v.Slots[1];
        Assert.Equal(Offset + r.Index("game:ingot-copper"), tile.SlotId);
        Assert.Equal(Array.IndexOf(entries, r.Index("game:ingot-copper")), tile.Position);
        Assert.Equal(r.GroupOf(r.Index("game:ingot-copper")), tile.Group);
        // Every slot id is a real input slot id, used once.
        Assert.Equal(v.Slots.Count, v.Slots.Select(s => s.SlotId).Distinct().Count());
        Assert.All(v.Slots, s => Assert.Contains(s.SlotId, slots));
    }

    [Fact]
    public void ItemCountExcludesHiddenOnly()
    {
        var r = Sample();
        var v = new CreativeView(r);
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries);
        Assert.Equal(r.Entries.Count - 3, v.ItemCount);   // three chest orientations hidden
        Assert.True(v.IsHidden(r.Index("game:chest-east")));
        Assert.False(v.IsHidden(r.Index("game:chest-north")));
        Assert.False(v.IsHidden(-1));
        Assert.False(v.IsHidden(r.Entries.Count));
    }

    [Fact]
    public void UnknownSlotsKeepTheirPlace()
    {
        var r = Sample();
        var v = new CreativeView(r);
        // ?901 first, ?902 between two ingots, ?903 last; two galena ores so no group is auto-expanded.
        int[] seq = [-1, r.Index("game:apple"), r.Index("game:ingot-tin"), -2, r.Index("game:ingot-zinc"), r.Index("game:bread"),
            r.Index("game:ore-poor-galena-andesite"), r.Index("game:ore-poor-galena-granite"), -3];
        var (slots, entries) = Tab(r, seq);
        v.Build(slots, entries);
        Assert.Equal("?901 apple [ingot-tinx2] ?902 bread [ore-poor-galena-granitex2] ?903", Show(r, v));
        Assert.Equal(9, v.ItemCount);
    }

    [Fact]
    public void ExpandedRunStaysTogetherAroundUnknownSlots()
    {
        var r = Sample();
        var v = new CreativeView(r);
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        int[] seq = [r.Index("game:ingot-tin"), -2, r.Index("game:ingot-zinc"), r.Index("game:apple")];
        var (slots, entries) = Tab(r, seq);
        v.Build(slots, entries, new HashSet<int> { ingots });
        Assert.Equal("<ingot-tin ingot-zinc> ?902 apple", Show(r, v));
    }

    [Fact]
    public void RepeatedEntryIsAPlainSlot()
    {
        var r = Sample();
        var v = new CreativeView(r);
        int tin = r.Index("game:ingot-tin");
        int a = r.Index("game:ore-poor-galena-andesite"), b = r.Index("game:ore-poor-galena-granite");
        int[] slots = [Offset + tin, Offset + r.Index("game:ingot-iron"), 5000, Offset + a, Offset + b];
        int[] entries = [tin, r.Index("game:ingot-iron"), tin, a, b];
        v.Build(slots, entries);
        Assert.Equal(3, v.Slots.Count);   // ingot tile, the repeat, ore tile
        Assert.True(v.Slots[0].IsTile);
        Assert.Equal(2, v.Slots[0].MemberCount);
        Assert.Equal(5000, v.Slots[1].SlotId);
        Assert.False(v.Slots[1].IsGrouped);
        Assert.Equal(tin, v.Slots[1].Entry);
    }

    [Fact]
    public void SearchOrderIsKeptAndOnlyGroupLeftAutoExpands()
    {
        var r = Sample();
        var v = new CreativeView(r);
        int[] seq = [r.Index("game:bread"), r.Index("game:ore-poor-galena-andesite"), -1, r.Index("game:ore-medium-galena-granite")];
        var (slots, entries) = Tab(r, seq);
        v.Build(slots, entries);
        Assert.Equal("bread <ore-poor-galena-andesite ore-medium-galena-granite> ?901", Show(r, v));
        Assert.Equal(r.GroupOf(r.Index("game:ore-poor-galena-andesite")), v.AutoExpandedGroup);
        Assert.All(v.Slots.Where(s => s.IsGrouped), s => Assert.True(s.IsAutoExpanded));
    }

    [Fact]
    public void HiddenSlotsAreDropped()
    {
        var r = Sample();
        var v = new CreativeView(r);
        var (slots, entries) = Tab(r, All(r).Where(i => r.Entries[i].Code.StartsWith("game:chest")));
        v.Build(slots, entries);
        Assert.Equal("chest-north", Show(r, v));
        Assert.Equal(1, v.ItemCount);
    }

    [Fact]
    public void ToggleTargets()
    {
        var r = Sample();
        var v = new CreativeView(r);
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries, new HashSet<int> { ingots });
        Assert.Equal(-1, CreativeView.ToggleTarget(v.Slots[0]));                     // apple, plain
        Assert.All(v.Slots.Where(s => s.IsExpandedMember), s => Assert.Equal(ingots, CreativeView.ToggleTarget(s)));
        var tile = v.Slots.First(s => s.IsTile);
        Assert.Equal(tile.Group, CreativeView.ToggleTarget(tile));

        // Auto-expanded members have nothing to toggle.
        var (s2, e2) = Tab(r, All(r).Where(i => r.Entries[i].Code.StartsWith("game:ingot")));
        v.Build(s2, e2);
        Assert.All(v.Slots, s => Assert.Equal(-1, CreativeView.ToggleTarget(s)));
    }

    [Fact]
    public void RightClickTogglesOnlyWithAnEmptyCursorOutsideADrag()
    {
        var r = Sample();
        var v = new CreativeView(r);
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries, new HashSet<int> { ingots });
        var tile = v.Slots.First(s => s.IsTile);
        var member = v.Slots.First(s => s.IsExpandedMember);
        var plain = v.Slots[0];

        Assert.Equal(tile.Group, CreativeView.RightClickTarget(tile, cursorEmpty: true, rightDragging: false));
        Assert.Equal(ingots, CreativeView.RightClickTarget(member, cursorEmpty: true, rightDragging: false));
        Assert.Equal(-1, CreativeView.RightClickTarget(plain, cursorEmpty: true, rightDragging: false));
        foreach (var s in new[] { tile, member, plain })
        {
            Assert.Equal(-1, CreativeView.RightClickTarget(s, cursorEmpty: false, rightDragging: false));   // item held: vanilla
            Assert.Equal(-1, CreativeView.RightClickTarget(s, cursorEmpty: true, rightDragging: true));     // mid-drag: vanilla
        }

        // Auto-expanded members stay vanilla (nothing to collapse).
        var (s2, e2) = Tab(r, All(r).Where(i => r.Entries[i].Code.StartsWith("game:ingot")));
        v.Build(s2, e2);
        Assert.All(v.Slots, s => Assert.Equal(-1, CreativeView.RightClickTarget(s, cursorEmpty: true, rightDragging: false)));
    }

    [Fact]
    public void ExpandingKeepsTheTilesIndexAsTheGroupsFirstSlot()
    {
        // The slot under the cursor after a right-click expand is the group's first member, at the tile's index.
        var r = Sample();
        var v = new CreativeView(r);
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries);
        var collapsed = v.Slots.ToList();
        foreach (var (tile, i) in collapsed.Select((s, i) => (s, i)).Where(t => t.s.IsTile).ToList())
        {
            v.Build(slots, entries, new HashSet<int> { tile.Group });
            Assert.True(v.Slots[i].IsExpandedMember);
            Assert.True((v.Slots[i].Flags & DisplayFlags.GroupStart) != 0);
            Assert.Equal(tile.Group, v.Slots[i].Group);
            Assert.Equal(collapsed.Take(i).Select(s => s.SlotId), v.Slots.Take(i).Select(s => s.SlotId));   // nothing before it moved
        }
    }

    [Fact]
    public void MismatchedLengthsThrow()
    {
        var v = new CreativeView(Sample());
        Assert.Throws<ArgumentException>(() => v.Build([1, 2], [0]));
    }

    [Fact]
    public void RepeatedBuildsAreIndependent()
    {
        var r = Sample();
        var v = new CreativeView(r);
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries);
        string full = Show(r, v);
        v.Build([], []);
        Assert.Empty(v.Slots);
        Assert.Equal(0, v.ItemCount);
        v.Build(slots, entries);
        Assert.Equal(full, Show(r, v));
    }

    [Fact]
    public void ExpandStateTogglesByIdAndKeepsUnknownIds()
    {
        var r = Sample();
        int ingots = r.GroupOf(r.Index("game:ingot-tin"));
        var st = new ExpandState(["no-such-group", "", r.Groups[ingots].Id]);
        Assert.True(st.IsExpanded(r, ingots));
        Assert.Equal([ingots], st.IndicesFor(r).ToArray());

        Assert.False(st.Toggle(r, ingots));
        Assert.False(st.IsExpanded(r, ingots));
        Assert.Equal(["no-such-group"], st.Ids);

        int ores = r.GroupOf(r.Index("game:ore-poor-galena-andesite"));
        Assert.True(st.Toggle(r, ores));
        Assert.Equal(new[] { "no-such-group", r.Groups[ores].Id }.OrderBy(s => s, StringComparer.Ordinal), st.Ids);

        // A fresh resolution (e.g. another world) maps the saved ids again.
        var r2 = Sample();
        Assert.Equal([ores], st.IndicesFor(r2).ToArray());
    }

    [Fact]
    public void ExpandStateDrivesTheView()
    {
        var r = Sample();
        var v = new CreativeView(r);
        var st = new ExpandState();
        var (slots, entries) = Tab(r, All(r));
        v.Build(slots, entries, st.IndicesFor(r));
        var tile = v.Slots.First(s => s.IsTile);
        st.Toggle(r, CreativeView.ToggleTarget(tile));
        v.Build(slots, entries, st.IndicesFor(r));
        Assert.Equal("apple <ingot-tin ingot-copper ingot-iron ingot-zinc> chest-north [ore-medium-nativecopper-granitex4] [ore-medium-galena-granitex4] bread", Show(r, v));
        var member = v.Slots.Last(s => s.IsExpandedMember);
        st.Toggle(r, CreativeView.ToggleTarget(member));
        v.Build(slots, entries, st.IndicesFor(r));
        Assert.Equal("apple [ingot-copperx4] chest-north [ore-medium-nativecopper-granitex4] [ore-medium-galena-granitex4] bread", Show(r, v));
    }
}
