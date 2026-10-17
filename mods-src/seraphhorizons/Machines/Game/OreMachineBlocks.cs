using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// An ore machine's frame, its controller cell (<c>{machine}-{side}</c>, class
/// <c>seraphhorizons.OreMachine</c>; #711): the frame as placed, the first of its build. Its block type
/// names the machine in <c>attributes.oreMachine.machine</c> (its rig <c>config/{machine}-rig.json</c>,
/// its build <c>config/builds/{machine}.json</c>) and the machine's own part <c>requires</c> besides its
/// stages' in <c>attributes.oreMachine.extraRequires</c>; its block entity is the machine's own
/// <see cref="BEOreMachine"/>. Placing it needs room for every cell of the rig and stamps a cell block into
/// each (<see cref="BlockOreMachineCell"/>, the power cell's <see cref="BlockOreMachineCellPower"/>);
/// breaking it (or a cell) gives back the frame, everything fitted and what it holds.
/// </summary>
public class BlockOreMachine : Block
{
    public const string ClassName = "seraphhorizons.OreMachine";

    /// <summary>The machine's id (<c>attributes.oreMachine.machine</c>), or the code's first part.</summary>
    public string Machine => Attributes?["oreMachine"]?["machine"]?.AsString(null) ?? Code.FirstCodePart();

    /// <summary>The machine's own part requires (<c>attributes.oreMachine.extraRequires</c>).</summary>
    public string[] ExtraRequires => Attributes?["oreMachine"]?["extraRequires"]?.AsArray<string>([])?.OfType<string>().ToArray() ?? [];

    public OreMachineRig? Rig => api == null ? null : OreMachinesSystem.Of(api).Rig(Machine, ExtraRequires);

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _ = Rig;
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        if (Rig is not { } rig || !Sides.TryParse(Variant["side"], out var side))
        {
            failureCode = "__ignore__";
            return false;
        }
        foreach (var cell in rig.OtherCells)
        {
            var w = Footprint.ToWorld(cell.Pos, side);
            var at = blockSel.Clone();
            at.Position = blockSel.Position.AddCopy(w.X, w.Y, w.Z);
            if (!base.CanPlaceBlock(world, byPlayer, at, ref failureCode))
                return false;
        }
        return true;
    }

    /// <summary>Places the variant whose front faces the player (<see cref="OreMachineRig.PlacedSide"/>):
    /// the block they clicked becomes the controller.</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = OreMachineRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEOreMachine)?.PlaceCells();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEOreMachine machine
            ? machine.OnInteract(byPlayer, blockSel)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEOreMachine machine ? [.. drops, .. machine.AssemblyDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        new(world.GetBlock(CodeWithVariant("side", "north")) is { Id: > 0 } north ? north : this);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEOreMachine)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEOreMachine)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEOreMachine { Built: true }
            ? Lang.Get($"seraphhorizons:block-{Machine}")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is, how to build it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = world.BlockAccessor.GetBlockEntity(selection.Position) is BEOreMachine machine
            ? machine.InteractionHelp(forPlayer)
            : [];
        return help.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}

/// <summary>
/// A cell of an ore machine around its controller (<c>seraphhorizons:oremachine-cell</c>, one block for
/// every machine; #711). Interaction, breaking, the pick-block stack, particles, name, info and help go to
/// the controller, as the other machines' ghosts do; its boxes are its cell's from the rig. A face of it
/// that is one of the machine's anchors (a water pipe's, a chute's) carries a block attached to it.
/// </summary>
public class BlockOreMachineCell : Block
{
    public const string ClassName = "seraphhorizons.OreMachineCell";
    public static readonly AssetLocation CellCode = new(OreMachinesSystem.Domain, "oremachine-cell");

    private static bool Controller(IBlockAccessor blockAccessor, BlockPos pos, out BlockOreMachine block, out BlockPos principal)
    {
        principal = (blockAccessor.GetBlockEntity(pos) as BEOreMachineCell)?.Principal!;
        block = (principal != null ? blockAccessor.GetBlock(principal) as BlockOreMachine : null)!;
        return block != null;
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
        if (!Controller(world.BlockAccessor, blockSel.Position, out var block, out var principal))
            return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
        return block.OnGettingBroken(player, At(blockSel, principal), itemslot, remainingResistance, dt, counter);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (!Controller(world.BlockAccessor, pos, out var block, out var principal))
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }
        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.BuildOrBreak))
            return;
        block.OnBlockBroken(world, principal, byPlayer, dropQuantityMultiplier);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Controller(world.BlockAccessor, blockSel.Position, out _, out var principal)
            && world.BlockAccessor.GetBlockEntity(principal) is BEOreMachine machine)
            return machine.OnInteract(byPlayer, blockSel);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
        Controller(world.BlockAccessor, selection.Position, out var block, out var principal)
            ? block.GetPlacedBlockInteractionHelp(world, At(selection, principal), forPlayer)
            : base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var block, out var principal)
            ? block.GetPlacedBlockName(world, principal)
            : base.GetPlacedBlockName(world, pos);

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        Controller(world.BlockAccessor, pos, out var block, out var principal)
            ? block.OnPickBlock(world, principal)
            : new ItemStack(this);

    public override Cuboidf GetParticleBreakBox(IBlockAccessor blockAccess, BlockPos pos, BlockFacing facing) =>
        Controller(blockAccess, pos, out var block, out var principal)
            ? block.GetParticleBreakBox(blockAccess, principal, facing)
            : base.GetParticleBreakBox(blockAccess, pos, facing);

    public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1) =>
        Controller(capi.World.BlockAccessor, pos, out var block, out var principal)
            ? block.GetRandomColor(capi, principal, facing, rndIndex)
            : 0;

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEOreMachineCell)?.Machine(blockAccessor)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEOreMachineCell)?.Machine(blockAccessor)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi? attachmentArea = null) =>
        (blockAccessor.GetBlockEntity(pos) as BEOreMachineCell)?.Machine(blockAccessor)?.AnchorFaceAt(pos, blockFace) == true
        || base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
}

/// <summary>The cell that takes the axle (<c>seraphhorizons:oremachine-cellpower-{face}</c>, face being the
/// world face the axle meets it at, the rig's <c>powerFace</c> turned to the machine's facing): a mechanical
/// power connector on that face only, carrying <see cref="BEBehaviorMachineMP"/>.</summary>
public class BlockOreMachineCellPower : BlockOreMachineCell, IMechanicalPowerBlock
{
    public new const string ClassName = "seraphhorizons.OreMachineCellPower";

    /// <summary>The power cell's block for a world face.</summary>
    public static AssetLocation CodeFor(BlockFacing face) => new(OreMachinesSystem.Domain, "oremachine-cellpower-" + face.Code);

    public BlockFacing PowerFace => BlockFacing.FromCode(Variant["face"]) ?? BlockFacing.UP;

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) => face == PowerFace;

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }
}
