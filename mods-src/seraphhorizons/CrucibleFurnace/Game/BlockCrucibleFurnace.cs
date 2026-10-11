using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// The crucible furnace's controller block (<c>cruciblefurnace-frame-{side}</c>), the front middle
/// cell, the ash mouth's: placed from the iron frame, the item, nearest the player, the furnace
/// extending away from them. Placing it needs room for every cell of the rig and stamps a ghost into
/// each; breaking it (or a ghost) drops the frame, every fitted item, the outlet's cap, the coke and
/// the pots, and clears the ghosts. A firestarter or a torch lights its coke, as a forge is lit
/// (<see cref="IIgnitable"/>), on any of its cells.
/// </summary>
public class BlockCrucibleFurnace : Block, IIgnitable
{
    public static readonly AssetLocation ItemCode = new(CrucibleFurnaceSystem.FrameCode);

    private ItemStack[] _potStacks = [];
    private ItemStack[] _cokeStacks = [];
    private ItemStack[] _capStacks = [];
    private ItemStack[] _igniteStacks = [];
    private Dictionary<int, ItemStack[]> _stageStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        ItemStack[] Stacks(params string[] codes) => codes
            .Select(c => CrucibleFurnaceSystem.Resolve(api.World, c)).OfType<CollectibleObject>()
            .Select(c => new ItemStack(c)).ToArray();
        _potStacks = ObjectCacheUtil.GetOrCreate(api, "cruciblefurnace-potstacks", () => Stacks(CrucibleFurnaceSystem.FiredPot));
        _cokeStacks = ObjectCacheUtil.GetOrCreate(api, "cruciblefurnace-cokestacks", () => Stacks("game:coke"));
        _igniteStacks = ObjectCacheUtil.GetOrCreate(api, "cruciblefurnace-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(api, true).ToArray());
        var rig = CrucibleFurnaceSystem.Of(api).Rig;
        if (rig == null)
            return;
        _capStacks = ObjectCacheUtil.GetOrCreate(api, "cruciblefurnace-capstacks", () => Stacks(rig.OutletCap.Code));
        _stageStacks = ObjectCacheUtil.GetOrCreate(api, "cruciblefurnace-stagestacks", () =>
            rig.Stages.Select((s, i) => (s, i)).ToDictionary(p => p.i, p => p.s.Materials
                .SelectMany(m => m.Code.Contains('*')
                    ? rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id))
                    : [m.Code])
                .Select(c => CrucibleFurnaceSystem.Resolve(api.World, c)).OfType<CollectibleObject>()
                .Select(c => new ItemStack(c)).ToArray()));
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = CrucibleFurnaceSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the furnace extend away from the player: the block they
    /// clicked becomes the front middle cell, the ash mouth facing them (<see cref="CrucibleFurnaceRig.PlacedSide"/>).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = CrucibleFurnaceRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BECrucibleFurnace)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BECrucibleFurnace
            ? InteractAt(world, byPlayer, blockSel.Position, blockSel)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the furnace's cells (<paramref name="sel"/>), <paramref name="principal"/>
    /// the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal, BlockSelection sel)
    {
        if (!world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(principal) is BECrucibleFurnace furnace && furnace.OnInteract(byPlayer, sel);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BECrucibleFurnace furnace ? [.. drops, .. furnace.ContentDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECrucibleFurnace)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BECrucibleFurnace)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BECrucibleFurnace { Complete: true }
            ? Lang.Get(CrucibleFurnaceSystem.Domain + ":block-cruciblefurnace")
            : base.GetPlacedBlockName(world, pos);

    // The description (what it is built of, how to assemble it) stays in the tooltip and handbook.
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer) =>
        this.WithoutDescription(base.GetPlacedBlockInfo(world, pos, forPlayer));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BECrucibleFurnace furnace)
            help.AddRange(HelpFor(furnace, forPlayer));
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    /// <summary>The help lines for the furnace as it stands, whichever of its cells is looked at.</summary>
    public IEnumerable<WorldInteraction> HelpFor(BECrucibleFurnace furnace, IPlayer forPlayer)
    {
        string Key(string action) => CrucibleFurnaceSystem.Domain + ":blockhelp-cruciblefurnace-" + action;
        var build = furnace.Build;
        if (build == null)
            yield break;
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (build.Next is { } next)
        {
            if (_stageStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                yield return new WorldInteraction { ActionLangCode = Key("stage"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks };
            if (creative)
                foreach (var h in SplittingBlockUpgrades.CreativeUpgradeHelp)
                    yield return h;
        }
        if (furnace.LiningIn && _capStacks.Length > 0)
            yield return furnace.Capped
                ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
                : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = _capStacks };
        if (!build.Complete)
            yield break;
        yield return new WorldInteraction { ActionLangCode = Key("lid"), MouseButton = EnumMouseButton.Right };
        yield return new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right };
        yield return new WorldInteraction { ActionLangCode = Key("pot"), MouseButton = EnumMouseButton.Right, Itemstacks = _potStacks };
        yield return new WorldInteraction { ActionLangCode = Key("coke"), MouseButton = EnumMouseButton.Right, Itemstacks = _cokeStacks };
        yield return new WorldInteraction { ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right, Itemstacks = _igniteStacks };
        yield return new WorldInteraction { ActionLangCode = Key("pull"), MouseButton = EnumMouseButton.Right };
    }

    // ---- IIgnitable: lit as the forge is, on any of the furnace's cells ----

    /// <summary>The furnace whose cell is at <paramref name="pos"/>: the controller, or a ghost's.</summary>
    public static BECrucibleFurnace? FurnaceAt(IBlockAccessor ba, BlockPos pos) => ba.GetBlockEntity(pos) switch
    {
        BECrucibleFurnace furnace => furnace,
        BECrucibleFurnaceGhost { Principal: { } principal } => ba.GetBlockEntity(principal) as BECrucibleFurnace,
        _ => null,
    };

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        IgniteState(byEntity, pos, secondsIgniting);

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling) =>
        IgniteOver(byEntity, pos, secondsIgniting, ref handling);

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        IgniteStack(byEntity, pos, secondsIgniting);

    public static EnumIgniteState IgniteState(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (FurnaceAt(byEntity.World.BlockAccessor, pos) is not { } furnace)
            return EnumIgniteState.NotIgnitable;
        if (furnace.CannotLight() is not null)
        {
            if (secondsIgniting == 0 && byEntity.World.Side == EnumAppSide.Server)
                furnace.TryLight((byEntity as EntityPlayer)?.Player);   // says why not
            return EnumIgniteState.NotIgnitablePreventDefault;
        }
        return secondsIgniting >= 1.5f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public static void IgniteOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        if (secondsIgniting < 1.45f)
            return;
        handling = EnumHandling.PreventDefault;
        if (byEntity.World.Side == EnumAppSide.Server && FurnaceAt(byEntity.World.BlockAccessor, pos) is { } furnace)
            furnace.TryLight((byEntity as EntityPlayer)?.Player);
    }

    public static EnumIgniteState IgniteStack(EntityAgent byEntity, BlockPos pos, float secondsIgniting) =>
        FurnaceAt(byEntity.World.BlockAccessor, pos) is { Lit: true }
            ? secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable
            : EnumIgniteState.NotIgnitable;
}
