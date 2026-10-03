using SeraphHorizons.Mod.TidyVariants.Core;
using static SeraphHorizons.Mod.TidyVariants.Tests.Fx;

namespace SeraphHorizons.Mod.TidyVariants.Tests;

public class HandbookPlanTests
{
    static IEnumerable<CreativeEntry> Ores() =>
        Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ["nativecopper", "galena"]), ("rock", ["andesite", "granite"]));

    // Page code as the handbook keys it: class + code (+ attributes for attribute stacks).
    static string PageOf(TidyResolution r, int e) =>
        (r.Entries[e].Kind == EntryKind.Block ? "block-" : "item-") + r.Entries[e].Code + (r.Entries[e].Stack is { } s ? "-" + s.Key : "");

    static HandbookCollapse CollapseAll(TidyResolution r, HandbookLayout l, Func<string, bool>? canRepresent = null) =>
        l.Collapse(e => PageOf(r, e), canRepresent ?? (_ => true));

    /// <summary>Every visible entry's page is either listed, or a member of a collapsed group whose representative page is listed and lists it.</summary>
    static void AssertNothingUnreachable(TidyResolution r, HandbookCollapse c)
    {
        var listedByMember = new Dictionary<string, string>();
        foreach (var g in c.Groups)
        {
            Assert.DoesNotContain(g.RepresentativePage, c.DuplicatePages);
            foreach (var (_, p) in g.MemberPages) listedByMember[p] = g.RepresentativePage;
        }
        for (int i = 0; i < r.Entries.Count; i++)
        {
            if (r.IsHidden(i)) continue;
            string p = PageOf(r, i);
            if (c.DuplicatePages.Contains(p)) Assert.True(listedByMember.ContainsKey(p), $"{p} left the list but no listed page links it");
        }
    }

    [Fact]
    public void EveryGroupKeepsOneListedPageThatListsItsMembers()
    {
        var r = Resolve(Ores());
        var l = HandbookLayout.Build(r);
        Assert.Equal(2, l.Groups.Count);
        var c = CollapseAll(r, l);
        Assert.Equal(2, c.Groups.Count);
        foreach (var g in c.Groups)
        {
            Assert.Equal(PageOf(r, r.Groups[g.Group.Group].Representative), g.RepresentativePage);
            Assert.Equal(8, g.MemberPages.Count);
            Assert.Equal(g.RepresentativePage, g.MemberPages[0].Page);
            Assert.All(g.MemberPages.Skip(1), m => Assert.Contains(m.Page, c.DuplicatePages));
        }
        Assert.Equal(14, c.DuplicatePages.Count);
        AssertNothingUnreachable(r, c);
    }

    [Fact]
    public void GroupByIsWrittenOncePerGroupedCollectible()
    {
        var r = Resolve(Ores());
        var l = HandbookLayout.Build(r);
        Assert.Equal(16, l.Attributes.Entries.Count);
        Assert.All(l.Attributes.Entries, e => Assert.Matches(@"^ore-\*-(nativecopper|galena)-\*$", e.GroupBy!));
        Assert.Equal(16, l.Attributes.Entries.Select(e => e.Code).Distinct().Count());
    }

    [Fact]
    public void FallsBackToTheFirstMemberThatCanRepresent()
    {
        var r = Resolve(Ores());
        var l = HandbookLayout.Build(r);
        string rep = PageOf(r, r.GroupOf("game:ore-poor-nativecopper-andesite").Representative);
        var c = CollapseAll(r, l, p => p != rep);
        var copper = c.Groups.Single(g => g.MemberPages.Any(m => m.Page.Contains("nativecopper")));
        Assert.NotEqual(rep, copper.RepresentativePage);
        Assert.Contains(rep, c.DuplicatePages);
        AssertNothingUnreachable(r, c);
    }

    [Fact]
    public void GroupWithoutAnyUsableRepresentativeIsLeftAlone()
    {
        var r = Resolve(Ores());
        var l = HandbookLayout.Build(r);
        var c = CollapseAll(r, l, p => !p.Contains("galena"));
        Assert.Equal(1, c.NoRepresentative);
        Assert.Single(c.Groups);
        Assert.DoesNotContain(c.DuplicatePages, p => p.Contains("galena"));
        AssertNothingUnreachable(r, c);
    }

    [Fact]
    public void MembersWithoutPagesAreSkippedAndAOnePageGroupDoesNotCollapse()
    {
        var r = Resolve(Ores());
        var l = HandbookLayout.Build(r);
        // Only one copper page exists (say a mod's GetHandBookStacks lists one stack).
        var c = l.Collapse(e => r.Entries[e].Code.Contains("nativecopper") && r.Entries[e].Code != "game:ore-poor-nativecopper-andesite" ? null : PageOf(r, e), _ => true);
        Assert.Single(c.Groups);
        Assert.DoesNotContain("item-game:ore-poor-nativecopper-andesite", c.DuplicatePages);
    }

    [Fact]
    public void ASharedPageStaysListedWhileAnyNonMemberUsesIt()
    {
        var r = Resolve([.. Ores(), E("game:gear", EntryKind.Item, ("type", "rusty"))]);
        var l = HandbookLayout.Build(r);
        string member = PageOf(r, r.Index("game:ore-rich-galena-granite"));
        int gear = r.Index("game:gear-rusty");
        // The ungrouped gear maps onto a member's page code.
        var c = l.Collapse(e => e == gear ? member : PageOf(r, e), _ => true);
        Assert.DoesNotContain(member, c.DuplicatePages);
    }

    [Fact]
    public void AttributeStackGroupsCollapsePerPage()
    {
        // One collectible, one stack per type (clutter-like): an override puts them in one group.
        var types = new[] { "crate1", "crate2", "crate3" };
        var r = Resolve(types.Select(t => new CreativeEntry("game:clutter", EntryKind.Block, null, ["decorative"], Stack(t, ("type", t)))),
            """{"rules": [{"match": {"domain": "game", "code": "clutter", "kind": "block"}, "group": {"id": "crates", "title": "seraphhorizons:tidyvariants-group-crates"}}]}""");
        var l = HandbookLayout.Build(r);
        var g = Assert.Single(l.Groups);
        Assert.Equal("seraphhorizons:tidyvariants-group-crates", g.Title);
        var c = CollapseAll(r, l);
        Assert.Equal(3, Assert.Single(c.Groups).MemberPages.Count);
        Assert.Equal(2, c.DuplicatePages.Count);
        AssertNothingUnreachable(r, c);
    }

    [Fact]
    public void HiddenEntriesLeaveTheListButVisibleOnesDoNot()
    {
        var r = Resolve(Product("game:chest", EntryKind.Block, ("side", Horizontal)));
        var l = HandbookLayout.Build(r);
        Assert.Equal(3, l.HiddenEntries.Count);
        Assert.Empty(l.Groups);
        var c = CollapseAll(r, l);
        Assert.Equal(["block-game:chest-east", "block-game:chest-south", "block-game:chest-west"], c.DuplicatePages.Order());
        Assert.All(l.Attributes.Entries, e => Assert.True(e.Exclude));
    }
}
