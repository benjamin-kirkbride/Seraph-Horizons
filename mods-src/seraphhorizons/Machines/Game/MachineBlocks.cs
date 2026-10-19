using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The frame of a machine on the shared base, its controller cell (<c>{machine}-{side}</c>, class
/// <c>seraphhorizons.Machine</c>; #711): the frame as placed, the first of its build. Its block type names
/// the machine in <c>attributes.machine.machine</c> (else the code's first part: its rig
/// <c>config/{machine}-rig.json</c>, its build <c>config/builds/{machine}.json</c>) and an ore machine's own
/// part <c>requires</c> besides its stages' in <c>attributes.machine.extraRequires</c>; its block entity is the
/// machine's own <see cref="BEMachine"/>. Placing it needs room for every cell of the rig and stamps a cell
/// block into each (<see cref="BlockMachineCell"/>, the power cell's <see cref="BlockMachineCellPower"/>);
/// breaking it (or a cell) gives back the frame, everything fitted and what it holds. A right-click, and
/// right-click held on a hand machine, goes to the block entity. A machine that burns
/// (<see cref="BEMachine.Ignitable"/>, the stall roaster) is lit with a firestarter or a torch on any of its
/// cells, as the forge is (<see cref="MachineFire"/>).
/// </summary>
public class BlockMachine : Block, IMechanicalPowerBlock, IIgnitable
{
    public const string ClassName = "seraphhorizons.Machine";

    /// <summary>The machine's id (<c>attributes.machine.machine</c>), or the code's first part.</summary>
    public string Machine => Attributes?["machine"]?["machine"]?.AsString(null) ?? Code.FirstCodePart();

    /// <summary>Whether the machine stands on the block below its controller and needs that block's solid top
    /// (<c>attributes.machine.needsSolidGround</c>: the rocker on its rockers): it is placed only there, and
    /// breaks when the top goes.</summary>
    public bool NeedsSolidGround => Attributes?["machine"]?["needsSolidGround"]?.AsBool(false) ?? false;

    /// <summary>An ore machine's own part requires (<c>attributes.machine.extraRequires</c>).</summary>
    public string[] ExtraRequires => Attributes?["machine"]?["extraRequires"]?.AsArray<string>([])?.OfType<string>().ToArray() ?? [];

    /// <summary>The machine's rig: the one its system registered (<see cref="MachinesSystem.RegisterRig"/>), else
    /// read as an ore machine's (<see cref="OreMachinesSystem.Rig"/>).</summary>
    public IMachineRig? Rig
    {
        get
        {
            if (api == null)
                return null;
            var rig = MachinesSystem.Of(api).RegisteredRig(Machine, out bool registered);
            return registered ? rig : OreMachinesSystem.Of(api).Rig(Machine, ExtraRequires);
        }
    }

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
        if (NeedsSolidGround && !SolidUnder(world.BlockAccessor, blockSel.Position))
        {
            failureCode = "requiresolidground";
            return false;
        }
        foreach (var cell in rig.OtherCells())
        {
            var w = Footprint.ToWorld(cell.Pos, side);
            var at = blockSel.Clone();
            at.Position = blockSel.Position.AddCopy(w.X, w.Y, w.Z);
            if (!base.CanPlaceBlock(world, byPlayer, at, ref failureCode))
                return false;
        }
        return true;
    }

    /// <summary>Places the variant the rig turns to the player (<see cref="IMachineRig.PlacedSide"/>: native
    /// north towards them, so the facing is the way they look, unless the rig says otherwise), its
    /// <see cref="IMachineRig.PlaceCell"/> on the block they clicked (the controller, by default).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var look = Sides.FromNormal(away.Normali.X, away.Normali.Z);
        var rig = Rig;
        var side = rig?.PlacedSide(look) ?? OreMachineRig.PlacedSide(look);
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        var at = blockSel;
        if (rig is { PlaceCell: var place } && place != Int3.Zero)
        {
            var w = Footprint.ToWorld(place, side);
            at = blockSel.Clone();
            at.Position = blockSel.Position.AddCopy(-w.X, -w.Y, -w.Z);
        }
        if (!block.CanPlaceBlock(world, byPlayer, at, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, at, itemstack);
    }

    private static bool SolidUnder(IBlockAccessor ba, BlockPos pos) => ba.GetBlock(pos.DownCopy()).SideSolid[BlockFacing.UP.Index];

    /// <summary>The ground going from under a machine that needs it breaks the machine; a block set against a
    /// controller that takes the axle itself (the amalgam pan's, from below) on its power face connects it.</summary>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side == EnumAppSide.Server && NeedsSolidGround && neibpos.Equals(pos.DownCopy()) && !SolidUnder(world.BlockAccessor, pos))
        {
            world.BlockAccessor.BreakBlock(pos, null);
            return;
        }
        if (world.BlockAccessor.GetBlockEntity(pos) is BEMachine { PowerAtController: true } machine
            && neibpos.Equals(pos.AddCopy(machine.PowerFace)))
            machine.GetBehavior<BEBehaviorMPBase>()?.tryConnect(machine.PowerFace);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEMachine)?.PlaceCells();
    }

    /// <summary>On a frame's stack: it places with every stage fitted (<see cref="BEMachine.OnBlockPlaced"/>; the
    /// creative inventory's assembled eidolon gantry).</summary>
    public const string AssembledAttribute = "assembled";

    /// <summary>Whether <paramref name="stack"/> places built (<see cref="AssembledAttribute"/>).</summary>
    public static bool Assembled(ItemStack? stack) => stack?.Attributes?.GetBool(AssembledAttribute) == true;

    // An assembled stack is named by the machine's seraphhorizons:{machine}-assembled ("{0} (assembled)").
    public override string GetHeldItemName(ItemStack itemStack)
    {
        string name = base.GetHeldItemName(itemStack);
        string key = $"seraphhorizons:{Machine}-assembled";
        return Assembled(itemStack) && Lang.HasTranslation(key) ? Lang.Get(key, name) : name;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        string key = $"seraphhorizons:{Machine}-assembled-info";
        if (Assembled(inSlot.Itemstack) && Lang.HasTranslation(key))
            dsc.AppendLine(Lang.Get(key));
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEMachine
            ? InteractAt(world, byPlayer, blockSel, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        StepAt(world, byPlayer, blockSel.Position, secondsUsed);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        ReleaseAt(world, byPlayer, blockSel.Position);

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
                                               EnumItemUseCancelReason cancelReason)
    {
        ReleaseAt(world, byPlayer, blockSel.Position);
        return true;
    }

    /// <summary>A right-click on one of the machine's cells, <paramref name="principal"/> the controller's
    /// position: the machine's, where the player may use it.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockPos principal)
    {
        if (!world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(principal) is BEMachine machine && machine.OnInteract(byPlayer, blockSel);
    }

    /// <summary>Right-click still held: whether the player goes on working a hand machine.</summary>
    public static bool StepAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal, float secondsUsed) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEMachine machine && machine.OnInteractStep(byPlayer, secondsUsed);

    /// <summary>Right-click let go (or the interaction cancelled).</summary>
    public static void ReleaseAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        (world.BlockAccessor.GetBlockEntity(principal) as BEMachine)?.OnInteractStop(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEMachine machine ? [.. drops, .. machine.AssemblyDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
        new(world.GetBlock(CodeWithVariant("side", "north")) is { Id: > 0 } north ? north : this);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachine)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachine)?.SelectionBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    // A face of the controller that is one of the machine's anchors (a water pipe's) carries a block attached to it.
    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi? attachmentArea = null) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachine)?.AnchorFaceAt(pos, blockFace) == true
        || base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);

    // A machine whose power cell is its controller's (the shaking table): its frame's block type carries
    // seraphhorizons.MachineMP, and the axle connects on the power face only.
    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEMachine { PowerAtController: true } machine && machine.PowerFace == face;

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        MachineFire.IgniteState(byEntity, pos, secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling) =>
        MachineFire.IgniteOver(byEntity, pos, secondsIgniting, ref handling);

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        MachineFire.IgniteStack(byEntity, pos, secondsIgniting);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEMachine { Complete: true }
            ? Lang.Get($"seraphhorizons:block-{Machine}")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is, how to build it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = world.BlockAccessor.GetBlockEntity(selection.Position) is BEMachine machine
            ? machine.InteractionHelp(forPlayer)
            : [];
        return help.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}

/// <summary>
/// A cell of a machine on the shared base around its controller (<c>seraphhorizons:machine-cell</c>, one
/// block for every machine; #711), in place of each machine's own ghost blocks. Interaction (a click, or
/// right-click held on a hand machine), breaking, the pick-block stack, particles, name, info and help go
/// to the controller; its boxes are its cell's from the rig. A face of it that is one of an ore machine's
/// anchors (a water pipe's, a chute's) carries a block attached to it. A fire lit on it lights the machine
/// (<see cref="MachineFire"/>).
/// </summary>
public class BlockMachineCell : Block, IIgnitable
{
    public const string ClassName = "seraphhorizons.MachineCell";
    public static readonly AssetLocation CellCode = new(MachinesSystem.Domain, "machine-cell");

    private static bool Controller(IBlockAccessor blockAccessor, BlockPos pos, out BlockMachine block, out BlockPos principal)
    {
        principal = (blockAccessor.GetBlockEntity(pos) as BEMachineCell)?.Principal!;
        block = (principal != null ? blockAccessor.GetBlock(principal) as BlockMachine : null)!;
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
        if (Controller(world.BlockAccessor, blockSel.Position, out _, out var principal))
            return BlockMachine.InteractAt(world, byPlayer, blockSel, principal);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        Controller(world.BlockAccessor, blockSel.Position, out _, out var principal)
        && BlockMachine.StepAt(world, byPlayer, principal, secondsUsed);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Controller(world.BlockAccessor, blockSel.Position, out _, out var principal))
            BlockMachine.ReleaseAt(world, byPlayer, principal);
    }

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
                                               EnumItemUseCancelReason cancelReason)
    {
        if (Controller(world.BlockAccessor, blockSel.Position, out _, out var principal))
            BlockMachine.ReleaseAt(world, byPlayer, principal);
        return true;
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

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        MachineFire.IgniteState(byEntity, pos, secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling) =>
        MachineFire.IgniteOver(byEntity, pos, secondsIgniting, ref handling);

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        MachineFire.IgniteStack(byEntity, pos, secondsIgniting);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachineCell)?.Machine(blockAccessor)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachineCell)?.Machine(blockAccessor)?.SelectionBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi? attachmentArea = null) =>
        (blockAccessor.GetBlockEntity(pos) as BEMachineCell)?.Machine(blockAccessor)?.AnchorFaceAt(pos, blockFace) == true
        || base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
}

/// <summary>The cell that takes the axle (<c>seraphhorizons:machine-cellpower-{face}</c>, face being the
/// world face the axle meets it at, the rig's power anchor's face turned to the machine's facing): a
/// mechanical power connector on that face only, carrying <see cref="BEBehaviorMachineMP"/>.</summary>
public class BlockMachineCellPower : BlockMachineCell, IMechanicalPowerBlock
{
    public new const string ClassName = "seraphhorizons.MachineCellPower";

    /// <summary>The power cell's block for a world face.</summary>
    public static AssetLocation CodeFor(BlockFacing face) => new(MachinesSystem.Domain, "machine-cellpower-" + face.Code);

    public BlockFacing PowerFace => BlockFacing.FromCode(Variant["face"]) ?? BlockFacing.UP;

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) => face == PowerFace;

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }
}

/// <summary>
/// Lighting a machine that burns (<see cref="BEMachine.Ignitable"/>: the stall roaster), as the forge
/// and the crucible furnace are lit: a firestarter or a torch held on any of its cells for 1.5 s lights it, if
/// it can be lit (<see cref="BEMachine.CannotIgnite"/>; else it says why, once); a torch is lit from it while
/// it burns. A machine that never burns is not ignitable.
/// </summary>
public static class MachineFire
{
    /// <summary>The machine whose controller or cell is at <paramref name="pos"/>.</summary>
    public static BEMachine? MachineAt(IBlockAccessor ba, BlockPos pos) => ba.GetBlockEntity(pos) switch
    {
        BEMachine machine => machine,
        BEMachineCell cell => cell.Machine(ba),
        _ => null,
    };

    public static EnumIgniteState IgniteState(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (MachineAt(byEntity.World.BlockAccessor, pos) is not { Ignitable: true } machine)
            return EnumIgniteState.NotIgnitable;
        if (machine.CannotIgnite() is not null)
        {
            if (secondsIgniting == 0 && byEntity.World.Side == EnumAppSide.Server)
                machine.Ignite((byEntity as EntityPlayer)?.Player);   // says why not
            return EnumIgniteState.NotIgnitablePreventDefault;
        }
        return secondsIgniting >= 1.5f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public static void IgniteOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (secondsIgniting < 1.45f)
            return;
        handling = EnumHandling.PreventDefault;
        if (byEntity.World.Side == EnumAppSide.Server && MachineAt(byEntity.World.BlockAccessor, pos) is { Ignitable: true } machine)
            machine.Ignite((byEntity as EntityPlayer)?.Player);
    }

    public static EnumIgniteState IgniteStack(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        MachineAt(byEntity.World.BlockAccessor, pos) is { Ignitable: true, Burning: true }
            ? secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable
            : EnumIgniteState.NotIgnitable;
}
