using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// The riddle's block (<c>riddle-frame-{side}</c>, #714): one cell, the plank box with its bearers, the operator's
/// side toward the player who placed it. Breaking it drops the frame, the fitted riddle's nails and strips, and
/// whatever it holds (a charge, its oversize, the fines). Right-click held riddles, as on the quern: the block
/// forwards the interaction's start, steps, stop and cancel to <see cref="BERiddle"/>.
/// </summary>
public class BlockRiddle : Block
{
    public static readonly AssetLocation ItemCode = new(RiddleSystem.Domain, "riddle-frame-north");

    // The riddle's parts and some crushed ore, as stacks that exist in the game being played (help icons).
    private ItemStack[] _meshStacks = [];
    private ItemStack[] _oreStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _meshStacks = ObjectCacheUtil.GetOrCreate(api, "riddle-meshstacks", () =>
            RiddleParts.MeshCodes
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item, RiddleParts.Needed) : null)
                .OfType<ItemStack>().ToArray());
        _oreStacks = ObjectCacheUtil.GetOrCreate(api, "riddle-orestacks", () =>
            api.World.Items.Where(i => i?.Code != null && !i.IsMissing && Core.Riddling.IsCrushedOre(i.Code.ToString()) && i.Code.Path.EndsWith("-medium"))
                .Select(i => new ItemStack(i, RiddleSystem.Of(api).Config.LumpsPerCharge)).ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        if (RiddleSystem.Of(api).Rig == null || !Sides.TryParse(Variant["side"], out _))
        {
            failureCode = "__ignore__";
            return false;
        }
        return true;
    }

    /// <summary>Places the variant whose operator's side faces the player (<see cref="RiddleRig.PlacedSide"/>).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = RiddleRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    // ---- Interaction: a click, or right-click held ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BERiddle riddle)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        return riddle.OnInteract(byPlayer);
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BERiddle riddle && riddle.OnWorkStep(byPlayer, secondsUsed);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BERiddle)?.Release(byPlayer);

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
                                               EnumItemUseCancelReason cancelReason)
    {
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BERiddle)?.Release(byPlayer);
        return true;
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BERiddle riddle ? [.. drops, .. riddle.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERiddle)?.CollisionBoxes ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERiddle)?.CellBoxes ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BERiddle { Complete: true }
            ? Lang.Get(RiddleSystem.Domain + ":block-riddle")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BERiddle riddle)
        {
            string Key(string action) => RiddleSystem.Domain + ":blockhelp-riddle-" + action;
            if (!riddle.Complete)
            {
                if (_meshStacks.Length > 0)
                    help.Add(new WorldInteraction { ActionLangCode = Key("fit"), MouseButton = EnumMouseButton.Right, Itemstacks = _meshStacks });
                if (forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
                    help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
            }
            else if (riddle.Oversize != null)
                help.Add(new WorldInteraction { ActionLangCode = Key("takeoversize"), MouseButton = EnumMouseButton.Right });
            else if (riddle.Riddling)
            {
                help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
                if (riddle.Job.Untouched)
                    help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            }
            else if (riddle.Fines != null)
                help.Add(new WorldInteraction { ActionLangCode = Key("takefines"), MouseButton = EnumMouseButton.Right });
            else if (_oreStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("load"), MouseButton = EnumMouseButton.Right, Itemstacks = _oreStacks });
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
