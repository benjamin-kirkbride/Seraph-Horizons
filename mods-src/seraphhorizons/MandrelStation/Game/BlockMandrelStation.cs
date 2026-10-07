using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// The mandrel station's controller block (<c>mandrelstation-frame-{side}</c>): the stump, nearest the
/// player who placed it. Placing it needs room for every cell of the rig and stamps a ghost into the
/// other; breaking it (or the ghost) drops the frame, the mandrel and a hollow not yet struck, and
/// clears the ghost. A right-click goes to <see cref="BEMandrelStation"/>: with a hammer in hand, a blow.
/// </summary>
public class BlockMandrelStation : Block
{
    public static readonly AssetLocation ItemCode = new(MandrelStationSystem.Domain, "mandrelstation-frame-north");

    // The mandrels, the hollows and the hammers, as stacks that exist in the game being played (help icons).
    private ItemStack[] _mandrelStacks = [];
    private ItemStack[] _hollowStacks = [];
    private ItemStack[] _hammerStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        ItemStack[] Stacks(IEnumerable<string> codes) => codes
            .Select(c => api.World.GetItem(new AssetLocation(c)) is { Id: > 0, IsMissing: false } item ? new ItemStack(item) : null)
            .OfType<ItemStack>().ToArray();
        _mandrelStacks = ObjectCacheUtil.GetOrCreate(api, "mandrelstation-mandrelstacks", () => Stacks(MandrelPart.Codes));
        _hollowStacks = ObjectCacheUtil.GetOrCreate(api, "mandrelstation-hollowstacks", () => Stacks([Forging.LeadHollow, Forging.CopperHollow]));
        _hammerStacks = ObjectCacheUtil.GetOrCreate(api, "mandrelstation-hammerstacks", () =>
            api.World.Items.Where(i => i?.Code != null && !i.IsMissing && Forging.IsHammer(i.Code.ToString()))
                .Select(i => new ItemStack(i)).ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = MandrelStationSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the station extend away from the player: the block they
    /// clicked becomes the stump, the mandrel pointing away. The station runs along native south, so
    /// the facing is the way the player looks (<see cref="MandrelStationRig.PlacedSide"/>).</summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var away = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var side = MandrelStationRig.PlacedSide(Sides.FromNormal(away.Normali.X, away.Normali.Z));
        if (world.GetBlock(CodeWithVariant("side", side.Code())) is not { Id: > 0 } block)
            return false;
        if (!block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        return block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEMandrelStation)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEMandrelStation
            ? InteractAt(world, byPlayer, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the station's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal)
    {
        if (!world.Claims.TryAccess(byPlayer, principal, EnumBlockAccessFlags.Use))
            return false;
        return world.BlockAccessor.GetBlockEntity(principal) is BEMandrelStation station && station.OnInteract(byPlayer);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEMandrelStation station ? [.. drops, .. station.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMandrelStation)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEMandrelStation)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEMandrelStation { Complete: true }
            ? Lang.Get(MandrelStationSystem.Domain + ":block-mandrelstation")
            : base.GetPlacedBlockName(world, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEMandrelStation station)
        {
            string Key(string action) => MandrelStationSystem.Domain + ":blockhelp-mandrelstation-" + action;
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (!station.Complete && _mandrelStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitmandrel"), MouseButton = EnumMouseButton.Right, Itemstacks = _mandrelStacks });
            else if (station.Complete && !station.HollowOn && _hollowStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadhollow"), MouseButton = EnumMouseButton.Right, Itemstacks = _hollowStacks });
            if (station.Complete && station.HollowOn && _hammerStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("strike"), MouseButton = EnumMouseButton.Right, Itemstacks = _hammerStacks });
            if (station.HollowOn && station.Job.Untouched)
                help.Add(new WorldInteraction { ActionLangCode = Key("takehollow"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            else if (MandrelPart.CanTakeBack(station.Mandrel, station.HollowOn))
                help.Add(new WorldInteraction { ActionLangCode = Key("takemandrel"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the machines' creative shortcut, while the mandrel is still to fit
            if (!station.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
