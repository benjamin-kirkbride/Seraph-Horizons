using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.TidyVariants;

/// <summary>
/// The creative inventory's Harmony patches (client only, applied by <see cref="TidyCreativeModSystem"/>).
/// Targets come from docs/variant-grouping/hooks.md §6; none is a method Dovidarium gates as Exclusive or
/// one TooManyTabs patches. Every patch body delegates to <see cref="CreativeUi"/>, which catches its own
/// exceptions and falls back to vanilla; the try/catch here is a last line of defence.
/// Parameter names match the game's (Harmony binds by name); verified against the decompiled 1.22.7 code.
/// </summary>
internal static class CreativePatches
{
    public const int Count = 5;

    /// <summary>Applies each patch on its own, so one missing target only costs its feature. Returns how many applied.</summary>
    public static int Apply(Harmony harmony, ILogger logger)
    {
        int n = 0;
        n += Patch(harmony, logger, "hide (DetermineAvailableSlots postfix)",
            AccessTools.DeclaredMethod(typeof(GuiElementItemSlotGrid), nameof(GuiElementItemSlotGrid.DetermineAvailableSlots), [typeof(int[])]),
            postfix: nameof(DetermineAvailableSlotsPostfix));
        n += Patch(harmony, logger, "group after search (GuiDialogInventory.OnTextChanged postfix)",
            AccessTools.DeclaredMethod(typeof(GuiDialogInventory), "OnTextChanged", [typeof(string)]),
            postfix: nameof(OnTextChangedPostfix));
        n += Patch(harmony, logger, "alt+click (SlotClick prefix)",
            AccessTools.DeclaredMethod(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick),
                [typeof(ICoreClientAPI), typeof(int), typeof(EnumMouseButton), typeof(bool), typeof(bool), typeof(bool)]),
            prefix: nameof(SlotClickPrefix));
        n += Patch(harmony, logger, "tooltip (ItemSlot.GetStackDescription postfix)",
            AccessTools.DeclaredMethod(typeof(ItemSlot), nameof(ItemSlot.GetStackDescription), [typeof(IClientWorldAccessor), typeof(bool)]),
            postfix: nameof(GetStackDescriptionPostfix));
        n += Patch(harmony, logger, "group borders (GuiDialog.OnRenderGUI postfix)",
            AccessTools.DeclaredMethod(typeof(GuiDialog), nameof(GuiDialog.OnRenderGUI), [typeof(float)]),
            postfix: nameof(OnRenderGuiPostfix));
        return n;
    }

    static int Patch(Harmony harmony, ILogger logger, string what, MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null)
        {
            logger.Warning("[tidyvariants] creative inventory: target of {0} not found; that part stays vanilla", what);
            return 0;
        }
        try
        {
            harmony.Patch(target,
                prefix: prefix is null ? null : new HarmonyMethod(typeof(CreativePatches), prefix),
                postfix: postfix is null ? null : new HarmonyMethod(typeof(CreativePatches), postfix));
            return 1;
        }
        catch (Exception ex)
        {
            logger.Error("[tidyvariants] creative inventory: could not patch {0}; that part stays vanilla: {1}", what, ex);
            return 0;
        }
    }

    // GuiElementItemSlotGrid.DetermineAvailableSlots(int[] visibleSlots = null)
    public static void DetermineAvailableSlotsPostfix(GuiElementItemSlotGrid __instance)
    {
        try { CreativeUi.HideFrom(__instance); }
        catch (Exception ex) { CreativeUi.Fail("hide", ex); }
    }

    // private void GuiDialogInventory.OnTextChanged(string text)
    public static void OnTextChangedPostfix(GuiDialogInventory __instance)
    {
        try { CreativeUi.Regroup(__instance); }
        catch (Exception ex) { CreativeUi.Fail("regroup", ex); }
    }

    // public virtual void GuiElementItemSlotGridBase.SlotClick(ICoreClientAPI api, int slotId, EnumMouseButton mouseButton,
    //     bool shiftPressed, bool ctrlPressed, bool altPressed)
    public static bool SlotClickPrefix(GuiElementItemSlotGridBase __instance, int slotId, EnumMouseButton mouseButton, bool altPressed)
    {
        if (!altPressed || mouseButton != EnumMouseButton.Left) return true;
        try { return !CreativeUi.TryAltClick(__instance, slotId); }
        catch (Exception ex) { CreativeUi.Fail("alt+click", ex); return true; }
    }

    // public virtual string ItemSlot.GetStackDescription(IClientWorldAccessor world, bool extendedDebugInfo)
    public static void GetStackDescriptionPostfix(ItemSlot __instance, ref string __result)
    {
        if (__instance is not ItemSlotCreative) return;
        try { __result = CreativeUi.DescribeSlot(__instance, __result); }
        catch (Exception ex) { CreativeUi.Fail("tooltip", ex); }
    }

    // public virtual void GuiDialog.OnRenderGUI(float deltaTime)
    public static void OnRenderGuiPostfix(GuiDialog __instance)
    {
        if (__instance is not GuiDialogInventory inv) return;
        try { CreativeUi.RenderMarks(inv); }
        catch (Exception ex) { CreativeUi.Fail("render", ex); }
    }
}
