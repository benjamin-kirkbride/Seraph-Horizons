using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Trunk blocks in the world. Placed tree trunk multiblocks, from before trunk entities ran, are
/// deleted as they load, with nothing given back and no migration (the pack's call: they were few,
/// and a trunk can no longer be placed). A Harmony postfix on Logging Expanded's trunk block
/// entity's <c>Initialize</c> queues the controller's removal for the next server tick, and the
/// game's multiblock behaviour takes the filler blocks with it. Server side, while trunk entities run.
///
/// And a trunk is never placed as a block: a trunk stack still in a hotbar from before the feature
/// is laid down as a trunk entity instead (<see cref="PlacePrefix"/>, both sides), and the game
/// takes the stack from the hotbar as it would for a placed block.
/// </summary>
public static class OldTrunkBlocks
{
    public const string BlockEntityType = "LoggingMod.BETreeTrunk";
    public const string BlockType = "LoggingMod.BlockTreeTrunk";

    // One id for both sides: a single-player game runs both in one process, so the placement is
    // patched once, by the first side to get there, and unpatched when the last side goes.
    public const string PlaceHarmonyId = "seraphhorizons.trunkplace";

    private static readonly object Lock = new();
    private static int _placeUsers;

    private static ICoreServerAPI? _api;

    /// <summary>Patches Logging Expanded's <c>BlockTreeTrunk.TryPlaceBlock</c> so a trunk is laid
    /// down as an entity, once per process; false when it is not found. Both sides, while trunk
    /// entities run; <see cref="UnpatchPlace"/> undoes it.</summary>
    public static bool PatchPlace()
    {
        var place = AccessTools.TypeByName(BlockType) is { } type
            ? AccessTools.DeclaredMethod(type, nameof(Block.TryPlaceBlock),
                [typeof(IWorldAccessor), typeof(IPlayer), typeof(ItemStack), typeof(BlockSelection), typeof(string).MakeByRefType()])
            : null;
        if (place == null)
            return false;
        lock (Lock)
            if (_placeUsers++ == 0)
                new Harmony(PlaceHarmonyId).Patch(place, prefix: new HarmonyMethod(typeof(OldTrunkBlocks), nameof(PlacePrefix)));
        return true;
    }

    public static void UnpatchPlace()
    {
        lock (Lock)
            if (_placeUsers > 0 && --_placeUsers == 0)
                new Harmony(PlaceHarmonyId).UnpatchAll(PlaceHarmonyId);
    }

    // BlockTreeTrunk.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode): the cell
    // the game places into (already moved off the clicked face) gets a trunk entity lying along the
    // player's view, and true, so the caller takes the stack (the game's placement from a hotbar;
    // Carry On's put-down never gets here, its own place-down is intercepted first). The client only
    // says yes: the server spawns.
    private static bool PlacePrefix(IWorldAccessor __0, IPlayer __1, ItemStack __2, BlockSelection __3, ref string __4, ref bool __result)
    {
        if (__0?.Api is not { } api || __2 == null || __3?.Position == null || !TrunkEntitySystem.Of(api).Enabled)
            return true;
        if (!__2.ResolveBlockOrItem(__0) || Trunks.StoredLogs(__2, __0) <= 0)
        {
            __4 = "loggingmod:trunk-empty";
            __result = false;
            return false;
        }
        if (__0.Side == EnumAppSide.Server)
        {
            var one = __2.Clone();
            one.StackSize = 1;
            var cell = __3.Position;
            float yaw = __1?.Entity?.Pos.Yaw ?? 0f;
            if (TrunkSpawns.Spawn(__0, one, new Vec3d(cell.X + 0.5, cell.Y, cell.Z + 0.5), yaw, cell.dimension) == null)
            {
                __4 = "loggingmod:trunk-noblock";
                __result = false;
                return false;
            }
            __0.PlaySoundAt(new AssetLocation("sounds/block/wood"), cell.X + 0.5, cell.Y, cell.Z + 0.5, null, true, 16, 0.75f);
        }
        __result = true;
        return false;
    }

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
