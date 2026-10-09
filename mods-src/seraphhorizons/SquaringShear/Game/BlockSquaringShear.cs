using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.SquaringShear;

/// <summary>
/// The squaring shear's controller block (<c>squaringshear-frame-{side}</c>): the table end, nearest
/// the player who placed it. Placing it needs room for every cell of the rig and stamps a ghost into
/// the other; breaking it (or the ghost) drops the frame, every fitted part and a plate not yet cut,
/// and clears the ghost. Right-click held works the treadle, as on the quern: the block forwards the
/// interaction's start, steps, stop and cancel to <see cref="BESquaringShear"/>.
/// </summary>
public class BlockSquaringShear : Block
{
    public static readonly AssetLocation ItemCode = new(SquaringShearSystem.Domain, "squaringshear-frame-north");

    // What each stage takes, and the plates, as stacks that exist in the game being played (help icons).
    private Dictionary<SquaringShearStage, ItemStack[]> _partStacks = [];
    private ItemStack[] _plateStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "squaringshear-partstacks", () =>
            SquaringShearRequires.Stages.ToDictionary(s => s, s => SquaringShearParts.CodesFor(s)
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray()));
        _plateStacks = ObjectCacheUtil.GetOrCreate(api, "squaringshear-platestacks", () =>
            new[] { Cutting.LeadPlate, Cutting.CopperPlate }
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = SquaringShearSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the shear extend away from the player: the block they
    /// clicked becomes the table end. The shear runs along native south, so the facing is the way the
    /// player looks (<see cref="SquaringShearRig.PlacedSide"/>), as the press brake's.</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = SquaringShearRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BESquaringShear)?.PlaceGhosts();
    }

    // ---- Interaction: a click, or right-click held on the treadle ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BESquaringShear
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

    /// <summary>A right-click on one of the shear's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal)
    {
        if (!world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(principal) is BESquaringShear shear && shear.OnInteract(byPlayer);
    }

    /// <summary>Right-click still held: whether the player goes on working the treadle.</summary>
    public static bool StepAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal, float secondsUsed) =>
        world.BlockAccessor.GetBlockEntity(principal) is BESquaringShear shear && shear.OnWorkStep(byPlayer, secondsUsed);

    /// <summary>Right-click let go (or the interaction cancelled).</summary>
    public static void ReleaseAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        (world.BlockAccessor.GetBlockEntity(principal) as BESquaringShear)?.Release(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BESquaringShear shear ? [.. drops, .. shear.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BESquaringShear)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BESquaringShear)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BESquaringShear { Complete: true }
            ? Lang.Get(SquaringShearSystem.Domain + ":block-squaringshear")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BESquaringShear shear)
        {
            string Key(string action) => SquaringShearSystem.Domain + ":blockhelp-squaringshear-" + action;
            var parts = shear.Parts;
            if (parts.Next is { } next && _partStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            else if (parts.Complete && !shear.PlateOn && _plateStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadplate"), MouseButton = EnumMouseButton.Right, Itemstacks = _plateStacks });
            if (parts.Complete && shear.PlateOn)
                help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (shear.PlateOn && shear.Job.Untouched)
                help.Add(new WorldInteraction { ActionLangCode = Key("takeplate"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the machines' creative shortcut, while there is a stage to fit
            if (!parts.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
