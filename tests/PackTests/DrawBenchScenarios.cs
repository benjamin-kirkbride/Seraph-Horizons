using Atlas.XUnit;
using SeraphHorizons.Mod.DrawBench;
using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines;
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
// come off the shaft's own turning, then finishes the hollow through BEDrawBench.Draw, as the gear cutter's scenarios finish a gear through Cut,
// the powered part on the virtual clock (tests/PackTests/README.md). Every
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
        be.PlaceCells();
        return be;
    }

    private const string BenchGearbox = "game:jonasframes-gearbox01";
    private const string BenchSpring = "seraphhorizons:spring-steel";

    /// <summary>The kept parts in order, as codes: a gearbox, an iron chain, a steel bracket, a meteoric iron rod
    /// and the mandrel station's spring (the mandrel stage takes both).</summary>
    private static readonly string[] BenchOrder =
        [BenchGearbox, "game:metalchain-iron", "game:bracket-heavy-steel", "game:rod-meteoriciron", BenchSpring];

    /// <summary>How many of a kept part its stage takes from one stack: two chains, two rods.</summary>
    private static int BenchCount(string code) => code is "game:metalchain-iron" or "game:rod-meteoriciron" ? 2 : 1;

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
        var ghost = MachineTest.Cells(bench).First().Pos;
        int i = 0;
        foreach (var part in BenchOrder.Select(c => CutterItem(c, BenchCount(c))).Append(die ?? BenchDie(Drawing.DieIronCode)))
            Assert.True(CutterClick(player, i++ % 2 == 0 ? bench.Pos : ghost, part) == null, $"{part.Collectible.Code} was not fitted");
        Assert.True(bench.Complete);
        MachineTest.FillOil(bench, 1);
    }

    // ---- Loading ----

    [AtlasScenario]
    public void Draw_bench_loads_with_its_blocks_dies_and_recipes()
    {
        Assert.True(DrawBenchSystem.Applies(World.Api));
        Assert.NotNull(BenchMod.Rig);
        // its frame is the shared machine block, its cells and power cell the shared ones, its build the shared build's
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:drawbench-frame-{side}")));
        Assert.True(W.GetBlock(new AssetLocation("seraphhorizons:drawbench-ghost")) is null or { IsMissing: true } or { Id: 0 });
        Assert.Same(BenchRig, ((BlockMachine)W.GetBlock(new AssetLocation("seraphhorizons:drawbench-frame-north"))).Rig);
        // every part the stages take exists here, the Jonas gearbox, the spring and the dies included
        var build = MachinesSystem.Of(World.Api).Build(DrawBenchSystem.MachineId)!;
        Assert.Equal(DrawBenchRequires.Stages, build.Stages.Select(s => s.Id));
        Assert.Equal(2, build.Stages[3].Materials.Count);   // springs exist: the mandrel stage takes one
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(W.GetItem(new AssetLocation(code)) != null, $"no {code}");
        Assert.Equal(BenchMod.Config.DieDurability, Assert.Single(build.Wearing).Uses);
        // what it draws, the game's chute section (copper the game's own and lead the state
        // UnifiedPipes' patch adds), and what comes off: the pack's pipe section
        foreach (var hollow in new[] { Drawing.LeadHollow, Drawing.CopperHollow })
            Assert.True(W.GetItem(new AssetLocation(hollow)) is { Id: > 0, IsMissing: false }, $"no {hollow}");
        foreach (var (k, section) in new[] { (1, LeadSection), (2, CopperSection) })
        {
            Assert.Equal(section, Drawing.SectionFor(k));
            Assert.True(W.GetItem(new AssetLocation(section)) is { Id: > 0, IsMissing: false }, $"no {section}");
        }
        foreach (var die in new[] { Drawing.DieIronCode, Drawing.DieSteelCode })
            Assert.Equal(BenchMod.Config.DieDurability, CutterItem(die).Collectible.GetMaxDurability(CutterItem(die)));
        Assert.Equal(100, BenchMod.Config.DieDurability);
        Assert.Equal("Draw bench frame", new ItemStack(W.GetBlock(new AssetLocation("seraphhorizons:drawbench-frame-north"))).GetName());
        Assert.Equal("Iron draw die", CutterItem(Drawing.DieIronCode).GetName());
        Assert.Equal("Steel draw die", CutterItem(Drawing.DieSteelCode).GetName());

        // the grid: the frame, of oak and iron (two plates in one slot, four rods, 12 nails and strips), with a hammer
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:drawbench-frame-north" && r.Enabled);
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("iron") && i.Quantity == 2);
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.Path == "rod-*" && i.Quantity == 2));
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 12);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:log-placed-oak-ud");
        // 2 stainless gears for the gearing the frame carries (the rectifier, the return shaft and barrel gears)
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "seraphhorizons:gear-stainless" && i.Quantity == 2);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the anvil: each die from two ingots of its metal (over one ingot's 42 voxels), not for the helve hammer
        foreach (var (die, metal) in new[] { (Drawing.DieIronCode, "iron"), (Drawing.DieSteelCode, "steel") })
        {
            var recipe = Assert.Single(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == die);
            Assert.True(recipe.Ingredient!.SatisfiesAsIngredient(CutterItem("game:ingot-" + metal)));
            Assert.False(recipe.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-copper")));
            Assert.DoesNotContain(recipe.Name?.Path, new[] { "plate", "blistersteel" });
            Assert.InRange(recipe.Voxels.Cast<bool>().Count(v => v), 43, 84);
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
            var cells = MachineTest.Cells(bench);
            Assert.Equal(3, cells.Count);
            var n = side.Normal();
            Assert.Contains(cells, c => c.Pos.Equals(pos.AddCopy(n.X * 3, 0, n.Z * 3)));
            foreach (var cell in cells.Select(c => c.Pos).Append(pos))
            {
                if (!cell.Equals(pos))
                    Assert.Equal(pos, Assert.IsType<BEMachineCell>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
                var block = W.BlockAccessor.GetBlock(cell);
                var selection = block.GetSelectionBoxes(W.BlockAccessor, cell);
                var collision = block.GetCollisionBoxes(W.BlockAccessor, cell);
                Assert.NotNull(selection);
                Assert.Equal(selection.Length + 1, collision.Length);
                Assert.Equal(1f, collision[^1].Y2, 3);
            }
            var powerGhost = Assert.Single(cells, c => c.Power).Pos;
            Assert.Equal(pos.AddCopy(n.X * 3, 0, n.Z * 3), powerGhost);
            var face = Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(powerGhost)).PowerFace;
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
        var ghost = MachineTest.Cells(bench).Last().Pos;
        Assert.Contains("Build: stage 1 of 5, gearbox", MachineTest.Info(bench, player));

        // a later stage's part is refused and stays in hand, from the frame or a ghost
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalchain-steel"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, BenchDie(Drawing.DieSteelCode))?.StackSize);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:rod-iron"))?.StackSize);
        Assert.Equal(0, bench.Assembly!.Build.Next);
        // no hollow before the bench is built
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        Assert.False(bench.JobOn);
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the parts in order, as many as each stage takes (two chains, two rods), taken from the hand;
        // fewer than that are refused and stay there
        var assembly = bench.Assembly!;
        for (int i = 0; i < BenchOrder.Length; i++)
        {
            int next = assembly.Build.Next!.Value;
            int count = BenchCount(BenchOrder[i]);
            if (count > 1)
            {
                Assert.Equal(count - 1, CutterClick(player, pos, CutterItem(BenchOrder[i], count - 1))?.StackSize);
                Assert.Empty(assembly.Returns().Where(d => d.Code == BenchOrder[i]));
            }
            var left = CutterClick(player, i % 2 == 0 ? pos : ghost, CutterItem(BenchOrder[i], count + (i == 0 ? 1 : 0)));
            // the mandrel stage's rods go in first, and the stage is in with its spring
            bool rods = BenchOrder[i] == "game:rod-meteoriciron";
            Assert.Contains(new StageDrop(BenchOrder[i], count), assembly.Returns());
            Assert.True(rods ? !assembly.Build.StageComplete(next) : assembly.Build.StageComplete(next), $"{BenchOrder[i]} did not go in as stage {next}");
            Assert.Equal(i == 0 ? 1 : 0, left?.StackSize ?? 0);
            // the same part again is refused once its stage is full
            if (i == 0)
                Assert.Equal(1, CutterClick(player, pos, CutterItem(BenchGearbox))?.StackSize);
        }
        // a second chain has nowhere to go
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalchain-steel"))?.StackSize);
        Assert.False(bench.Complete);
        Assert.Contains("Build: stage 5 of 5, die", MachineTest.Info(bench, player));
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        Assert.Null(CutterClick(player, ghost, BenchDie(Drawing.DieIronCode)));
        Assert.True(bench.Complete);
        Assert.Equal("Draw bench", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.Contains("Die: iron die, draws lead", MachineTest.Info(bench, player));
        Assert.Contains("Die: 100% left", MachineTest.Info(bench, player));
        // unpowered, the shared state's order says so first (an empty bench under power says it is empty)
        Assert.Contains("No power: its shaft must turn at 0.05 or faster", MachineTest.Info(bench, player));
        // more for a wrench: the die's uses, the oil's drain, the facing and the axle's face
        Assert.Contains("Hold a wrench to see more", MachineTest.Info(bench, player));
        var detail = MachineTest.WrenchInfo(bench, player);
        Assert.Contains("Die: 100 of 100 uses left", detail);
        Assert.Contains("Oil a job: 2", detail);
        Assert.Contains("Faces east; its axle meets its south face", detail);
        Assert.DoesNotContain("Hold a wrench", detail);

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        bench.ToTreeAttributes(tree);
        bench.FromTreeAttributes(tree, W);
        Assert.True(bench.Complete);
        Assert.Equal("game:bracket-heavy-steel", bench.Assembly!.Build.CodeIn(2, 0));
        Assert.Equal(BenchSpring, bench.Assembly.Build.CodeIn(3, 1));
        Assert.Equal(6, bench.Assembly.Returns().Count);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_creative_shortcut_fits_the_next_stage_free()
    {
        var pos = await CutterSite(-252, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos);
        // a click a stage (the shared build's shortcut fits a whole stage: the mandrel's rods and spring together)
        int clicks = 0;
        while (!bench.Complete && clicks < 10)
        {
            int next = bench.Assembly!.Build.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(bench.Assembly.Build.StageComplete(next), $"the shortcut did not fit stage {next}");
            clicks++;
        }
        Assert.Equal(5, clicks);
        Assert.Equal(BenchSpring, bench.Assembly!.Build.CodeIn(3, 1));
        Assert.Equal("steel", bench.DieMetal);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.Null(bench.Die);
    }

    // ---- Drawing ----

    // Under power a lead hollow section on an iron die becomes four lead pipe sections at the output
    // face, the first one by the shaft's own turning; copper is refused by the iron die, and an ingot,
    // an angle or a pipe section is never taken; the die loses a point a hollow and the tank 2 points
    // a pipe section.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_draws_four_lead_pipe_sections_from_a_hollow_with_an_iron_die_under_power()
    {
        var pos = await CutterSite(-264, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "south");
        AssembleBench(bench, player);
        using var clock = VirtualClock.Start(World);

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
        Assert.NotNull(bench.Die);

        // unpowered, nothing moves; powered, the shaft draws the first section off by itself
        await World.Ticks(10);
        Assert.Equal(0, bench.Job.Work);
        Assert.Contains("No power", MachineTest.Info(bench, player));
        Assert.Contains("On the bench: a lead tube blank, 0 of 4 sections drawn", MachineTest.Info(bench, player));
        var rotor = await PowerMachine(bench);
        await World.Until(() => bench.Job.Work > 0.02, 30000);
        Assert.True(bench.Running);
        await World.Until(() => bench.State == MachineState.Working, 5000);
        Assert.Contains("Drawing (", MachineTest.Info(bench, player));
        // the first comes off as its draw finishes, the jaws open, before the dog goes back
        await World.Until(() => bench.Job.Work >= Drawing.HandOut, 120000);
        await World.Ticks(3);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        // it dropped beyond the output face (native east), not inside the bench
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().First(e => e.Itemstack.Collectible.Code.ToString() == LeadSection);
        var local = Footprint.ToLocal(new Float3((float)(dropped.Pos.X - pos.X), (float)(dropped.Pos.Y - pos.Y), (float)(dropped.Pos.Z - pos.Z)), bench.Side);
        Assert.True(local.X > 1 - 0.01f, $"the section is at native {local}");

        // the other three: just short of the second's hand-out, then each in turn
        Assert.Equal(0, bench.Draw(MachineTest.RadiansTo(bench.Job, 1 + Drawing.HandOut, bench.TurnsPerSection(bench.Job.Class)) - 2 * Math.PI * 0.2));
        Assert.Equal(1, bench.Draw(MachineTest.RadiansTo(bench.Job, 1 + Drawing.HandOut, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Equal(1, bench.Draw(MachineTest.RadiansTo(bench.Job, 2 + Drawing.HandOut, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Equal(1, bench.Draw(MachineTest.RadiansTo(bench.Job, 3 + Drawing.HandOut, bench.TurnsPerSection(bench.Job.Class))));
        // all four are out, but the hollow is done, and the die worn, only once the dog is back at the die
        Assert.True(bench.JobOn);
        Assert.Equal(100, bench.DieLeft);
        Assert.Equal(0, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        Assert.False(bench.JobOn);
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        Assert.Equal(0, CutterItemsNear(pos).GetValueOrDefault(Drawing.LeadHollow));   // the hollow is used up
        Assert.Equal(99, bench.DieLeft);
        Assert.Equal(992, bench.Oiling!.Tank.Points, 6);   // 2 points a pipe section, 8 a hollow
        Assert.Contains("Die: 99 of 100 uses left", MachineTest.WrenchInfo(bench, player));
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
        AssembleBench(bench, player, BenchDie(Drawing.DieIronCode, 40));
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.CopperHollow))?.StackSize);
        Assert.False(bench.JobOn);

        // the iron die back out, with its durability, and the steel one in
        CutterClick(player, MachineTest.Cells(bench).First().Pos, null, ctrl: true);
        Assert.False(bench.Complete);
        var iron = MachineTest.HeldStack(player, Drawing.DieIronCode)!;
        Assert.Equal(40, iron.Collectible.GetRemainingDurability(iron));
        Assert.Null(CutterClick(player, pos, BenchDie(Drawing.DieSteelCode)));
        Assert.Contains("Die: steel die, draws lead, copper", MachineTest.Info(bench, player));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.CopperHollow)));
        Assert.Equal(2, bench.Job.Class);

        // a lead section's turns are half a copper section's: past the first copper section's hand-out
        Assert.Equal(bench.TurnsPerSection(1) * 2, bench.TurnsPerSection(2), 0.02);
        Assert.Equal(1, bench.Draw(2 * Math.PI * bench.TurnsPerSection(1)));
        Assert.Equal(0.5, bench.Job.Work, 0.01);
        Assert.Equal(3, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(CopperSection));
        Assert.Equal(99, bench.DieLeft);
        // and lead too
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        await World.Ticks(5);
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        Assert.Equal(98, bench.DieLeft);
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
        AssembleBench(bench, player, BenchDie(Drawing.DieIronCode, 2));
        var mp = W.BlockAccessor.GetBlockEntity(bench.CellPos(BenchRig.PowerCell))!.GetBehavior<BEBehaviorMachineMP>()!;
        Assert.Equal(BenchMod.Config.ResistanceLead, mp.GetResistance(), 4);

        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(1, bench.Draw(MachineTest.RadiansTo(bench.Job, 1, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Equal(2, bench.DieLeft);   // nothing until the whole hollow is drawn
        Assert.Equal(3, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Equal(1, bench.DieLeft);
        // dry: the same die wear, three times the load
        MachineTest.FillOil(bench, 0);
        Assert.Equal(BenchMod.Config.ResistanceLead * 3, mp.GetResistance(), 4);
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Null(bench.Die);
        Assert.False(bench.Complete);
        await World.Ticks(5);
        Assert.Equal(8, CutterItemsNear(pos).GetValueOrDefault(LeadSection));   // the last hollow still came out
        CutterKillItems(pos);
        // no hollow goes on without a die, and nothing comes back by Ctrl
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Drawing.LeadHollow))?.StackSize);
        CutterClick(player, pos, null, ctrl: true);
        Assert.Equal(0, MachineTest.Held(player, Drawing.DieIronCode));
        // a worn-out die is refused, a new one goes in and it draws again
        Assert.Equal(1, CutterClick(player, pos, BenchDie(Drawing.DieIronCode, 0))?.StackSize ?? 0);
        Assert.Null(CutterClick(player, pos, BenchDie(Drawing.DieIronCode)));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        Assert.Equal(99, bench.DieLeft);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_gives_back_the_die_and_everything_when_broken()
    {
        var pos = await CutterSite(-300, -260);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "west");
        AssembleBench(bench, player, BenchDie(Drawing.DieSteelCode, 77));
        var ghost = MachineTest.Cells(bench).First().Pos;
        // the die back by Ctrl from a ghost, with its durability; the rest stays
        CutterClick(player, ghost, null, ctrl: true);
        Assert.Equal(1, MachineTest.Held(player, Drawing.DieSteelCode));
        CutterClick(player, ghost, null, ctrl: true);
        Assert.True(bench.Assembly!.Build.StageComplete(3));
        Assert.Equal(5, bench.Assembly.Returns().Count);

        // refit and load; broken before a pipe section comes off, the hollow comes back too
        Assert.Null(CutterClick(player, pos, BenchDie(Drawing.DieSteelCode, 77)));
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.CopperHollow)));
        Assert.Equal(0, bench.Draw(MachineTest.RadiansTo(bench.Job, Drawing.HandOut, bench.TurnsPerSection(bench.Job.Class)) - 2 * Math.PI * 0.05));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault("seraphhorizons:drawbench-frame-north"));
        Assert.Equal(0, drops.GetValueOrDefault(CopperSection));
        foreach (var code in BenchOrder.Append(Drawing.DieSteelCode).Append(Drawing.CopperHollow))
        {
            int want = BenchOrder.Contains(code) ? BenchCount(code) : 1;
            Assert.True(drops.GetValueOrDefault(code) == want, $"{code}: {drops.GetValueOrDefault(code)}, want {want}");
        }
        var die = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == Drawing.DieSteelCode).Itemstack;
        Assert.Equal(77, die.Collectible.GetRemainingDurability(die));
        CutterKillItems(pos);

        // broken once a pipe section has come off (the first, as its draw finished), the rest of the hollow is
        // lost with it
        bench = await PlaceBench(pos, "west");
        AssembleBench(bench, player);
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(1, bench.Draw(MachineTest.RadiansTo(bench.Job, 0.5, bench.TurnsPerSection(bench.Job.Class))));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(Drawing.DieIronCode));
        Assert.Equal(0, drops.GetValueOrDefault(Drawing.LeadHollow));
        CutterKillItems(pos);
    }

    // ---- Chests ----

    // A chest in front of the die end and one by the output face are left alone: the running bench
    // takes no hollow from the first, and the pipe sections of a hollow put on by hand drop by the
    // output face rather than going into the second.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Draw_bench_takes_nothing_from_a_chest_and_puts_nothing_in_one()
    {
        var pos = await CutterSite(-252, -280);
        var player = await CutterPlayer();
        var bench = await PlaceBench(pos, "north");
        AssembleBench(bench, player);
        var infeed = bench.CellPos(new Int3(0, 0, -1));
        var outfeed = bench.CellPos(BenchRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        source.Inventory[0].Itemstack = CutterItem(Drawing.LeadHollow, 2);
        source.MarkDirty(true);

        using var clock = VirtualClock.Start(World);
        await PowerMachine(bench);
        await World.Ticks(60);
        Assert.False(bench.JobOn);
        Assert.Contains("Bench empty: put a lead or copper tube blank on", MachineTest.Info(bench, player));
        Assert.Equal(2, source.Inventory[0].StackSize);
        Assert.Null(CutterClick(player, pos, CutterItem(Drawing.LeadHollow)));
        Assert.Equal(4, bench.Draw(MachineTest.RadiansTo(bench.Job, 4, bench.TurnsPerSection(bench.Job.Class))));
        await World.Ticks(5);
        Assert.All(sink.Inventory, s => Assert.True(s.Empty));
        Assert.Equal(4, CutterItemsNear(pos).GetValueOrDefault(LeadSection));
        await World.Ticks(40);
        Assert.False(bench.JobOn);
        Assert.Equal(2, source.Inventory[0].StackSize);
        CutterKillItems(pos);
    }
}
