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
// BEGearCutter.Cut, as the rosser's scenarios feed through BERosser.Feed; the powered part runs on the
// virtual clock (tests/PackTests/README.md). Every scenario builds on a
// granite floor of its own 40 above spawn at x -140 to -200, z -140 to -200 (clear of the gear blanks
// at x -90 and gear reclamation at x -100), and they share one player, gearcutterhand.
public partial class SharedWorldScenarios
{
    private GearCutterSystem CutterMod => GearCutterSystem.Of(World.Api);
    private GearCutterRig CutterRig => CutterMod.Rig ?? throw new Xunit.Sdk.XunitException("the gear cutter's rig did not load");

    private async Task<BEGearCutter> PlaceCutter(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:gearcutter-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEGearCutter ?? throw new Xunit.Sdk.XunitException($"no gear cutter at {pos}");
        be.PlaceCells();
        return be;
    }

    private static string CutterInfo(BEMachine machine, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        machine.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>The block info as a player holding a wrench sees it.</summary>
    private string CutterWrenchInfo(BEMachine machine, IPlayer player)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        var held = slot.Itemstack;
        slot.Itemstack = CutterItem("game:wrench-iron");
        try { return CutterInfo(machine, player); }
        finally { slot.Itemstack = held; }
    }

    /// <summary>A machine's cells but the controller's (the shared machine cells), and whether each is the power cell.</summary>
    private static List<(BlockPos Pos, bool Power)> MachineCells(BEMachine machine) =>
        machine.CellBlocks().Select(c => (c.Pos, c.Block is BlockMachineCellPower)).ToList();

    /// <summary>The gear cutter's stage <paramref name="id"/>'s index in its build.</summary>
    private int CutterStage(string id) => MachinesSystem.Of(World.Api).Build(GearCutterSystem.MachineId)!.Stages.Select(s => s.Id).ToList().IndexOf(id);

    /// <summary>The spindle's and the index's bill, a material a click.</summary>
    private static readonly (string Code, int Count)[] CutterBill =
        [(GearCutterBuild.RodCode, 2), (GearCutterBuild.PlateCode, 2), (GearCutterBuild.MetalPartsCode, 1)];

    /// <summary>The clicks in order, as stacks: every stage up to the head (the spindle's and the
    /// index's materials one a click), then the kit; the master after.</summary>
    private static readonly (string Code, int Count)[] CutterOrder =
    [
        .. CutterBill, (GearCutterBuild.FeedScrewCode, 1), (GearCutterBuild.GearboxCode, 1), (GearCutterBuild.GearboxCode, 1),
        (GearCutterBuild.LiftCamCode, 1), .. CutterBill, (GearCutterBuild.ValveCode, 1), (GearCutterBuild.HeadAltCode, 1),
        (GearCutterBuild.KitCode, 1),
    ];

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and a ghost,
    /// the master last; then a full oil tank.</summary>
    private void AssembleCutter(BEGearCutter cutter, IPlayer player, string master = GearCutterBuild.MasterCode)
    {
        var ghost = MachineCells(cutter).First().Pos;
        int i = 0;
        foreach (var (code, n) in CutterOrder.Append((master, 1)))
            Assert.True(CutterClick(player, i++ % 2 == 0 ? cutter.Pos : ghost, CutterItem(code, n)) == null, $"{code} was not fitted");
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
        var face = Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(cutter.ShaftSpeed) >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>Radians that finish the gear on the arbor from where it is.</summary>
    private static double RestOfGear(BEGearCutter cutter) =>
        (cutter.Job.End - cutter.Job.Work) * 2 * Math.PI * cutter.TurnsPerTooth + 1e-6;

    // ---- Loading ----

    [AtlasScenario]
    public void Gear_cutter_loads_with_its_blocks_parts_and_recipes()
    {
        Assert.True(GearCutterSystem.Applies(World.Api));
        Assert.NotNull(CutterMod.Rig);
        // its frame is the shared machine block (placed as the mills are), its cells and power cell the shared
        // ones, its build the shared build's
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.Equal(Footprint.PlacedFacing(Side.North), Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:gearcutter-frame-{side}"))).Rig!.PlacedSide(Side.North));   // placed as the mills are
        Assert.True(W.GetBlock(new AssetLocation("seraphhorizons:gearcutter-ghost")) is null or { IsMissing: true } or { Id: 0 });
        Assert.Same(CutterRig, ((BlockMachine)W.GetBlock(new AssetLocation("seraphhorizons:gearcutter-frame-north"))).Rig);
        // every part and material the stages take exists here, the Jonas parts, the masters and the
        // metal parts (a block) included; the spindle's and the index's old items are gone
        var build = MachinesSystem.Of(World.Api).Build(GearCutterSystem.MachineId)!;
        Assert.Equal(GearCutterRequires.Stages, build.Stages.Select(s => s.Adds[0]));
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(W.GetItem(new AssetLocation(code)) != null || W.GetBlock(new AssetLocation(code)) is { Id: > 0 }, $"no {code}");
        Assert.Null(W.GetItem(new AssetLocation("seraphhorizons:gearcutterspindle")));
        Assert.Null(W.GetItem(new AssetLocation("seraphhorizons:gearcutterindex")));
        Assert.Equal(500, CutterItem(GearCutterBuild.KitCode).Collectible.GetMaxDurability(CutterItem(GearCutterBuild.KitCode)));
        Assert.Equal(500, Assert.Single(build.Wearing).Uses);
        Assert.Equal("Gear cutter frame", new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:gearcutter-frame-north"))).GetName());

        // the grid: the frame only (the spindle and the index are fed at the frame)
        Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString().StartsWith("seraphhorizons:gearcutter") == true);
        var frame = W.GridRecipes.Single(r => r.Output?.Code?.ToString() == "seraphhorizons:gearcutter-frame-north");
        // the end-game machine: 4 steel ingots in each of its two ingot slots (steel only), 32 nails and strips
        var ingots = frame.ResolvedIngredients!.Where(i => i?.Code?.Path == "ingot-*").ToList();
        Assert.Equal(2, ingots.Count);
        Assert.All(ingots, i => { Assert.Equal(new[] { "steel" }, i!.AllowedVariants); Assert.Equal(4, i.Quantity); });
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 32);
        // 3 stainless gears for the gearing the frame carries (the feed rectifier, the camshaft's worm wheel)
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "seraphhorizons:gear-stainless" && i.Quantity == 3);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the anvil: the feed screw, the lift cam and the kit, each from one steel ingot, none for the helve hammer
        foreach (var output in new[] { GearCutterBuild.FeedScrewCode, GearCutterBuild.LiftCamCode, GearCutterBuild.KitCode })
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

    // On all four facings: room for every cell, a shared cell stamped in each, the power cell's axle face
    // the native west turned to the facing, the output face turned with it; broken
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
            var cells = MachineCells(cutter);
            Assert.Equal(7, cells.Count);
            foreach (var (cell, power) in cells)
            {
                var be = Assert.IsType<BEMachineCell>(W.BlockAccessor.GetBlockEntity(cell));
                Assert.Equal(pos, be.Principal);
                Assert.Equal(power, W.BlockAccessor.GetBlock(cell) is BlockMachineCellPower);
                Assert.NotNull(W.BlockAccessor.GetBlock(cell).GetSelectionBoxes(W.BlockAccessor, cell));
            }
            var powerGhost = cells.Single(c => c.Power).Pos;
            var face = Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(powerGhost)).PowerFace;
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
        var ghost = MachineCells(cutter).Last().Pos;
        var assembly = cutter.Assembly!;
        Assert.Contains("Build: stage 1 of 10, spindle", CutterInfo(cutter, player));

        // a later stage's part is refused and stays in hand, from the frame or a ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterBuild.FeedScrewCode))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(GearCutterBuild.MasterCode))?.StackSize);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(GearCutterBuild.KitCode))?.StackSize);
        Assert.Equal(0, assembly.Build.Next);
        Assert.Empty(assembly.Returns());
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the spindle's materials: one rod of the two it takes, from a stack that holds more, is refused
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterBuild.RodCode))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.Count(0, 0));

        // the stages in order, from the frame and a ghost by turns: a part one at a time, a material
        // all the stage still needs of it, the rest of the stack left in the hand
        for (int i = 0; i < CutterOrder.Length; i++)
        {
            var (code, n) = CutterOrder[i];
            int next = assembly.Build.Next!.Value;
            var left = CutterClick(player, i % 2 == 0 ? pos : ghost, CutterItem(code, n + 1));
            Assert.Equal(1, left?.StackSize ?? 0);
            if (assembly.Build.Stages[next].Materials.Count == 1)
                Assert.True(assembly.Build.StageComplete(next), $"{code} did not go in as stage {next}");
            if (i == 0)
            {
                // part way in: not drawn, the info says what it still takes, the rods again are refused
                Assert.False(assembly.Build.StageComplete(0));
                Assert.False(cutter.ShowsPart("spindle"));
                Assert.Contains("Still takes: 2 ×", CutterInfo(cutter, player));
                Assert.Equal(2, CutterClick(player, pos, CutterItem(GearCutterBuild.RodCode, 2))?.StackSize);
                // and a save keeps the count
                var partial = new Vintagestory.API.Datastructures.TreeAttribute();
                cutter.ToTreeAttributes(partial);
                cutter.FromTreeAttributes(partial, W);
                assembly = cutter.Assembly!;
                Assert.Equal(2, assembly.Build.Count(0, 0));
                Assert.Equal(0, assembly.Build.Next);
            }
            if (i == 2)
                Assert.True(cutter.ShowsPart("spindle"));
        }
        Assert.True(assembly.Build.StageComplete(CutterStage("index")));
        // a third eccentric gearbox has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterBuild.GearboxCode))?.StackSize);
        Assert.False(cutter.Complete);
        Assert.Contains("Build: stage 10 of 10, master", CutterInfo(cutter, player));
        // no blank before the master
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);
        Assert.Null(CutterClick(player, ghost, CutterItem(GearCutterBuild.MasterCode)));
        Assert.True(cutter.Complete);
        Assert.Equal("Gear cutter", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Master: temporal gear", CutterInfo(cutter, player));
        Assert.Contains("Cutter kit: 100% left", CutterInfo(cutter, player));
        // unpowered, the shared state's order says so first
        Assert.Contains("No power: its shaft must turn at 0.05 or faster", CutterInfo(cutter, player));
        // more for a wrench: the kit's uses, the oil's drain, the facing and the axle's face
        Assert.Contains("Hold a wrench to see more", CutterInfo(cutter, player));
        var detail = CutterWrenchInfo(cutter, player);
        Assert.Contains("Cutter kit: 500 of 500 uses left", detail);
        Assert.Contains("Oil a job: 10", detail);
        Assert.Contains("its axle meets its", detail);
        Assert.DoesNotContain("Hold a wrench", detail);

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        cutter.ToTreeAttributes(tree);
        cutter.FromTreeAttributes(tree, W);
        Assert.True(cutter.Complete);
        Assert.Equal(GearCutterBuild.HeadAltCode, cutter.Assembly!.Build.CodeIn(CutterStage("head"), 0));
        Assert.Equal(10, cutter.Assembly.Returns().Count);
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
            int next = cutter.Assembly!.Build.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(cutter.Assembly.Build.StageComplete(next), $"the shortcut did not fit stage {next}");
            stages++;
        }
        Assert.Equal(10, stages);
        Assert.Equal(1, cutter.Master);
        Assert.Equal(500, cutter.KitLeft);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.Null(cutter.Kit);
    }

    // ---- Cutting ----

    // Under power a small blank turns into a stainless gear at the output face, the large master takes a
    // large blank to a large gear, each master refuses the other size, and the cut advances with the
    // shaft only.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Gear_cutter_cuts_small_and_large_gears_under_power()
    {
        var pos = await CutterSite(-140, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "east");
        AssembleCutter(cutter, player);
        using var clock = VirtualClock.Start(World);

        // the wrong size is refused, the right one goes on
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.LargeBlank))?.StackSize);
        Assert.False(cutter.BlankOn);
        Assert.Equal(2, CutterClick(player, pos, CutterItem(GearCut.Blank, 3))?.StackSize);
        Assert.True(cutter.BlankOn);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);   // one at a time

        // unpowered, nothing moves; powered, the shaft cuts
        await World.Ticks(10);
        Assert.Equal(0, cutter.Job.Work);
        Assert.Contains("No power", CutterInfo(cutter, player));
        Assert.Contains("On the arbor: 0 of 12 teeth cut", CutterInfo(cutter, player));
        var rotor = await PowerCutter(cutter);
        await World.Until(() => cutter.Job.Work > 0.02, 30000);
        Assert.True(cutter.Running);
        await World.Until(() => cutter.State == MachineState.Working, 5000);
        Assert.Contains("Cutting (", CutterInfo(cutter, player));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.False(cutter.BlankOn);
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(GearCut.Gear));
        // it dropped beyond the output face, not inside the machine
        var gear = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == GearCut.Gear);
        var local = Footprint.ToLocal(new Float3((float)(gear.Pos.X - pos.X), (float)(gear.Pos.Y - pos.Y), (float)(gear.Pos.Z - pos.Z)), cutter.Side);
        Assert.True(local.Z > 2 - 0.01f, $"the gear is at native {local}");
        Assert.Equal(490, cutter.KitLeft);
        Assert.Equal(990, cutter.Oiling!.Tank.Points, 6);
        CutterKillItems(pos);

        // changed over to the large master: kit out, master out, then the master (the last stage) in before the kit
        CutterClick(player, pos, null, ctrl: true);
        CutterClick(player, pos, null, ctrl: true);
        Assert.Equal(0, cutter.Master);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCutterBuild.KitCode))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterBuild.LargeMasterCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterBuild.KitCode)));
        Assert.Equal(2, cutter.Master);
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
        Assert.Equal(500 - 17, cutter.KitLeft);
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
        var mp = W.BlockAccessor.GetBlockEntity(cutter.CellPos(CutterRig.PowerCell))!.GetBehavior<BEBehaviorMachineMP>()!;
        float oiledLoad = mp.GetResistance();
        Assert.Equal(CutterMod.Config.Resistance, oiledLoad);

        int GearWear(double fill)
        {
            FillCutterOil(cutter, fill);
            int before = cutter.KitLeft;
            Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
            Assert.True(cutter.Cut(RestOfGear(cutter)));
            return before - cutter.KitLeft;
        }
        Assert.Equal(10, GearWear(1));
        Assert.Equal(20, GearWear(0.5));
        Assert.Equal(100, GearWear(0.1));
        Assert.Equal(370, cutter.KitLeft);
        FillCutterOil(cutter, 0.1);   // the gear drained the tank to 0.09
        Assert.Contains("Cutter wear: 10× per gear", CutterWrenchInfo(cutter, player));
        Assert.Contains("Cutter kit: 370 of 500 uses left", CutterWrenchInfo(cutter, player));
        Assert.Contains("Cutter kit: 74% left", CutterInfo(cutter, player));
        Assert.Contains("About 4 more gears from the cutter kit", CutterInfo(cutter, player));

        // dry: the same load, and the next gear breaks the kit
        FillCutterOil(cutter, 0);
        Assert.Equal(oiledLoad, mp.GetResistance());
        Assert.Contains("No oil: the next gear breaks the cutter kit", CutterInfo(cutter, player));
        Assert.Contains("Dry: it wears its cutter fast", CutterInfo(cutter, player));
        Assert.Contains($"load {OilText.Load(oiledLoad)} kN", CutterWrenchInfo(cutter, player));
        Assert.DoesNotContain("turns harder", CutterInfo(cutter, player));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.Null(cutter.Kit);
        Assert.False(cutter.Complete);
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(GearCut.Gear));   // the last gear still came out
        CutterKillItems(pos);
        // no blank goes on without a kit
        Assert.Equal(1, CutterClick(player, pos, CutterItem(GearCut.Blank))?.StackSize);

        // a refit resumes a cut stopped half way by taking the kit out
        FillCutterOil(cutter, 1);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterBuild.KitCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 5);
        Assert.Equal(5, cutter.Job.Work, 3);
        CutterClick(player, pos, null, ctrl: true);   // the kit comes out first
        Assert.True(cutter.BlankOn);
        var kit = Assert.Single(player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!
            .Concat(player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)!),
            s => s.Itemstack?.Collectible.Code.ToString() == GearCutterBuild.KitCode).Itemstack!;
        Assert.Equal(500, kit.Collectible.GetRemainingDurability(kit));
        Assert.False(cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 20));
        Assert.Equal(5, cutter.Job.Work, 3);
        Assert.Null(CutterClick(player, pos, kit));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        Assert.Equal(490, cutter.KitLeft);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Gear_cutter_gives_back_the_kit_then_the_blank_then_the_master_and_everything_when_broken()
    {
        var pos = await CutterSite(-180, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "west");
        AssembleCutter(cutter, player, GearCutterBuild.LargeMasterCode);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.LargeBlank)));
        cutter.Cut(2 * Math.PI * cutter.TurnsPerTooth * 3);
        cutter.Assembly!.Wearing.Wear(GearCutterBuild.KitPart, 123);

        int Held(string code) => player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
            .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);
        var ghost = MachineCells(cutter).First().Pos;
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCutterBuild.KitCode));
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCut.LargeBlank));
        Assert.False(cutter.BlankOn);
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, Held(GearCutterBuild.LargeMasterCode));
        Assert.Equal(0, cutter.Master);
        // the rest only come back by breaking
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(cutter.Assembly!.Build.StageComplete(CutterStage("head")));

        // refit, wear and load, then break through a ghost: every part, the kit with its wear, and the blank
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterBuild.MasterCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCutterBuild.KitCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        cutter.Assembly!.Wearing.Wear(GearCutterBuild.KitPart, 77);
        // a save keeps it all, the blank and the kit's wear too
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        cutter.ToTreeAttributes(tree);
        cutter.FromTreeAttributes(tree, W);
        Assert.True(cutter.Complete);
        Assert.True(cutter.ShowsPart("spindle"));
        Assert.True(cutter.ShowsPart("index"));
        Assert.True(cutter.BlankOn);
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:gearcutter-frame-north"));
        foreach (var code in new[] { GearCutterBuild.FeedScrewCode, GearCutterBuild.LiftCamCode,
                                     GearCutterBuild.ValveCode, GearCutterBuild.HeadAltCode, GearCutterBuild.KitCode, GearCutterBuild.MasterCode, GearCut.Blank })
            Assert.True(drops.GetValueOrDefault(code) == 1, $"{code}: {drops.GetValueOrDefault(code)}");
        Assert.Equal(2, drops.GetValueOrDefault(GearCutterBuild.GearboxCode));
        // the spindle's and the index's bill, twice over
        foreach (var (code, n) in CutterBill)
            Assert.True(drops.GetValueOrDefault(code) == 2 * n, $"{code}: {drops.GetValueOrDefault(code)}");
        var kit = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == GearCutterBuild.KitCode).Itemstack;
        Assert.Equal(423, kit.Collectible.GetRemainingDurability(kit));
        CutterKillItems(pos);
    }

    // ---- Chests ----

    // A chest against the infeed end and one against the output face are left alone: the running
    // cutter takes no blank from the first, and the gear of a blank put on by hand drops at the output
    // face rather than going into the second.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Gear_cutter_takes_nothing_from_a_chest_and_puts_nothing_in_one()
    {
        var pos = await CutterSite(-200, -160);
        var player = await CutterPlayer();
        var cutter = await PlaceCutter(pos, "south");
        AssembleCutter(cutter, player);
        var infeed = cutter.CellPos(new Int3(0, 0, -1));
        var outfeed = cutter.CellPos(CutterRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        source.Inventory[0].Itemstack = CutterItem(GearCut.Blank, 2);
        source.MarkDirty(true);

        using var clock = VirtualClock.Start(World);
        await PowerCutter(cutter);
        await World.Ticks(60);
        Assert.False(cutter.BlankOn);
        Assert.Equal(2, source.Inventory[0].StackSize);
        Assert.Null(CutterClick(player, pos, CutterItem(GearCut.Blank)));
        Assert.True(cutter.Cut(RestOfGear(cutter)));
        await World.Ticks(5);
        Assert.False(cutter.BlankOn);
        Assert.All(sink.Inventory, s => Assert.True(s.Empty));
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(GearCut.Gear));
        Assert.Equal(2, source.Inventory[0].StackSize);
        CutterKillItems(pos);
    }
}
