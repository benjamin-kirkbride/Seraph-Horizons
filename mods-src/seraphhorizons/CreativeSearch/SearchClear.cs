using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CreativeSearch;

/// <summary>
/// A right-click on the creative inventory's search box, or the handbook's, empties it (client main thread only).
/// Emptying goes through the box's own <c>SetValue("")</c>, so the dialog's search runs as for typing: the creative
/// grid shows the whole tab again (Tidy Variants regrouping it) and the handbook its whole list.
///
/// Focus is the game's: a press the box takes is marked handled by the element, and the composer then focuses
/// it, whatever the button (vanilla already focuses the box on a right-click, it just does nothing else). So the
/// player can type straight away. Every other text box (chat, signs, the handbook's detail pages, other mods'
/// dialogs) is left alone: the box must be the creative composer's <c>"searchbox"</c> or the handbook overview's
/// <c>"searchField"</c> in a dialog that is open.
/// </summary>
internal static class SearchClear
{
    const string CreativeComposer = "inventory-creative", CreativeBox = "searchbox";
    const string HandbookComposer = "handbook-overview", HandbookBox = "searchField";

    static ICoreClientAPI? capi;
    static bool failed;

    public static void Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
    }

    public static void Reset()
    {
        capi = null;
        failed = false;
    }

    /// <summary>Logs the first failure; the click then stays vanilla.</summary>
    public static void Fail(Exception ex)
    {
        if (failed) return;
        failed = true;
        capi?.Logger.Error("[seraphhorizons] Right-click clears the search box failed (logged once): {0}", ex);
    }

    /// <summary>A right-click the GUI routed to this text input.</summary>
    public static void OnRightClick(GuiElementTextInput box)
    {
        if (capi is null || box.GetText().Length == 0 || !IsSearchBox(box)) return;
        box.SetValue("");
    }

    static bool IsSearchBox(GuiElementTextInput box)
    {
        foreach (var dialog in capi!.Gui.OpenedGuis)
        {
            // The creative composer only while the dialog shows it (not the survival inventory's).
            if (dialog is GuiDialogInventory
                && dialog.Composers["maininventory"] is { DialogName: CreativeComposer } creative
                && ReferenceEquals(creative.GetTextInput(CreativeBox), box))
                return true;
            // Dovidarium may build the overview composer itself; it gives it the game's name and keys.
            if (dialog is GuiDialogHandbook)
                foreach (var composer in dialog.Composers.Values)
                    if (composer?.DialogName == HandbookComposer && ReferenceEquals(composer.GetTextInput(HandbookBox), box))
                        return true;
        }
        return false;
    }
}
