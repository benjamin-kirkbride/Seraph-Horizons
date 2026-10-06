using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>
/// The gear cutter's controller block (<c>gearcutter-frame-{side}</c>): the operator's side, north,
/// at ground level. Placing it needs room for every cell of the rig and stamps a ghost into each;
/// breaking it (or a ghost) drops the frame, every fitted part (the kit with its durability) and a
/// blank on the arbor, and clears the ghosts.
/// </summary>
public class BlockGearCutter : Block
{
    public static readonly AssetLocation ItemCode = new(GearCutterSystem.Domain, "gearcutter-frame-north");

    // What each stage takes, as stacks that exist in the game being played (help icons).
    private Dictionary<GearCutterStage, ItemStack[]> _partStacks = [];
    private ItemStack[] _blankStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "gearcutter-partstacks", () =>
            GearCutterRequires.Stages.ToDictionary(s => s, s => GearCutterParts.CodesFor(s)
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray()));
        _blankStacks = ObjectCacheUtil.GetOrCreate(api, "gearcutter-blankstacks", () =>
            new[] { GearCut.Blank, GearCut.LargeBlank }
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = GearCutterSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the cutter extend away from the player
    /// (<see cref="Footprint.PlacedFacing"/>, as the bucking mill and the rosser): the block they
    /// clicked becomes the controller, on the operator's side.</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
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
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEGearCutter)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEGearCutter
            ? InteractAt(world, byPlayer, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the cutter's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEGearCutter cutter && cutter.OnInteract(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEGearCutter cutter ? [.. drops, .. cutter.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEGearCutter)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEGearCutter)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEGearCutter { Complete: true }
            ? Lang.Get(GearCutterSystem.Domain + ":block-gearcutter")
            : base.GetPlacedBlockName(world, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEGearCutter cutter)
        {
            string Key(string action) => GearCutterSystem.Domain + ":blockhelp-gearcutter-" + action;
            var parts = cutter.Parts;
            if (parts.Next is { } next && _partStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            else if (parts.Complete && !cutter.BlankOn)
            {
                var blanks = _blankStacks.Where(s => GearCut.ClassOfBlank(s.Collectible.Code.ToString()) == parts.Master).ToArray();
                if (blanks.Length > 0)
                    help.Add(new WorldInteraction { ActionLangCode = Key("loadblank"), MouseButton = EnumMouseButton.Right, Itemstacks = blanks });
            }
            string? take = parts.TakeBack(cutter.BlankOn) switch
            {
                GearCutterTakeBack.Kit => "takekit",
                GearCutterTakeBack.Blank => "takeblank",
                GearCutterTakeBack.Master => "takemaster",
                _ => null,
            };
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (take != null && !(creative && !parts.Complete))
                help.Add(new WorldInteraction { ActionLangCode = Key(take), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the machines' creative shortcut, while there is a stage to fit
            if (!parts.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
