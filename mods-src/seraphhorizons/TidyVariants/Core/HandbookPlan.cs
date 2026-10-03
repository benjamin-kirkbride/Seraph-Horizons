namespace SeraphHorizons.Mod.TidyVariants.Core;

/// <summary>A group the handbook list may show as one page.</summary>
/// <param name="Group">Index into <see cref="TidyResolution.Groups"/>.</param>
/// <param name="Title">The group's title lang key, or null (the game layer then uses the representative's name).</param>
/// <param name="Representative">The engine's preferred member (entry index).</param>
/// <param name="Members">Every member entry, in creative order (the representative included).</param>
public sealed record HandbookListGroup(int Group, string? Title, int Representative, IReadOnlyList<int> Members);

/// <summary>
/// What the handbook side of Tidy Variants does, decided from a resolution alone (docs/variant-grouping/handbook.md):
/// the collectible attributes (<see cref="Attributes"/>: one <c>groupBy</c> per grouped collectible, <c>exclude</c> on
/// fully hidden ones), the groups that may collapse to one list page, and the hidden entries whose pages leave the list.
/// <see cref="Collapse"/> then maps that onto the pages the handbook actually built.
/// </summary>
public sealed class HandbookLayout
{
    readonly HashSet<int> hiddenSet;

    HandbookLayout(HandbookPlan attributes, IReadOnlyList<HandbookListGroup> groups, IReadOnlyList<int> hidden, int entryCount)
    {
        Attributes = attributes; Groups = groups; HiddenEntries = hidden; EntryCount = entryCount;
        hiddenSet = [.. hidden];
    }

    /// <summary>The <c>groupBy</c> / <c>exclude</c> attributes (<see cref="Handbook.Build"/>).</summary>
    public HandbookPlan Attributes { get; }
    /// <summary>Groups with two or more member entries (pages), in group order.</summary>
    public IReadOnlyList<HandbookListGroup> Groups { get; }
    /// <summary>Hidden entries: if the handbook still has a page for one, it leaves the list.</summary>
    public IReadOnlyList<int> HiddenEntries { get; }
    /// <summary>Number of engine entries the layout was built from.</summary>
    public int EntryCount { get; }

    /// <param name="verifyAcrossKinds">Passed to <see cref="Handbook.Build"/>. The handbook's slideshow matches a
    /// <c>groupBy</c> pattern against every stack's code whatever its kind, so true is the exact setting.</param>
    public static HandbookLayout Build(TidyResolution res, bool verifyAcrossKinds = true)
    {
        var attributes = Handbook.Build(res, verifyAcrossKinds);
        var groups = new List<HandbookListGroup>();
        foreach (var g in res.Groups)
            if (g.Members.Count >= 2)
                groups.Add(new HandbookListGroup(g.Index, g.Title, g.Representative, g.Members));
        var hidden = new List<int>();
        for (int i = 0; i < res.Entries.Count; i++)
            if (res.IsHidden(i)) hidden.Add(i);
        return new HandbookLayout(attributes, groups, hidden, res.Entries.Count);
    }

    /// <summary>
    /// Decides which handbook pages leave the list, given the pages the handbook built. Pages are keyed by page code;
    /// several entries may share one (stacks that differ only in ignored attributes).
    /// <list type="bullet">
    /// <item>A group collapses only if a member's page can represent it (<paramref name="canRepresent"/>): the engine's
    /// representative first, else the first member in creative order that can. Otherwise none of its pages change.</item>
    /// <item>A page leaves the list only if every entry with that page is hidden or a non-representative member of a
    /// collapsed group, and it is no group's representative page. So nothing visible loses its only list entry.</item>
    /// </list>
    /// </summary>
    /// <param name="pageOf">Entry to its page code, or null when the handbook has no page for it.</param>
    /// <param name="canRepresent">Whether a page is in the list and can carry the group's member list.</param>
    public HandbookCollapse Collapse(Func<int, string?> pageOf, Func<string, bool> canRepresent)
    {
        var pageCache = new Dictionary<int, string?>();
        string? Page(int e)
        {
            if (!pageCache.TryGetValue(e, out var p)) pageCache[e] = p = pageOf(e);
            return p;
        }

        var collapsed = new List<CollapsedGroup>();
        var repPages = new HashSet<string>(StringComparer.Ordinal);
        var memberOfCollapsed = new HashSet<int>();
        var usable = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool CanRepresent(string p)
        {
            if (!usable.TryGetValue(p, out bool ok)) usable[p] = ok = canRepresent(p);
            return ok;
        }
        int noRep = 0;
        foreach (var g in Groups)
        {
            // Member pages in creative order, one per page code.
            var memberPages = new List<(int Entry, string Page)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (int m in g.Members)
                if (Page(m) is { } p && seen.Add(p)) memberPages.Add((m, p));
            if (memberPages.Count < 2) continue; // one page or none: nothing to collapse

            int rep = -1;
            if (Page(g.Representative) is { } rp && CanRepresent(rp)) rep = g.Representative;
            else
                foreach (var (e, p) in memberPages)
                    if (CanRepresent(p)) { rep = e; break; }
            if (rep < 0) { noRep++; continue; }

            string repPage = Page(rep)!;
            // Representative's page first, then the others in creative order.
            var ordered = new List<(int Entry, string Page)>(memberPages.Count);
            ordered.AddRange(memberPages.Where(x => x.Page == repPage));
            ordered.AddRange(memberPages.Where(x => x.Page != repPage));
            collapsed.Add(new CollapsedGroup(g, rep, repPage, ordered));
            repPages.Add(repPage);
            foreach (int m in g.Members) memberOfCollapsed.Add(m);
        }

        // Every entry that has a page, by page: a page may leave the list only if all of them allow it.
        var leaving = new Dictionary<string, bool>(StringComparer.Ordinal);
        void Consider(int e, bool mayLeave)
        {
            if (Page(e) is not { } p) return;
            leaving[p] = leaving.TryGetValue(p, out bool prev) ? prev && mayLeave : mayLeave;
        }
        foreach (int h in HiddenEntries) Consider(h, true);
        foreach (var c in collapsed)
            foreach (int m in c.Group.Members) Consider(m, true);
        // Every other entry with a page must stay listed (ungrouped, or in a group that did not collapse).
        for (int e = 0; e < EntryCount; e++)
            if (!memberOfCollapsed.Contains(e) && !hiddenSet.Contains(e)) Consider(e, false);

        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (p, mayLeave) in leaving)
            if (mayLeave && !repPages.Contains(p)) duplicates.Add(p);
        return new HandbookCollapse(collapsed, duplicates, noRep);
    }
}

/// <summary>A group that collapses: its representative page stays in the list and lists <see cref="MemberPages"/>.</summary>
/// <param name="MemberPages">One per distinct page, the representative's first, then creative order.</param>
public sealed record CollapsedGroup(HandbookListGroup Group, int Representative, string RepresentativePage, IReadOnlyList<(int Entry, string Page)> MemberPages);

/// <summary>The result of <see cref="HandbookLayout.Collapse"/>.</summary>
/// <param name="Groups">Groups that collapse.</param>
/// <param name="DuplicatePages">Page codes that leave the list (they stay openable by link).</param>
/// <param name="NoRepresentative">Groups left alone because no member page could represent them.</param>
public sealed record HandbookCollapse(IReadOnlyList<CollapsedGroup> Groups, IReadOnlySet<string> DuplicatePages, int NoRepresentative);
