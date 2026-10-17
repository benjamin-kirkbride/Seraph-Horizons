using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Rocker;

/// <summary>
/// The rocker's block (<c>rocker-{side}</c>), one cell, placed whole: a hand station with no build
/// stages. It stands on its rockers on the block below, so it needs a solid top under it and drops
/// when that goes. It draws nothing in the chunk mesh: <see cref="RockerRenderer"/> draws the whole
/// rocker, rocking. Right-click held works it, as on the quern: the block forwards the interaction's
/// start, steps, stop and cancel to <see cref="BERocker"/>. Breaking it drops the rocker, the charge
/// while untouched and everything behind the riffles.
/// </summary>
public class BlockRocker : Block
{
    public static readonly AssetLocation ItemCode = new(RockerSystem.Domain, "rocker-north");

    private ItemStack[] _feedStacks = [];
    private ItemStack[] _waterStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _feedStacks = ObjectCacheUtil.GetOrCreate(api, "rocker-feedstacks", () =>
            new[] { "seraphhorizons:crushedore-quartz_nativegold-medium", "seraphhorizons:crushedore-cassiterite-medium",
                    "seraphhorizons:groundore-galena-poor", "game:richgravel-granite" }
                .Select(c => new AssetLocation(c))
                .Select(l => (CollectibleObject?)api.World.GetItem(l) ?? api.World.GetBlock(l))
                .Where(c => c is { IsMissing: false } && c.Id > 0)
                .Select(c => new ItemStack(c!)).ToArray());
        _waterStacks = ObjectCacheUtil.GetOrCreate(api, "rocker-waterstacks", () =>
        {
            if (api.World.GetBlock(new AssetLocation("game:woodbucket")) is not BlockLiquidContainerBase bucket
                || api.World.GetItem(new AssetLocation(RockerWater.PlainWater)) is not { } water)
                return Array.Empty<ItemStack>();
            var full = new ItemStack(bucket);
            var props = BlockLiquidContainerBase.GetContainableProps(new ItemStack(water));
            bucket.SetContent(full, new ItemStack(water, (int)((props?.ItemsPerLitre ?? 100) * bucket.CapacityLitres)));
            return new[] { full };
        });
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        if (RockerSystem.Of(api).Rig == null)
        {
            failureCode = "__ignore__";
            return false;
        }
        if (!StandsOn(world.BlockAccessor, blockSel.Position))
        {
            failureCode = "requiresolidground";
            return false;
        }
        return true;
    }

    /// <summary>Whether the block under <paramref name="pos"/> has a solid top for the rockers to stand on.</summary>
    public static bool StandsOn(IBlockAccessor ba, BlockPos pos)
    {
        var below = pos.DownCopy();
        return ba.GetBlock(below).SideSolid[BlockFacing.UP.Index];
    }

    /// <summary>Places the variant that puts the handle on the player's side: the block's side is the
    /// way they look (<see cref="RockerRig.PlacedSide"/>).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = RockerRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side == EnumAppSide.Server && neibpos.Equals(pos.DownCopy()) && !StandsOn(world.BlockAccessor, pos))
            world.BlockAccessor.BreakBlock(pos, null);
    }

    // ---- Interaction ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BERocker be
            ? be.OnInteract(byPlayer)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BERocker be && be.OnWorkStep(byPlayer, secondsUsed);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BERocker)?.Release(byPlayer);

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
                                               EnumItemUseCancelReason cancelReason)
    {
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BERocker)?.Release(byPlayer);
        return true;
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BERocker be ? [.. drops, .. be.ContentDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERocker)?.CollisionBoxes ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERocker)?.SelectionBoxes ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BERocker be)
        {
            string Key(string action) => RockerSystem.Domain + ":blockhelp-rocker-" + action;
            if (be.CanTakeMoreCharge)
                help.Add(new WorldInteraction { ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right, Itemstacks = _feedStacks });
            if (be.State.HasCharge)
                help.Add(new WorldInteraction { ActionLangCode = Key("rock"), MouseButton = EnumMouseButton.Right, Itemstacks = _waterStacks });
            if (be.State.HasCatch)
                help.Add(new WorldInteraction { ActionLangCode = Key("takeconcentrate"), MouseButton = EnumMouseButton.Right });
            if (be.State.HasCharge && be.State.ChargeUntouched)
                help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
