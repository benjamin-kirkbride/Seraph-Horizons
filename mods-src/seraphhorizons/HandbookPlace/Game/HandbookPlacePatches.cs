using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.HandbookPlace.Game;

/// <summary>
/// The handbook's keep-place patches (client only, applied by <see cref="HandbookPlaceModSystem"/>). None is
/// on a method Dovidarium 0.9.5 gates against them (docs/variant-grouping/hooks.md §8): its overview reuse
/// takes <c>OnGuiOpened</c> and <c>initOverviewGui</c> as Exclusive (any foreign patch turns it off), so
/// the place is put back in a postfix on <c>GuiDialog.TryOpen(bool)</c>, which calls <c>OnGuiOpened</c>;
/// <c>OnGuiClosed</c> is registered by no Dovidarium feature, and its category tabs take any patch on
/// <c>selectTab</c>. Every body catches its own exceptions and leaves that call vanilla. Parameter names
/// match the game's (Harmony binds by name); verified against the decompiled 1.22.7 code.
/// </summary>
internal static class HandbookPlacePatches
{
    public const int Count = 3;

    public static int Apply(Harmony harmony, ILogger logger)
    {
        int n = 0;
        n += Patch(harmony, logger, "GuiDialogHandbook.OnGuiClosed prefix",
            AccessTools.DeclaredMethod(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.OnGuiClosed), Type.EmptyTypes),
            prefix: nameof(OnGuiClosedPrefix));
        n += Patch(harmony, logger, "GuiDialog.TryOpen postfix",
            AccessTools.DeclaredMethod(typeof(GuiDialog), nameof(GuiDialog.TryOpen), [typeof(bool)]),
            prefix: nameof(TryOpenPrefix), postfix: nameof(TryOpenPostfix));
        n += Patch(harmony, logger, "GuiDialogHandbook.selectTab prefix",
            AccessTools.DeclaredMethod(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.selectTab), [typeof(string)]),
            prefix: nameof(SelectTabPrefix));
        return n;
    }

    static int Patch(Harmony harmony, ILogger logger, string what, MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null)
        {
            logger.Warning("[seraphhorizons] Handbook keeps its place: target of {0} not found; that part stays vanilla", what);
            return 0;
        }
        try
        {
            harmony.Patch(target,
                prefix: prefix is null ? null : new HarmonyMethod(typeof(HandbookPlacePatches), prefix),
                postfix: postfix is null ? null : new HarmonyMethod(typeof(HandbookPlacePatches), postfix));
            return 1;
        }
        catch (Exception ex)
        {
            logger.Error("[seraphhorizons] Handbook keeps its place: could not patch {0}; that part stays vanilla: {1}", what, ex);
            return 0;
        }
    }

    // public override void GuiDialogHandbook.OnGuiClosed(): clears browseHistory and sets the search box to "",
    // then unpauses. Called by GuiDialog.TryClose only when the dialog was open.
    public static void OnGuiClosedPrefix(GuiDialogHandbook __instance)
    {
        try { KeepHandbookPlace.BeforeClose(__instance); }
        catch (Exception ex) { KeepHandbookPlace.Fail("remembering the place", ex); }
    }

    // public virtual bool GuiDialog.TryOpen(bool withFocus): calls OnGuiOpened only when the dialog was closed.
    // The handbook doesn't override it (nor TryOpen(), which calls it). The prefix tells the postfix whether this
    // call opened it.
    public static void TryOpenPrefix(GuiDialog __instance, out bool __state)
    {
        __state = __instance is GuiDialogHandbook && !__instance.IsOpened();
    }

    public static void TryOpenPostfix(GuiDialog __instance, bool __state)
    {
        if (!__state || __instance is not GuiDialogHandbook dialog) return;
        try { KeepHandbookPlace.AfterOpen(dialog); }
        catch (Exception ex) { KeepHandbookPlace.Fail("restoring the place", ex); }
    }

    // public void GuiDialogHandbook.selectTab(string code): sets the category and filters; called by a tab click on
    // the list page and by a handbook://tab- link.
    public static void SelectTabPrefix(GuiDialogHandbook __instance)
    {
        try { KeepHandbookPlace.BeforeSelectTab(__instance); }
        catch (Exception ex) { KeepHandbookPlace.Fail("showing the tab", ex); }
    }
}
