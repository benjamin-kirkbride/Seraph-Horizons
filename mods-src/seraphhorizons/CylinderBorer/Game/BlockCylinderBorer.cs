using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>
/// The cylinder borer's controller block (<c>cylinderborer-frame-{side}</c>): the bench end, with the
/// feed's drum and weight, nearest the player who placed it. Placing it needs room for every cell of
/// the rig and stamps a ghost into each; breaking it (or a ghost) drops the frame, every fitted part
/// (the cutter head with its durability) and the casting in the cradle, and clears the ghosts.
/// </summary>
public class BlockCylinderBorer : Block
{
    public static readonly AssetLocation ItemCode = new(CylinderBorerSystem.Domain, "cylinderborer-frame-north");

    // The castings, as stacks that exist in the game being played (help icons).
    private ItemStack[] _blankStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _blankStacks = ObjectCacheUtil.GetOrCreate(api, "cylinderborer-blankstacks", () =>
            new[] { Boring.BlankIron, Boring.BlankSteel }
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = CylinderBorerSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the borer extend away from the player: the block they
    /// clicked becomes the bench end. The borer runs along native south (+z), which
    /// <see cref="Footprint.ToWorld(Int3, Side)"/> turns to the facing itself, so the facing is the
    /// way the player looks (<see cref="PlacedSide"/>), as the draw bench's.</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>.</summary>
    public static Side PlacedSide(Side look) => CylinderBorerRig.PlacedSide(look);

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BECylinderBorer)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BECylinderBorer
            ? InteractAt(world, byPlayer, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the borer's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BECylinderBorer borer && borer.OnInteract(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BECylinderBorer borer ? [.. drops, .. borer.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECylinderBorer)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECylinderBorer)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BECylinderBorer { Complete: true }
            ? Lang.Get(CylinderBorerSystem.Domain + ":block-cylinderborer")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BECylinderBorer borer)
        {
            string Key(string action) => CylinderBorerSystem.Domain + ":blockhelp-cylinderborer-" + action;
            var parts = borer.Parts;
            if (parts.Next != null && borer.NeedStacks() is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            else if (parts.Complete && !borer.BlankOn && _blankStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadblank"), MouseButton = EnumMouseButton.Right, Itemstacks = _blankStacks });
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            switch (parts.TakeBack(borer.BlankOn))
            {
                case CylinderBorerTakeBack.Head when !(creative && !parts.Complete):
                    help.Add(new WorldInteraction { ActionLangCode = Key("takehead"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
                    break;
                case CylinderBorerTakeBack.Blank when !(creative && !parts.Complete):
                    help.Add(new WorldInteraction { ActionLangCode = Key("takeblank"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
                    break;
            }
            // the machines' creative shortcut, while there is a stage to fit
            if (!parts.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
