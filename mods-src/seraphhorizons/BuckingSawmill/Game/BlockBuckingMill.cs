using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// The mill's controller block (<c>buckingmill-frame-{side}</c>). Placing it needs room for every
/// cell of the rig and stamps a ghost into each; breaking it (or a ghost) drops the frame, the
/// fitted parts, the blade kit and a recoverable trunk, and clears the ghosts.
/// </summary>
public class BlockBuckingMill : Block
{
    public static readonly AssetLocation ItemCode = new(BuckingSawmillSystem.Domain, "buckingmill-frame-north");

    private ItemStack[] _sashStacks = [], _crankshaftStacks = [], _leversStacks = [], _bladeStacks = [], _trunkStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        ItemStack[] Items(string path) =>
            api.World.GetItem(new AssetLocation(BEBuckingMill.IwDomain, path)) is { } item ? [new ItemStack(item)] : [];
        _sashStacks = Items(Parts.SashPath);
        _crankshaftStacks = Items(Parts.CrankshaftPath);
        _leversStacks = Items(Parts.LeversPath);
        _bladeStacks = ObjectCacheUtil.GetOrCreate(api, "buckingsawmill-bladekits", () => api.World.Items
            .Where(i => i?.Code is { Domain: BEBuckingMill.IwDomain } c && c.Path.StartsWith(Parts.BladePrefix, StringComparison.Ordinal))
            .Select(i => new ItemStack(i))
            .ToArray());
        // One debranched size per wood: enough to show what goes in.
        _trunkStacks = ObjectCacheUtil.GetOrCreate(api, "buckingsawmill-trunks", () => api.World.Blocks
            .Where(b => b?.Code is { Domain: LoggingBridge.ModId } c && c.Path.StartsWith("treetrunk-", StringComparison.Ordinal)
                        && b.Variant["size"] == "md" && b.Variant["branches"] == "no" && b.Variant["side"] == "north")
            .Select(b => new ItemStack(b))
            .ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = BuckingSawmillSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the mill extend away from the player: the block they
    /// clicked becomes the middle of its near (output) end, and the axle end is the far one
    /// (<see cref="Footprint.PlacedFacing"/>).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        // the horizontal direction from the player to the block they clicked: the way they look
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var look = Sides.FromNormal(away.Normali.X, away.Normali.Z);
        var side = Footprint.PlacedFacing(look);
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEBuckingMill)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBuckingMill
            ? InteractAt(world, byPlayer, blockSel, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBuckingMill mill && mill.OnInteractStep(byPlayer);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEBuckingMill)?.OnInteractEnd(byPlayer);

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        EnumItemUseCancelReason cancelReason)
    {
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEBuckingMill)?.OnInteractEnd(byPlayer);
        return true;
    }

    /// <summary>A right-click on one of the mill's cells: <paramref name="cellSel"/> as the player
    /// made it (its cell and the hit point in it), <paramref name="principal"/> the controller's
    /// position. Whether it is on the loaded trunk decides what an unrelated held item does.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockSelection cellSel, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEBuckingMill mill
        && mill.OnInteract(byPlayer, mill.HitsTrunk(cellSel.Position, cellSel.HitPosition));

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEBuckingMill mill ? [.. drops, .. mill.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEBuckingMill)?.CellBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEBuckingMill)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEBuckingMill { Complete: true }
            ? Lang.Get(BuckingSawmillSystem.Domain + ":block-buckingmill")
            : base.GetPlacedBlockName(world, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEBuckingMill mill)
        {
            string Key(string action) => BuckingSawmillSystem.Domain + ":blockhelp-buckingmill-" + action;
            if (!mill.Complete)
            {
                var stacks = new List<ItemStack>();
                if (mill.SashCount < Parts.SashesNeeded)
                    stacks.AddRange(_sashStacks);
                if (!mill.HasCrankshaft)
                    stacks.AddRange(_crankshaftStacks);
                if (!mill.HasLevers)
                    stacks.AddRange(_leversStacks);
                if (stacks.Count > 0)
                    help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = [.. stacks] });
                // the blade kit goes in once both sashes are in, one blade in each
                if (!mill.HasBladeKit && mill.SashCount == Parts.SashesNeeded)
                    help.Add(new WorldInteraction { ActionLangCode = Key("fitblades"), MouseButton = EnumMouseButton.Right, Itemstacks = _bladeStacks });
            }
            else if (mill.Trunk == null)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadtrunk"), MouseButton = EnumMouseButton.Right, Itemstacks = _trunkStacks });
            // a stopped mill's saws, if they are down, are wound up by hand
            if (mill.SashCount > 0 && Feeding.WindsUp(mill.Running, mill.SideDepth))
                help.Add(new WorldInteraction { ActionLangCode = Key("windup"), MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
            if (mill.Trunk != null && Cutting.Recoverable(mill.Progress))
                help.Add(new WorldInteraction { ActionLangCode = Key("taketrunk"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            else if (mill.Trunk == null && mill.HasBladeKit)
                help.Add(new WorldInteraction { ActionLangCode = Key("removeblade"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the woodworking stations' creative shortcut, while there is a part to fit
            if (!mill.Complete && forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative && mill.CreativeShortcut)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
