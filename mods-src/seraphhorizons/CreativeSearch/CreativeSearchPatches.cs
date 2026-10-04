using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.Mod.CreativeSearch;

/// <summary>
/// The creative search patches (client only, applied by <see cref="CreativeSearchModSystem"/>). None is on a
/// method Dovidarium gates against foreign prefixes or one TooManyTabs patches (docs/variant-grouping/hooks.md §7):
/// <c>GuiDialogInventory.OnGuiClosed</c>, where the game clears the box, takes only foreign postfixes, so the
/// place is read in a prefix on <c>GuiDialog.TryClose</c>, which calls it. Every body catches its own exceptions
/// and leaves that call vanilla. Parameter names match the game's (Harmony binds by name); verified against the
/// decompiled 1.22.7 code.
/// </summary>
internal static class CreativeSearchPatches
{
    public const int KeepPlaceCount = 2;

    public static int ApplyKeepPlace(Harmony harmony, ILogger logger)
    {
        int n = 0;
        n += Patch(harmony, logger, "keep place (GuiDialog.TryClose prefix)",
            AccessTools.DeclaredMethod(typeof(GuiDialog), nameof(GuiDialog.TryClose), Type.EmptyTypes),
            prefix: nameof(TryClosePrefix));
        n += Patch(harmony, logger, "keep place (GuiDialogInventory.OnGuiOpened postfix)",
            AccessTools.DeclaredMethod(typeof(GuiDialogInventory), nameof(GuiDialogInventory.OnGuiOpened), Type.EmptyTypes),
            postfix: nameof(OnGuiOpenedPostfix));
        return n;
    }

    public static bool ApplyRightClick(Harmony harmony, ILogger logger) =>
        Patch(harmony, logger, "right-click clears (GuiElementEditableTextBase.OnMouseDownOnElement postfix)",
            AccessTools.DeclaredMethod(typeof(GuiElementEditableTextBase), nameof(GuiElementEditableTextBase.OnMouseDownOnElement),
                [typeof(ICoreClientAPI), typeof(MouseEvent)]),
            postfix: nameof(OnMouseDownOnElementPostfix)) == 1;

    static int Patch(Harmony harmony, ILogger logger, string what, MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null)
        {
            logger.Warning("[seraphhorizons] Creative search: target of {0} not found; that part stays vanilla", what);
            return 0;
        }
        try
        {
            harmony.Patch(target,
                prefix: prefix is null ? null : new HarmonyMethod(typeof(CreativeSearchPatches), prefix),
                postfix: postfix is null ? null : new HarmonyMethod(typeof(CreativeSearchPatches), postfix));
            return 1;
        }
        catch (Exception ex)
        {
            logger.Error("[seraphhorizons] Creative search: could not patch {0}; that part stays vanilla: {1}", what, ex);
            return 0;
        }
    }

    // public virtual bool GuiDialog.TryClose(). GuiDialogInventory doesn't override it; it calls OnGuiClosed,
    // which empties the search box (and so scrolls to the top), only when the dialog was open.
    public static void TryClosePrefix(GuiDialog __instance)
    {
        if (__instance is not GuiDialogInventory dialog) return;
        try { KeepPlace.BeforeClose(dialog); }
        catch (Exception ex) { KeepPlace.Fail("remembering the place", ex); }
    }

    // public override void GuiDialogInventory.OnGuiOpened(): runs ComposeGui(false) (or Dovidarium's reuse of the
    // last composer), so the postfix sees the dialog as it will be shown.
    public static void OnGuiOpenedPostfix(GuiDialogInventory __instance)
    {
        try { KeepPlace.AfterOpen(__instance); }
        catch (Exception ex) { KeepPlace.Fail("restoring the place", ex); }
    }

    // public override void GuiElementEditableTextBase.OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args).
    // Called only when the GUI routes the press to this element (it is on top there); the base marks the event
    // handled, ignores every button but the left one, and the composer then focuses the element.
    public static void OnMouseDownOnElementPostfix(GuiElementEditableTextBase __instance, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Right || __instance is not GuiElementTextInput box) return;
        try { SearchClear.OnRightClick(box); }
        catch (Exception ex) { SearchClear.Fail(ex); }
    }
}
