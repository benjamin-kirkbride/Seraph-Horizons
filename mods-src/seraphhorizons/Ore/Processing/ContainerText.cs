using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>A smelting container of ore processing's that says itself what it will make: the cupel
/// (<see cref="BlockCupel"/>), the liquation pan (<see cref="BlockLiquationPan"/>).</summary>
public interface IOreContainerText
{
    /// <summary>What the container will do with its charge where it is, for the forge's or the
    /// firepit's dialog; null with nothing in it.</summary>
    string? OutputText(IWorldAccessor world, ISlotProvider provider);
}

/// <summary>
/// The line the forge's (and the firepit's) dialog shows for what a container will make: the game's
/// <c>BlockSmeltingContainer.GetOutputText</c> is not virtual, and crucibulum calls it on whatever
/// crucible is in the forge, so a postfix (Harmony id <see cref="OreProcessingSystem.HarmonyId"/>)
/// gives an <see cref="IOreContainerText"/>'s own text in place of the crucible's "Will create N units
/// of lead".
/// </summary>
public static class ContainerText
{
    public static void Patch(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(BlockSmeltingContainer), nameof(BlockSmeltingContainer.GetOutputText)),
            postfix: new HarmonyMethod(typeof(ContainerText), nameof(Postfix)));

    private static void Postfix(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ref string __result)
    {
        if (inputSlot?.Itemstack?.Collectible is IOreContainerText container)
            __result = container.OutputText(world, cookingSlotsProvider)!;
    }
}
