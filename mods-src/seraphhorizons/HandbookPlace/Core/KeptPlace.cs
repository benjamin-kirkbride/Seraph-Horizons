namespace SeraphHorizons.Mod.HandbookPlace.Core;

/// <summary>
/// One step of the handbook's browse history (the game's <c>BrowseHistoryElement</c>): a page, by its
/// page code, or a search (the page code null). <paramref name="ScrollY"/> is the page's own scroll
/// (the game's <c>PosY</c>; a search step's is unused).
/// </summary>
public readonly record struct HistoryStep(string? PageCode, string? SearchText, float ScrollY)
{
    public bool IsPage => PageCode is not null;

    public static HistoryStep Page(string code, float scrollY) => new(code, null, scrollY);

    public static HistoryStep Search(string text) => new(null, text, 0);
}

/// <summary>
/// Where the handbook was when it closed (README, "The handbook keeps your place"): the list page's
/// search text and scroll, and the browse history, bottom first, whose top is what was shown (a page,
/// or the list when it is empty or its top is a search). Game-independent; the game side
/// (<c>HandbookPlace/Game/KeepHandbookPlace.cs</c>) reads it from the dialog at a close and writes the
/// <see cref="Plan"/> back at the next open.
/// </summary>
public sealed class KeptPlace
{
    /// <summary>The most history kept: the game never clears it while the handbook stays open, and
    /// with this tweak a close no longer clears it either, so it is cut to its newest steps.</summary>
    public const int MaxSteps = 64;

    public string SearchText { get; }
    public float ListScrollY { get; }
    /// <summary>Bottom (oldest) first; at most <see cref="MaxSteps"/>, the newest kept.</summary>
    public IReadOnlyList<HistoryStep> History { get; }

    public KeptPlace(string? searchText, float listScrollY, IEnumerable<HistoryStep> historyBottomFirst)
    {
        SearchText = searchText ?? "";
        ListScrollY = float.IsFinite(listScrollY) && listScrollY > 0 ? listScrollY : 0;
        var steps = historyBottomFirst.ToList();
        if (steps.Count > MaxSteps) steps.RemoveRange(0, steps.Count - MaxSteps);
        History = steps;
    }

    /// <summary>Nothing to put back: the handbook would open this way anyway.</summary>
    public bool IsEmpty => SearchText.Length == 0 && ListScrollY == 0 && History.Count == 0;

    /// <summary>
    /// What to put back, given which page codes the handbook still has (pages are rebuilt when the
    /// hotkeys change and on <c>.debug reloadhandbook</c>, so one can be gone): steps whose page is
    /// gone are dropped, and so is a step equal to the one it would now sit on (the game never pushes
    /// the page or search already on top: <c>OpenDetailPageFor</c>, <c>Search</c>).
    /// </summary>
    public RestorePlan Plan(Func<string, bool> pageExists)
    {
        var steps = new List<HistoryStep>(History.Count);
        foreach (var step in History)
        {
            if (step.IsPage ? !pageExists(step.PageCode!) : step.SearchText is null) continue;
            if (steps.Count > 0 && SameTarget(steps[^1], step))
            {
                steps[^1] = step; // the later one's scroll
                continue;
            }
            steps.Add(step);
        }
        return new RestorePlan(SearchText, ListScrollY, steps);
    }

    static bool SameTarget(HistoryStep a, HistoryStep b) =>
        a.IsPage ? b.IsPage && a.PageCode == b.PageCode : !b.IsPage && a.SearchText == b.SearchText;
}

/// <summary>The place to put back: the search text into the list page's box, the history (bottom first),
/// then the page on top shown, and the list's scroll.</summary>
public sealed record RestorePlan(string SearchText, float ListScrollY, IReadOnlyList<HistoryStep> History)
{
    /// <summary>The top of the history is a page, so the handbook opens on it rather than on the list.</summary>
    public bool ShowsPage => History.Count > 0 && History[^1].IsPage;
}
