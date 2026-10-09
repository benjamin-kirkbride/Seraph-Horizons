using Atlas.XUnit;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake;
using SeraphHorizons.Mod.PressBrake.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, PressBrake (mods-src/seraphhorizons/PressBrake/): the press brake in the plain
// world, worked by hand. The player's right-click held on it is played through the block's own
// interaction calls (start, a step each tick, stop), as the game makes them, so each scenario that
// folds shows W moving only while held; a half plate takes 1.5 seconds of holding (copper 2.25), so
// they then finish it through BEPressBrake.Fold, as the draw bench's scenarios finish an ingot through Draw. Every
// scenario builds on a granite floor of its own 40 above spawn at x -320 to -368, z -320 to -340
// (clear of the draw bench's at x -240 to -300, z -240 to -280), and they share the gear cutter's
// player, gearcutterhand (the server takes 16 players at most, and the other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private PressBrakeSystem BrakeMod => PressBrakeSystem.Of(World.Api);
    private PressBrakeRig BrakeRig => BrakeMod.Rig ?? throw new Xunit.Sdk.XunitException("the press brake's rig did not load");

    // What it makes: the angle, UnifiedPipes' item, one a half plate (the squaring shear's item).
    private const string AngleLead = "seraphhorizons:angle-lead";
    private const string AngleCopper = "seraphhorizons:angle-copper";
    private const string BrakeFrame = "seraphhorizons:pressbrake-frame-north";

    private async Task<BEPressBrake> PlaceBrake(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:pressbrake-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEPressBrake ?? throw new Xunit.Sdk.XunitException($"no press brake at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>The far cell, the ghost.</summary>
    private static BlockPos BrakeGhost(BEPressBrake brake) => brake.GhostCells().Single();

    /// <summary>Both stages by right-clicks with real items, the screws on the frame and the edges on the ghost.</summary>
    private void AssembleBrake(BEPressBrake brake, IPlayer player, string screws = "game:metal-parts", string edge = "game:metalplate-iron")
    {
        Assert.Null(CutterClick(player, brake.Pos, CutterItem(screws)));
        Assert.Null(CutterClick(player, BrakeGhost(brake), CutterItem(edge)));
        Assert.True(brake.Complete);
    }

    private static BlockSelection BrakeSelection(BlockPos at) =>
        new() { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };

    /// <summary>
    /// Right-click held on <paramref name="at"/> with <paramref name="held"/> in hand for
    /// <paramref name="ticks"/> server ticks, as the game plays it: the interaction's start, a step
    /// each tick while the block says go on, then the stop. Returns whether the start was the
    /// brake's and how many steps it went on for.
    /// </summary>
    private async Task<(bool Started, int Steps)> BrakeHold(IPlayer player, BlockPos at, int ticks, ItemStack? held = null)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = BrakeSelection(at);
        var block = W.BlockAccessor.GetBlock(at);
        if (!block.OnBlockInteractStart(W, player, sel))
            return (false, 0);
        int steps = 0;
        float seconds = 0;
        for (int i = 0; i < ticks; i++)
        {
            await World.Ticks(1);
            seconds += 0.05f;
            if (!W.BlockAccessor.GetBlock(at).OnBlockInteractStep(seconds, W, player, sel))
                break;
            steps++;
        }
        W.BlockAccessor.GetBlock(at).OnBlockInteractStop(seconds, W, player, sel);
        return (true, steps);
    }

    /// <summary>Radians of the lever that bring the plate on the bed to W = <paramref name="work"/>.</summary>
    private static double BrakeRadiansTo(BEPressBrake brake, double work) =>
        (work - brake.Job.Work) * 2 * Math.PI * brake.LeverTurnsPerPlate(brake.Job.Class) + 1e-6;

    private static string BrakeInfo(BEPressBrake brake, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        brake.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private static int BrakeHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    // ---- Loading ----

    [AtlasScenario]
    public void Press_brake_loads_with_its_blocks_and_recipe()
    {
        Assert.True(PressBrakeSystem.Applies(World.Api));
        Assert.NotNull(BrakeMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockPressBrake>(W.GetBlock(new AssetLocation($"seraphhorizons:pressbrake-frame-{side}")));
        Assert.IsType<BlockPressBrakeGhost>(W.GetBlock(new AssetLocation("seraphhorizons:pressbrake-ghost")));
        // every part the stages take exists here: the game's metal parts (a block) and plates, and the
        // rods a brake saved before the screws were metal parts may hold
        foreach (var stage in PressBrakeRequires.Stages)
            foreach (var code in PressBrakeParts.CodesFor(stage))
                Assert.True(BEPressBrake.PartStack(W, code) is { }, $"no {code} for {stage}");
        Assert.True(W.GetBlock(new AssetLocation(PressBrakeParts.ScrewCode)) is { Id: > 0, IsMissing: false });
        foreach (var code in PressBrakeParts.LegacyScrewCodes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false }, $"no {code}");
        // what it folds, the squaring shear's lead and copper half plates, and what comes off: UnifiedPipes' angles
        foreach (var k in new[] { 1, 2 })
        {
            Assert.True(W.GetItem(new AssetLocation(Folding.PlateFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Folding.PlateFor(k)}");
            Assert.True(W.GetItem(new AssetLocation(Folding.AngleFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Folding.AngleFor(k)}");
        }
        Assert.Equal((AngleLead, AngleCopper), (Folding.AngleFor(1), Folding.AngleFor(2)));
        Assert.Equal(("seraphhorizons:halfplate-lead", "seraphhorizons:halfplate-copper"), (Folding.PlateFor(1), Folding.PlateFor(2)));
        // the brake is the only maker of angles: no anvil recipe, no grid recipe
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.Domain == "seraphhorizons" && r.Output.Code.Path.StartsWith("angle-"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Domain == "seraphhorizons" && r.Output.Code.Path.StartsWith("angle-"));
        Assert.Equal("Press brake frame", new ItemStack(W.GetBlock(BlockPressBrake.ItemCode)).GetName());

        // the grid: the frame, of oak and iron fittings, with a hammer
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == BrakeFrame && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:log-placed-oak-ud");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:plank-oak");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the settings and the rig's pace agree
        Assert.Equal(BrakeRig.LeverTurnsPerPlate[1], BrakeMod.Config.LeverTurnsPerPlateLead, 0.001);
        Assert.Equal(BrakeRig.LeverTurnsPerPlate[2], BrakeMod.Config.LeverTurnsPerPlateCopper, 0.001);
    }

    // ---- Placing ----

    // On all four facings: room for both cells, a ghost stamped in the far one, the brake running
    // away along the facing from the leaf end, each cell's collision boxes its selection boxes and its
    // lid; broken through the ghost, both cells are cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Press_brake_places_on_all_four_facings_with_its_ghost_and_lids()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-320 - 12 * i++, -320);
            var brake = await PlaceBrake(pos, side.Code());
            Assert.Equal(side, brake.Side);
            var ghost = BrakeGhost(brake);
            var n = side.Normal();
            Assert.Equal(pos.AddCopy(n.X, 0, n.Z), ghost);
            Assert.Equal(pos, Assert.IsType<BEPressBrakeGhost>(W.BlockAccessor.GetBlockEntity(ghost)).Principal);
            foreach (var cell in new[] { pos, ghost })
            {
                var block = W.BlockAccessor.GetBlock(cell);
                var selection = block.GetSelectionBoxes(W.BlockAccessor, cell);
                var collision = block.GetCollisionBoxes(W.BlockAccessor, cell);
                Assert.NotNull(selection);
                Assert.Equal(selection.Length + 1, collision.Length);
                Assert.Equal(BrakeRig.Cells[0].Lid!.Value, collision[^1].Y2, 3);
            }
            Assert.Equal("Press brake frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
            Assert.Equal("Press brake frame", W.BlockAccessor.GetBlock(ghost).GetPlacedBlockName(W, ghost));
            // the infeed is beyond the far end, the outfeed in front of the leaf end
            Assert.Equal(pos.AddCopy(2 * n.X, 0, 2 * n.Z), brake.CellPos(Assert.Single(BrakeRig.InfeedNeighbours())));
            Assert.Equal(pos.AddCopy(-n.X, 0, -n.Z), brake.CellPos(BrakeRig.OutputNeighbour()));

            W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.Equal(0, W.BlockAccessor.GetBlock(ghost).Id);
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(BrakeFrame));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Press_brake_is_built_in_order_and_refuses_parts_out_of_order()
    {
        var pos = await CutterSite(-320, -340);
        var player = await CutterPlayer();
        var brake = await PlaceBrake(pos, "east");
        var ghost = BrakeGhost(brake);
        Assert.Contains("Next part: screws", BrakeInfo(brake, player));
        Assert.Contains("Then: edges", BrakeInfo(brake, player));

        // the edges before the screws are refused and stay in hand, from the frame or the ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-iron"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:metalplate-steel"))?.StackSize);
        Assert.False(brake.Parts.Has(PressBrakeStage.Edge));
        // no half plate before the brake is built, and an empty hand works no lever on a bare frame
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Folding.LeadHalfPlate))?.StackSize);
        Assert.False(brake.PlateOn);
        Assert.Null(CutterClick(player, pos, null));
        Assert.False(_cutterHandled);
        // a rod (what the screws once took, or of a metal they never did) is the item's own business
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper"))?.StackSize);
        Assert.False(_cutterHandled);
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-iron"))?.StackSize);
        Assert.False(_cutterHandled);
        Assert.False(brake.Parts.Has(PressBrakeStage.Screws));

        // the screws, one lot of metal parts from a stack of two; a second has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metal-parts", 2))?.StackSize);
        Assert.Equal("game:metal-parts", brake.Parts.FittedIn(PressBrakeStage.Screws));
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:metal-parts"))?.StackSize);
        Assert.Contains("Next part: edges", BrakeInfo(brake, player));
        Assert.False(brake.Complete);
        Assert.Equal("Press brake frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        // the edges, from the ghost
        Assert.Null(CutterClick(player, ghost, CutterItem("game:metalplate-steel")));
        Assert.True(brake.Complete);
        Assert.Equal("steel", brake.EdgeMetal);
        Assert.Equal("Press brake", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Bed empty", BrakeInfo(brake, player));

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        brake.ToTreeAttributes(tree);
        brake.FromTreeAttributes(tree, W);
        Assert.True(brake.Complete);
        Assert.Equal(["game:metal-parts", "game:metalplate-steel"], brake.Parts.Returns());

        // fitted parts never come back out: Ctrl on the frame or the ghost takes nothing
        CutterClick(player, pos, null, ctrl: true);
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(brake.Complete);
        Assert.Equal(0, BrakeHeld(player, "game:metalplate-steel"));
        Assert.Equal(0, BrakeHeld(player, "game:metal-parts"));
        Assert.Equal(["game:metal-parts", "game:metalplate-steel"], brake.Parts.Returns());

        // only breaking gives them back
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(BrakeFrame));
        Assert.Equal(1, drops.GetValueOrDefault("game:metal-parts"));
        Assert.Equal(1, drops.GetValueOrDefault("game:metalplate-steel"));
        CutterKillItems(pos);

        // a brake saved when the screws were a rod loads complete with its rod, works, and gives the
        // rod back when broken
        brake = await PlaceBrake(pos, "east");
        var old = new Vintagestory.API.Datastructures.TreeAttribute();
        brake.ToTreeAttributes(old);
        old.SetString("part-screws", "game:rod-meteoriciron");
        old.SetString("part-edge", "game:metalplate-iron");
        brake.FromTreeAttributes(old, W);
        Assert.True(brake.Complete);
        Assert.Equal(["game:rod-meteoriciron", "game:metalplate-iron"], brake.Parts.Returns());
        Assert.Equal("iron", brake.EdgeMetal);
        Assert.Null(CutterClick(player, pos, CutterItem(Folding.LeadHalfPlate)));
        Assert.True(brake.PlateOn);
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(brake.PlateOn);
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-meteoriciron"));
        Assert.Equal(1, drops.GetValueOrDefault("game:metalplate-iron"));
        Assert.Equal(0, drops.GetValueOrDefault("game:metal-parts"));
        CutterKillItems(pos);

        // the creative shortcut fits each stage's first code, free
        brake = await PlaceBrake(pos, "east");
        Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
        Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
        Assert.True(brake.Complete);
        Assert.Equal(["game:metal-parts", "game:metalplate-iron"], brake.Parts.Returns());
    }

    // ---- Folding ----

    // A lead half plate goes on by hand and is folded only while the lever is worked (right-click held,
    // as on the quern); at W = 1 one lead angle comes off over the leaf, beyond the output face, and the
    // half plate is used up. A whole plate (the squaring shear's work), a hollow (chute) section, a pipe
    // section, an angle, an ingot and another metal's plate never go on.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Press_brake_folds_a_lead_half_plate_into_one_angle_while_the_lever_is_held()
    {
        var pos = await CutterSite(-332, -340);
        var player = await CutterPlayer();
        var brake = await PlaceBrake(pos, "south");
        AssembleBrake(brake, player);

        // not what it folds: a whole plate, a hollow section, a pipe section, an ingot, a tin plate and its own angle stay in hand
        Assert.True(W.GetItem(new AssetLocation(AngleLead)) is { Id: > 0, IsMissing: false }, $"no {AngleLead}");
        foreach (var code in new[] { "game:metalplate-lead", "game:metalplate-copper", "game:chutesection-copper", "game:chutesection-lead", "seraphhorizons:pipesection-lead", "game:ingot-lead", "game:metalplate-tin", AngleLead, AngleCopper })
        {
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(brake.PlateOn, $"{code} went on");
        }
        // with nothing on the bed, the lever is not worked
        Assert.False((await BrakeHold(player, pos, 3)).Started);

        // a lead half plate goes on, one from the stack; another in hand works the lever instead
        Assert.Equal(2, CutterClick(player, pos, CutterItem(Folding.LeadHalfPlate, 3))?.StackSize);
        Assert.True(brake.PlateOn);
        Assert.Equal(1, brake.Job.Class);
        Assert.Equal(Folding.LeadHalfPlate, brake.Plate?.Collectible.Code.ToString());
        Assert.Contains("A lead half plate on the bed, 0% folded", BrakeInfo(brake, player));

        // nothing moves until the lever is worked; held from the ghost, it folds; let go, it stops
        await World.Ticks(10);
        Assert.Equal(0, brake.Job.Work);
        var (started, steps) = await BrakeHold(player, BrakeGhost(brake), 10);
        Assert.True(started);
        Assert.Equal(10, steps);
        double held = brake.Job.Work;
        Assert.InRange(held, 0.01, 0.6);
        Assert.False(brake.Running);
        await World.Ticks(20);
        Assert.Equal(held, brake.Job.Work);
        // held again with a half plate in hand: it works the lever, the half plate stays in hand
        (started, _) = await BrakeHold(player, pos, 5, CutterItem(Folding.LeadHalfPlate));
        Assert.True(started);
        Assert.True(brake.Job.Work > held);
        Assert.Equal(1, player.InventoryManager.ActiveHotbarSlot.StackSize);

        // the rest of the fold: just short of the end, then over it
        Assert.Equal(0, brake.Fold(BrakeRadiansTo(brake, 0.99)));
        Assert.True(brake.PlateOn);
        Assert.Equal(1, brake.Fold(BrakeRadiansTo(brake, 1)));
        Assert.False(brake.PlateOn);
        await World.Ticks(5);
        var near = CutterItemsNear(pos);
        Assert.Equal(1, near.GetValueOrDefault(AngleLead));
        Assert.Equal(0, near.GetValueOrDefault(Folding.LeadHalfPlate));   // the half plate is used up
        // it dropped beyond the output face (native north), in front of the leaf end
        foreach (var e in World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
                     .OfType<EntityItem>().Where(e => e.Itemstack.Collectible.Code.ToString() == AngleLead))
        {
            var local = Footprint.ToLocal(new Float3((float)(e.Pos.X - pos.X), (float)(e.Pos.Y - pos.Y), (float)(e.Pos.Z - pos.Z)), brake.Side);
            Assert.True(local.Z < 0.01f, $"the angle is at native {local}");
        }
        Assert.Contains("Bed empty", BrakeInfo(brake, player));
        CutterKillItems(pos);
    }

    // Copper folds the same, into one copper angle, at two and a quarter lever turns a half plate against lead's one and a half.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Press_brake_folds_copper_at_its_own_pace()
    {
        var pos = await CutterSite(-344, -340);
        var player = await CutterPlayer();
        var brake = await PlaceBrake(pos, "west");
        AssembleBrake(brake, player, edge: "game:metalplate-steel");
        Assert.Null(CutterClick(player, pos, CutterItem(Folding.CopperHalfPlate)));
        Assert.Equal(2, brake.Job.Class);
        Assert.Equal(brake.LeverTurnsPerPlate(1) * 1.5, brake.LeverTurnsPerPlate(2), 3);
        // a lead half plate's lever turns (1.5) fold two thirds of a copper one
        Assert.Equal(0, brake.Fold(2 * Math.PI * brake.LeverTurnsPerPlate(1)));
        Assert.Equal(2 / 3.0, brake.Job.Work, 3);
        Assert.Equal(1, brake.Fold(BrakeRadiansTo(brake, 1)));
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(AngleCopper));
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Press_brake_gives_back_a_flat_half_plate_and_its_parts_and_everything_when_broken()
    {
        var pos = await CutterSite(-356, -340);
        var player = await CutterPlayer();
        var brake = await PlaceBrake(pos, "north");
        AssembleBrake(brake, player, edge: "game:metalplate-iron");

        // a flat half plate comes back by Ctrl; the parts stay while one is on
        Assert.Null(CutterClick(player, pos, CutterItem(Folding.CopperHalfPlate)));
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(brake.PlateOn);
        Assert.Equal(1, BrakeHeld(player, Folding.CopperHalfPlate));
        Assert.True(brake.Complete);
        // once folding has begun it stays, and so do the parts
        Assert.Null(CutterClick(player, pos, CutterItem(Folding.LeadHalfPlate)));
        brake.Fold(BrakeRadiansTo(brake, 0.3));
        CutterClick(player, BrakeGhost(brake), null, ctrl: true);
        Assert.True(brake.PlateOn);
        Assert.True(brake.Complete);
        Assert.Equal(0, BrakeHeld(player, Folding.LeadHalfPlate));

        // broken with a half plate part folded: the frame and both parts, and the half plate is lost
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(BrakeGhost(brake)).OnBlockBroken(W, BrakeGhost(brake), player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(BrakeFrame));
        Assert.Equal(1, drops.GetValueOrDefault("game:metal-parts"));
        Assert.Equal(1, drops.GetValueOrDefault("game:metalplate-iron"));
        Assert.Equal(0, drops.GetValueOrDefault(Folding.LeadHalfPlate));
        Assert.Equal(0, drops.GetValueOrDefault(AngleLead));
        CutterKillItems(pos);

        // broken with a flat half plate on, it comes back too
        brake = await PlaceBrake(pos, "north");
        AssembleBrake(brake, player);
        Assert.Null(CutterClick(player, pos, CutterItem(Folding.LeadHalfPlate)));
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(Folding.LeadHalfPlate));
        Assert.Equal(1, drops.GetValueOrDefault("game:metal-parts"));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    // Worked on an empty bed, the brake takes a half plate from a chest beyond its far end, never
    // anything else (a whole plate, an ingot, an angle, a hollow section), and puts the angles in a
    // chest in front of the leaf end.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Press_brake_takes_half_plates_from_a_chest_when_worked_and_puts_angles_in_one()
    {
        var pos = await CutterSite(-368, -340);
        var player = await CutterPlayer();
        var brake = await PlaceBrake(pos, "north");
        AssembleBrake(brake, player);
        var infeed = brake.CellPos(Assert.Single(BrakeRig.InfeedNeighbours()));
        var outfeed = brake.CellPos(BrakeRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        source.Inventory[0].Itemstack = CutterItem("game:ingot-copper");
        source.Inventory[1].Itemstack = CutterItem(AngleLead);
        source.Inventory[2].Itemstack = CutterItem(Folding.CopperHalfPlate, 2);
        source.Inventory[3].Itemstack = CutterItem("game:chutesection-copper");
        source.Inventory[4].Itemstack = CutterItem("game:metalplate-copper");
        source.MarkDirty(true);

        // a hand machine: nothing is taken while no one works it
        await World.Ticks(30);
        Assert.False(brake.PlateOn);
        var (started, _) = await BrakeHold(player, pos, 4);
        Assert.True(started);
        Assert.True(brake.PlateOn);
        Assert.Equal(2, brake.Job.Class);
        Assert.Equal(1, source.Inventory[2].StackSize);
        Assert.Equal(1, brake.Fold(BrakeRadiansTo(brake, 1)));
        Assert.Equal(1, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == AngleCopper).Sum(s => s.StackSize));
        Assert.DoesNotContain(AngleCopper, CutterItemsNear(pos).Keys);
        // worked again once the leaf has cleared, the next half plate goes on
        await World.Ticks(20);
        (started, _) = await BrakeHold(player, pos, 4);
        Assert.True(started);
        Assert.True(brake.PlateOn);
        Assert.True(source.Inventory[2].Empty);
        Assert.Equal(1, brake.Fold(BrakeRadiansTo(brake, 1)));
        Assert.Equal(2, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == AngleCopper).Sum(s => s.StackSize));
        // only the ingot, the angle, the hollow section and the whole plate are left, and the lever is not worked on them
        await World.Ticks(20);
        Assert.False((await BrakeHold(player, pos, 4)).Started);
        Assert.False(brake.PlateOn);
        Assert.Equal(1, source.Inventory[0].StackSize);
        Assert.Equal(AngleLead, source.Inventory[1].Itemstack?.Collectible.Code.ToString());
        Assert.Equal(1, source.Inventory[3].StackSize);
        Assert.Equal("game:metalplate-copper", source.Inventory[4].Itemstack?.Collectible.Code.ToString());
    }
}
