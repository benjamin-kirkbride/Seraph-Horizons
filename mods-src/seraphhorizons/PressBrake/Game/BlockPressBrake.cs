using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.PressBrake;

/// <summary>
/// The press brake's controller block (<c>pressbrake-frame-{side}</c>): the leaf end, nearest the
/// player who placed it. Placing it needs room for every cell of the rig and stamps a ghost into the
/// other; breaking it (or the ghost) drops the frame, every fitted part and a plate not yet folded,
/// and clears the ghost. Right-click held works the lever, as on the quern: the block forwards the
/// interaction's start, steps, stop and cancel to <see cref="BEPressBrake"/>.
/// </summary>
public class BlockPressBrake : Block
{
    public static readonly AssetLocation ItemCode = new(PressBrakeSystem.Domain, "pressbrake-frame-north");

    // What each stage takes, and the half plates, as stacks that exist in the game being played (help icons).
    private Dictionary<PressBrakeStage, ItemStack[]> _partStacks = [];
    private ItemStack[] _plateStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "pressbrake-partstacks", () =>
            PressBrakeRequires.Stages.ToDictionary(s => s, s => PressBrakeParts.CodesFor(s)
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray()));
        _plateStacks = ObjectCacheUtil.GetOrCreate(api, "pressbrake-platestacks", () =>
            new[] { Folding.LeadHalfPlate, Folding.CopperHalfPlate }
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = PressBrakeSystem.Of(api).Rig;
        if (rig == null || !Sides.TryParse(Variant["side"], out var side))
        {
            failureCode = "__ignore__";
            return false;
        }
        foreach (var cell in rig.GhostCells)
        {
            var w = Footprint.ToWorld(cell.Pos, side);
            var at = blockSel.Clone();
            at.Position = blockSel.Position.AddCopy(w.X, w.Y, w.Z);
            if (!base.CanPlaceBlock(world, byPlayer, at, ref failureCode))
                return false;
        }
        return true;
    }

    /// <summary>Places the variant that makes the brake extend away from the player: the block they
    /// clicked becomes the leaf end. The brake runs along native south, so the facing is the way the
    /// player looks (<see cref="PressBrakeRig.PlacedSide"/>), as the draw bench's.</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = PressBrakeRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEPressBrake)?.PlaceGhosts();
    }

    // ---- Interaction: a click, or right-click held on the lever ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEPressBrake
            ? InteractAt(world, byPlayer, blockSel.Position)
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

    /// <summary>A right-click on one of the brake's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal)
    {
        if (!world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(principal) is BEPressBrake brake && brake.OnInteract(byPlayer);
    }

    /// <summary>Right-click still held: whether the player goes on working the lever.</summary>
    public static bool StepAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal, float secondsUsed) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEPressBrake brake && brake.OnWorkStep(byPlayer, secondsUsed);

    /// <summary>Right-click let go (or the interaction cancelled).</summary>
    public static void ReleaseAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        (world.BlockAccessor.GetBlockEntity(principal) as BEPressBrake)?.Release(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEPressBrake brake ? [.. drops, .. brake.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEPressBrake)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEPressBrake)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEPressBrake { Complete: true }
            ? Lang.Get(PressBrakeSystem.Domain + ":block-pressbrake")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEPressBrake brake)
        {
            string Key(string action) => PressBrakeSystem.Domain + ":blockhelp-pressbrake-" + action;
            var parts = brake.Parts;
            if (parts.Next is { } next && _partStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            else if (parts.Complete && !brake.PlateOn && _plateStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadplate"), MouseButton = EnumMouseButton.Right, Itemstacks = _plateStacks });
            if (parts.Complete && brake.PlateOn)
                help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (brake.PlateOn && brake.Job.Untouched)
                help.Add(new WorldInteraction { ActionLangCode = Key("takeplate"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the machines' creative shortcut, while there is a stage to fit
            if (!parts.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
