using Atlas.XUnit;
using SeraphHorizons.Mod.EidolonGantry;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Eidolon (mods-src/seraphhorizons/EidolonGantry/): the eidolon gantry in the plain
// world. Every scenario builds on a granite floor of its own 40 above spawn at x -560 to -620,
// z -560 to -580 (clear of the mandrel station's at x -460 to -508), cleared 7 high for the 5.5-block
// frame, and they share the gear cutter's player, gearcutterhand (the server takes 16 players at
// most, and the other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private EidolonGantrySystem GantryMod => EidolonGantrySystem.Of(World.Api);
    private GantryRig GantryRigOf => GantryMod.Rig ?? throw new Xunit.Sdk.XunitException("the eidolon gantry's rig did not load");

    /// <summary>A cutter site wide and high enough for the gantry: the floor out to 9 blocks, air 7 high.</summary>
    private async Task<BlockPos> GantrySite(int dx, int dz)
    {
        var origin = await CutterSite(dx, dz, reach: 9);
        for (int x = -9; x <= 9; x++)
            for (int z = -9; z <= 9; z++)
                for (int y = 5; y <= 7; y++)
                    W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        return origin;
    }

    private async Task<BEEidolonGantry> PlaceGantry(BlockPos pos, string wood = "oak", string side = "south")
    {
        World.SetBlock($"seraphhorizons:eidolongantry-{wood}-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEEidolonGantry ?? throw new Xunit.Sdk.XunitException($"no eidolon gantry at {pos}");
        be.PlaceGhosts();
        return be;
    }

    private static string GantryInfo(BEEidolonGantry gantry, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        gantry.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>Each stage's first item, as many as it takes, on a gantry of <paramref name="wood"/>.</summary>
    private ItemStack GantryStageStack(GantryStage stage, string wood, int? count = null) =>
        CutterItem(GantryParts.CodesFor(stage, wood)[0], count ?? GantryParts.Needed(stage));

    // ---- Loading ----

    [AtlasScenario]
    public void Eidolon_gantry_loads_with_its_blocks_and_recipes()
    {
        Assert.True(EidolonGantrySystem.Applies(World.Api));
        Assert.NotNull(GantryMod.Rig);
        foreach (var wood in GantryParts.Woods)
        {
            foreach (var side in Sides.All)
                Assert.IsType<BlockEidolonGantry>(W.GetBlock(new AssetLocation($"seraphhorizons:eidolongantry-{wood}-{side.Code()}")));
            // every item a stage takes exists here, the drum's planks and the spine's beams in this wood
            foreach (var stage in GantryRequires.Stages)
                foreach (var code in GantryParts.CodesFor(stage, wood))
                    Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 } || W.GetBlock(new AssetLocation(code)) is { Id: > 0 },
                        $"no {code} for {stage}");
            // the grid: 24 beams of the wood in two slots, 16 nails and strips, a hammer and a saw
            var output = $"seraphhorizons:eidolongantry-{wood}-north";
            var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == output && r.Enabled);
            Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.ToString() == "game:supportbeam-" + wood && i.Quantity == 12));
            Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 16
                                                           && i.AllowedVariants!.Contains("meteoriciron"));
            Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
            Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "saw-*");
        }
        Assert.IsType<BlockEidolonGantryGhost>(W.GetBlock(BEEidolonGantry.GhostCode));
        var oak = new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:eidolongantry-oak-north")));
        Assert.Equal("Eidolon gantry", oak.GetName());
        oak.Attributes.SetBool(BlockEidolonGantry.AssembledAttribute, true);
        Assert.Equal("Eidolon gantry (assembled)", oak.GetName());
    }

    // ---- Placing ----

    // Placed as a player places it: the open front faces them and its middle is the block they
    // clicked; a ghost in every other cell, none of the hollow ones (the crank's among them)
    // selectable or solid; broken through a ghost, every cell is cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_gantry_places_facing_the_player_with_hollow_cells()
    {
        var player = await CutterPlayer();
        var site = await GantrySite(-560, -560);
        // the player stands west of the clicked block, looking east
        player.Entity.TeleportTo(site.AddCopy(-6, 0, 0));
        await World.Ticks(2);
        var stack = new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:eidolongantry-walnut-north")));
        var sel = new BlockSelection { Position = site.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        string failure = "";
        Assert.True(stack.Block.TryPlaceBlock(W, player, stack, sel, ref failure), failure);
        await World.Ticks(5);
        // facing south, native west (the front) is west, towards the player; the front's middle is the click
        var pos = site.AddCopy(0, 0, -2);
        var gantry = Assert.IsType<BEEidolonGantry>(W.BlockAccessor.GetBlockEntity(pos));
        Assert.Equal(Side.South, gantry.Side);
        Assert.Equal("walnut", gantry.Wood);
        Assert.Equal(Side.West, gantry.WorldSide(GantryRigOf.ExitSide));
        var cells = gantry.GhostCells().ToList();
        Assert.Equal(180, cells.Count);
        foreach (var cell in GantryRigOf.GhostCells)
        {
            var at = gantry.CellPos(cell.Pos);
            Assert.Equal(pos, Assert.IsType<BEEidolonGantryGhost>(W.BlockAccessor.GetBlockEntity(at)).Principal);
            var block = W.BlockAccessor.GetBlock(at);
            var selection = block.GetSelectionBoxes(W.BlockAccessor, at);
            Assert.Equal(cell.Hollow, selection.Length == 0);
            Assert.Equal(selection.Length, block.GetCollisionBoxes(W.BlockAccessor, at).Length);
        }
        // the crank's cell, outside the box south of the back left post
        Assert.Empty(W.BlockAccessor.GetBlock(gantry.CellPos(GantryRigOf.CrankCell)).GetSelectionBoxes(W.BlockAccessor, gantry.CellPos(GantryRigOf.CrankCell)));
        Assert.Equal("Eidolon gantry", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));

        // nothing goes where the frame stands
        failure = "";
        Assert.False(stack.Block.CanPlaceBlock(W, player, new BlockSelection { Position = cells[50], Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) }, ref failure));

        var ghost = gantry.CellPos(new Int3(3, 0, 0));
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(2);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        var dropped = CutterItemsNear(pos, 10);
        Assert.Equal(1, dropped.GetValueOrDefault("seraphhorizons:eidolongantry-walnut-north"));
        Assert.Single(dropped);
        CutterKillItems(pos, 10);
    }

    // ---- Building ----

    // Stage by stage with real items, alternately on the frame and a ghost: out of order, too few
    // and another wood's planks are refused with nothing taken; each stage takes its whole count;
    // the info line names the next stage; a winch part once the winch is done is refused; breaking
    // the finished gantry returns the frame and every item that went in.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_gantry_is_built_stage_by_stage_in_order_and_breaking_returns_everything()
    {
        var player = await CutterPlayer();
        var pos = await GantrySite(-590, -560);
        var gantry = await PlaceGantry(pos, "pine");
        var ghost = gantry.CellPos(new Int3(1, 0, 0));
        Assert.Contains("Next stage: shafts, 8 wooden axles", GantryInfo(gantry, player));

        // out of order: a spur gear before the axles
        Assert.Equal(4, CutterClick(player, pos, GantryStageStack(GantryStage.Gears, "pine"))?.StackSize);
        Assert.False(gantry.Parts.Has(GantryStage.Gears));
        // too few: seven axles of eight
        Assert.Equal(7, CutterClick(player, ghost, CutterItem(GantryParts.AxleCode, 7))?.StackSize);
        Assert.Equal(GantryStage.Axles, gantry.Parts.Next);

        int i = 0;
        foreach (var stage in GantryRequires.Stages)
        {
            if (stage == GantryStage.Drum)
            {
                // another wood's planks are not a part of a pine gantry
                Assert.Equal(10, CutterClick(player, pos, CutterItem("game:plank-oak", 10))?.StackSize);
                Assert.False(_cutterHandled);
            }
            // a stack with one over: the stage takes its count and leaves the rest in hand
            var left = CutterClick(player, i++ % 2 == 0 ? pos : ghost, GantryStageStack(stage, "pine", GantryParts.Needed(stage) + 1));
            Assert.True(gantry.Parts.Has(stage), $"{stage} was not fitted");
            Assert.Equal(1, left?.StackSize);
        }
        Assert.True(gantry.WinchComplete);
        Assert.Contains("Winch built, spine hung", GantryInfo(gantry, player));
        // a winch part now: refused, nothing taken
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-iron"))?.StackSize);
        Assert.True(_cutterHandled);

        // saved and loaded as the world does
        var tree = new TreeAttribute();
        gantry.ToTreeAttributes(tree);
        gantry.FromTreeAttributes(tree, W);
        Assert.Equal(GantryRequires.Stages.Count, gantry.Parts.Snapshot().Count);

        CutterKillItems(pos, 10);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        var dropped = CutterItemsNear(pos, 10);
        var expected = new Dictionary<string, int>
        {
            ["seraphhorizons:eidolongantry-pine-north"] = 1,
            [GantryParts.AxleCode] = 8,
            ["game:rod-iron"] = 2,
            [GantryParts.SpurGearCode] = 4,
            ["game:plank-pine"] = 10,
            ["game:metalnailsandstrips-iron"] = 10,
            ["game:metalplate-iron"] = 1,
            ["game:metalchain-iron"] = 4,
            ["game:supportbeam-pine"] = 3,
        };
        Assert.Equal(expected.OrderBy(kv => kv.Key), dropped.OrderBy(kv => kv.Key));
        CutterKillItems(pos, 10);
    }

    // Creative: Ctrl + right-click fits the next stage with nothing taken, and the assembled stack
    // places with the winch and spine fitted; breaking a half-built gantry returns what is in it.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_gantry_creative_shortcut_and_assembled_stack()
    {
        var player = await CutterPlayer();
        var pos = await GantrySite(-620, -560);
        var gantry = await PlaceGantry(pos, "acacia", "east");
        Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
        Assert.True(gantry.Parts.Has(GantryStage.Axles));
        Assert.Null(CutterClick(player, gantry.CellPos(new Int3(2, 0, 0)), null, ctrl: true, creative: true));
        Assert.True(gantry.Parts.Has(GantryStage.CrankShaft));
        Assert.False(gantry.Parts.Has(GantryStage.Gears));
        // in survival, Ctrl with nothing in hand is not the shortcut
        Assert.Null(CutterClick(player, pos, null, ctrl: true));
        Assert.False(gantry.Parts.Has(GantryStage.Gears));

        CutterKillItems(pos, 10);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        var dropped = CutterItemsNear(pos, 10);
        Assert.Equal(8, dropped.GetValueOrDefault(GantryParts.AxleCode));
        Assert.Equal(1, dropped.GetValueOrDefault("game:rod-iron"));
        Assert.Equal(1, dropped.GetValueOrDefault("seraphhorizons:eidolongantry-acacia-north"));
        CutterKillItems(pos, 10);

        // the assembled stack, as the creative inventory gives it
        var block = W.GetBlock(new AssetLocation("seraphhorizons:eidolongantry-oak-south"))!;
        var assembled = new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:eidolongantry-oak-north")));
        assembled.Attributes.SetBool(BlockEidolonGantry.AssembledAttribute, true);
        var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        Assert.True(block.DoPlaceBlock(W, player, sel, assembled));
        await World.Ticks(5);
        var built = Assert.IsType<BEEidolonGantry>(W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(built.WinchComplete);
        Assert.Equal("game:supportbeam-oak", built.Parts.FittedIn(GantryStage.Spine));
        Assert.Equal(180, built.GhostCells().Count(c => W.BlockAccessor.GetBlock(c) is BlockEidolonGantryGhost));
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        CutterKillItems(pos, 10);
    }
}
