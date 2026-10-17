using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.AmalgamPan;

/// <summary>
/// The amalgam pan's block (<c>amalgampan-frame-{side}</c>): one cell, its operator's side (native
/// north) towards the player who placed it. A mechanical power connector on its down face only, as
/// the quern's (whose is up or down): an axle in the block below turns the muller. Right-click held
/// works it by hand, as on the quern; the block forwards the interaction's start, steps, stop and
/// cancel to <see cref="BEAmalgamPan"/>. Breaking it drops the frame, the muller and shoes, and what
/// is in the pan that can be carried (the quartz, the amalgam; the mercury and the water are lost).
/// </summary>
public class BlockAmalgamPan : Block, IMechanicalPowerBlock
{
    private ItemStack[][] _stageStacks = [];
    private ItemStack[] _mercuryStacks = [];
    private ItemStack[] _waterStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _stageStacks = ObjectCacheUtil.GetOrCreate(api, "amalgampan-stagestacks", () =>
            (AmalgamPanSystem.Of(api).Build?.Stages ?? [])
                .Select(s => s.Materials.SelectMany(m => m.Codes)
                    .Select(c => BEAmalgamPan.StackOf(api.World, c.Replace("*", "iron"))).OfType<ItemStack>().ToArray())
                .ToArray());
        _mercuryStacks = ObjectCacheUtil.GetOrCreate(api, "amalgampan-mercurystacks", () =>
            LiquidHelp(api, BEAmalgamPan.MercuryCode));
        _waterStacks = ObjectCacheUtil.GetOrCreate(api, "amalgampan-waterstacks", () =>
            LiquidHelp(api, "game:waterportion"));
    }

    /// <summary>A bucket of the liquid, for the interaction help.</summary>
    private static ItemStack[] LiquidHelp(ICoreAPI api, string liquid)
    {
        if (api.World.GetBlock(new AssetLocation("game:woodbucket")) is not BlockLiquidContainerBase bucket
            || api.World.GetItem(new AssetLocation(liquid)) is not { } item)
            return [];
        var stack = new ItemStack(bucket);
        bucket.SetContent(stack, new ItemStack(item, 1000));
        return [stack];
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = Sides.FromNormal(away.Normali.X, away.Normali.Z);
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    // ---- Mechanical power: the down face only ----

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) =>
        face == BlockFacing.DOWN;

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (neibpos.Equals(pos.DownCopy()))
            world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.tryConnect(BlockFacing.DOWN);
    }

    // ---- Interaction: a click, or right-click held on the pan ----

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEAmalgamPan pan)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;
        return pan.OnInteract(byPlayer);
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEAmalgamPan pan && pan.OnWorkStep(byPlayer, secondsUsed);

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEAmalgamPan)?.Release(byPlayer);

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
                                               EnumItemUseCancelReason cancelReason)
    {
        (world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEAmalgamPan)?.Release(byPlayer);
        return true;
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEAmalgamPan pan ? [.. drops, .. pan.BreakDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(AmalgamPanSystem.FrameCode));

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEAmalgamPan { Complete: true }
            ? Lang.Get(AmalgamPanSystem.Domain + ":block-amalgampan")
            : base.GetPlacedBlockName(world, pos);

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEAmalgamPan pan)
        {
            string Key(string action) => AmalgamPanSystem.Domain + ":blockhelp-amalgampan-" + action;
            if (pan.Assembly.Build.Next is { } next && next < _stageStacks.Length && _stageStacks[next].Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = _stageStacks[next] });
            else if (pan.Complete)
            {
                var batch = pan.Batch;
                if (batch.Worked)
                    help.Add(new WorldInteraction { ActionLangCode = Key("takeout"), MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
                else
                {
                    if (_mercuryStacks.Length > 0)
                        help.Add(new WorldInteraction { ActionLangCode = Key("mercury"), MouseButton = EnumMouseButton.Right, Itemstacks = _mercuryStacks });
                    if (batch.Items < pan.Config.ChargeItems && batch.Untouched)
                        help.Add(new WorldInteraction { ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right });
                    if (batch.Items > 0 && _waterStacks.Length > 0 && batch.WaterRoom(pan.Config) > 0)
                        help.Add(new WorldInteraction { ActionLangCode = Key("water"), MouseButton = EnumMouseButton.Right, Itemstacks = _waterStacks });
                    if (batch.Items > 0)
                        help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
                    if (batch.Items > 0 && batch.Untouched)
                        help.Add(new WorldInteraction { ActionLangCode = Key("takecharge"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
                }
            }
            if (!pan.Complete && forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
