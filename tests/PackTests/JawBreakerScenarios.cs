using Atlas.XUnit;
using SeraphHorizons.Mod.JawBreaker;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, JawBreaker (#912) on the ore machines' shared game code (Machines/Game, #711): the jaw
// breaker in the pack's world, placed, built stage by stage with real items, driven by a vanilla creative
// rotor on its power face, fed raw ore by a vanilla chute and by hand, its crushed ore pushed into a chest
// at its output face, its load following its charge, and broken back into everything that went in. Built
// on granite floors 40 above spawn at x 600 to 640, z 600 to 620; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string JawFrame = "seraphhorizons:jawbreaker-";

    private async Task<BEJawBreaker> PlaceJawBreaker(BlockPos pos, string side = "south")
    {
        World.SetBlock(JawFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEJawBreaker ?? throw new Xunit.Sdk.XunitException($"no jaw breaker at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>A raw ore that spalls, in granite.</summary>
    private ItemStack RawOre(int size) =>
        new(W.SearchItems(new AssetLocation("game:ore-*-*-granite")).First(i => i is ItemGradedOre { Spalls: true }), size);

    /// <summary>Every stage by right-clicks with real items, from the frame and a cell in turn.</summary>
    private void BuildJawBreaker(BEJawBreaker breaker, IPlayer player)
    {
        var cell = breaker.CellPos(new Int3(1, 0, 0));
        int i = 0;
        while (breaker.Assembly!.Build.Next is { } _)
        {
            var (material, missing) = breaker.Assembly.Build.NextNeeds().First();
            string code = material.Codes[0].Replace("*", "oak").Replace("chute-straight-oak", "chute-straight-ud");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? breaker.Pos : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
            Assert.True(_cutterHandled);
        }
        Assert.True(breaker.Assembly.Ready);
        OilUp(breaker);
    }

    [AtlasScenario]
    public void Jaw_breaker_loads_with_its_frame_cells_build_and_recipe()
    {
        Assert.True(JawBreakerSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(JawFrame + side)));
        Assert.IsType<BlockMachineCell>(W.GetBlock(BlockMachineCell.CellCode));
        foreach (var face in BlockFacing.ALLFACES)
            Assert.IsType<BlockMachineCellPower>(W.GetBlock(BlockMachineCellPower.CodeFor(face)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(JawFrame + "north"));
        Assert.Equal("jawbreaker", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(MachinesSystem.Of(World.Api).Build("jawbreaker"));
        Assert.Equal("Jaw breaker frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == JawFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalplate-iron" && i.Quantity == 9);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "supportbeam-*" && i.Quantity == 11);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the figures it runs on: ore processing's
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(SeraphHorizons.Mod.Ore.Core.OreMachine.JawBreaker);
        Assert.Equal(24, spec.ItemsPerHour, 6);
        Assert.Equal(0, spec.WaterPerItem);
    }

    // On all four facings: room for every cell, a cell stamped in each (the power cell's axle face the
    // rig's east turned to the facing), their boxes the rig's; broken through a cell, every cell is cleared.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Jaw_breaker_places_on_all_four_facings_with_its_cells()
    {
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(600 + 10 * i++, 600);
            var breaker = await PlaceJawBreaker(pos, side.Code());
            Assert.Equal(side, breaker.Side);
            var rig = breaker.Rig!;
            foreach (var cell in rig.OtherCells)
            {
                var at = breaker.CellPos(cell.Pos);
                var be = Assert.IsType<BEMachineCell>(W.BlockAccessor.GetBlockEntity(at));
                Assert.Equal(pos, be.Principal);
                Assert.NotNull(W.BlockAccessor.GetBlock(at).GetSelectionBoxes(W.BlockAccessor, at));
            }
            var power = breaker.CellPos(rig.Anchor("power")!.Value.Cell);
            var powerBlock = Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(power));
            Assert.Equal(CellFaces.ToWorld(CellFace.East, side).Code(), powerBlock.PowerFace.Code);
            Assert.Equal(powerBlock.PowerFace, breaker.PowerFace);
            // the output face is the machine's front: toward a player who placed it looking along the facing
            Assert.Equal(CellFaces.ToWorld(CellFace.North, side), rig.Anchor("output")!.Value.ToWorld(side).Face);

            // broken through a cell: every cell goes, and the frame drops
            W.BlockAccessor.GetBlock(power).OnBlockBroken(W, power, null);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            foreach (var cell in rig.OtherCells)
                Assert.Equal(0, W.BlockAccessor.GetBlock(breaker.CellPos(cell.Pos)).Id);
        }
    }

    // Built stage by stage: a later stage's material refused, the stages in order from the frame and a
    // cell, the toggles drawn before their springs; then fed and run: crushed ore of the raw ore's grade,
    // one for one, into a chest at the output face, the load following the charge, the jaw faces worn a use
    // a rock; and broken, everything fitted given back.
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Jaw_breaker_builds_in_stages_crushes_raw_ore_into_a_chest_and_breaks_back()
    {
        var player = await CutterPlayer();
        var pos = await CutterSite(600, 620);
        var breaker = await PlaceJawBreaker(pos);
        var assembly = breaker.Assembly!;
        Assert.Equal(MachineLoad.Unassembled, breaker.Resistance);

        // out of order: the grizzly's iron rods before the jaws
        Assert.Equal(5, CutterClick(player, pos, CutterItem("game:rod-iron", 5))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.StagesDone);
        // nothing it takes yet: raw ore before it is built goes nowhere
        Assert.False(breaker.Takes(CutterItem("game:stick")));
        BuildJawBreaker(breaker, player);
        Assert.True(breaker.Built);
        Assert.True(breaker.ShowsPart("springs"));
        Assert.Equal(0.16f, breaker.Resistance, 3);

        // a chest at the output face (the front, north on a south-facing machine)
        var (outCell, outFace) = MachineChutes.World(pos, breaker.Side, breaker.Rig!.Anchor("output")!.Value);
        var chestPos = outCell.AddCopy(outFace);
        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;

        // fed by hand: a charge of 8, the rest stays in hand; the load at its full figure
        var ore = RawOre(12);
        var crushedCode = ore.Collectible.CrushingProps!.CrushedStack.ResolvedItemstack.Collectible.Code.ToString();
        Assert.Equal(4, CutterClick(player, pos, ore)?.StackSize);
        Assert.Equal(8, breaker.InfeedSlot.StackSize);
        Assert.Equal(0.8f, breaker.Resistance, 3);
        Assert.Equal(MachineState.NoPower, await StateSoon(breaker));

        // powered: it breaks rock and the crushed ore leaves into the chest
        var power = breaker.CellPos(breaker.Rig.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(breaker.PowerFace);
        World.SetBlock($"game:creativerotor-{breaker.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(breaker.ShaftSpeed) >= 0.3f, 15000);
        using (VirtualClock.Start(World))
            await World.Until(() => ChestCount(chest, crushedCode) >= 2, 60000);
        Assert.Equal(MachineState.Working, breaker.State);
        Assert.True(breaker.InfeedSlot.StackSize <= 6);
        // its load fell with the charge
        Assert.True(breaker.Resistance < 0.8f);
        var made = chest.Inventory.First(s => s.Itemstack?.Collectible.Code.ToString() == crushedCode).Itemstack;
        Assert.Equal("granite", made.Attributes.GetString("rock"));
        var faces = assembly.Wearing.Fitted("jawfaces")!.Value;
        Assert.True(faces.Left < 1000);

        // a chute laid down onto the grizzly's head feeds the infeed
        var (inCell, inFace) = MachineChutes.World(pos, breaker.Side, breaker.Rig.Anchor("infeed")!.Value);
        var chutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-ud", chutePos);
        await World.Ticks(2);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        int before = breaker.InfeedSlot.StackSize;
        chute.Inventory[0].Itemstack = RawOre(1);
        chute.Inventory[0].MarkDirty();
        await UntilChuteFed(breaker, chute);
        Assert.True(breaker.InfeedSlot.StackSize + ChestCount(chest, crushedCode) >= before + 1);

        // broken: the frame, every stage's items and the faces with use left; the chute's ore was its own
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == JawFrame + "north");
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:spring-steel").Sum(d => d.StackSize));
        Assert.Equal(5, drops.Where(d => d.Collectible.Code.ToString() == "game:rod-iron").Sum(d => d.StackSize));
        // the stage's own steel plates (jaws 1, eccentric 1) and the faces' 2
        Assert.Equal(4, drops.Where(d => d.Collectible.Code.ToString() == "game:metalplate-steel").Sum(d => d.StackSize));
        foreach (var p in new[] { chestPos, chutePos })
            W.BlockAccessor.SetBlock(0, p);
        W.BlockAccessor.BreakBlock(pos, null);
    }

    private static int ChestCount(BlockEntityContainer chest, string code) =>
        chest.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    private async Task<MachineState> StateSoon(BEOreMachine machine)
    {
        await World.Ticks(5);
        return machine.State;
    }
}
