using System.Text;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon gantry's controller block (<c>eidolongantry-{wood}-{side}</c>): the foot of the front
/// right post. Placing it needs room for every cell of the rig and stamps a ghost into each, with
/// the open front facing the player and its middle on the block they clicked; breaking it (or a
/// ghost) drops the frame and every fitted item, and clears the ghosts. A stack with
/// <see cref="AssembledAttribute"/> (the creative inventory's) places with the winch and spine fitted.
/// </summary>
public class BlockEidolonGantry : Block
{
    /// <summary>On a gantry's stack: place it with every winch stage and the spine fitted.</summary>
    public const string AssembledAttribute = "assembled";

    // What each stage takes, as stacks that exist in the game being played (help icons).
    private Dictionary<GantryStage, ItemStack[]> _partStacks = [];

    public string Wood => Variant["wood"] ?? "oak";

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "eidolongantry-partstacks-" + Wood, () =>
            GantryRequires.Stages.ToDictionary(s => s, s => GantryParts.CodesFor(s, Wood)
                .Select(c => Collectible(api.World, c) is { } coll ? new ItemStack(coll, GantryParts.Needed(s)) : null)
                .OfType<ItemStack>().ToArray()));
    }

    private static CollectibleObject? Collectible(IWorldAccessor world, string code)
    {
        var loc = new AssetLocation(code);
        if (world.GetItem(loc) is { Id: > 0 } item)
            return item;
        return world.GetBlock(loc) is { Id: > 0 } block ? block : null;
    }

    /// <summary>Where the controller goes for a click on <paramref name="clicked"/>: the front's middle
    /// cell (<see cref="GantryRig.PlaceCell"/>) on the clicked block.</summary>
    public static BlockPos ControllerFor(BlockPos clicked, GantryRig rig, Side side)
    {
        var w = Footprint.ToWorld(rig.PlaceCell, side);
        return clicked.AddCopy(-w.X, -w.Y, -w.Z);
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = EidolonGantrySystem.Of(api).Rig;
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

    /// <summary>Places the variant whose open front faces the player, its front's middle on the
    /// block they clicked (the controller, the front right post's foot, to its side).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var rig = EidolonGantrySystem.Of(api).Rig;
        var look = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = GantryRig.PlacedSide(Sides.FromNormal(look.Normali.X, look.Normali.Z));
        if (rig == null || world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        var at = blockSel.Clone();
        at.Position = ControllerFor(blockSel.Position, rig, side);
        if (!block.CanPlaceBlock(world, byPlayer, at, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, at, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        if (world.BlockAccessor.GetBlockEntity(blockPos) is not BEEidolonGantry gantry)
            return;
        gantry.PlaceGhosts();
        if (world.Side == EnumAppSide.Server && byItemStack?.Attributes?.GetBool(AssembledAttribute) == true)
            gantry.FitAll();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEEidolonGantry
            ? InteractAt(world, byPlayer, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the gantry's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEEidolonGantry gantry && gantry.OnInteract(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEEidolonGantry gantry ? [.. drops, .. gantry.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(CodeWithVariant("side", "north")));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEEidolonGantry)?.CellBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEEidolonGantry)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetHeldItemName(ItemStack itemStack) =>
        itemStack?.Attributes?.GetBool(AssembledAttribute) == true
            ? Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-assembled", base.GetHeldItemName(itemStack))
            : base.GetHeldItemName(itemStack);

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack?.Attributes?.GetBool(AssembledAttribute) == true)
            dsc.AppendLine(Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-assembled-info"));
    }

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEEidolonGantry gantry)
        {
            var parts = gantry.Parts;
            if (parts.Next is { } next && _partStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction
                {
                    ActionLangCode = EidolonGantrySystem.Domain + ":blockhelp-eidolongantry-fitpart",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = stacks,
                });
            else
                foreach (var extension in gantry.Extensions)
                    help.AddRange(extension.Help(gantry, forPlayer));
            // the machines' creative shortcut, while there is a stage to fit
            if (!gantry.Complete && forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
