using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace BuckingSawmill;

/// <summary>
/// An invisible cell of the mill around its controller. Interaction, breaking, the pick-block
/// stack, particles, name, info and help all go to the controller, as Immersive Woodworking's
/// sawmill ghosts do; its collision and selection boxes are its cell's from the rig.
/// </summary>
public class BlockMillGhost : Block
{
    private static BlockPos? PrincipalAt(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMillGhost)?.Principal;

    /// <summary>The controller block at this ghost's principal, with its position.</summary>
    private static bool Controller(IBlockAccessor blockAccessor, BlockPos pos, out BlockBuckingMill mill, out BlockPos principal)
    {
        principal = PrincipalAt(blockAccessor, pos)!;
        mill = (principal != null ? blockAccessor.GetBlock(principal) as BlockBuckingMill : null)!;
        return mill != null;
    }

    private static BlockSelection At(BlockSelection sel, BlockPos pos)
    {
        var clone = sel.Clone();
        clone.Position = pos;
        return clone;
    }

    public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        var world = player?.Entity?.World ?? api.World;
        if (!Controller(world.BlockAccessor, blockSel.Position, out var mill, out var principal))
            return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
        return mill.OnGettingBroken(player, At(blockSel, principal), itemslot, remainingResistance, dt, counter);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (!Controller(world.BlockAccessor, pos, out var mill, out var principal))
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }
        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.BuildOrBreak))
            return;
        mill.OnBlockBroken(world, principal, byPlayer, dropQuantityMultiplier);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Controller(world.BlockAccessor, blockSel.Position, out var mill, out var principal))
            return mill.OnBlockInteractStart(world, byPlayer, At(blockSel, principal));
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (Controller(world.BlockAccessor, selection.Position, out var mill, out var principal))
            return mill.GetPlacedBlockInteractionHelp(world, At(selection, principal), forPlayer);
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var mill, out var principal)
            ? mill.GetPlacedBlockName(world, principal)
            : base.GetPlacedBlockName(world, pos);

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var mill, out var principal)
            ? mill.OnPickBlock(world, principal)
            : new ItemStack(world.GetBlock(BlockBuckingMill.ItemCode));

    public override Cuboidf GetParticleBreakBox(IBlockAccessor blockAccess, BlockPos pos, BlockFacing facing) =>
        Controller(blockAccess, pos, out var mill, out var principal)
            ? mill.GetParticleBreakBox(blockAccess, principal, facing)
            : base.GetParticleBreakBox(blockAccess, pos, facing);

    public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1) =>
        Controller(capi.World.BlockAccessor, pos, out var mill, out var principal)
            ? mill.GetRandomColor(capi, principal, facing, rndIndex)
            : 0;

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMillGhost)?.CellBoxes(blockAccessor) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMillGhost)?.CellBoxes(blockAccessor) ?? base.GetSelectionBoxes(blockAccessor, pos);
}
