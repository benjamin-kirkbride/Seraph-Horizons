using HarmonyLib;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>
/// The trade window's client patches (Harmony id <see cref="TradeWindowSystem.HarmonyId"/>), both on
/// what the game shows only:
/// <list type="bullet">
/// <item><c>DlgTalkComponent.genText</c> (protected; postfix): the dialogue component
/// <see cref="TradeWindowSystem.StandingComponent"/> of one of the pack's traders says the trader's
/// view of the player's standing (<see cref="StandingSpeech"/>), from the state the server sent when
/// the conversation began. A dialogue line is static text and the game has no variables in text, so
/// this is where the numbers go in; without a state the file's own line stays.</item>
/// <item><c>ItemSlot.GetStackDescription</c> (postfix): while the trade window is open, the tooltip of
/// an item in the player's own inventory starts with what this trader pays for it, the breakdown and
/// which budget pays, or why it does not (<see cref="GuiDialogSeraphTrade.PriceText"/>).</item>
/// </list>
/// A method missing after a game update is logged and its patch skipped.
/// </summary>
public static class TradeWindowPatches
{
    private static readonly AccessTools.FieldRef<DialogueComponent, DialogueController>? Controller =
        AccessTools.Field(typeof(DialogueComponent), "controller") is { } f ? AccessTools.FieldRefAccess<DialogueComponent, DialogueController>(f) : null;

    public static void Patch(Harmony harmony, ICoreClientAPI capi)
    {
        var genText = AccessTools.Method(typeof(DlgTalkComponent), "genText");
        if (genText is null || Controller is null)
            capi.Logger.Warning("[seraphhorizons] Trade window: DlgTalkComponent.genText or DialogueComponent.controller not found; the dialogue's standing reply keeps its plain line");
        else harmony.Patch(genText, postfix: new HarmonyMethod(typeof(TradeWindowPatches), nameof(GenTextPostfix)));
        harmony.Patch(AccessTools.Method(typeof(ItemSlot), nameof(ItemSlot.GetStackDescription)),
            postfix: new HarmonyMethod(typeof(TradeWindowPatches), nameof(StackDescriptionPostfix)));
    }

    public static void GenTextPostfix(DlgTalkComponent __instance, ref RichTextComponentBase[] __result)
    {
        if (__instance.Code != TradeWindowSystem.StandingComponent || Controller is null) return;
        if (Controller(__instance)?.NPCEntity is not EntitySeraphTrader trader || trader.Api is not ICoreClientAPI capi) return;
        if (TradeWindowSystem.Of(capi)?.StateFor(trader.EntityId) is not { } state) return;
        string vtml = WindowText.Speech(capi, StandingSpeech.Build(state));
        __result = VtmlUtil.Richtextify(capi, vtml + "\r\n", CairoFont.WhiteSmallText().WithLineHeightMultiplier(1.2));
    }

    public static void StackDescriptionPostfix(ItemSlot __instance, ref string __result)
    {
        if (GuiDialogSeraphTrade.Current is not { } window || __instance.Itemstack is not { } stack) return;
        if (__instance is ItemSlotSell && __instance.Inventory is SeraphTraderInventory own && own.Api?.Side == EnumAppSide.Client)
        {
            // A sell slot: why it does not sell, or the whole breakdown of what it fetches.
            if (own.RefusalOf(stack) is { } why) __result = Lang.Get("seraphhorizons:" + why) + "\n\n" + __result;
            else if (window.PriceText(stack) is { Length: > 0 } price) __result = price + "\n\n" + __result;
            return;
        }
        if (__instance.Inventory is not InventoryBasePlayer || __instance.Inventory.Api?.Side != EnumAppSide.Client) return;
        if (window.PriceText(stack) is { Length: > 0 } text) __result = text + "\n\n" + __result;
    }
}
