using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Keeps trunks out of survival players' inventories without a storage flag: a trunk item sits in
/// and moves between slots like any block, but <c>TryGiveItemstack</c>, which every give path uses
/// (a station or machine unload, Logging Expanded's own, picking up an item entity), refuses one
/// unless the player is in creative mode. The caller then drops it, and the spawn swap lays a
/// trunk entity. Server side.
/// </summary>
public static class TrunkPockets
{
    /// <summary>Patches the game's <c>PlayerInventoryManager.TryGiveItemstack</c>; false when it
    /// is not as expected (trunks can then be given to inventories).</summary>
    public static bool Patch(Harmony harmony)
    {
        var type = AccessTools.TypeByName("Vintagestory.Common.PlayerInventoryManager");
        var method = type == null ? null : AccessTools.Method(type, "TryGiveItemstack", [typeof(ItemStack), typeof(bool)]);
        if (method == null)
            return false;
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(TrunkPockets), nameof(Prefix)));
        return true;
    }

    /// <summary>Whether <paramref name="player"/> may be given <paramref name="stack"/>.</summary>
    public static bool MayGive(IPlayer? player, ItemStack? stack) =>
        !Trunks.IsTrunk(stack) || player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    private static bool Prefix(ItemStack itemstack, IPlayer ___player, ref bool __result)
    {
        if (MayGive(___player, itemstack))
            return true;
        __result = false;
        return false;
    }
}
