using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Holding oil, a right-click on an eidolon oils it, whatever the held item would have done.
///
/// <para>The game gives a right-click on an entity to the held item first: the client's mouse
/// handling calls the item's <see cref="CollectibleObject.OnHeldUseStart"/>, and only when the item
/// leaves the click (<see cref="EnumHandHandling.NotHandled"/>) does it call the entity's
/// <c>OnInteract</c> and send it to the server. A block is the other way round, its interaction
/// first, which is why a machine takes oil from any container. Rendered fat is food, so the click
/// began eating it, and an oil container that can be drunk from began drinking: the eidolon's oil
/// (<see cref="EntityBehaviorEidolonOil"/>) never saw the click.</para>
///
/// <para>A prefix on <see cref="CollectibleObject.OnHeldUseStart"/> leaves to the eidolon a held
/// interaction aimed at it, with no block selected, that its oil takes
/// (<see cref="EntityBehaviorEidolonOil.Takes"/>): the item does nothing and the click goes on to
/// the eidolon. Patched on both sides, once per process.</para>
/// </summary>
public static class EidolonHeldClicks
{
    public const string HarmonyId = "seraphhorizons.eidolonheldclicks";

    public static void Patch(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStart)),
            prefix: new HarmonyMethod(typeof(EidolonHeldClicks), nameof(OnHeldUseStartPrefix)));

    /// <summary>Whether a held use is the eidolon's to take instead of the item's.</summary>
    public static bool LeftToEidolon(ItemSlot? slot, BlockSelection? blockSel, EntitySelection? entitySel, EnumHandInteract useType) =>
        useType == EnumHandInteract.HeldItemInteract && blockSel == null
        && entitySel?.Entity is EntityLaborEidolon { Oil: { } oil } && oil.Takes(slot?.Itemstack);

    private static bool OnHeldUseStartPrefix(ItemSlot slot, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType,
                                             ref EnumHandHandling handling)
    {
        if (!LeftToEidolon(slot, blockSel, entitySel, useType))
            return true;
        handling = EnumHandHandling.NotHandled;
        return false;
    }
}
