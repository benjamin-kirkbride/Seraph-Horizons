using HarmonyLib;
using SeraphHorizons.Mod.HandbookPlace.Core;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.HandbookPlace.Game;

/// <summary>
/// The handbook reopens where it was closed (README, "The handbook keeps your place"; client main thread
/// only). <c>GuiDialogHandbook.OnGuiClosed</c> clears the browse history and empties the list page's
/// search box, and <c>OnGuiOpened</c> composes the list page again (a new one at the top, or with
/// Dovidarium the last one, which the close emptied), so every open starts on the whole list. So:
///
/// <list type="bullet">
/// <item>just before the close (<see cref="BeforeClose"/>, a prefix on <c>OnGuiClosed</c>), the search
/// text, the list's scroll and the history (each page by its page code, with its own scroll) are kept
/// in memory until the next open;</item>
/// <item>after the next open (<see cref="AfterOpen"/>, a postfix on <c>GuiDialog.TryOpen</c>, so after
/// <c>OnGuiOpened</c> has composed and filtered the list), the text goes back into the box (its
/// <c>SetValue</c> runs the handbook's own search, vanilla's or Dovidarium's, over the list Tidy
/// Variants has already collapsed), the history goes back, the page on top is composed as Back
/// composes it (<c>initDetailGui</c>, which scrolls it to its <c>PosY</c>), and the list's scroll is
/// put back (<see cref="ListScroll"/>).</item>
/// </list>
///
/// What opened the handbook runs after the postfix, as before: the selection hotkey and a
/// <c>handbook://</c> link open their page on top of the restored history (so Back returns to where
/// the player was), a <c>handbooksearch://</c> link pushes its search. A <c>handbook://tab-</c> link
/// calls <c>selectTab</c>, which shows nothing new while a page is on top; <see cref="BeforeSelectTab"/>
/// clears the history first then, as the game does for a tab clicked on a page
/// (<c>OnDetailViewTabClicked</c>). Every open uses up what was kept.
/// </summary>
internal static class KeepHandbookPlace
{
    /// <summary>The list's scroll is tried again this often while it falls short (Dovidarium filling the
    /// list), for at most <see cref="RetryTicks"/> times.</summary>
    const float RetrySeconds = 0.1f;
    const int RetryTicks = 20;

    static ICoreClientAPI? capi;
    static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    static KeptPlace? kept;
    /// <summary>The page objects the kept steps had, by page code: the same page is put back while the
    /// handbook still has it, else its code is looked up.</summary>
    static readonly Dictionary<string, GuiHandbookPage> keptPages = new(StringComparer.Ordinal);
    static ScrollRetry? retry;

    static AccessTools.FieldRef<GuiDialogHandbook, Stack<BrowseHistoryElement>>? historyRef;
    static AccessTools.FieldRef<GuiDialogHandbook, string>? searchTextRef;
    static AccessTools.FieldRef<GuiDialogHandbook, GuiComposer>? overviewRef;
    static AccessTools.FieldRef<GuiDialogHandbook, Dictionary<string, int>>? pageNumbersRef;
    static AccessTools.FieldRef<GuiDialogHandbook, List<GuiHandbookPage>>? pagesRef;
    static Action<GuiDialogHandbook>? initDetailGui;

    /// <summary>Binds the dialog's fields; false (and a warning) if the dialog has changed shape.</summary>
    public static bool Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
        try
        {
            historyRef = AccessTools.FieldRefAccess<GuiDialogHandbook, Stack<BrowseHistoryElement>>("browseHistory");
            searchTextRef = AccessTools.FieldRefAccess<GuiDialogHandbook, string>("currentSearchText");
            overviewRef = AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("overviewGui");
            pageNumbersRef = AccessTools.FieldRefAccess<GuiDialogHandbook, Dictionary<string, int>>("pageNumberByPageCode");
            pagesRef = AccessTools.FieldRefAccess<GuiDialogHandbook, List<GuiHandbookPage>>("allHandbookPages");
            var method = AccessTools.DeclaredMethod(typeof(GuiDialogHandbook), "initDetailGui", Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(GuiDialogHandbook), "initDetailGui");
            initDetailGui = AccessTools.MethodDelegate<Action<GuiDialogHandbook>>(method);
            return true;
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[seraphhorizons] Handbook keeps its place: the handbook dialog has changed shape ({0}); off", ex.Message);
            Reset();
            return false;
        }
    }

    public static void Reset()
    {
        retry?.Stop();
        retry = null;
        capi = null;
        kept = null;
        keptPages.Clear();
        failed.Clear();
        historyRef = null;
        searchTextRef = null;
        overviewRef = null;
        pageNumbersRef = null;
        pagesRef = null;
        initDetailGui = null;
    }

    /// <summary>Logs the first failure per place; the caller then leaves that call vanilla.</summary>
    public static void Fail(string where, Exception ex)
    {
        if (failed.Add(where))
            capi?.Logger.Error("[seraphhorizons] Handbook keeps its place: {0} failed (logged once): {1}", where, ex);
    }

    /// <summary>GuiDialogHandbook.OnGuiClosed prefix: the history, the box and the scrollbar are still as shown.</summary>
    public static void BeforeClose(GuiDialogHandbook dialog)
    {
        retry?.Stop();
        retry = null;
        kept = null;
        keptPages.Clear();
        if (capi is null || historyRef is null) return;

        var overview = overviewRef!(dialog);
        string text = overview?.GetTextInput("searchField")?.GetText() ?? searchTextRef!(dialog) ?? "";
        float scroll = overview?.GetScrollbar("scrollbar")?.CurrentYPosition ?? 0;

        var steps = new List<HistoryStep>();
        // A Stack enumerates from its top; the place keeps the bottom first.
        foreach (var element in historyRef(dialog).Reverse())
        {
            if (element is null) continue;
            if (element.Page is { } page)
            {
                steps.Add(HistoryStep.Page(page.PageCode, element.PosY));
                keptPages[page.PageCode] = page;
            }
            else if (element.SearchText is { } search)
                steps.Add(HistoryStep.Search(search));
        }

        var place = new KeptPlace(text, scroll, steps);
        if (!place.IsEmpty) kept = place;
        else keptPages.Clear();
    }

    /// <summary>GuiDialog.TryOpen postfix, the handbook only, when this call opened it: <c>OnGuiOpened</c>
    /// has composed the list page and filtered it.</summary>
    public static void AfterOpen(GuiDialogHandbook dialog)
    {
        var place = kept;
        kept = null;
        try
        {
            if (place is null || capi is null || historyRef is null || !dialog.IsOpened()) return;
            Restore(dialog, place);
        }
        finally
        {
            keptPages.Clear();
        }
    }

    static void Restore(GuiDialogHandbook dialog, KeptPlace place)
    {
        var plan = place.Plan(code => FindPage(dialog, code) is not null);
        var overview = overviewRef!(dialog);

        // The text first: SetValue runs FilterItemsBySearchText, which filters only on a change.
        if (plan.SearchText.Length > 0 && overview?.GetTextInput("searchField") is { } box && box.GetText() != plan.SearchText)
            box.SetValue(plan.SearchText);

        var history = historyRef!(dialog);
        history.Clear();
        foreach (var step in plan.History)
        {
            history.Push(step.IsPage
                ? new BrowseHistoryElement { Page = FindPage(dialog, step.PageCode!), PosY = step.ScrollY }
                : new BrowseHistoryElement { Page = null, SearchText = step.SearchText, PosY = 0 });
        }

        // The page on top, as Back shows it; OnRenderGUI picks the detail composer while a page is on top.
        if (plan.ShowsPage) initDetailGui!(dialog);

        if (plan.ListScrollY > 0 && overview is not null && overview.GetScrollbar("scrollbar") is not null)
        {
            var scroll = new ListScroll(plan.ListScrollY, RetryTicks);
            if (scroll.Try(y => ApplyListScroll(overview, y)) == ListScroll.Outcome.Pending)
                retry = new ScrollRetry(capi!, dialog, overview, scroll);
        }
    }

    /// <summary>The page the handbook has for a kept code: the kept page itself if it is still one of the
    /// handbook's pages (its <c>PageNumber</c> is its index there), else the page the code opens now.</summary>
    static GuiHandbookPage? FindPage(GuiDialogHandbook dialog, string code)
    {
        var pages = pagesRef!(dialog);
        if (pages is null) return null;
        if (keptPages.TryGetValue(code, out var page)
            && page.PageNumber >= 0 && page.PageNumber < pages.Count && ReferenceEquals(pages[page.PageNumber], page))
            return page;
        return pageNumbersRef!(dialog) is { } numbers && numbers.TryGetValue(code, out int n) && n >= 0 && n < pages.Count
            ? pages[n] : null;
    }

    /// <summary>
    /// Sets the list's scroll as the creative inventory's keep-place does: the position, then the total height
    /// again (the list's, as the handbook's filter set it), which clamps the handle and calls the dialog's
    /// <c>OnNewScrollbarvalueOverviewPage</c> (and Dovidarium's lazy append, which may lengthen the list).
    /// </summary>
    static (float Position, float Total) ApplyListScroll(GuiComposer overview, float y)
    {
        var bar = overview.GetScrollbar("scrollbar");
        var list = overview.GetFlatList("stacklist");
        if (bar is null || list is null) return (y, 0); // gone: count as there
        list.CalcTotalHeight();
        bar.CurrentYPosition = y;
        bar.SetNewTotalHeight((float)list.insideBounds.fixedHeight);
        return (bar.CurrentYPosition, (float)list.insideBounds.fixedHeight);
    }

    /// <summary>Retries the list's scroll every <see cref="RetrySeconds"/> while it falls short. A renderer, not
    /// a game tick: single player pauses the game while the handbook is open.</summary>
    sealed class ScrollRetry : IRenderer
    {
        readonly ICoreClientAPI api;
        readonly GuiDialogHandbook dialog;
        readonly GuiComposer overview;
        readonly ListScroll scroll;
        float since;
        bool registered;

        public ScrollRetry(ICoreClientAPI api, GuiDialogHandbook dialog, GuiComposer overview, ListScroll scroll)
        {
            this.api = api;
            this.dialog = dialog;
            this.overview = overview;
            this.scroll = scroll;
            api.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "seraphhorizons-handbookplace");
            registered = true;
        }

        public double RenderOrder => 1;
        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if ((since += deltaTime) < RetrySeconds) return;
            since = 0;
            try
            {
                // Only while the same list page is open and shown (no page on top of it).
                bool shown = dialog.IsOpened() && ReferenceEquals(overviewRef?.Invoke(dialog), overview)
                    && ReferenceEquals(dialog.SingleComposer, overview);
                if (!shown || overview.GetScrollbar("scrollbar") is not { } bar
                    || scroll.Tick(bar.CurrentYPosition, y => ApplyListScroll(overview, y)) != ListScroll.Outcome.Pending)
                    Stop();
            }
            catch (Exception ex)
            {
                Stop();
                Fail("putting the list's scroll back", ex);
            }
        }

        public void Stop()
        {
            if (!registered) return;
            registered = false;
            api.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            if (ReferenceEquals(retry, this)) retry = null;
        }

        public void Dispose() { }
    }

    /// <summary>GuiDialogHandbook.selectTab prefix: with a page on top the list (and so the tab) is not shown;
    /// clear the history as the game does for a tab clicked on a page.</summary>
    public static void BeforeSelectTab(GuiDialogHandbook dialog)
    {
        if (capi is null || historyRef is null) return;
        var history = historyRef(dialog);
        if (history.Count > 0 && history.Peek() is { Page: not null, SearchText: null })
            history.Clear();
    }
}
