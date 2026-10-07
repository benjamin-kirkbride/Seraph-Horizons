using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Logging Expanded (<c>loggingmod</c>): a Trunk Heating Rack (<c>loggingmod:resinrack-*</c>,
/// <c>BlockResinRack</c>) placed onto the top face of a block stands on that block, one cell up,
/// with the cell between left for a firepit, unless the block is a firepit already.
///
/// The rack's legs reach a full block below its own cell: Logging Expanded builds it one block above
/// the ground (binding a heating rack frame) and reads its firepit from the cell under it. Placed
/// from a stack (the hotbar, a creative pick, Carry On's place-down, which runs the block's
/// <c>TryPlaceBlock</c> too) it goes in the cell over the aimed-at face like any block, legs drawn
/// inside the floor.
///
/// A prefix on the rack's <c>DoPlaceBlock</c> (the one method on the rack's own class every
/// placement from a stack goes through: its <c>HorizontalOrientable</c> behavior runs
/// <c>CanPlaceBlock</c> and then <c>DoPlaceBlock</c>) moves <c>blockSel.Position</c> up one, in place,
/// when the placement offset the selection off an up face (<c>DidOffset</c>, face up), the block
/// aimed at (the cell below) is neither replaceable nor a firepit, and the cell above can take the
/// rack (in the world, replaceable, no entity in it, the player may build there). The position is
/// moved in place, not replaced, so every caller holding the same <see cref="BlockSelection"/> sees
/// the lifted cell: the game's client sends it to the server, and Carry On's <c>TryPlaceDownAt</c>
/// reports it as <c>placedAt</c> and restores the block entity's tree and plays the sound there. The
/// face stays up and <c>DidOffset</c> set: the selection still says "placed off the up face", and a
/// server that receives a lifted position finds air below it, so it does not lift again. If the
/// placement fails after the lift, a postfix moves the position back down (the game's client undoes
/// its own offset by the face).
///
/// Logging Expanded is not referenced at build time: if the rack or the method is gone, the tweak
/// logs a warning and leaves the rack as it ships. Patched on both sides (the client predicts the
/// placement, the server makes it), once per process with its own Harmony id, since a singleplayer
/// game runs both in one.
/// </summary>
public static class HeatingRackPlacement
{
    public const string ModId = "loggingmod";
    public const string HarmonyId = "seraphhorizons.heatingrackplacement";
    public const string RackBlockType = "LoggingMod.BlockResinRack";

    private static MethodInfo? _doPlaceBlock;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Finds the rack's <c>DoPlaceBlock</c>. Returns whether it is there as expected; if
    /// not, logs that Logging Expanded changed.</summary>
    public static bool Bind(ILogger logger)
    {
        var rack = AccessTools.TypeByName(RackBlockType);
        _doPlaceBlock = rack == null ? null
            : AccessTools.DeclaredMethod(rack, nameof(Block.DoPlaceBlock),
                [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), typeof(ItemStack)]);
        if (rack != null && typeof(Block).IsAssignableFrom(rack) && _doPlaceBlock?.ReturnType == typeof(bool))
            return true;
        logger.Warning("[seraphhorizons] Logging Expanded's heating rack does not look as expected; Logging Expanded "
                       + "changed, so a heating rack placed on a block stands in it, as it ships");
        return false;
    }

    /// <summary>Applies the patch with <paramref name="harmony"/>, unless it is in already (the
    /// other side of a singleplayer game applied it).</summary>
    public static void Patch(Harmony harmony)
    {
        if (Harmony.HasAnyPatches(HarmonyId))
            return;
        harmony.Patch(_doPlaceBlock,
            prefix: new HarmonyMethod(typeof(HeatingRackPlacement), nameof(DoPlaceBlockPrefix)),
            postfix: new HarmonyMethod(typeof(HeatingRackPlacement), nameof(DoPlaceBlockPostfix)));
    }

    /// <summary>Whether the block at <paramref name="pos"/> is a firepit, in any of its stages.</summary>
    public static bool IsFirepit(IBlockAccessor blocks, BlockPos pos) =>
        blocks.GetBlock(pos) is BlockFirepit || blocks.GetBlockEntity(pos) is BlockEntityFirepit;

    /// <summary>Whether a placement of <paramref name="rack"/> at <paramref name="sel"/> should go one
    /// cell higher: it was offset off an up face, the block aimed at is neither replaceable nor a
    /// firepit, and the cell above can take the rack.</summary>
    public static bool ShouldLift(IWorldAccessor world, IPlayer? byPlayer, BlockSelection? sel, Block rack)
    {
        if (sel?.Position == null || !sel.DidOffset || sel.Face != BlockFacing.UP)
            return false;
        var blocks = world.BlockAccessor;
        var below = sel.Position.DownCopy();
        if (blocks.GetBlock(below).IsReplacableBy(rack) || IsFirepit(blocks, below))
            return false;
        var above = sel.Position.UpCopy();
        if (!blocks.IsValidPos(above) || !blocks.GetBlock(above).IsReplacableBy(rack))
            return false;
        var boxes = rack.GetCollisionBoxes(blocks, above);
        if (boxes is { Length: > 0 } && world.GetIntersectingEntities(above, boxes, (Entity e) => e.IsInteractable).Length != 0)
            return false;
        return byPlayer == null || world.Claims == null
               || world.Claims.TestAccess(byPlayer, above, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
    }

    public static void DoPlaceBlockPrefix(Block __instance, IWorldAccessor __0, IPlayer __1, BlockSelection __2, out bool __state)
    {
        __state = ShouldLift(__0, __1, __2, __instance);
        if (__state)
            __2.Position.Up();
    }

    public static void DoPlaceBlockPostfix(bool __result, BlockSelection __2, bool __state)
    {
        if (__state && !__result)
            __2.Position.Down();
    }
}
