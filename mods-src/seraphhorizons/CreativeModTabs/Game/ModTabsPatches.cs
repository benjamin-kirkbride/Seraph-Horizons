using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>
/// The creative mod tabs' GUI patches (client only, applied by <see cref="ModTabsClient"/>). One target:
/// <c>GuiComposer.Compose(bool)</c>, filtered to the creative dialog's composer (<c>"inventory-creative"</c>)
/// and the survival one (<c>"inventory-backpack"</c>). It is the one point every build of the creative dialog
/// passes, without patching the dialog's own compose methods (Dovidarium turns features off when they have a
/// foreign patch; docs/variant-grouping/hooks.md §4). TooManyTabs prefixes it too; ours only looks after the compose
/// (docs/variant-grouping/creative-mod-tabs.md). The postfix catches its own exceptions.
/// </summary>
internal static class ModTabsPatches
{
    public const string CreativeComposer = "inventory-creative";
    public const string SurvivalComposer = "inventory-backpack";

    public static bool Apply(Harmony harmony, ILogger logger)
    {
        var target = AccessTools.DeclaredMethod(typeof(GuiComposer), nameof(GuiComposer.Compose), [typeof(bool)]);
        if (target is null)
        {
            logger.Warning("[seraphhorizons] Creative mod tabs: GuiComposer.Compose(bool) not found; no mod tabs");
            return false;
        }
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(ModTabsPatches), nameof(ComposePrefix)),
                postfix: new HarmonyMethod(typeof(ModTabsPatches), nameof(ComposePostfix)));
            return true;
        }
        catch (Exception ex)
        {
            logger.Error("[seraphhorizons] Creative mod tabs: could not patch GuiComposer.Compose; no mod tabs: {0}", ex);
            return false;
        }
    }

    // public GuiComposer GuiComposer.Compose(bool focusFirstElement = true). It returns at once when already
    // composed; __state remembers whether this call composes. The prefix changes nothing.
    public static void ComposePrefix(GuiComposer __instance, out bool __state) => __state = !__instance.Composed;

    public static void ComposePostfix(GuiComposer __instance, bool __state)
    {
        if (!__state) return;
        try
        {
            if (__instance.DialogName == CreativeComposer) ModTabsClient.AfterCreativeCompose(__instance);
            else if (__instance.DialogName == SurvivalComposer) ModTabsClient.AfterSurvivalCompose();
        }
        catch (Exception ex) { ModTabsClient.Fail("the mod tab button", ex); }
    }
}
