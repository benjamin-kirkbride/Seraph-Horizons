using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The line the forge's (and the firepit's) dialog shows for what a container will make: the game's
/// <c>BlockSmeltingContainer.GetOutputText</c> is not virtual, and crucibulum calls it on whatever
/// crucible is in the forge, so a postfix (Harmony id <see cref="OreProcessingSystem.HarmonyId"/>)
/// gives the cupel's own text (<see cref="BlockCupel.OutputText"/>) in place of the crucible's
/// "Will create N units of lead".
/// </summary>
public static class CupelText
{
    public static void Patch(Harmony harmony) =>
        harmony.Patch(AccessTools.Method(typeof(BlockSmeltingContainer), nameof(BlockSmeltingContainer.GetOutputText)),
            postfix: new HarmonyMethod(typeof(CupelText), nameof(Postfix)));

    private static void Postfix(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ref string __result)
    {
        if (inputSlot?.Itemstack?.Collectible is BlockCupel cupel)
            __result = cupel.OutputText(world, cookingSlotsProvider)!;
    }
}
