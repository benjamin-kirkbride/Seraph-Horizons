using Atlas.XUnit;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear;
using SeraphHorizons.Mod.SquaringShear.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, SquaringShear (mods-src/seraphhorizons/SquaringShear/): the squaring shear in the
// plain world, worked by hand. The player's right-click held on it is played through the block's own
// interaction calls (start, a step each tick, stop), as the game makes them, so each scenario that
// cuts shows W moving only while held; a lead plate takes 1 second of holding (copper 1.5), so the
// scenarios hold for less and then finish it through BESquaringShear.Cut, as the press brake's finish a
// plate through Fold. Every scenario builds on a granite floor of its own 40 above spawn at x -380 to
// -428, z -380 to -400 (clear of the press brake's at x -320 to -368, z -320 to -340), and they share
// the gear cutter's player, gearcutterhand (the server takes 16 players at most, and the other
// scenarios use the rest).
public partial class SharedWorldScenarios
{
    private SquaringShearSystem ShearMod => SquaringShearSystem.Of(World.Api);
    private SquaringShearRig ShearRig => ShearMod.Rig ?? throw new Xunit.Sdk.XunitException("the squaring shear's rig did not load");

    // What it makes: the half plate, its own item, two a plate.
    private const string HalfLead = "seraphhorizons:halfplate-lead";
    private const string HalfCopper = "seraphhorizons:halfplate-copper";
    private const string ShearFrame = "seraphhorizons:squaringshear-frame-north";

    private async Task<BESquaringShear> PlaceShear(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:squaringshear-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BESquaringShear ?? throw new Xunit.Sdk.XunitException($"no squaring shear at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>The far cell, the ghost.</summary>
    private static BlockPos ShearGhost(BESquaringShear shear) => shear.GhostCells().Single();

    /// <summary>Both stages by right-clicks with real items, the blades on the frame and the gauge on the ghost.</summary>
    private void AssembleShear(BESquaringShear shear, IPlayer player, string blade = "game:metalplate-iron", string gauge = "game:rod-iron")
    {
        Assert.Null(CutterClick(player, shear.Pos, CutterItem(blade)));
        Assert.Null(CutterClick(player, ShearGhost(shear), CutterItem(gauge)));
        Assert.True(shear.Complete);
    }

    /// <summary>
    /// Right-click held on <paramref name="at"/> with <paramref name="held"/> in hand for
    /// <paramref name="ticks"/> server ticks, as the game plays it: the interaction's start, a step
    /// each tick while the block says go on, then the stop. Returns whether the start was the
    /// shear's and how many steps it went on for.
    /// </summary>
    private async Task<(bool Started, int Steps)> ShearHold(IPlayer player, BlockPos at, int ticks, ItemStack? held = null)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
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

    /// <summary>Radians of the treadle that bring the plate on the table to W = <paramref name="work"/>.</summary>
    private static double ShearRadiansTo(BESquaringShear shear, double work) =>
        (work - shear.Job.Work) * 2 * Math.PI * shear.StrokesPerPlate(shear.Job.Class) + 1e-6;

    private static string ShearInfo(BESquaringShear shear, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        shear.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private static int ShearHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    // ---- Loading ----

    [AtlasScenario]
    public void Squaring_shear_loads_with_its_blocks_half_plate_and_recipe()
    {
        Assert.True(SquaringShearSystem.Applies(World.Api));
        Assert.NotNull(ShearMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockSquaringShear>(W.GetBlock(new AssetLocation($"seraphhorizons:squaringshear-frame-{side}")));
        Assert.IsType<BlockSquaringShearGhost>(W.GetBlock(new AssetLocation("seraphhorizons:squaringshear-ghost")));
        // every part the stages take exists here: the game's plates and rods
        foreach (var stage in SquaringShearRequires.Stages)
            foreach (var code in SquaringShearParts.CodesFor(stage))
                Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false }, $"no {code} for {stage}");
        // what it cuts, the game's lead and copper plates, and what comes off: its own half plates
        foreach (var k in new[] { 1, 2 })
        {
            Assert.True(W.GetItem(new AssetLocation(Cutting.PlateFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Cutting.PlateFor(k)}");
            Assert.True(W.GetItem(new AssetLocation(Cutting.HalfPlateFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Cutting.HalfPlateFor(k)}");
        }
        Assert.Equal((HalfLead, HalfCopper), (Cutting.HalfPlateFor(1), Cutting.HalfPlateFor(2)));
        Assert.Equal("Squaring shear frame", new ItemStack(W.GetBlock(BlockSquaringShear.ItemCode)).GetName());
        Assert.Equal("Lead half plate", new ItemStack(W.GetItem(new AssetLocation(HalfLead))).GetName());
        Assert.Equal("Copper half plate", new ItemStack(W.GetItem(new AssetLocation(HalfCopper))).GetName());
        // a half plate melts back into one ingot of its metal
        var smelted = W.GetItem(new AssetLocation(HalfCopper))!.CombustibleProps!.SmeltedStack;
        smelted.Resolve(W, "test");
        Assert.Equal(("game:ingot-copper", 1), (smelted.ResolvedItemstack.Collectible.Code.ToString(), smelted.ResolvedItemstack.StackSize));

        // the grid: the frame, of oak and iron fittings, with a hammer; nothing else makes a half plate
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == ShearFrame && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:log-placed-oak-ud");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:plank-oak");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        Assert.DoesNotContain(W.GridRecipes, r => r.Enabled && r.Output?.Code?.Path?.StartsWith("halfplate") == true);
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Enabled && r.Output?.Code?.Path?.StartsWith("halfplate") == true);
        // the settings and the rig's pace agree
        Assert.Equal(ShearRig.StrokesPerPlate[1], ShearMod.Config.StrokesPerPlateLead, 0.001);
        Assert.Equal(ShearRig.StrokesPerPlate[2], ShearMod.Config.StrokesPerPlateCopper, 0.001);
    }

    // ---- Placing ----

    // On all four facings: room for both cells, a ghost stamped in the far one, the shear running
    // away along the facing from the table end, each cell's collision boxes its selection boxes and its
    // lid; broken through the ghost, both cells are cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Squaring_shear_places_on_all_four_facings_with_its_ghost_and_lids()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-380 - 12 * i++, -380);
            var shear = await PlaceShear(pos, side.Code());
            Assert.Equal(side, shear.Side);
            var ghost = ShearGhost(shear);
            var n = side.Normal();
            Assert.Equal(pos.AddCopy(n.X, 0, n.Z), ghost);
            Assert.Equal(pos, Assert.IsType<BESquaringShearGhost>(W.BlockAccessor.GetBlockEntity(ghost)).Principal);
            foreach (var cell in new[] { pos, ghost })
            {
                var block = W.BlockAccessor.GetBlock(cell);
                var selection = block.GetSelectionBoxes(W.BlockAccessor, cell);
                var collision = block.GetCollisionBoxes(W.BlockAccessor, cell);
                Assert.NotNull(selection);
                Assert.Equal(selection.Length + 1, collision.Length);
                Assert.Equal(ShearRig.Cells[0].Lid!.Value, collision[^1].Y2, 3);
            }
            Assert.Equal("Squaring shear frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
            Assert.Equal("Squaring shear frame", W.BlockAccessor.GetBlock(ghost).GetPlacedBlockName(W, ghost));
            // the half plates drop in front of the table end
            Assert.Equal(pos.AddCopy(-n.X, 0, -n.Z), shear.CellPos(ShearRig.OutputNeighbour()));

            W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.Equal(0, W.BlockAccessor.GetBlock(ghost).Id);
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(ShearFrame));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Squaring_shear_is_built_in_order_and_refuses_parts_out_of_order()
    {
        var pos = await CutterSite(-380, -400);
        var player = await CutterPlayer();
        var shear = await PlaceShear(pos, "east");
        var ghost = ShearGhost(shear);
        Assert.Contains("Next part: blades", ShearInfo(shear, player));
        Assert.Contains("Then: gauge", ShearInfo(shear, player));

        // the gauge before the blades is refused and stays in hand, from the frame or the ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-iron"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:rod-steel"))?.StackSize);
        Assert.False(shear.Parts.Has(SquaringShearStage.Gauge));
        // no plate before the shear is built, and an empty hand works no treadle on a bare frame
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Cutting.LeadPlate))?.StackSize);
        Assert.False(shear.PlateOn);
        Assert.Null(CutterClick(player, pos, null));
        Assert.False(_cutterHandled);
        // a rod or a plate of a metal it does not take is the item's own business
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper"))?.StackSize);
        Assert.False(_cutterHandled);

        // the blades, one plate from a stack of two; a second plate has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-steel", 2))?.StackSize);
        Assert.Equal("game:metalplate-steel", shear.Parts.FittedIn(SquaringShearStage.Blade));
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:metalplate-iron"))?.StackSize);
        Assert.Contains("Next part: gauge", ShearInfo(shear, player));
        Assert.False(shear.Complete);
        Assert.Equal("Squaring shear frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        // the gauge, from the ghost
        Assert.Null(CutterClick(player, ghost, CutterItem("game:rod-meteoriciron")));
        Assert.True(shear.Complete);
        Assert.Equal(("steel", "meteoriciron"), (shear.BladeMetal, shear.GaugeMetal));
        Assert.Equal("Squaring shear", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Table empty", ShearInfo(shear, player));

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        shear.ToTreeAttributes(tree);
        shear.FromTreeAttributes(tree, W);
        Assert.True(shear.Complete);
        Assert.Equal(["game:metalplate-steel", "game:rod-meteoriciron"], shear.Parts.Returns());

        // fitted parts never come back out: Ctrl on the frame or the ghost takes nothing
        CutterClick(player, pos, null, ctrl: true);
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(shear.Complete);
        Assert.Equal(0, ShearHeld(player, "game:rod-meteoriciron"));
        Assert.Equal(0, ShearHeld(player, "game:metalplate-steel"));
        Assert.Equal(["game:metalplate-steel", "game:rod-meteoriciron"], shear.Parts.Returns());

        // only breaking gives them back
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(ShearFrame));
        Assert.Equal(1, drops.GetValueOrDefault("game:metalplate-steel"));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-meteoriciron"));
        CutterKillItems(pos);

        // the creative shortcut fits each stage's first code, free
        shear = await PlaceShear(pos, "east");
        Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
        Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
        Assert.True(shear.Complete);
        Assert.Equal(["game:metalplate-iron", "game:rod-iron"], shear.Parts.Returns());
    }

    // ---- Cutting ----

    // A lead plate goes on by hand and is cut only while the treadle is worked (right-click held, as
    // on the quern); at W = 1 two lead half plates come off over the table, beyond the output face, and
    // the plate is used up. A half plate, an angle, an ingot and another metal's plate never go on.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Squaring_shear_cuts_a_lead_plate_into_two_half_plates_while_the_treadle_is_held()
    {
        var pos = await CutterSite(-392, -400);
        var player = await CutterPlayer();
        var shear = await PlaceShear(pos, "south");
        AssembleShear(shear, player);

        // not what it cuts: its own half plates, an angle, an ingot and a tin plate stay in hand
        foreach (var code in new[] { HalfLead, HalfCopper, "seraphhorizons:angle-lead", "game:ingot-lead", "game:metalplate-tin" })
        {
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(shear.PlateOn, $"{code} went on");
        }
        // with nothing on the table, the treadle is not worked
        Assert.False((await ShearHold(player, pos, 3)).Started);

        // a lead plate goes on, one from the stack; another in hand works the treadle instead
        Assert.Equal(2, CutterClick(player, pos, CutterItem(Cutting.LeadPlate, 3))?.StackSize);
        Assert.True(shear.PlateOn);
        Assert.Equal(1, shear.Job.Class);
        Assert.Equal(Cutting.LeadPlate, shear.Plate?.Collectible.Code.ToString());
        Assert.Contains("A lead plate on the table, 0% cut", ShearInfo(shear, player));

        // nothing moves until the treadle is worked; held from the ghost, it cuts; let go, it stops
        await World.Ticks(10);
        Assert.Equal(0, shear.Job.Work);
        var (started, steps) = await ShearHold(player, ShearGhost(shear), 6);
        Assert.True(started);
        Assert.Equal(6, steps);
        double held = shear.Job.Work;
        Assert.InRange(held, 0.01, 0.7);
        Assert.False(shear.Running);
        await World.Ticks(20);
        Assert.Equal(held, shear.Job.Work);
        // held again with a plate in hand: it works the treadle, the plate stays in hand
        (started, _) = await ShearHold(player, pos, 2, CutterItem(Cutting.LeadPlate));
        Assert.True(started);
        Assert.True(shear.Job.Work > held);
        Assert.Equal(1, player.InventoryManager.ActiveHotbarSlot.StackSize);

        // the rest of the cut: just short of the end, then over it
        Assert.Equal(0, shear.Cut(ShearRadiansTo(shear, 0.99)));
        Assert.True(shear.PlateOn);
        Assert.Equal(2, shear.Cut(ShearRadiansTo(shear, 1)));
        Assert.False(shear.PlateOn);
        await World.Ticks(5);
        var near = CutterItemsNear(pos);
        Assert.Equal(2, near.GetValueOrDefault(HalfLead));
        Assert.Equal(0, near.GetValueOrDefault(Cutting.LeadPlate));   // the plate is used up
        // they dropped beyond the output face (native north), in front of the table end
        foreach (var e in World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
                     .OfType<EntityItem>().Where(e => e.Itemstack.Collectible.Code.ToString() == HalfLead))
        {
            var local = Footprint.ToLocal(new Float3((float)(e.Pos.X - pos.X), (float)(e.Pos.Y - pos.Y), (float)(e.Pos.Z - pos.Z)), shear.Side);
            Assert.True(local.Z < 0.01f, $"the half plates are at native {local}");
        }
        Assert.Contains("Table empty", ShearInfo(shear, player));
        CutterKillItems(pos);
    }

    // Copper cuts the same, into two copper half plates, at one and a half strokes a plate against lead's one.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Squaring_shear_cuts_copper_at_its_own_pace()
    {
        var pos = await CutterSite(-404, -400);
        var player = await CutterPlayer();
        var shear = await PlaceShear(pos, "west");
        AssembleShear(shear, player, "game:metalplate-steel", "game:rod-steel");
        Assert.Null(CutterClick(player, pos, CutterItem(Cutting.CopperPlate)));
        Assert.Equal(2, shear.Job.Class);
        Assert.Equal(shear.StrokesPerPlate(1) * 1.5, shear.StrokesPerPlate(2), 3);
        // one stroke cuts a lead plate, two thirds of a copper one
        Assert.Equal(0, shear.Cut(2 * Math.PI * shear.StrokesPerPlate(1)));
        Assert.Equal(2 / 3.0, shear.Job.Work, 3);
        Assert.Equal(2, shear.Cut(ShearRadiansTo(shear, 1)));
        await World.Ticks(5);
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(HalfCopper));
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Squaring_shear_gives_back_a_whole_plate_and_its_parts_and_everything_when_broken()
    {
        var pos = await CutterSite(-416, -400);
        var player = await CutterPlayer();
        var shear = await PlaceShear(pos, "north");
        AssembleShear(shear, player, "game:metalplate-iron", "game:rod-steel");

        // a whole plate comes back by Ctrl; the parts stay while one is on
        Assert.Null(CutterClick(player, pos, CutterItem(Cutting.CopperPlate)));
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(shear.PlateOn);
        Assert.Equal(1, ShearHeld(player, Cutting.CopperPlate));
        Assert.True(shear.Complete);
        // once cutting has begun it stays, and so do the parts
        Assert.Null(CutterClick(player, pos, CutterItem(Cutting.LeadPlate)));
        shear.Cut(ShearRadiansTo(shear, 0.3));
        CutterClick(player, ShearGhost(shear), null, ctrl: true);
        Assert.True(shear.PlateOn);
        Assert.True(shear.Complete);
        Assert.Equal(0, ShearHeld(player, Cutting.LeadPlate));

        // broken with a plate half cut: the frame and both parts, and the plate is lost
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ShearGhost(shear)).OnBlockBroken(W, ShearGhost(shear), player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(ShearFrame));
        Assert.Equal(1, drops.GetValueOrDefault("game:metalplate-iron"));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-steel"));
        Assert.Equal(0, drops.GetValueOrDefault(Cutting.LeadPlate));
        Assert.Equal(0, drops.GetValueOrDefault(HalfLead));
        CutterKillItems(pos);

        // broken with a whole plate on, the plate comes back too
        shear = await PlaceShear(pos, "north");
        AssembleShear(shear, player);
        Assert.Null(CutterClick(player, pos, CutterItem(Cutting.LeadPlate)));
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(Cutting.LeadPlate));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-iron"));
        CutterKillItems(pos);
    }

    // ---- Chests ----

    // A chest beyond the far end and one in front of the table end are left alone: worked on an empty
    // table, the shear takes no plate from the first, and the half plates of a plate put on by hand
    // drop in front of the table end rather than going into the second.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Squaring_shear_takes_nothing_from_a_chest_and_puts_nothing_in_one()
    {
        var pos = await CutterSite(-428, -400);
        var player = await CutterPlayer();
        var shear = await PlaceShear(pos, "north");
        AssembleShear(shear, player);
        var infeed = shear.CellPos(new Int3(0, 0, 2));
        var outfeed = shear.CellPos(ShearRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        source.Inventory[0].Itemstack = CutterItem(Cutting.CopperPlate, 2);
        source.MarkDirty(true);

        await World.Ticks(20);
        Assert.False((await ShearHold(player, pos, 4)).Started);
        Assert.False(shear.PlateOn);
        Assert.Equal(2, source.Inventory[0].StackSize);
        Assert.Null(CutterClick(player, pos, CutterItem(Cutting.CopperPlate)));
        Assert.Equal(2, shear.Cut(ShearRadiansTo(shear, 1)));
        await World.Ticks(5);
        Assert.All(sink.Inventory, s => Assert.True(s.Empty));
        Assert.Equal(2, CutterItemsNear(pos).GetValueOrDefault(HalfCopper));
        Assert.Equal(2, source.Inventory[0].StackSize);
        CutterKillItems(pos);
    }
}
