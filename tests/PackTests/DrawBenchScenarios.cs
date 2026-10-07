using Atlas.XUnit;
using SeraphHorizons.Mod.DrawBench;
using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, DrawBench (mods-src/seraphhorizons/DrawBench/): the draw bench in the plain world,
// driven by a real mechanical power network (a vanilla creative rotor against its power face). A
// hollow section (the game's chute section) is four pipe sections of about 8 axle turns each (lead;
// copper twice that), so each scenario that runs it under power waits for the first pipe section to
// come off the shaft's own turning, then finishes the hollow through BEDrawBench.Draw, as the gear cutter's scenarios finish a gear through Cut. Every
// scenario builds on a granite floor of its own 40 above spawn at x -240 to -300, z -240 to -260
// (clear of the gear cutter's at x/z -140 to -200), and they share the gear cutter's player,
// gearcutterhand (the server takes 16 players at most, and the other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private DrawBenchSystem BenchMod => DrawBenchSystem.Of(World.Api);
    private DrawBenchRig BenchRig => BenchMod.Rig ?? throw new Xunit.Sdk.XunitException("the draw bench's rig did not load");

    // What it draws (Drawing.LeadHollow, CopperHollow: the game's chute section, whose lead state
    // UnifiedPipes' patch adds) and what comes off: the pack's pipe section.
    private const string LeadSection = "seraphhorizons:pipesection-lead";
    private const string CopperSection = "seraphhorizons:pipesection-copper";

    private async Task<BEDrawBench> PlaceBench(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:drawbench-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEDrawBench ?? throw new Xunit.Sdk.XunitException($"no draw bench at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>The kept parts in order, as codes: a gearbox, an iron chain, a steel bracket, a meteoric iron rod.</summary>
    private static readonly string[] BenchOrder =
        [DrawBenchParts.GearboxCode, "game:metalchain-iron", "game:bracket-heavy-steel", "game:rod-meteoriciron"];

    /// <summary>A die of <paramref name="code"/> with <paramref name="durability"/> left (full when null).</summary>
    private ItemStack BenchDie(string code, int? durability = null)
    {
        var die = CutterItem(code);
        if (durability is { } left)
            die.Attributes.SetInt("durability", left);
        return die;
    }

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and a ghost,
    /// the die last; then a full oil tank.</summary>
    private void AssembleBench(BEDrawBench bench, IPlayer player, ItemStack? die = null)
    {
        var ghost = bench.GhostCells().First().Pos;
        int i = 0;
        foreach (var part in BenchOrder.Select(c => CutterItem(c)).Append(die ?? BenchDie(DrawBenchParts.DieIronCode)))
            Assert.True(CutterClick(player, i++ % 2 == 0 ? bench.Pos : ghost, part) == null, $"{part.Collectible.Code} was not fitted");
        Assert.True(bench.Complete);
        FillBenchOil(bench, 1);
    }

    private static void FillBenchOil(BEDrawBench bench, double fill)
    {
        Assert.NotNull(bench.Oiling);
        var tank = bench.Oiling.Tank;
        bench.Oiling.Tank = tank with { Points = tank.Capacity * fill };
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns fast.</summary>
    private async Task<BlockPos> PowerBench(BEDrawBench bench)
    {
        var ghost = bench.CellPos(BenchRig.PowerCell);
        var face = Assert.IsType<BlockDrawBenchGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => bench.ShaftSpeed >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>Radians that bring the hollow on the bench to W = <paramref name="work"/> from where it is.</summary>
    private static double BenchRadiansTo(BEDrawBench bench, double work) =>
        (work - bench.Job.Work) * 2 * Math.PI * bench.TurnsPerSection(bench.Job.Class) + 1e-6;

    private static string BenchInfo(BEDrawBench bench, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        bench.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private static int BenchHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    private static ItemStack? BenchHeldStack(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).FirstOrDefault(s => s.Itemstack?.Collectible.Code.ToString() == code)?.Itemstack;

    // ---- Loading ----

    [AtlasScenario]
    public void Draw_bench_loads_with_its_blocks_dies_and_recipes()
    {
        Assert.True(DrawBenchSystem.Applies(World.Api));
        Assert.NotNull(BenchMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
        {
            Assert.IsType<BlockDrawBench>(W.GetBlock(new AssetLocation($"seraphhorizons:drawbench-frame-{side}")));
            Assert.IsType<BlockDrawBenchGhostPower>(W.GetBlock(new AssetLocation($"seraphhorizons:drawbench-ghostpower-{side}")));
        }
        Assert.IsType<BlockDrawBenchGhost>(W.GetBlock(new AssetLocation("seraphhorizons:drawbench-ghost")));
        // every part the stages take exists here, the Jonas gearbox and the dies included
        foreach (var stage in DrawBenchRequires.Stages)
            foreach (var code in DrawBenchParts.CodesFor(stage))
                Assert.True(W.GetItem(new AssetLocation(code)) != null, $"no {code} for {stage}");
        // what it draws, the game's chute section (copper the game's own and lead the state
        // UnifiedPipes' patch adds), and what comes off: the pack's pipe section
        foreach (var hollow in new[] { Drawing.LeadHollow, Drawing.CopperHollow })
            Assert.True(W.GetItem(new AssetLocation(hollow)) is { Id: > 0, IsMissing: false }, $"no {hollow}");
        foreach (var (k, section) in new[] { (1, LeadSection), (2, CopperSection) })
        {
            Assert.Equal(section, Drawing.SectionFor(k));
            Assert.True(W.GetItem(new AssetLocation(section)) is { Id: > 0, IsMissing: false }, $"no {section}");
        }
        foreach (var die in new[] { DrawBenchParts.DieIronCode, DrawBenchParts.DieSteelCode })
            Assert.Equal(BenchMod.Config.DieDurability, CutterItem(die).Collectible.GetMaxDurability(CutterItem(die)));
        Assert.Equal(100, BenchMod.Config.DieDurability);
        Assert.Equal("Draw bench frame", new ItemStack(W.GetBlock(BlockDrawBench.ItemCode)).GetName());
        Assert.Equal("Iron draw die", CutterItem(DrawBenchParts.DieIronCode).GetName());
        Assert.Equal("Steel draw die", CutterItem(DrawBenchParts.DieSteelCode).GetName());

        // the grid: the frame, of oak and iron, with a hammer
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:drawbench-frame-north" && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:log-placed-oak-ud");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the anvil: each die from one ingot of its metal, not for the helve hammer
        foreach (var (die, metal) in new[] { (DrawBenchParts.DieIronCode, "iron"), (DrawBenchParts.DieSteelCode, "steel") })
        {
            var recipe = Assert.Single(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == die);
            Assert.True(recipe.Ingredient!.SatisfiesAsIngredient(CutterItem("game:ingot-" + metal)));
            Assert.False(recipe.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-copper")));
            Assert.DoesNotContain(recipe.Name?.Path, new[] { "plate", "blistersteel" });
            Assert.InRange(recipe.Voxels.Cast<bool>().Count(v => v), 1, 42);
        }
        // the settings and the rig's pace agree
        Assert.Equal(BenchRig.TurnsPerSection[1], BenchMod.Config.TurnsPerSectionLead, 0.01);
        Assert.Equal(BenchRig.TurnsPerSection[2], BenchMod.Config.TurnsPerSectionCopper, 0.01);
        Assert.Equal(2f, SeraphHorizons.Mod.MachineOil.MachineOilSystem.Of(World.Api).Config.DrawBench.DrainPerJob);
        Assert.Contains("draw bench", Lang.Get("seraphhorizons:machineoil-text"));
    }

    // ---- Placing ----

    // On all four facings: room for every cell, a ghost stamped in each, the bench running away
    // along the facing from the die end, the power ghost's axle face the native west turned to the
    // facing, every cell's collision boxes its selection boxes and its lid; broken through a ghost,
    // every cell is cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Draw_bench_places_on_all_four_facings_with_ghosts_and_lids()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-240 - 12 * i++, -240);
            var bench = await PlaceBench(pos, side.Code());
            Assert.Equal(side, bench.Side);
            var cells = bench.GhostCells().ToList();
            Assert.Equal(3, cells.Count);
            var n = side.Normal();
            Assert.Contains(cells, c => c.Pos.Equals(pos.AddCopy(n.X * 3, 0, n.Z * 3)));
            foreach (var cell in cells.Select(c => c.Pos).Append(pos))
            {
                if (!cell.Equals(pos))
                    Assert.Equal(pos, Assert.IsType<BEDrawBenchGhost>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
                var block = W.BlockAccessor.GetBlock(cell);
                var selection = block.GetSelectionBoxes(W.BlockAccessor, cell);
                var collision = block.GetCollisionBoxes(W.BlockAccessor, cell);
                Assert.NotNull(selection);
                Assert.Equal(selection.Length + 1, collision.Length);
                Assert.Equal(1f, collision[^1].Y2, 3);
            }
            var powerGhost = Assert.Single(cells, c => c.Power).Pos;
            Assert.Equal(pos.AddCopy(n.X * 3, 0, n.Z * 3), powerGhost);
            var face = Assert.IsType<BlockDrawBenchGhostPower>(W.BlockAccessor.GetBlock(powerGhost)).PowerFace;
            Assert.Equal(Footprint.ToWorld(Side.West, side).Code(), face.Code);
            Assert.DoesNotContain(cells, c => c.Pos.Equals(powerGhost.AddCopy(face)));
            Assert.Equal("Draw bench frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));

            W.BlockAccessor.GetBlock(cells[1].Pos).OnBlockBroken(W, cells[1].Pos, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault("seraphhorizons:drawbench-frame-north"));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Draw_bench_is_built_in_order_and_refuses_parts_out_of_order()
    {
        var pos = await CutterSite(-240, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "east");
        var ghost = bench.GhostCells().Last().Pos;
        Assert.Contains("Next part: gearbox", BenchInfo(bench, player));

        // a later stage's part is refused and stays in hand, from the frame or a ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalchain-steel"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, BenchDie(DrawBenchParts.DieSteelCode))?.StackSize);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:rod-iron"))?.StackSize);
        Assert.False(bench.Parts.Has(DrawBenchStage.Gearbox));
        // no hollow before the bench is built
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        Assert.False(bench.JobOn);
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the parts in order, one each, taken from the hand
        for (int i = 0; i < BenchOrder.Length; i++)
        {
            var next = bench.Parts.Next!.Value;
            var left = CutterClick(player, i % 2 == 0 ? pos : ghost, CutterItem(BenchOrder[i], i == 0 ? 2 : 1));
            Assert.True(bench.Parts.Has(next), $"{BenchOrder[i]} did not go in as {next}");
            Assert.Equal(i == 0 ? 1 : 0, left?.StackSize ?? 0);
            // the same part again is refused once its stage is full
            if (i == 0)
                Assert.Equal(1, CutterClick(player, pos, CutterItem(DrawBenchParts.GearboxCode))?.StackSize);
        }
        // a second chain has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalchain-steel"))?.StackSize);
        Assert.False(bench.Complete);
        Assert.Contains("Next part: die", BenchInfo(bench, player));
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        Assert.Null(CutterClick(player, ghost, BenchDie(DrawBenchParts.DieIronCode)));
        Assert.True(bench.Complete);
        Assert.Equal("Draw bench", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Die: iron die, draws lead; 100 of 100 hollow sections left", BenchInfo(bench, player));
        Assert.Contains("Bench empty", BenchInfo(bench, player));

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        bench.ToTreeAttributes(tree);
        bench.FromTreeAttributes(tree, W);
        Assert.True(bench.Complete);
        Assert.Equal("game:bracket-heavy-steel", bench.Parts.FittedIn(DrawBenchStage.Dog));
        Assert.Equal(5, bench.Parts.Returns().Count);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_creative_shortcut_fits_the_next_stage_free()
    {
        var pos = await CutterSite(-252, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos);
        int stages = 0;
        while (!bench.Complete && stages < 10)
        {
            var next = bench.Parts.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(bench.Parts.Has(next), $"the shortcut did not fit {next}");
            stages++;
        }
        Assert.Equal(5, stages);
        Assert.Equal("steel", bench.Parts.DieMetal);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.False(bench.Parts.Has(DrawBenchStage.Die));
    }

    // ---- Drawing ----

    // Under power a lead hollow section on an iron die becomes four lead pipe sections at the output
    // face, the first one by the shaft's own turning; copper is refused by the iron die, and an ingot,
    // an angle or a pipe section is never taken; the die loses a point a hollow and the tank 2 points
    // a pipe section.
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Draw_bench_draws_four_lead_pipe_sections_from_a_hollow_with_an_iron_die_under_power()
    {
        var pos = await CutterSite(-264, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "south");
        AssembleBench(bench, player);

        // an ingot, an angle or a pipe section (what comes off) is not what it draws: the click is the
        // item's own business
        foreach (var code in new[] { LeadSection, CopperSection, "game:ingot-lead", "game:ingot-copper", "seraphhorizons:angle-lead", "game:ingot-iron" })
        {
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(_cutterHandled, $"{code} was the bench's");
        }
        Assert.False(bench.JobOn);
        // copper is refused by the iron die and stays in hand; lead goes on, one at a time
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.CopperHollow))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.False(bench.JobOn);
        Assert.Equal(2, CutterClick(player, pos, CutterItem(Drawing.LeadHollow, 3))?.StackSize);
        Assert.True(bench.JobOn);
        Assert.Equal(1, bench.Job.Class);
        Assert.Equal(Drawing.LeadHollow, bench.Hollow?.Collectible.Code.ToString());
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        // the die stays in while the hollow is on
        CutterClick(player, pos, null, ctrl: true);
        Assert.True(bench.Parts.Has(DrawBenchStage.Die));

        // unpowered, nothing moves; powered, the shaft draws the first section off by itself
        await World.Ticks(10);
        Assert.Equal(0, bench.Job.Work);
        Assert.Contains("Stopped with lead on: 0 of 4 sections", BenchInfo(bench, player));
        var rotor = await PowerBench(bench);
        await World.Until(() => bench.Job.Work > 0.02, 30000);
        Assert.True(bench.Running);
        Assert.Contains("Drawing lead:", BenchInfo(bench, player));
        await World.Until(() => bench.Job.Work >= 1, 120000);
        await World.Ticks(3);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        // it dropped beyond the output face (native east), not inside the bench
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().First(e => e.Itemstack.Collectible.Code.ToString() == LeadSection);
        var local = Footprint.ToLocal(new Float3((float)(dropped.Pos.X - pos.X), (float)(dropped.Pos.Y - pos.Y), (float)(dropped.Pos.Z - pos.Z)), bench.Side);
        Assert.True(local.X > 1 - 0.01f, $"the section is at native {local}");

        // the other three: just short of the second, then each in turn
        Assert.Equal(0, bench.Draw(BenchRadiansTo(bench, 2) - 2 * Math.PI * 0.2));
        Assert.Equal(1, bench.Draw(BenchRadiansTo(bench, 2)));
        Assert.Equal(1, bench.Draw(BenchRadiansTo(bench, 3)));
        Assert.True(bench.JobOn);
        Assert.Equal(100, bench.Parts.DieLeft);   // the die wears when the whole hollow is drawn
        Assert.Equal(1, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.False(bench.JobOn);
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        Assert.Equal(0, CutterItemsNear(pos).GetValueOrDefault(Drawing.LeadHollow));   // the hollow is used up
        Assert.Equal(99, bench.Parts.DieLeft);
        Assert.Equal(992, bench.Oiling!.Tank.Points, 6);   // 2 points a pipe section, 8 a hollow
        Assert.Contains("99 of 100 hollow sections left", BenchInfo(bench, player));
        W.BlockAccessor.SetBlock(0, rotor);
        CutterKillItems(pos);
    }

    // The steel die draws copper (and lead), in the slow gear: twice the turns a section.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Draw_bench_copper_is_refused_by_the_iron_die_and_drawn_by_the_steel_die()
    {
        var pos = await CutterSite(-276, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "west");
        AssembleBench(bench, player, BenchDie(DrawBenchParts.DieIronCode, 40));
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.CopperHollow))?.StackSize);
        Assert.False(bench.JobOn);

        // the iron die back out, with its durability, and the steel one in
        CutterClick(player, bench.GhostCells().First().Pos, null, ctrl: true);
        Assert.False(bench.Complete);
        var iron = BenchHeldStack(player, DrawBenchParts.DieIronCode)!;
        Assert.Equal(40, iron.Collectible.GetRemainingDurability(iron));
        Assert.Null(CutterClick(player, pos, BenchDie(DrawBenchParts.DieSteelCode)));
        Assert.Contains("Die: steel die, draws lead, copper", BenchInfo(bench, player));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.CopperHollow)));
        Assert.Equal(2, bench.Job.Class);

        // a lead section's turns are half a copper section's
        Assert.Equal(bench.TurnsPerSection(1) * 2, bench.TurnsPerSection(2), 0.02);
        Assert.Equal(0, bench.Draw(2 * Math.PI * bench.TurnsPerSection(1)));
        Assert.Equal(0.5, bench.Job.Work, 0.01);
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(CopperSection));
        Assert.Equal(99, bench.Parts.DieLeft);
        // and lead too
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        Assert.Equal(98, bench.Parts.DieLeft);
        CutterKillItems(pos);
    }

    // A die wears a point a hollow section, oiled or dry; worn out it is gone, the bench stops and
    // takes no hollow until a new die is fitted. Dry, the bench takes three times the load.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Draw_bench_die_wears_out_and_the_bench_stops_until_a_new_one_is_fitted()
    {
        var pos = await CutterSite(-288, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos);
        AssembleBench(bench, player, BenchDie(DrawBenchParts.DieIronCode, 2));
        var mp = W.BlockAccessor.GetBlockEntity(bench.CellPos(BenchRig.PowerCell))!.GetBehavior<BEBehaviorDrawBenchMP>()!;
        Assert.Equal(BenchMod.Config.ResistanceLead, mp.GetResistance(), 4);

        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(1, bench.Draw(BenchRadiansTo(bench, 1)));
        Assert.Equal(2, bench.Parts.DieLeft);   // nothing until the whole hollow is drawn
        Assert.Equal(3, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.Equal(1, bench.Parts.DieLeft);
        // dry: the same die wear, three times the load
        FillBenchOil(bench, 0);
        Assert.Equal(BenchMod.Config.ResistanceLead * 3, mp.GetResistance(), 4);
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.False(bench.Parts.Has(DrawBenchStage.Die));
        Assert.False(bench.Complete);
        await World.Ticks(5);
        Assert.Equal(8, CutterItemsNear(pos).GetValueOrDefault(LeadSection));   // the last hollow still came out
        CutterKillItems(pos);
        // no hollow goes on without a die, and nothing comes back by Ctrl
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        CutterClick(player, pos, null, ctrl: true);
        Assert.Equal(0, BenchHeld(player, DrawBenchParts.DieIronCode));
        // a worn-out die is refused, a new one goes in and it draws again
        Assert.Equal(1, CutterClick(player, pos, BenchDie(DrawBenchParts.DieIronCode, 0))?.StackSize ?? 0);
        Assert.Null(CutterClick(player, pos, BenchDie(DrawBenchParts.DieIronCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.Equal(99, bench.Parts.DieLeft);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_gives_back_the_die_and_everything_when_broken()
    {
        var pos = await CutterSite(-300, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "west");
        AssembleBench(bench, player, BenchDie(DrawBenchParts.DieSteelCode, 77));
        var ghost = bench.GhostCells().First().Pos;
        // the die back by Ctrl from a ghost, with its durability; the rest stays
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, BenchHeld(player, DrawBenchParts.DieSteelCode));
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(bench.Parts.Has(DrawBenchStage.Mandrel));
        Assert.Equal(4, bench.Parts.Returns().Count);

        // refit and load; broken before a pipe section comes off, the hollow comes back too
        Assert.Null(CutterClick(player, pos, BenchDie(DrawBenchParts.DieSteelCode, 77)));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.CopperHollow)));
        bench.Draw(BenchRadiansTo(bench, 0.5));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:drawbench-frame-north"));
        Assert.Equal(0, drops.GetValueOrDefault(CopperSection));
        foreach (var code in BenchOrder.Append(DrawBenchParts.DieSteelCode).Append(Drawing.CopperHollow))
            Assert.True(drops.GetValueOrDefault(code) == 1, $"{code}: {drops.GetValueOrDefault(code)}");
        var die = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == DrawBenchParts.DieSteelCode).Itemstack;
        Assert.Equal(77, die.Collectible.GetRemainingDurability(die));
        CutterKillItems(pos);

        // broken once a pipe section has come off, the rest of the hollow is lost with it
        bench = await PlaceBench(pos, "west");
        AssembleBench(bench, player);
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(1, bench.Draw(BenchRadiansTo(bench, 1.5)));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(DrawBenchParts.DieIronCode));
        Assert.Equal(0, drops.GetValueOrDefault(Drawing.LeadHollow));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Draw_bench_takes_hollows_from_a_chest_and_puts_pipe_sections_in_one()
    {
        var pos = await CutterSite(-252, -280);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "north");
        AssembleBench(bench, player);
        var infeed = bench.CellPos(Assert.Single(BenchRig.InfeedNeighbours()));
        var outfeed = bench.CellPos(BenchRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        // copper the iron die does not draw, then two lead hollows, and a lead pipe section and a lead
        // ingot it never takes
        source.Inventory[0].Itemstack = CutterItem(Drawing.CopperHollow);
        source.Inventory[1].Itemstack = CutterItem(Drawing.LeadHollow, 2);
        source.Inventory[2].Itemstack = CutterItem(LeadSection);
        source.Inventory[3].Itemstack = CutterItem("game:ingot-lead");
        source.MarkDirty(true);

        // not taken while the shaft stands
        await World.Ticks(30);
        Assert.False(bench.JobOn);
        await PowerBench(bench);
        await World.Until(() => bench.JobOn, 5000);
        Assert.Equal(1, source.Inventory[1].StackSize);
        Assert.Equal(Drawing.CopperHollow, source.Inventory[0].Itemstack?.Collectible.Code.ToString());
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.Equal(4, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == LeadSection).Sum(s => s.StackSize));
        // the next hollow goes on once the bench has cleared
        Assert.False(bench.JobOn);
        await World.Until(() => bench.JobOn, 5000);
        Assert.True(source.Inventory[1].Empty);
        Assert.Equal(4, bench.Draw(BenchRadiansTo(bench, 4)));
        Assert.Equal(8, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == LeadSection).Sum(s => s.StackSize));
        Assert.DoesNotContain(LeadSection, CutterItemsNear(pos).Keys);
        await World.Ticks(40);
        Assert.False(bench.JobOn);   // only the copper hollow, the pipe section and the ingot are left
        Assert.Equal(1, source.Inventory[0].StackSize);
        Assert.Equal(1, source.Inventory[2].StackSize);
        Assert.Equal(LeadSection, source.Inventory[2].Itemstack?.Collectible.Code.ToString());
        Assert.Equal("game:ingot-lead", source.Inventory[3].Itemstack?.Collectible.Code.ToString());
    }
}
