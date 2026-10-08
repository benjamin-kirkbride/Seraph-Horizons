using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.GearCutter;
using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, GearCutter (mods-src/seraphhorizons/GearCutter/, #480, #481): the gear cutter in
// the plain world, driven by a real mechanical power network (a vanilla creative rotor against its
// power face). A whole gear is 144 axle turns at the default settings, minutes of a creative
// rotor, so each scenario shows the shaft cutting and then finishes the gear through
// BEGearCutter.Cut, as the rosser's scenarios feed through BERosser.Feed. Every scenario builds on a
// granite floor of its own 40 above spawn at x -140 to -200, z -140 to -200 (clear of the gear blanks
// at x -90 and gear reclamation at x -100), and they share one player, gearcutterhand.
public partial class SharedWorldScenarios
{
    private GearCutterSystem CutterMod => GearCutterSystem.Of(World.Api);
    private GearCutterRig CutterRig => CutterMod.Rig ?? throw new Xunit.Sdk.XunitException("the gear cutter's rig did not load");

    private static ITestPlayer? _cutterHand;
    private static object? _cutterWorld;

    /// <summary>The gear cutter scenarios' player, in survival with empty hands and inventory and its
    /// keys up. The trunk tool scenarios work with it too (<see cref="TrunkToolSetup"/>): the server
    /// takes 16 players at most, and the other scenarios use the rest.</summary>
    private async Task<ITestPlayer> CutterHand()
    {
        if (_cutterHand == null || !ReferenceEquals(_cutterWorld, World.Api))
        {
            _cutterHand = await World.JoinPlayer("gearcutterhand");
            _cutterWorld = World.Api;
        }
        var player = _cutterHand.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        player.Entity.LeftHandItemSlot.Itemstack = null;
        player.Entity.LeftHandItemSlot.MarkDirty();
        player.Entity.Controls.CtrlKey = player.Entity.Controls.ShiftKey = false;
        return _cutterHand;
    }

    private async Task<IPlayer> CutterPlayer() => (await CutterHand()).Player;

    private ItemStack CutterItem(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size) : throw new Xunit.Sdk.XunitException($"no item {code}");

    /// <summary>A spot 40 above spawn, its chunk columns loaded, cleared around on a granite floor.</summary>
    private async Task<BlockPos> CutterSite(int dx, int dz, int reach = 5)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        if (World.Api is ICoreServerAPI sapi)
            foreach (var c in columns)
                sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        int floor = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 4; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        CutterKillItems(origin);
        return origin;
    }

    private async Task<BEGearCutter> PlaceCutter(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:gearcutter-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEGearCutter ?? throw new Xunit.Sdk.XunitException($"no gear cutter at {pos}");
        be.PlaceGhosts();
        return be;
    }

    // Whether the last CutterClick was taken by the block.
    private bool _cutterHandled;

    /// <summary>Right-clicks <paramref name="at"/> holding <paramref name="held"/>; returns what is left in the hand.</summary>
    private ItemStack? CutterClick(IPlayer player, BlockPos at, ItemStack? held, bool ctrl = false, bool creative = false)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        if (creative)
            player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        try
        {
            var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
            _cutterHandled = W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        }
        finally
        {
            player.Entity.Controls.CtrlKey = false;
            player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        }
        return slot.Itemstack;
    }

    private static string CutterInfo(BEGearCutter cutter, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        cutter.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>The parts in order, as stacks: every stage up to the head, then the kit and the master.</summary>
    private static readonly string[] CutterOrder =
    [
        GearCutterParts.SpindleCode, GearCutterParts.FeedScrewCode, GearCutterParts.GearboxCode, GearCutterParts.GearboxCode,
        GearCutterParts.LiftCamCode, GearCutterParts.IndexCode, GearCutterParts.ValveCode, GearCutterParts.HeadAltCode,
        GearCutterParts.KitCode,
    ];

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and a ghost,
    /// the master last; then a full oil tank.</summary>
    private void AssembleCutter(BEGearCutter cutter, IPlayer player, string master = GearCutterParts.MasterCode)
    {
        var ghost = cutter.GhostCells().First().Pos;
        int i = 0;
        foreach (var code in CutterOrder.Append(master))
            Assert.True(CutterClick(player, i++ % 2 == 0 ? cutter.Pos : ghost, CutterItem(code)) == null, $"{code} was not fitted");
        Assert.True(cutter.Complete);
        FillCutterOil(cutter, 1);
    }

    private static void FillCutterOil(BEGearCutter cutter, double fill)
    {
        Assert.NotNull(cutter.Oiling);
        var tank = cutter.Oiling.Tank;
        cutter.Oiling.Tank = tank with { Points = tank.Capacity * fill };
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns fast.</summary>
    private async Task<BlockPos> PowerCutter(BEGearCutter cutter)
    {
        var ghost = cutter.CellPos(CutterRig.PowerCell);
        var face = Assert.IsType<BlockGearCutterGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => cutter.ShaftSpeed >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>Radians that finish the gear on the arbor from where it is.</summary>
    private static double RestOfGear(BEGearCutter cutter) =>
        (cutter.Job.End - cutter.Job.Work) * 2 * Math.PI * cutter.TurnsPerTooth + 1e-6;

    private Dictionary<string, int> CutterItemsNear(BlockPos around, int radius = 6)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    private void CutterKillItems(BlockPos around, int radius = 6)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        foreach (var e in World.EntitiesIn(box).OfType<EntityItem>())
            e.Die(EnumDespawnReason.Removed);
    }

    // ---- Loading ----

    [AtlasScenario]
    public void Gear_cutter_loads_with_its_blocks_parts_and_recipes()
    {
        Assert.True(GearCutterSystem.Applies(World.Api));
        Assert.NotNull(CutterMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
        {
            Assert.IsType<BlockGearCutter>(W.GetBlock(new AssetLocation($"seraphhorizons:gearcutter-frame-{side}")));
            Assert.IsType<BlockGearCutterGhostPower>(W.GetBlock(new AssetLocation($"seraphhorizons:gearcutter-ghostpower-{side}")));
        }
        Assert.IsType<BlockGearCutterGhost>(W.GetBlock(new AssetLocation("seraphhorizons:gearcutter-ghost")));
        // every part the stages take exists here, the Jonas parts and the masters included
        foreach (var stage in GearCutterRequires.Stages)
            foreach (var code in GearCutterParts.CodesFor(stage))
                Assert.True(W.GetItem(new AssetLocation(code)) != null, $"no {code} for {stage}");
        Assert.Equal(500, CutterItem(GearCutterParts.KitCode).Collectible.GetMaxDurability(CutterItem(GearCutterParts.KitCode)));
        Assert.Equal("Gear cutter frame", new ItemStack(W.GetBlock(BlockGearCutter.ItemCode)).GetName());

        // the grid: the frame, the spindle and the index
        foreach (var output in new[] { "gearcutter-frame-north", "gearcutterspindle", "gearcutterindex" })
            Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:" + output && r.Enabled);
        var frame = W.GridRecipes.Single(r => r.Output?.Code?.ToString() == "seraphhorizons:gearcutter-frame-north");
        // the end-game machine: 4 steel ingots in each of its two ingot slots (steel only), 32 nails and strips
        var ingots = frame.ResolvedIngredients!.Where(i => i?.Code?.Path == "ingot-*").ToList();
        Assert.Equal(2, ingots.Count);
        Assert.All(ingots, i => { Assert.Equal(new[] { "steel" }, i!.AllowedVariants); Assert.Equal(4, i.Quantity); });
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 32);
        // 3 steel gears for the gearing the frame carries (the feed rectifier, the camshaft's worm wheel)
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "seraphhorizons:gear-steel" && i.Quantity == 3);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the spindle and the index: 2 steel rods and 2 steel plates each
        foreach (var output in new[] { "gearcutterspindle", "gearcutterindex" })
        {
            var part = W.GridRecipes.Single(r => r.Output?.Code?.ToString() == "seraphhorizons:" + output);
            Assert.Contains(part.ResolvedIngredients!, i => i?.Code?.Path == "rod-steel" && i.Quantity == 2);
            Assert.Contains(part.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-steel" && i.Quantity == 2);
        }
        // the anvil: the feed screw, the lift cam and the kit, each from one steel ingot, none for the helve hammer
        foreach (var output in new[] { GearCutterParts.FeedScrewCode, GearCutterParts.LiftCamCode, GearCutterParts.KitCode })
        {
            var recipe = Assert.Single(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == output);
            Assert.True(recipe.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-steel")));
            Assert.False(recipe.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-iron")));
            Assert.DoesNotContain(recipe.Name?.Path, new[] { "plate", "blistersteel" });
            Assert.InRange(recipe.Voxels.Cast<bool>().Count(v => v), 1, 42);
        }
        // the settings and the rig's pace agree
        Assert.Equal(CutterRig.TurnsPerTooth, CutterMod.Config.TurnsPerTooth);
        Assert.Equal(10f, SeraphHorizons.Mod.MachineOil.MachineOilSystem.Of(World.Api).Config.GearCutter.DrainPerJob);
    }

    // ---- Placing ----

    // On all four facings: room for every cell, a ghost stamped in each, the power ghost's axle face
    // the native west turned to the facing, the infeed and output faces turned with it; broken
    // through a ghost, every cell is cleared.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Gear_cutter_places_on_all_four_facings()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-140 - 10 * i++, -140);
            var cutter = await PlaceCutter(pos, side.Code());
            Assert.Equal(side, cutter.Side);
            var cells = cutter.GhostCells().ToList();
            Assert.Equal(7, cells.Count);
            foreach (var (cell, power) in cells)
            {
                var be = Assert.IsType<BEGearCutterGhost>(W.BlockAccessor.GetBlockEntity(cell));
                Assert.Equal(pos, be.Principal);
                Assert.Equal(power, W.BlockAccessor.GetBlock(cell) is BlockGearCutterGhostPower);
                Assert.NotNull(W.BlockAccessor.GetBlock(cell).GetSelectionBoxes(W.BlockAccessor, cell));
            }
            var powerGhost = cells.Single(c => c.Power).Pos;
            var face = Assert.IsType<BlockGearCutterGhostPower>(W.BlockAccessor.GetBlock(powerGhost)).PowerFace;
            Assert.Equal(Footprint.ToWorld(Side.West, side).Code(), face.Code);
            // the axle's face is outside the machine
            Assert.DoesNotContain(cells, c => c.Pos.Equals(powerGhost.AddCopy(face)));
            Assert.Equal("Gear cutter frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));

            W.BlockAccessor.GetBlock(cells[3].Pos).OnBlockBroken(W, cells[3].Pos, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault("seraphhorizons:gearcutter-frame-north"));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Gear_cutter_is_built_stage_by_stage_in_order()
    {
        var pos = await CutterSite(-180, -140);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos);
        var ghost = cutter.GhostCells().Last().Pos;
        Assert.Contains("Next part: spindle", CutterInfo(cutter, player));

        // a later stage's part is refused and stays in hand, from the frame or a ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterParts.FeedScrewCode))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(GearCutterParts.MasterCode))?.StackSize);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(GearCutterParts.KitCode))?.StackSize);
        Assert.False(cutter.Parts.Has(GearCutterStage.Spindle));
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the parts in order, one each, taken from the hand
        for (int i = 0; i < CutterOrder.Length; i++)
        {
            var next = cutter.Parts.Next!.Value;
            var left = CutterClick(player, i % 2 == 0 ? pos : ghost, CutterItem(CutterOrder[i], i == 0 ? 2 : 1));
            Assert.True(cutter.Parts.Has(next), $"{CutterOrder[i]} did not go in as {next}");
            Assert.Equal(i == 0 ? 1 : 0, left?.StackSize ?? 0);
            // the same part again is refused once its stages are full
            if (i == 0)
                Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterParts.SpindleCode))?.StackSize);
        }
        // a third eccentric gearbox has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterParts.GearboxCode))?.StackSize);
        Assert.False(cutter.Complete);
        Assert.Contains("Next part: master", CutterInfo(cutter, player));
        // no blank before the master
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);
        Assert.Null(CutterClick(player, ghost, CutterItem(GearCutterParts.MasterCode)));
        Assert.True(cutter.Complete);
        Assert.Equal("Gear cutter", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Master: temporal gear", CutterInfo(cutter, player));
        Assert.Contains("Cutter kit: 500 of 500", CutterInfo(cutter, player));

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        cutter.ToTreeAttributes(tree);
        cutter.FromTreeAttributes(tree, W);
        Assert.True(cutter.Complete);
        Assert.Equal(GearCutterParts.HeadAltCode, cutter.Parts.FittedIn(GearCutterStage.Head));
        Assert.Equal(10, cutter.Parts.Returns().Count);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Gear_cutter_creative_shortcut_fits_the_next_stage_free()
    {
        var pos = await CutterSite(-200, -140);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos);
        int stages = 0;
        while (!cutter.Complete && stages < 20)
        {
            var next = cutter.Parts.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(cutter.Parts.Has(next), $"the shortcut did not fit {next}");
            stages++;
        }
        Assert.Equal(10, stages);
        Assert.Equal(1, cutter.Parts.Master);
        Assert.Equal(500, cutter.Parts.KitLeft);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.False(cutter.Parts.Has(GearCutterStage.Cutter));
    }

    // ---- Cutting ----

    // Under power a small blank turns into a steel gear at the output face, the large master takes a
    // large blank to a large gear, each master refuses the other size, and the cut advances with the
    // shaft only.
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Gear_cutter_cuts_small_and_large_gears_under_power()
    {
        var pos = await CutterSite(-140, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "east");
        AssembleCutter(cutter, player);

        // the wrong size is refused, the right one goes on
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.LargeBlank))?.StackSize);
        Assert.False(cutter.BlankOn);
        Assert.Equal(2, CutterClick(player, pos, CutterItem(GearCut.Blank, 3))?.StackSize);
        Assert.True(cutter.BlankOn);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);   // one at a time

        // unpowered, nothing moves; powered, the shaft cuts
        await World.Ticks(10);
        Assert.Equal(0, cutter.Job.Work);
        Assert.Contains("Stopped with a blank on: 0 of 12 teeth", CutterInfo(cutter, player));
        var rotor = await PowerCutter(cutter);
        await World.Until(() => cutter.Job.Work > 0.02, 30000);
        Assert.True(cutter.Running);
        Assert.Contains("Cutting:", CutterInfo(cutter, player));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.False(cutter.BlankOn);
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(GearCut.Gear));
        // it dropped beyond the output face, not inside the machine
        var gear = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == GearCut.Gear);
        var local = Footprint.ToLocal(new Float3((float)(gear.Pos.X - pos.X), (float)(gear.Pos.Y - pos.Y), (float)(gear.Pos.Z - pos.Z)), cutter.Side);
        Assert.True(local.Z > 2 - 0.01f, $"the gear is at native {local}");
        Assert.Equal(490, cutter.Parts.KitLeft);
        Assert.Equal(990, cutter.Oiling!.Tank.Points, 6);
        CutterKillItems(pos);

        // changed over to the large master: kit out, master out, master in, kit in
        CutterClick(player, pos, null, ctrl: true);
        CutterClick(player, pos, null, ctrl: true);
        Assert.Equal(0, cutter.Parts.Master);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterParts.KitCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterParts.LargeMasterCode)));
        Assert.Equal(2, cutter.Parts.Master);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.LargeBlank)));
        Assert.Equal(20, cutter.Job.End);
        await World.Until(() => cutter.Job.Work > 0.02, 30000);
        // a large gear is 240 turns: 239 is not enough
        Assert.False(cutter.Cut(RestOfGear(cutter) - 2 * Math.PI * cutter.TurnsPerTooth * 0.1));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(GearCut.LargeGear));
        // a large gear drains double and wears 20/12
        Assert.Equal(970, cutter.Oiling!.Tank.Points, 6);
        Assert.Equal(500 - 17, cutter.Parts.KitLeft);
        W.BlockAccessor.SetBlock(0, rotor);
        CutterKillItems(pos);
    }

    // The kit wears by the oil's fill when each gear finishes (base, double at half, ten times at a
    // tenth), breaks on a gear cut dry, and the cutter stops with the blank on until a new kit goes
    // in, then resumes where it was. The shaft's load is the same oiled or dry.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Gear_cutter_kit_wears_with_the_oil_and_breaks_dry()
    {
        var pos = await CutterSite(-160, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos);
        AssembleCutter(cutter, player);
        var mp = W.BlockAccessor.GetBlockEntity(cutter.CellPos(CutterRig.PowerCell))!.GetBehavior<BEBehaviorGearCutterMP>()!;
        float oiledLoad = mp.GetResistance();
        Assert.Equal(CutterMod.Config.Resistance, oiledLoad);

        int GearWear(double fill)
        {
            FillCutterOil(cutter, fill);
            int before = cutter.Parts.KitLeft;
            Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
            Assert.True(cutter.Cut(RestOfGear(cutter)));
            return before - cutter.Parts.KitLeft;
        }
        Assert.Equal(10, GearWear(1));
        Assert.Equal(20, GearWear(0.5));
        Assert.Equal(100, GearWear(0.1));
        Assert.Equal(370, cutter.Parts.KitLeft);
        FillCutterOil(cutter, 0.1);   // the gear drained the tank to 0.09
        Assert.Contains("Cutter wear: 10× per gear", CutterInfo(cutter, player));
        Assert.Contains("Cutter kit: 370 of 500, about 4 more gears", CutterInfo(cutter, player));

        // dry: the same load, and the next gear breaks the kit
        FillCutterOil(cutter, 0);
        Assert.Equal(oiledLoad, mp.GetResistance());
        Assert.Contains("No oil: the next gear breaks the cutter kit", CutterInfo(cutter, player));
        Assert.Contains($"Load: {OilText.Load(oiledLoad)} kN", CutterInfo(cutter, player));
        Assert.DoesNotContain("Dry:", CutterInfo(cutter, player));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.False(cutter.Parts.Has(GearCutterStage.Cutter));
        Assert.False(cutter.Complete);
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(GearCut.Gear));   // the last gear still came out
        CutterKillItems(pos);
        // no blank goes on without a kit
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);

        // a refit resumes a cut stopped half way by taking the kit out
        FillCutterOil(cutter, 1);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterParts.KitCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 5);
        Assert.Equal(5, cutter.Job.Work, 3);
        CutterClick(player, pos, null, ctrl: true);   // the kit comes out first
        Assert.True(cutter.BlankOn);
        var kit = Assert.Single(player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!
            .Concat(player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)!),
            s => s.Itemstack?.Collectible.Code.ToString() == GearCutterParts.KitCode).Itemstack!;
        Assert.Equal(500, kit.Collectible.GetRemainingDurability(kit));
        Assert.False(cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 20));
        Assert.Equal(5, cutter.Job.Work, 3);
        Assert.Null(CutterClick(player, pos, kit));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.Equal(490, cutter.Parts.KitLeft);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Gear_cutter_gives_back_the_kit_then_the_blank_then_the_master_and_everything_when_broken()
    {
        var pos = await CutterSite(-180, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "west");
        AssembleCutter(cutter, player, GearCutterParts.LargeMasterCode);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.LargeBlank)));
        cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 3);
        cutter.Parts.WearKit(123);

        int Held(string code) => player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
            .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);
        var ghost = cutter.GhostCells().First().Pos;
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCutterParts.KitCode));
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCut.LargeBlank));
        Assert.False(cutter.BlankOn);
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCutterParts.LargeMasterCode));
        Assert.Equal(0, cutter.Parts.Master);
        // the rest only come back by breaking
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(cutter.Parts.Has(GearCutterStage.Head));

        // refit, wear and load, then break through a ghost: every part, the kit with its wear, and the blank
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterParts.KitCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterParts.MasterCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        cutter.Parts.WearKit(77);
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:gearcutter-frame-north"));
        foreach (var code in new[] { GearCutterParts.SpindleCode, GearCutterParts.FeedScrewCode, GearCutterParts.LiftCamCode, GearCutterParts.IndexCode,
                                     GearCutterParts.ValveCode, GearCutterParts.HeadAltCode, GearCutterParts.KitCode, GearCutterParts.MasterCode, GearCut.Blank })
            Assert.True(drops.GetValueOrDefault(code) == 1, $"{code}: {drops.GetValueOrDefault(code)}");
        Assert.Equal(2, drops.GetValueOrDefault(GearCutterParts.GearboxCode));
        var kit = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == GearCutterParts.KitCode).Itemstack;
        Assert.Equal(423, kit.Collectible.GetRemainingDurability(kit));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Gear_cutter_takes_blanks_from_a_chest_and_puts_gears_in_one()
    {
        var pos = await CutterSite(-200, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "south");
        AssembleCutter(cutter, player);
        var infeed = cutter.CellPos(CutterRig.InfeedNeighbours().First());
        var outfeed = cutter.CellPos(CutterRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        // a large blank the small master does not take, then two small ones
        source.Inventory[0].Itemstack = CutterItem(GearCut.LargeBlank);
        source.Inventory[1].Itemstack = CutterItem(GearCut.Blank, 2);
        source.MarkDirty(true);

        // not taken while the shaft stands
        await World.Ticks(30);
        Assert.False(cutter.BlankOn);
        await PowerCutter(cutter);
        await World.Until(() => cutter.BlankOn, 5000);
        Assert.Equal(1, source.Inventory[1].StackSize);
        Assert.Equal(GearCut.LargeBlank, source.Inventory[0].Itemstack?.Collectible.Code.ToString());
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        // the gear in the chest, the next blank on at once
        Assert.Contains(sink.Inventory, s => s.Itemstack?.Collectible.Code.ToString() == GearCut.Gear);
        Assert.True(cutter.BlankOn);
        Assert.True(source.Inventory[1].Empty);
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.Equal(2, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == GearCut.Gear).Sum(s => s.StackSize));
        Assert.DoesNotContain(GearCut.Gear, CutterItemsNear(pos).Keys);
        await World.Ticks(30);
        Assert.False(cutter.BlankOn);   // only the large blank is left
    }
}
