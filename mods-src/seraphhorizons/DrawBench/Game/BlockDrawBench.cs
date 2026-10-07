using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>
/// The draw bench's controller block (<c>drawbench-frame-{side}</c>): the die end, nearest the
/// player who placed it. Placing it needs room for every cell of the rig and stamps a ghost into
/// each; breaking it (or a ghost) drops the frame, every fitted part (the die with its durability)
/// and a hollow no section has been drawn from yet, and clears the ghosts.
/// </summary>
public class BlockDrawBench : Block
{
    public static readonly AssetLocation ItemCode = new(DrawBenchSystem.Domain, "drawbench-frame-north");

    // What each stage takes, and the hollow sections, as stacks that exist in the game being played (help icons).
    private Dictionary<DrawBenchStage, ItemStack[]> _partStacks = [];
    private ItemStack[] _hollowStacks = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        _partStacks = ObjectCacheUtil.GetOrCreate(api, "drawbench-partstacks", () =>
            DrawBenchRequires.Stages.ToDictionary(s => s, s => DrawBenchParts.CodesFor(s)
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray()));
        _hollowStacks = ObjectCacheUtil.GetOrCreate(api, "drawbench-hollowstacks", () =>
            new[] { Drawing.LeadHollow, Drawing.CopperHollow }
                .Select(c => api.World.GetItem(new AssetLocation(c)) is { } item ? new ItemStack(item) : null)
                .OfType<ItemStack>().ToArray());
    }

    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
    {
        if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            return false;
        var rig = DrawBenchSystem.Of(api).Rig;
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

    /// <summary>Places the variant that makes the bench extend away from the player: the block they
    /// clicked becomes the die end. The bench runs along native south (+z), which
    /// <see cref="Footprint.ToWorld(Int3, Side)"/> turns to the facing itself, so the facing is the
    /// way the player looks (<see cref="PlacedSide"/>); the mills' <see cref="Footprint.PlacedFacing"/>
    /// is for machines that run along native west.</summary>
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
    public static Side PlacedSide(Side look) => DrawBenchRig.PlacedSide(look);

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        (world.BlockAccessor.GetBlockEntity(blockPos) as BEDrawBench)?.PlaceGhosts();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) =>
        world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEDrawBench
            ? InteractAt(world, byPlayer, blockSel.Position)
            : base.OnBlockInteractStart(world, byPlayer, blockSel);

    /// <summary>A right-click on one of the bench's cells, <paramref name="principal"/> the controller's position.</summary>
    public static bool InteractAt(IWorldAccessor world, IPlayer byPlayer, BlockPos principal) =>
        world.BlockAccessor.GetBlockEntity(principal) is BEDrawBench bench && bench.OnInteract(byPlayer);

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) ?? [];
        return world.BlockAccessor.GetBlockEntity(pos) is BEDrawBench bench ? [.. drops, .. bench.PartDrops()] : drops;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(world.GetBlock(ItemCode));

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEDrawBench)?.CollisionBoxes(pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        (blockAccessor.GetBlockEntity(pos) as BEDrawBench)?.CellBoxes(pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BEDrawBench { Complete: true }
            ? Lang.Get(DrawBenchSystem.Domain + ":block-drawbench")
            : base.GetPlacedBlockName(world, pos);

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BEDrawBench bench)
        {
            string Key(string action) => DrawBenchSystem.Domain + ":blockhelp-drawbench-" + action;
            var parts = bench.Parts;
            if (parts.Next is { } next && _partStacks.GetValueOrDefault(next) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitpart"), MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            else if (parts.Complete && !bench.JobOn)
            {
                // the hollow sections the fitted die draws
                var draws = bench.DieDraws;
                var hollows = _hollowStacks.Where(s => draws.Contains(Drawing.MetalOf(Drawing.ClassOfHollow(s.Collectible.Code.ToString()))!)).ToArray();
                if (hollows.Length > 0)
                    help.Add(new WorldInteraction { ActionLangCode = Key("loadhollow"), MouseButton = EnumMouseButton.Right, Itemstacks = hollows });
            }
            bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
            if (parts.CanTakeDie(bench.JobOn) && !(creative && !parts.Complete))
                help.Add(new WorldInteraction { ActionLangCode = Key("takedie"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
            // the machines' creative shortcut, while there is a stage to fit
            if (!parts.Complete && creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        }
        return help.ToArray().Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
