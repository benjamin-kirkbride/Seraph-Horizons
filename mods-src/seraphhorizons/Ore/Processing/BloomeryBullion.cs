using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Bullion is made in the crucible only (README "Ore processing: the handbook's smelting counts"): the
/// game's bloomery takes anything that melts between 1000 and 1500 °C and has a smelted stack, which
/// let it take tetrahedrite's and gold quartz's forms (copper at 1084 °C, gold at 1064 °C) and give their
/// argentiferous copper or doré, a bar the bloomery has no business making. A prefix on
/// <c>BlockEntityBloomery.CanAdd</c> (the interaction help's items) and <c>TryAdd</c> (the right-click)
/// refuses a stack whose smelted stack is bullion (<see cref="Bullion.OreOf"/>), and on the client says
/// why (an in-game error). The handbook's bloomery section leaves them out to match
/// (<see cref="SmeltingHandbook"/>). Not switched: without ore processing there is no bullion, so it
/// refuses nothing. A bar of bullion itself, which smelts plainly to its metal, the bloomery still
/// takes, as it takes an ingot.
/// </summary>
public static class BloomeryBullion
{
    public const string HarmonyId = "seraphhorizons.bloomerybullion";

    /// <summary>The in-game error's code and lang key.</summary>
    public const string ErrorCode = "seraphhorizons-bloomerynobullion", ErrorKey = "seraphhorizons:bloomery-nobullion";

    public static System.Reflection.MethodInfo? CanAddTarget() =>
        AccessTools.DeclaredMethod(typeof(BlockEntityBloomery), nameof(BlockEntityBloomery.CanAdd), [typeof(ItemStack), typeof(int)]);

    public static System.Reflection.MethodInfo? TryAddTarget() =>
        AccessTools.DeclaredMethod(typeof(BlockEntityBloomery), nameof(BlockEntityBloomery.TryAdd), [typeof(IPlayer), typeof(int)]);

    /// <summary>Patches both methods; false, with a warning and nothing patched, when either is not as expected.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var canAdd = CanAddTarget();
        var tryAdd = TryAddTarget();
        if (canAdd?.ReturnType != typeof(bool) || tryAdd?.ReturnType != typeof(bool))
        {
            logger.Warning("[seraphhorizons] Bloomery bullion: BlockEntityBloomery.CanAdd or TryAdd is not as expected; the game changed, "
                           + "so the bloomery takes tetrahedrite and gold quartz and gives their bullion");
            return false;
        }
        harmony.Patch(canAdd, prefix: new HarmonyMethod(typeof(BloomeryBullion), nameof(CanAddPrefix)));
        harmony.Patch(tryAdd, prefix: new HarmonyMethod(typeof(BloomeryBullion), nameof(TryAddPrefix)));
        return true;
    }

    /// <summary>Whether a smelting gives bullion.</summary>
    public static bool SmeltsToBullion(CombustibleProperties? props) =>
        props?.SmeltedStack?.ResolvedItemstack?.Collectible?.Code is { } code && Bullion.OreOf(code.ToString()) != null;

    /// <summary>Whether <paramref name="stack"/>'s smelting gives bullion.</summary>
    public static bool SmeltsToBullion(IWorldAccessor world, ItemStack? stack) =>
        stack?.Collectible is { } c && SmeltsToBullion(c.GetCombustibleProperties(world, stack, null));

    private static bool CanAddPrefix(BlockEntityBloomery __instance, ItemStack stack, ref bool __result)
    {
        if (__instance.Api?.World is not { } world || !SmeltsToBullion(world, stack))
            return true;
        __result = false;
        return false;
    }

    private static bool TryAddPrefix(BlockEntityBloomery __instance, IPlayer byPlayer, ref bool __result)
    {
        if (__instance.Api?.World is not { } world
            || !SmeltsToBullion(world, byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack))
            return true;
        __result = false;
        if (__instance.Api is ICoreClientAPI capi)
            capi.TriggerIngameError(__instance, ErrorCode, Lang.Get(ErrorKey));
        return false;
    }
}
