using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// The rosser's controller block (<c>rosser-frame-{side}</c>): the middle of its outfeed end at
/// ground level. Placing it needs room for every cell of the rig and stamps a ghost into each;
/// breaking it (or a ghost) drops the frame, the fitted parts and the trunk as far as it got, and
/// clears the ghosts.
/// </summary>
public class BlockRosser : Block
{
    public static readonly AssetLocation ItemCode = new(RosserSystem.Domain, "rosser-frame-north");

    // What each stage takes, as stacks that exist in the game being played (help icons).
    private Dictionary<RosserStage, ItemStack[]> _partStacks = [];
    private ItemStack[] _trunkStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "rosser-partstacks", () => PartStacks(api));
        // One clean size per wood: enough to show what goes in. Only woods the game has logs of:
        // Logging Expanded has trunks of other mods' woods too, which draw without a texture when
        // that mod is not installed.
        _trunkStacks = ObjectCacheUtil.GetOrCreate(api, "rosser-trunks", () => api.World.Blocks
            .Where(b => b?.Code is { Domain: LoggingBridge.ModId } c && c.Path.StartsWith("treetrunk-", StringComparison.Ordinal)
                        && b.Variant["size"] == "md" && b.Variant["branches"] == "yes" && b.Variant["side"] == "north"
                        && api.World.GetBlock(new AssetLocation("game", $"log-placed-{b.Variant["wood"]}-ud")) != null)
            .Select(b => new ItemStack(b))
            .ToArray());
    }

    /// <summary>The items each stage takes that exist here: the metal parts in every metal the
    /// rosser takes, the heads in every metal there is.</summary>
    private static Dictionary<RosserStage, ItemStack[]> PartStacks(ICoreAPI api)
    {
        var metals = RosserSystem.Of(api).PartMetals;
        ItemStack[] Exact(string code, int count) =>
            api.World.GetItem(new AssetLocation(code)) is { } item ? [new ItemStack(item, count)] : [];
        ItemStack[] Prefixed(string prefix, int count, bool anyMetal) => api.World.Items
            .Where(i => i?.Code is { } c && c.ToString().StartsWith(prefix, StringComparison.Ordinal)
                        && RosserParts.StageOf(c.ToString(), out var metal) != null
                        && (anyMetal || metals == null || metals.Contains(metal!)))
            .Select(i => new ItemStack(i, count))
            .ToArray();
        return new Dictionary<RosserStage, ItemStack[]>
        {
            [RosserStage.Shaft] = Exact(RosserParts.ShaftCode, 1),
            [RosserStage.Ring] = Exact(RosserParts.RingCode, 4),
            [RosserStage.Tyres] = Prefixed(RosserParts.HoopPrefix, 2, false),
            [RosserStage.RollsIn] = Prefixed(RosserParts.RodPrefix, 2, false),
            [RosserStage.RollsOut] = Prefixed(RosserParts.RodPrefix, 2, false),
            [RosserStage.Breaker] = Prefixed(RosserParts.PlatePrefix, 2, false),
            [RosserStage.Levers] = Exact(RosserParts.LeversCode, 1),
            [RosserStage.Heads] = Prefixed(RosserParts.HeadPrefix, 4, true),
        };
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = RosserSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the rosser extend away from the player: the block
    /// they clicked becomes the middle of its near (outfeed) end, and the infeed end is the far one
    /// (<see cref="Footprint.PlacedFacing"/>, as the bucking mill), so a rosser placed on the cell
    /// just beyond a mill's infeed end, looking the same way, feeds it.</summary>
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
        (world.BlockAccessor.GetBlockEntity(blockPos) as BERosser)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BERosser
            ? InteractAt(world, byPlayer, blockSel, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the rosser's cells: <paramref name="cellSel"/> as the player
    /// made it (its cell and the hit point in it), <paramref name="principal"/> the controller's
    /// position. Whether it is on the trunk decides what an unrelated held item does.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockSelection cellSel, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BERosser rosser
        && rosser.OnInteract(byPlayer, rosser.HitsTrunk(cellSel.Position, cellSel.HitPosition));

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BERosser rosser ? [.. drops, .. rosser.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERosser)?.CellBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BERosser)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BERosser { Complete: true }
            ? Lang.Get(RosserSystem.Domain + ":block-rosser")
            : base.GetPlacedBlockName(world, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BERosser rosser)
        {
            string Key(string action) => RosserSystem.Domain + ":blockhelp-rosser-" + action;
            var parts = rosser.Parts;
            if (!parts.Complete)
            {
                // the tyres and the heads go on the ring, so they are offered once it is in
                var stacks = parts.Missing()
                    .Where(m => m.Stage is not (RosserStage.Tyres or RosserStage.Heads) || parts.Has(RosserStage.Ring))
                    .Where(m => m.Stage != RosserStage.Heads)
                    .SelectMany(m => _partStacks.GetValueOrDefault(m.Stage) ?? [])
                    .GroupBy(s => s.Collectible.Code)
                    .Select(g => g.First())
                    .ToArray();
                if (stacks.Length > 0)
                    help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
                if (!parts.Has(RosserStage.Heads) && parts.Has(RosserStage.Ring) && _partStacks.GetValueOrDefault(RosserStage.Heads) is { Length: > 0 } heads)
                    help.Add(new WorldInteraction { ActionLangCode = Key("fitheads"), MouseButton = EnumMouseButton.Right, Itemstacks = heads });
            }
            else if (rosser.State == RosserState.Empty && _trunkStacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadtrunk"), MouseButton = EnumMouseButton.Right, Itemstacks = _trunkStacks });
            if (rosser.State is RosserState.Waiting or RosserState.Delivered)
                help.Add(new WorldInteraction { ActionLangCode = Key("taketrunk"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            else if (rosser.State == RosserState.Empty && parts.HeadsUnworn)
                help.Add(new WorldInteraction { ActionLangCode = Key("removeheads"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the woodworking stations' creative shortcut, while there is a stage to fit
            if (!parts.Complete && forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative && rosser.CreativeShortcut)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
