using System.Text;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// An invisible cell of the crucible furnace around its controller: the other seventeen cells of the
/// brickwork, and the six hollow cells over the hole's rows, where the lid stands open and swings.
/// Interaction, lighting, breaking, the pick-block stack, particles, name, info and help all go to the
/// controller; its collision and selection boxes are its cell's by the furnace's state (none in a
/// hollow cell but the lid's).
/// </summary>
public class BlockCrucibleFurnaceGhost : Block, IIgnitable
{
    private static BlockPos? PrincipalAt(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECrucibleFurnaceGhost)?.Principal;

    /// <summary>The controller block at this ghost's principal, with its position.</summary>
    private static bool Controller(IBlockAccessor blockAccessor, BlockPos pos, out BlockCrucibleFurnace furnace, out BlockPos principal)
    {
        principal = PrincipalAt(blockAccessor, pos)!;
        furnace = (principal != null ? blockAccessor.GetBlock(principal) as BlockCrucibleFurnace : null)!;
        return furnace != null;
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
        if (!Controller(world.BlockAccessor, blockSel.Position, out var furnace, out var principal))
            return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
        return furnace.OnGettingBroken(player, At(blockSel, principal), itemslot, remainingResistance, dt, counter);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (!Controller(world.BlockAccessor, pos, out var furnace, out var principal))
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }
        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.BuildOrBreak))
            return;
        furnace.OnBlockBroken(world, principal, byPlayer, dropQuantityMultiplier);
    }

    // The click keeps its own cell and hit point: the furnace works out which part of it was clicked.
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Controller(world.BlockAccessor, blockSel.Position, out _, out var principal))
            return BlockCrucibleFurnace.InteractAt(world, byPlayer, principal, blockSel);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (Controller(world.BlockAccessor, selection.Position, out var furnace, out var principal))
            return furnace.GetPlacedBlockInteractionHelp(world, At(selection, principal), forPlayer);
        return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var furnace, out var principal)
            ? furnace.GetPlacedBlockName(world, principal)
            : base.GetPlacedBlockName(world, pos);

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var furnace, out var principal)
            ? furnace.OnPickBlock(world, principal)
            : new ItemStack(world.GetBlock(BlockCrucibleFurnace.ItemCode));

    public override Cuboidf GetParticleBreakBox(IBlockAccessor blockAccess, BlockPos pos, BlockFacing facing) =>
        Controller(blockAccess, pos, out var furnace, out var principal)
            ? furnace.GetParticleBreakBox(blockAccess, principal, facing)
            : base.GetParticleBreakBox(blockAccess, pos, facing);

    public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1) =>
        Controller(capi.World.BlockAccessor, pos, out var furnace, out var principal)
            ? furnace.GetRandomColor(capi, principal, facing, rndIndex)
            : 0;

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECrucibleFurnaceGhost)?.CollisionBoxes(blockAccessor) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECrucibleFurnaceGhost)?.CellBoxes(blockAccessor) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        BlockCrucibleFurnace.IgniteState(byEntity, pos, secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling) =>
        BlockCrucibleFurnace.IgniteOver(byEntity, pos, secondsIgniting, ref handling);

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        BlockCrucibleFurnace.IgniteStack(byEntity, pos, secondsIgniting);
}

/// <summary>A ghost cell of the crucible furnace: remembers the controller's position and takes its
/// cell's boxes from it.</summary>
public class BECrucibleFurnaceGhost : BlockEntity, IMachineGhost
{
    public BlockPos? Principal { get; set; }

    public BECrucibleFurnace? Furnace =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BECrucibleFurnace : null;

    public Cuboidf[]? CellBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BECrucibleFurnace furnace ? furnace.CellBoxes(Pos) : null;

    public Cuboidf[]? CollisionBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BECrucibleFurnace furnace ? furnace.CollisionBoxes(Pos) : null;

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        int x = tree.GetInt("cx", -1), y = tree.GetInt("cy", -1), z = tree.GetInt("cz", -1);
        Principal = x == -1 && y == -1 && z == -1 ? null : new BlockPos(x, y, z);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("cx", Principal?.X ?? -1);
        tree.SetInt("cy", Principal?.Y ?? -1);
        tree.SetInt("cz", Principal?.Z ?? -1);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        Furnace?.GetBlockInfo(forPlayer, dsc);
    }
}
