using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod;

/// <summary>
/// Immersive Woodworking: the powered chopper (the multiblock frame, <c>BlockEntityChopper</c>, not
/// the hand chopping block) drops its finished batch the way the mod's sawmill does: gently, in the
/// cell right in front of its output side, so one hopper sunk into the floor there catches it.
///
/// The chopper's own <c>EjectBatch(ItemStack template, int dropCount)</c> spawns the pieces 0.7
/// out from the master block's centre, 0.8 up, and throws them outward at 0.084 with up to ±0.04
/// sideways: they fly over the cell in front and land 2 blocks out, spread over three cells. A
/// prefix replaces it with the same split into piles, dropped at rest over the middle of that cell
/// (<see cref="ChopperEject"/>). The sawmill spawns its planks over the middle of the cell behind
/// its footprint, 0.4 up, with a 0.02 push.
///
/// Immersive Woodworking is not referenced at build time. The chopper is found by name, and if
/// <c>EjectBatch</c> or its <c>Facing</c> is missing or changed the tweak logs a warning and leaves
/// the chopper as it is.
/// </summary>
public static class ChopperOutput
{
    public const string ModId = "immersivewoodworking";
    public const string ChopperType = "ImmersiveWoodworking.BlockEntityChopper";

    private static PropertyInfo? _facing;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Prefixes <c>EjectBatch</c>. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var chopper = AccessTools.TypeByName(ChopperType);
        var eject = chopper == null
            ? null
            : AccessTools.DeclaredMethod(chopper, "EjectBatch", [typeof(ItemStack), typeof(int)]);
        _facing = chopper == null ? null : AccessTools.Property(chopper, "Facing");
        if (chopper == null || !typeof(BlockEntity).IsAssignableFrom(chopper) || eject?.ReturnType != typeof(void)
            || _facing?.PropertyType != typeof(BlockFacing) || _facing.GetMethod == null)
        {
            logger.Warning($"[seraphhorizons] {ChopperType} does not have EjectBatch(ItemStack, int) and Facing as "
                           + "expected; Immersive Woodworking changed, so its chopper still throws its output");
            return false;
        }

        harmony.Patch(eject, prefix: new HarmonyMethod(typeof(ChopperOutput), nameof(EjectBatchPrefix)));
        return true;
    }

    /// <summary>Drops the batch in front of the output side (<see cref="ChopperEject.Plan"/>) in
    /// place of the chopper's own throw. The output side is opposite the frame's facing, as in
    /// the original. Arguments by position, so a renamed parameter does not break the patch.</summary>
    public static bool EjectBatchPrefix(BlockEntity __instance, ItemStack __0, int __1)
    {
        ItemStack template = __0;
        if (template.StackSize <= 0)
            return false;
        Vec3i output = ((BlockFacing)_facing!.GetValue(__instance)!).Opposite.Normali;
        BlockPos pos = __instance.Pos;
        IWorldAccessor world = __instance.Api.World;
        foreach (var drop in ChopperEject.Plan(pos.X, pos.Y, pos.Z, output.X, output.Z, template.StackSize, __1,
                     template.Collectible.MaxStackSize, world.Rand.NextDouble))
        {
            ItemStack stack = template.Clone();
            stack.StackSize = drop.Count;
            world.SpawnItemEntity(stack, new Vec3d(drop.X, drop.Y, drop.Z), new Vec3d());
        }
        return false;
    }
}
