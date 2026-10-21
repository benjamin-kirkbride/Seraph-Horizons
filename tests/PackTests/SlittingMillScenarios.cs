using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SlittingMill;
using SeraphHorizons.Mod.SlittingMill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, SlittingMill (mods-src/seraphhorizons/SlittingMill/): the slitting mill in the plain world,
// driven by a real mechanical power network (a vanilla creative rotor against its power face). A plate is five
// axle turns, so each scenario that runs it under power waits for the shaft's own turning to move W, then finishes
// the plate through BESlittingMill.Slit, as the cylinder borer's scenarios finish a bore through Bore; the powered
// part runs on the virtual clock (tests/PackTests/README.md). Every scenario builds on a granite floor of its own
// 40 above spawn at x 1000 to 1060, z -1000 to -1040 (clear of every other site), and they share the gear cutter's
// player, gearcutterhand (the server takes 16 players at most, and the other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private SlittingMillSystem MillMod => SlittingMillSystem.Of(World.Api);
    private SlittingMillRig MillRig => MillMod.Rig ?? throw new Xunit.Sdk.XunitException("the slitting mill's rig did not load");

    private async Task<BESlittingMill> PlaceMill(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:slittingmill-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BESlittingMill ?? throw new Xunit.Sdk.XunitException($"no slitting mill at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>The stages' materials as the stacks a player puts in, a material a click, the knife after the
    /// shear's bracket and bed knife: the cutters and rods, the Jonas gears and metal parts, the gearbox and rods,
    /// the bracket and plate, the push bar's rods and plank (rods, plates and the bracket of mixed metals).</summary>
    private static readonly (string Code, int Count)[] MillMaterials =
    [
        (SlittingMillBuild.CutterCode, 10), ("game:rod-iron", 2),
        ("game:jonasframes-gears01", 1), ("game:metal-parts", 1),
        ("game:jonasframes-gearbox01", 1), ("game:rod-steel", 2),
        ("game:bracket-heavy-steel", 1), ("game:metalplate-meteoriciron", 1),
    ];

    private static readonly (string Code, int Count)[] MillPushBar = [("game:rod-meteoriciron", 2), ("game:plank-oak", 1)];

    /// <summary>A cleaver of <paramref name="metal"/> with <paramref name="durability"/> left (full when null).</summary>
    private ItemStack MillKnife(string metal = "steel", int? durability = null)
    {
        var knife = CutterItem("game:cleaver-" + metal);
        if (durability is { } left)
            knife.Attributes.SetInt("durability", left);
        return knife;
    }

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and a cell, the knife closing
    /// the shear; then a full oil tank.</summary>
    private void AssembleMill(BESlittingMill mill, IPlayer player, ItemStack? knife = null)
    {
        var cell = MachineTest.Cells(mill).First().Pos;
        int i = 0;
        var parts = MillMaterials.Select(m => CutterItem(m.Code, m.Count)).Append(knife ?? MillKnife())
            .Concat(MillPushBar.Select(m => CutterItem(m.Code, m.Count)));
        foreach (var part in parts)
            Assert.True(CutterClick(player, i++ % 2 == 0 ? mill.Pos : cell, part) == null, $"{part.StackSize} {part.Collectible.Code} did not go in");
        Assert.True(mill.Complete);
        MachineTest.FillOil(mill, 1);
    }

    /// <summary>Radians that finish the plate on the table from where it is.</summary>
    private static double RestOfPlate(BESlittingMill mill) =>
        (mill.Job.End - mill.Job.Work) * 2 * Math.PI * mill.TurnsPerStroke + 1e-6;

    // ---- Loading ----

    [AtlasScenario]
    public void Slitting_mill_loads_with_its_blocks_cutters_and_recipes()
    {
        Assert.True(SlittingMillSystem.Applies(World.Api));
        Assert.NotNull(MillMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.Equal(Side.North, Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:slittingmill-frame-{side}"))).Rig!.PlacedSide(Side.North));
        Assert.Same(MillRig, ((BlockMachine)W.GetBlock(new AssetLocation("seraphhorizons:slittingmill-frame-north"))).Rig);
        var build = MachinesSystem.Of(World.Api).Build(SlittingMillSystem.MachineId)!;
        Assert.Equal(SlittingMillRequires.Stages, build.Stages.Select(s => s.Id));
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(W.GetItem(new AssetLocation(code)) != null || W.GetBlock(new AssetLocation(code)) is { Id: > 0 }, $"no {code}");   // metal parts are a block
        // the knife: the game's cleavers, each its own durability its uses
        Assert.Equal(SlittingMillBuild.KnifeCodes, Assert.Single(build.Wearing).Codes);
        foreach (var (metal, durability) in new[] { ("steel", 2000), ("meteoriciron", 950), ("iron", 800) })
        {
            var cleaver = MillKnife(metal);
            Assert.Equal(durability, cleaver.Collectible.GetMaxDurability(cleaver));   // the knife's uses (BESlittingMill.BuildSpec)
        }
        // every plate the mill takes and the nails and strips it makes exist in the game
        foreach (var metal in Slitting.Metals)
        {
            Assert.True(W.GetItem(new AssetLocation(Slitting.PlateFor(metal))) is { Id: > 0, IsMissing: false }, $"no plate of {metal}");
            Assert.True(W.GetItem(new AssetLocation(Slitting.OutputFor(metal))) is { Id: > 0, IsMissing: false }, $"no nails of {metal}");
        }
        Assert.Equal("Slitting mill frame", new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:slittingmill-frame-north"))).GetName());
        Assert.Equal("Steel slitter cutter", CutterItem(SlittingMillBuild.CutterCode).GetName());

        // the grid: the frame, of oak (two logs, two planks) and iron (two plates, two rods in one slot, 12 nails and strips)
        // and a hammer, no gearing
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:slittingmill-frame-north" && r.Enabled);
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("meteoriciron")));
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "rod-*" && i.Quantity == 2);
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 12);
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.ToString() == "game:log-placed-oak-ud"));
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.ToString() == "game:plank-oak"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        Assert.DoesNotContain(frame.ResolvedIngredients!, i => i?.Code?.Domain == "seraphhorizons");
        // the anvil: two cutters from one steel ingot, steel only, by hand
        var smith = Assert.Single(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == SlittingMillBuild.CutterCode);
        Assert.Equal(2, smith.Output!.ResolvedItemstack!.StackSize);
        Assert.True(smith.Ingredient!.SatisfiesAsIngredient(CutterItem("game:ingot-steel")));
        Assert.False(smith.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-iron")));
        Assert.InRange(smith.Voxels.Cast<bool>().Count(v => v), 1, 42);
        // the anvil's nails are left as they are: 4 or 8 from an ingot
        Assert.Contains(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == "game:metalnailsandstrips-iron"
                                                             && r.Output.ResolvedItemstack.StackSize == 8);
        // the settings and the rig's pace agree
        Assert.Equal(MillRig.TurnsPerStroke, MillMod.Config.TurnsPerStroke, 4);
        Assert.Equal(20, MillMod.Config.NailsPerPlate);
        Assert.Equal(5f, SeraphHorizons.Mod.MachineOil.MachineOilSystem.Of(World.Api).Config.SlittingMill.DrainPerJob);
        Assert.Contains("slitting mill", Lang.Get("seraphhorizons:machineoil-text"));
    }

    // ---- Placing ----

    // On all four facings: room for every cell, a shared cell stamped in each, the mill running away along the
    // facing from the feed table, the power cell a block up at the near end with its axle face native east turned
    // with the facing; broken through a cell, every cell is cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Slitting_mill_places_on_all_four_facings_with_its_cells()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(1000 + 12 * i++, -1000);
            var mill = await PlaceMill(pos, side.Code());
            Assert.Equal(side, mill.Side);
            var cells = MachineTest.Cells(mill);
            Assert.Equal(7, cells.Count);
            foreach (var cell in cells.Select(c => c.Pos))
            {
                Assert.Equal(pos, Assert.IsType<BEMachineCell>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
                var block = W.BlockAccessor.GetBlock(cell);
                Assert.NotNull(block.GetSelectionBoxes(W.BlockAccessor, cell));
            }
            var powerCell = Assert.Single(cells, c => c.Power).Pos;
            var w = Footprint.ToWorld(MillRig.PowerCell, side);
            Assert.Equal(pos.AddCopy(w.X, w.Y, w.Z), powerCell);
            var face = Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(powerCell)).PowerFace;
            Assert.Equal(Footprint.ToWorld(Side.East, side).Code(), face.Code);
            Assert.DoesNotContain(cells, c => c.Pos.Equals(powerCell.AddCopy(face)));
            // the far end is a block along the facing
            var far = Footprint.ToWorld(new Int3(0, 0, 1), side);
            var n = side.Normal();
            Assert.Equal(new Int3(n.X, 0, n.Z), far);
            Assert.Equal("Slitting mill frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));

            W.BlockAccessor.GetBlock(cells[3].Pos).OnBlockBroken(W, cells[3].Pos, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault("seraphhorizons:slittingmill-frame-north"));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Slitting_mill_is_built_in_order_and_refuses_parts_out_of_order()
    {
        var pos = await CutterSite(1000, -1020);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos, "east");
        var cell = MachineTest.Cells(mill).Last().Pos;
        var assembly = mill.Assembly!;
        Assert.Contains("Build: stage 1 of 5, slitter rolls", MachineTest.Info(mill, player));

        // a later stage's material is refused and stays in hand, from the frame or a cell
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:jonasframes-gearbox01"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, cell, MillKnife())?.StackSize);
        Assert.Equal(0, assembly.Build.Next);
        // no plate before the mill is built
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-copper"))?.StackSize);
        Assert.False(mill.PlateOn);
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the rolls: nine cutters are too few, ten of sixteen go in
        Assert.Equal(9, CutterClick(player, pos, CutterItem(SlittingMillBuild.CutterCode, 9))?.StackSize);
        Assert.Equal(6, CutterClick(player, pos, CutterItem(SlittingMillBuild.CutterCode, 16))?.StackSize);
        Assert.False(mill.ShowsPart("rolls"));
        Assert.Null(CutterClick(player, cell, CutterItem("game:rod-iron", 2)));
        Assert.True(mill.ShowsPart("rolls"));
        Assert.Contains("Build: stage 2 of 5, pinions and great wheel", MachineTest.Info(mill, player));
        // a stage partly in keeps what it holds through a save
        Assert.Null(CutterClick(player, pos, CutterItem("game:jonasframes-gears02")));
        var partly = new Vintagestory.API.Datastructures.TreeAttribute();
        mill.ToTreeAttributes(partly);
        mill.FromTreeAttributes(partly, W);
        assembly = mill.Assembly!;
        Assert.Equal(1, assembly.Build.Count(1, 0));
        Assert.Null(CutterClick(player, pos, CutterItem("game:metal-parts")));
        Assert.True(mill.ShowsPart("pinions"));
        foreach (var (code, count) in MillMaterials.Skip(4))
            Assert.Null(CutterClick(player, cell, CutterItem(code, count)));
        Assert.False(mill.Built);
        Assert.Contains("Build: stage 4 of 5, shear", MachineTest.Info(mill, player));
        // a spent cleaver is refused; a worn one goes in with its wear
        Assert.Equal(1, CutterClick(player, pos, MillKnife("steel", 0))?.StackSize ?? 0);
        Assert.Null(CutterClick(player, pos, MillKnife("iron", 600)));
        Assert.True(mill.ShowsPart("knife"));
        Assert.Equal(600, mill.KnifeLeft);
        Assert.Contains("Build: stage 5 of 5, push bar", MachineTest.Info(mill, player));
        foreach (var (code, count) in MillPushBar)
            Assert.Null(CutterClick(player, pos, CutterItem(code, count)));
        Assert.True(mill.Complete);
        Assert.Equal("Slitting mill", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.DoesNotContain("Build:", MachineTest.Info(mill, player));
        Assert.Contains("Knife: 75% left", MachineTest.Info(mill, player));
        Assert.Contains("No power", MachineTest.Info(mill, player));
        var detail = MachineTest.WrenchInfo(mill, player);
        Assert.Contains("Knife: 600 of 800 uses left", detail);
        Assert.Contains("Oil a job: 5", detail);

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        mill.ToTreeAttributes(tree);
        mill.FromTreeAttributes(tree, W);
        Assert.True(mill.Complete);
        Assert.Equal(600, mill.KnifeLeft);
        Assert.Equal(MillMaterials.Concat(MillPushBar).Sum(m => m.Count) + 1, mill.Assembly!.Returns().Sum(d => d.Count));
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Slitting_mill_creative_shortcut_fits_the_next_stage_free()
    {
        var pos = await CutterSite(1012, -1020);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos);
        int stages = 0;
        while (!mill.Complete && stages < 10)
        {
            int next = mill.Assembly!.Build.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(mill.Assembly.Build.StageComplete(next), $"the shortcut did not fit stage {next}");
            stages++;
        }
        Assert.Equal(5, stages);
        Assert.Equal(2000, mill.KnifeLeft);
        Assert.Equal("game:cleaver-steel", mill.Knife!.Value.Code);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.Null(mill.Knife);
    }

    // ---- Slitting ----

    // Under power a copper plate is slit into 20 copper nails and strips, beyond the output face; W moves by the
    // shaft's own turning, the knife wears its copper figure, the tank drains 5 points, and the load is copper's;
    // nails, a lead plate and an ingot are never taken on.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Slitting_mill_slits_a_copper_plate_into_twenty_nails_and_strips_under_power()
    {
        var pos = await CutterSite(1024, -1020);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos, "south");
        AssembleMill(mill, player);
        using var clock = VirtualClock.Start(World);
        var mp = W.BlockAccessor.GetBlockEntity(mill.CellPos(MillRig.PowerCell))!.GetBehavior<BEBehaviorMachineMP>()!;
        Assert.Equal(MillMod.Config.LightestResistance(), mp.GetResistance(), 4);   // empty: the lightest metal's

        foreach (var code in new[] { "game:metalnailsandstrips-copper", "game:metalplate-lead", "game:ingot-copper" })
        {
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(_cutterHandled, $"{code} was the mill's");
        }
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-copper", 2))?.StackSize);
        Assert.True(mill.PlateOn);
        Assert.Equal("copper", mill.Metal);
        Assert.Equal(MillMod.Config.Resistance("copper"), mp.GetResistance(), 4);
        // one at a time
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-copper"))?.StackSize);

        await World.Ticks(10);
        Assert.Equal(0, mill.Job.Work);
        Assert.Contains("No power", MachineTest.Info(mill, player));
        Assert.Contains("On the table: a copper plate, 0 of 2 nails cut from every rod", MachineTest.Info(mill, player));
        var rotor = await PowerMachine(mill);
        await World.Until(() => mill.Job.Work > 0.01, 60000);
        Assert.True(mill.Running);
        await World.Until(() => mill.State == MachineState.Working, 5000);
        Assert.Contains("Slitting (", MachineTest.Info(mill, player));

        Assert.True(mill.Slit(RestOfPlate(mill)));
        Assert.False(mill.PlateOn);
        await World.Ticks(5);
        Assert.Equal(20, CutterItemsNear(pos).GetValueOrDefault("game:metalnailsandstrips-copper"));
        Assert.Equal(0, CutterItemsNear(pos).GetValueOrDefault("game:metalplate-copper"));
        // they dropped beyond the output face (native south), not inside the mill
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().First(e => e.Itemstack.Collectible.Code.ToString() == "game:metalnailsandstrips-copper");
        var local = Footprint.ToLocal(new Float3((float)(dropped.Pos.X - pos.X), (float)(dropped.Pos.Y - pos.Y), (float)(dropped.Pos.Z - pos.Z)), mill.Side);
        Assert.True(local.Z > 1.99f, $"the nails are at native {local}");
        Assert.Equal(2000 - MillMod.Config.KnifeWear("copper"), mill.KnifeLeft);
        Assert.Equal(995, mill.Oiling!.Tank.Points, 6);
        W.BlockAccessor.SetBlock(0, rotor);
        CutterKillItems(pos);
    }

    // Steel loads the shaft the most and wears the knife 40 a plate; a knife with less left breaks on its plate (the
    // plate still comes out) and the mill stops until another goes in; dry, the load is three times its own.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Slitting_mill_slits_steel_harder_and_a_worn_knife_breaks()
    {
        var pos = await CutterSite(1036, -1020);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos, "west");
        AssembleMill(mill, player, MillKnife("steel", 50));
        var mp = W.BlockAccessor.GetBlockEntity(mill.CellPos(MillRig.PowerCell))!.GetBehavior<BEBehaviorMachineMP>()!;

        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-steel")));
        Assert.Equal(MillMod.Config.Resistance("steel"), mp.GetResistance(), 4);
        Assert.True(MillMod.Config.Resistance("steel") > MillMod.Config.Resistance("copper"));
        Assert.Contains("About 2 more steel plates from the knife", MachineTest.Info(mill, player));
        Assert.Contains("the knife loses 40 a plate of this metal", MachineTest.WrenchInfo(mill, player));
        // dry: three times the load
        MachineTest.FillOil(mill, 0);
        Assert.Equal(MillMod.Config.Resistance("steel") * 3, mp.GetResistance(), 4);
        MachineTest.FillOil(mill, 1);
        Assert.False(mill.Slit(2 * Math.PI * 2.5));
        Assert.Equal(2.5, mill.Job.Work, 3);
        Assert.True(mill.Slit(RestOfPlate(mill)));
        Assert.Equal(10, mill.KnifeLeft);
        await World.Ticks(5);
        Assert.Equal(20, CutterItemsNear(pos).GetValueOrDefault("game:metalnailsandstrips-steel"));

        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-iron")));
        Assert.True(mill.Slit(RestOfPlate(mill)));
        Assert.Null(mill.Knife);
        Assert.False(mill.Complete);
        await World.Ticks(5);
        Assert.Equal(20, CutterItemsNear(pos).GetValueOrDefault("game:metalnailsandstrips-iron"));   // the last plate still came out
        Assert.Contains("Stopped: no knife", MachineTest.Info(mill, player));
        // no plate goes on without a knife
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-iron"))?.StackSize);
        Assert.Null(CutterClick(player, pos, MillKnife("meteoriciron")));
        Assert.True(mill.Complete);
        Assert.Equal(950, mill.KnifeLeft);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Slitting_mill_gives_back_the_knife_the_plate_and_everything_when_broken()
    {
        var pos = await CutterSite(1048, -1020);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos, "north");
        AssembleMill(mill, player, MillKnife("steel", 1500));
        var cell = MachineTest.Cells(mill).First().Pos;
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-brass")));
        Assert.False(mill.Slit(2 * Math.PI * 1.5));
        // Ctrl: the knife first, with its durability, unworn by it; then the plate, its slitting lost
        CutterClick(player, cell, null, ctrl: true);
        var knife = MachineTest.HeldStack(player, "game:cleaver-steel")!;
        Assert.Equal(1500, knife.Collectible.GetRemainingDurability(knife));
        Assert.True(mill.PlateOn);
        Assert.False(mill.ShowsPart("knife"));
        Assert.True(mill.ShowsPart("shear"));
        CutterClick(player, cell, null, ctrl: true);
        Assert.False(mill.PlateOn);
        Assert.NotNull(MachineTest.HeldStack(player, "game:metalplate-brass"));

        // refit and load; broken mid-plate, every part, the knife with its wear, and the plate come back
        Assert.Null(CutterClick(player, pos, MillKnife("steel", 1500)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-gold")));
        Assert.False(mill.Slit(2 * Math.PI * 3.2));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(cell).OnBlockBroken(W, cell, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        var want = MillMaterials.Concat(MillPushBar).GroupBy(m => m.Code).ToDictionary(g => g.Key, g => g.Sum(m => m.Count));
        foreach (var code in new[] { "game:cleaver-steel", "game:metalplate-gold", "seraphhorizons:slittingmill-frame-north" })
            want[code] = 1;
        foreach (var (code, count) in want)
            Assert.True(drops.GetValueOrDefault(code) == count, $"{code}: {drops.GetValueOrDefault(code)}, want {count}");
        Assert.Equal(0, drops.GetValueOrDefault("game:metalnailsandstrips-gold"));
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == "game:cleaver-steel").Itemstack;
        Assert.Equal(1500, dropped.Collectible.GetRemainingDurability(dropped));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Slitting_mill_takes_plates_from_a_chest_and_puts_nails_in_one()
    {
        var pos = await CutterSite(1000, -1040);
        var player = await CutterPlayer();
        var mill = await PlaceMill(pos, "north");
        AssembleMill(mill, player);
        var infeed = mill.CellPos(MillRig.InfeedNeighbours().First(p => p.Y == 0));
        var outfeed = mill.CellPos(MillRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        // nails and a lead plate it never takes, then an iron and a silver plate
        source.Inventory[0].Itemstack = CutterItem("game:metalnailsandstrips-iron", 5);
        source.Inventory[1].Itemstack = CutterItem("game:metalplate-lead");
        source.Inventory[2].Itemstack = CutterItem("game:metalplate-iron");
        source.Inventory[3].Itemstack = CutterItem("game:metalplate-silver");
        source.MarkDirty(true);

        using var clock = VirtualClock.Start(World);
        // not taken while the shaft stands
        await World.Ticks(30);
        Assert.False(mill.PlateOn);
        await PowerMachine(mill);
        await World.Until(() => mill.PlateOn, 5000);
        Assert.Equal("iron", mill.Metal);
        Assert.True(source.Inventory[2].Empty);
        Assert.True(mill.Slit(RestOfPlate(mill)));
        Assert.Equal(20, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == "game:metalnailsandstrips-iron").Sum(s => s.StackSize));
        // the next plate goes on once the clutch has eased out
        Assert.False(mill.PlateOn);
        await World.Until(() => mill.PlateOn, 5000);
        Assert.Equal("silver", mill.Metal);
        Assert.True(mill.Slit(RestOfPlate(mill)));
        Assert.Equal(20, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == "game:metalnailsandstrips-silver").Sum(s => s.StackSize));
        Assert.DoesNotContain("game:metalnailsandstrips-iron", CutterItemsNear(pos).Keys);
        await World.Ticks(40);
        Assert.False(mill.PlateOn);   // only the nails and the lead plate are left
        Assert.Equal("game:metalplate-lead", source.Inventory[1].Itemstack?.Collectible.Code.ToString());
    }
}
