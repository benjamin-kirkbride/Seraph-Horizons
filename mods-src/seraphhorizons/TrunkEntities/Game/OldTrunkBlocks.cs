using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Placed tree trunk multiblocks, from before trunk entities ran, are deleted as they load, with
/// nothing given back and no migration (the pack's call: they were few, and a trunk can no longer
/// be placed). A Harmony postfix on Logging Expanded's trunk block entity's <c>Initialize</c>
/// queues the controller's removal for the next server tick, and the game's multiblock behaviour
/// takes the filler blocks with it. Server side, while trunk entities run.
/// </summary>
public static class OldTrunkBlocks
{
    public const string BlockEntityType = "LoggingMod.BETreeTrunk";

    private static ICoreServerAPI? _api;

    /// <summary>Patches the trunk block entity; false when it is not found.</summary>
    public static bool Patch(Harmony harmony, ICoreServerAPI api)
    {
        var assembly = api.ModLoader.GetMod(TrunkEntitySystem.LeModId)?.Systems.FirstOrDefault()?.GetType().Assembly;
        var initialize = assembly?.GetType(BlockEntityType)?.GetMethod(nameof(BlockEntity.Initialize),
            BindingFlags.Public | BindingFlags.Instance, [typeof(ICoreAPI)]);
        if (initialize == null)
            return false;
        _api = api;
        harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(OldTrunkBlocks), nameof(InitializePostfix)));
        return true;
    }

    private static void InitializePostfix(BlockEntity __instance, ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || _api == null || !TrunkEntitySystem.Of(api).Enabled)
            return;
        var pos = __instance.Pos.Copy();
        _api.Event.RegisterCallback(_ => Remove(api.World, pos), 0);
    }

    /// <summary>Removes the trunk multiblock whose controller is at <paramref name="pos"/>, if it
    /// is still there; nothing drops.</summary>
    public static void Remove(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        if (block?.Code is not { Domain: TrunkEntitySystem.LeModId } code || !code.Path.StartsWith("treetrunk-", StringComparison.Ordinal))
            return;
        world.BlockAccessor.SetBlock(0, pos);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        world.Logger.Notification("[seraphhorizons] Trunk entities: removed a placed {0} at {1}", code, pos);
    }
}
