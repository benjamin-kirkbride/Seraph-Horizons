namespace SeraphHorizons.Mod.Core;

/// <summary>One page in the handbook's list, as far as choosing the woodworking guide needs: its
/// page code, and its title's lang key for a guide (text) page, null for any other page.</summary>
public sealed record HandbookPageRef(string? PageCode, string? TitleKey);

/// <summary>
/// Which woodworking guide pages the handbook shows (<c>UnifiedWoodworking</c>): with the tweak on,
/// this mod's six pages (an overview and five chapters) take the place of Immersive Woodworking's
/// and Logging Expanded's guides; with it off, only theirs. Game-independent, so tests/ runs it
/// without the game; the client applies it to the handbook's page list as it is built.
/// </summary>
public static class WoodworkingGuidePages
{
    /// <summary>The overview keeps Immersive Woodworking's page code, so every link its item pages
    /// make to "the Woodworking overview" opens this one.</summary>
    public const string OverviewCode = "craftinginfo-woodworking";

    /// <summary>This mod's pages, in reading order: page code and the lang key prefix of its
    /// <c>-title</c> and <c>-text</c>.</summary>
    public static readonly IReadOnlyList<(string PageCode, string LangKey)> Pages =
    [
        (OverviewCode, "seraphhorizons:woodworking-overview"),
        ("seraphhorizons-woodworking-trunks", "seraphhorizons:woodworking-trunks"),
        ("seraphhorizons-woodworking-sawhorses", "seraphhorizons:woodworking-sawhorses"),
        ("seraphhorizons-woodworking-splittingblock", "seraphhorizons:woodworking-splittingblock"),
        ("seraphhorizons-woodworking-bark", "seraphhorizons:woodworking-bark"),
        ("seraphhorizons-woodworking-machines", "seraphhorizons:woodworking-machines"),
    ];

    /// <summary>The guides the unified one replaces, by page code and title key: the title tells
    /// Logging Expanded's apart from any other page coded <c>introduction</c>, and Immersive
    /// Woodworking's from this mod's overview, which has its code.</summary>
    public static readonly IReadOnlyList<(string PageCode, string TitleKey)> Replaced =
    [
        (OverviewCode, "immersivewoodworking:craftinginfo-woodworking-title"),
        ("introduction", "loggingmod:introduction-title"),
    ];

    public static string TitleKey(this (string PageCode, string LangKey) page) => page.LangKey + "-title";

    /// <summary>The guide pages players do not see, by page code and title key: the replaced
    /// guides when <paramref name="unified"/>, else this mod's pages. What <see cref="Arrange"/>
    /// drops, for code that lists the guides without the client's handbook (the recipe export).</summary>
    public static IReadOnlyList<(string PageCode, string TitleKey)> Hidden(bool unified) =>
        unified ? Replaced : Pages.Select(page => (page.PageCode, page.TitleKey())).ToList();

    public static string TextKey(this (string PageCode, string LangKey) page) => page.LangKey + "-text";

    /// <summary>The order of this mod's page, or -1 if <paramref name="page"/> is not one of them.</summary>
    public static int OwnIndex(HandbookPageRef page)
    {
        for (int i = 0; i < Pages.Count; i++)
            if (page.PageCode == Pages[i].PageCode && page.TitleKey == Pages[i].TitleKey())
                return i;
        return -1;
    }

    public static bool IsReplaced(HandbookPageRef page) =>
        Replaced.Any(replaced => page.PageCode == replaced.PageCode && page.TitleKey == replaced.TitleKey);

    /// <summary>
    /// The pages to keep, as indexes into <paramref name="pages"/>, in the order to show them.
    /// Unified: the replaced guides go, and this mod's pages stand together, in reading order, where
    /// the first replaced guide stood (where the first of them stood if neither is there).
    /// Not unified: this mod's pages go. Every other page keeps its place.
    /// </summary>
    public static IReadOnlyList<int> Arrange(IReadOnlyList<HandbookPageRef> pages, bool unified)
    {
        var order = new List<int>(pages.Count);
        if (!unified)
        {
            for (int i = 0; i < pages.Count; i++)
                if (OwnIndex(pages[i]) < 0)
                    order.Add(i);
            return order;
        }

        var own = Enumerable.Range(0, pages.Count).Where(i => OwnIndex(pages[i]) >= 0)
            .OrderBy(i => OwnIndex(pages[i])).ThenBy(i => i).ToList();
        int anchor = Enumerable.Range(0, pages.Count).FirstOrDefault(i => IsReplaced(pages[i]), -1);
        if (anchor < 0)
            anchor = own.Count > 0 ? own.Min() : -1;
        for (int i = 0; i < pages.Count; i++)
        {
            if (i == anchor)
                order.AddRange(own);
            if (!IsReplaced(pages[i]) && OwnIndex(pages[i]) < 0)
                order.Add(i);
        }
        return order;
    }
}
