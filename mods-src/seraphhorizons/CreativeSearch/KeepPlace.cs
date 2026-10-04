using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.CreativeSearch;

/// <summary>
/// The creative inventory reopens where it was closed (client main thread only). Closing it empties the search
/// box (<c>GuiDialogInventory.OnGuiClosed</c>), which also scrolls the grid to the top, and reopening composes it
/// again or, with Dovidarium, shows the last composer as it was left. The game already keeps the selected tab
/// (<c>currentTabIndex</c>), and the creative mod tabs keep their own mode and tab; Tidy Variants' expanded groups
/// are saved by Tidy Variants. What is lost is the search text and the scroll, so:
///
/// <list type="bullet">
/// <item>just before a creative close (<see cref="BeforeClose"/>), the box's text, the tab shown and the scroll
/// position are kept, in memory, until the next open;</item>
/// <item>after the next open has composed (<see cref="AfterOpen"/>), in creative, the text goes back into the box
/// (<c>SetValue</c> runs the dialog's own search, and Tidy Variants' regrouping after it), and the scroll goes back
/// too if the same tab is shown, clamped to the new list's height.</item>
/// </list>
///
/// Every open uses up what was kept, so a close outside creative, or after the game mode changed while the
/// dialog was open, restores nothing. Flipping the creative mod tabs recomposes the open dialog without closing
/// it, so it never passes here; it keeps the search text itself.
/// </summary>
internal static class KeepPlace
{
    /// <summary>The search text, the tab shown (code and index) and the grid's scroll offset at a close.</summary>
    readonly record struct Place(string Text, string? TabCode, int TabIndex, float ScrollY);

    static ICoreClientAPI? capi;
    static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    static Place? kept;

    static AccessTools.FieldRef<GuiDialogInventory, GuiComposer>? composerRef;
    static AccessTools.FieldRef<GuiDialogInventory, int>? colsRef;

    /// <summary>Binds the dialog's fields; false (and a warning) if the dialog has changed shape.</summary>
    public static bool Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
        try
        {
            composerRef = AccessTools.FieldRefAccess<GuiDialogInventory, GuiComposer>("creativeInvDialog");
            colsRef = AccessTools.FieldRefAccess<GuiDialogInventory, int>("cols");
            return true;
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[seraphhorizons] Creative inventory keeps its place: the creative dialog has changed shape ({0}); off", ex.Message);
            capi = null;
            return false;
        }
    }

    public static void Reset()
    {
        capi = null;
        kept = null;
        failed.Clear();
        composerRef = null;
        colsRef = null;
    }

    /// <summary>Logs the first failure per place; the caller then leaves that call vanilla.</summary>
    public static void Fail(string where, Exception ex)
    {
        if (failed.Add(where))
            capi?.Logger.Error("[seraphhorizons] Creative inventory keeps its place: {0} failed (logged once): {1}", where, ex);
    }

    static bool IsCreative => capi?.World?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    /// <summary>The creative composer, if it is the one the dialog shows.</summary>
    static GuiComposer? ShownCreativeComposer(GuiDialogInventory dialog) =>
        composerRef is not null && composerRef(dialog) is { } composer && ReferenceEquals(dialog.Composers["maininventory"], composer)
            ? composer : null;

    static CreativeTab? CurrentTab() =>
        (capi?.World?.Player?.InventoryManager?.GetOwnInventory("creative") as InventoryPlayerCreative)?.CurrentTab;

    /// <summary>GuiDialog.TryClose prefix, the inventory dialog only: the dialog is still open and its box still full.</summary>
    public static void BeforeClose(GuiDialogInventory dialog)
    {
        if (capi is null || !dialog.IsOpened() || !IsCreative || ShownCreativeComposer(dialog) is not { } composer) return;
        var tab = CurrentTab();
        kept = new Place(
            composer.GetTextInput("searchbox")?.GetText() ?? "",
            tab?.Code, tab?.Index ?? -1,
            composer.GetScrollbar("scrollbar")?.CurrentYPosition ?? 0);
    }

    /// <summary>GuiDialogInventory.OnGuiOpened postfix: the dialog is open and composed (or its last composer reused).</summary>
    public static void AfterOpen(GuiDialogInventory dialog)
    {
        var place = kept;
        kept = null;
        if (place is not { } p || capi is null || !IsCreative || ShownCreativeComposer(dialog) is not { } composer) return;

        if (p.Text.Length > 0 && composer.GetTextInput("searchbox") is { } box)
        {
            // Runs the dialog's OnTextChanged: the search, Tidy Variants' regrouping, and a scroll to the top.
            if (box.GetText() != p.Text) box.SetValue(p.Text);
            // As if the box had been left: vanilla's DeleteOnRefocusBackSpace then makes the first Backspace in it
            // clear the remembered text whole, as it does for any search the player comes back to.
            if (!box.HasFocus) box.OnFocusLost();
        }

        var tab = CurrentTab();
        if (p.ScrollY > 0 && tab is not null && tab.Code == p.TabCode && tab.Index == p.TabIndex)
            ScrollTo(dialog, composer, p.ScrollY);
    }

    /// <summary>
    /// Puts the grid's scroll back, as Tidy Variants does when a group is toggled: the bar's total height is the
    /// one the dialog just set for the list shown (vanilla's formula over the grid's rendered slots, which Tidy
    /// Variants has already regrouped), and setting it again clamps the handle and moves the grid there.
    /// </summary>
    static void ScrollTo(GuiDialogInventory dialog, GuiComposer composer, float y)
    {
        if (colsRef is null || composer.GetScrollbar("scrollbar") is not { } sb || composer.GetSlotGrid("slotgrid") is not { } grid) return;
        int cols = Math.Max(1, colsRef(dialog));
        int rows = (int)Math.Ceiling(grid.renderedSlots.Count / (double)cols);
        float total = (float)(ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, cols, rows).fixedHeight + 3.0);
        sb.CurrentYPosition = y;
        sb.SetNewTotalHeight(total);
    }
}
