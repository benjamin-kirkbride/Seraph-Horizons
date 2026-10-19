using Atlas.XUnit;
using SeraphHorizons.Mod.BallMill;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, BallMill (#915) on the ore machines' shared game code (Machines/Game, #711): the ball mill
// in the pack's world, placed, built stage by stage with real items, driven by a vanilla creative rotor on its
// power face, fed crushed ore by hand and by a vanilla chute into its feed box, its ground ore pushed into a
// chest at its output face, stopped dry and started again with water, the shaking table's middlings freed, and
// broken back into everything that went in. Built on granite floors 40 above spawn at x 660 to 700, z 660 to
// 680; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string MillFrame = "seraphhorizons:ballmill-";

    private async Task<BEBallMill> PlaceBallMill(BlockPos pos, string side = "south")
    {
        World.SetBlock(MillFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEBallMill ?? throw new Xunit.Sdk.XunitException($"no ball mill at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>Poor crushed hematite from andesite: the ore the mill lifts most.</summary>
    private ItemStack PoorCrushed(int size)
    {
        var stack = CutterItem(OreProducts.CrushedCode("hematite", "poor"), size);
        stack.Attributes.SetString(OreStreamAttributes.Rock, "andesite");
        return stack;
    }

    /// <summary>Every stage by right-clicks with real items, from the frame and a cell in turn.</summary>
    private void BuildBallMill(BEBallMill mill, IPlayer player, string pipe = "seraphhorizons:pipesection-lead")
    {
        var cell = mill.CellPos(new Int3(1, 0, 0));
        int i = 0;
        while (mill.Assembly!.Build.Next is { } _)
        {
            var (material, missing) = mill.Assembly.Build.NextNeeds().First();
            string code = material.Codes.Contains(pipe) ? pipe : material.Codes[0].Replace("plank-*", "plank-oak").Replace("chute-straight-*", "chute-straight-we");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? mill.Pos : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
            Assert.True(_cutterHandled);
            // the girth wheel shows as soon as its wooden gear is in, before the rest of the gearing
            if (code == "game:spurgear-s")
                Assert.True(mill.ShowsPart("girth") && !mill.ShowsPart("pinion"));
        }
        Assert.True(mill.Assembly.Ready);
    }

    private static int MillChestCount(BlockEntityContainer chest, string code) =>
        chest.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    /// <summary>Waits on the virtual clock for <paramref name="done"/>, saying where the mill stands if it never is.</summary>
    private async Task MillUntil(BEBallMill mill, Func<bool> done, int ticks = 90000)
    {
        try
        {
            using (VirtualClock.Start(World))
                await World.Until(done, ticks);
        }
        catch (Exception e) when (e.GetType().Name.Contains("Timeout"))
        {
            string held = string.Join(", ", mill.OutputSlots("output").Where(s => !s.Empty).Select(s => $"{s.StackSize}x {s.Itemstack.Collectible.Code}"));
            throw new Xunit.Sdk.XunitException($"the mill never got there: {mill.State}, speed {mill.ShaftSpeed}, infeed {mill.InfeedSlot.StackSize}, " +
                $"water {mill.Water.Litres} of {mill.ReservoirCapacity}, turns {mill.Turns.Turns}, held [{held}]");
        }
    }

    private async Task<MachineState> MillStateSoon(BEOreMachine machine)
    {
        await World.Ticks(5);
        return machine.State;
    }

    [AtlasScenario]
    public void Ball_mill_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(BallMillSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(MillFrame + side)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(MillFrame + "north"));
        Assert.Equal("ballmill", block.Machine);
        Assert.NotNull(block.Rig);
        var build = MachinesSystem.Of(World.Api).Build("ballmill")!;
        Assert.Equal(["drum", "gearing", "screens", "sheetiron", "services"], build.Stages.Select(s => s.Id).ToArray());
        Assert.Empty(build.Wearing);
        Assert.Equal("Ball mill", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == MillFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalplate-iron" && i.Quantity == 4);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "stonebricks-*" && i.Quantity == 2);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.BallMill);
        Assert.Equal(6, spec.ItemsPerHour, 6);
        Assert.Equal(25, spec.WaterPerItem, 6);
        // every crushed ore grinds to a ground ore the game has
        foreach (var crushed in W.SearchItems(new AssetLocation(OreProducts.Domain, "crushedore-*")))
        {
            var parts = crushed.Code.Path.Split('-');
            Assert.True(W.GetItem(new AssetLocation(OreProducts.GroundCode(parts[1], parts[2]))) is { Id: > 0 }, $"no ground ore for {crushed.Code}");
        }
    }

    // Built stage by stage; then fed and run: ground ore of the crushed ore's grade, one for one, classified,
    // into a chest at the output face, on a steady load; dry it stops and resumes with water; the table's
    // middlings come out with nothing locked; a chute into the feed box feeds it; broken, everything back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Ball_mill_builds_in_stages_grinds_crushed_ore_and_middlings_into_a_chest_and_breaks_back()
    {
        var hand = await CutterHand();
        var pos = await CutterSite(660, 660);
        // its chunk columns kept loaded, so the mill runs on the virtual clock with no player beside it (the
        // shared player stays where the other scenarios count on it)
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 5) / size; cx <= (pos.X + 5) / size; cx++)
            for (int cz = (pos.Z - 5) / size; cz <= (pos.Z + 5) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
        await BallMillRun(hand.Player, pos);
    }

    private async Task BallMillRun(IPlayer player, BlockPos pos)
    {
        var mill = await PlaceBallMill(pos);
        var assembly = mill.Assembly!;
        Assert.Equal(MachineLoad.Unassembled, mill.Resistance);

        // out of order: the screens' chain before the drum
        Assert.Equal(25, CutterClick(player, pos, CutterItem("game:metalchain-iron", 25))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.StagesDone);
        BuildBallMill(mill, player);
        Assert.True(mill.Built);
        Assert.True(mill.ShowsPart("pipelead") && !mill.ShowsPart("pipecopper"));
        // a steady load: its full figure, empty as working
        Assert.Equal(1.1f, mill.Resistance, 3);

        // a chest at the output face
        var (outCell, outFace) = MachineChutes.World(pos, mill.Side, mill.Rig!.Anchor("output")!.Value);
        var chestPos = outCell.AddCopy(outFace);
        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;

        // not what it grinds: raw ore and a stick stay in hand
        Assert.False(mill.Takes(CutterItem("game:stick")));
        Assert.False(mill.Takes(new ItemStack(W.SearchItems(new AssetLocation("game:ore-poor-hematite-*")).First())));
        // fed by hand
        Assert.Null(CutterClick(player, pos, PoorCrushed(4)));
        Assert.Equal(4, mill.InfeedSlot.StackSize);
        Assert.Equal(1.1f, mill.Resistance, 3);
        Assert.Equal(MachineState.NoPower, await MillStateSoon(mill));

        // powered, but dry: it stops until water comes
        var power = mill.CellPos(mill.Rig.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(mill.PowerFace);
        World.SetBlock($"game:creativerotor-{mill.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        // the heaviest machine (1.1) stalls a creative rotor at its top setting (10, about a torque of 1): three times it
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 30);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(mill.ShaftSpeed) >= 0.3f, 15000);
        Assert.Equal(MachineState.Dry, await MillStateSoon(mill));
        Assert.Equal(4, mill.InfeedSlot.StackSize);
        Assert.True(mill.ReservoirCapacity >= 50);
        mill.FillWater(mill.ReservoirCapacity);

        const string GroundCode = "seraphhorizons:groundore-hematite-poor";
        await MillUntil(mill, () => MillChestCount(chest, GroundCode) >= 2);
        var made = chest.Inventory.First(s => s.Itemstack?.Collectible.Code.ToString() == GroundCode).Itemstack;
        var stream = OreStreamAttributes.Read(made)!;
        Assert.Equal(OreState.Ground | OreState.Classified, stream.State);
        Assert.Equal(OreProducts.RawUnits("hematite", "poor")!.Value, stream.Units, 9);
        Assert.Equal("andesite", made.Attributes.GetString("rock"));
        Assert.Equal(1.1f, mill.Resistance, 3);
        // each rock spent its 25 L
        Assert.True(mill.Water.Litres <= mill.ReservoirCapacity - 50 + 1e-6);

        // the table's middlings (its locked share split out) come back ground, freed
        mill.FillWater(mill.ReservoirCapacity);
        await MillUntil(mill, () => mill.InfeedSlot.Empty);
        // (the table's middlings are its feed's item: crushed ore, classified but not ground)
        var middlings = CutterItem(OreProducts.CrushedCode("hematite", "poor"), 1);
        OreStreamAttributes.Write(middlings, new OreStream("hematite", "poor", 2.4, Locked: 2.4, State: OreState.Classified, Worked: WorkedBy.Sluice | WorkedBy.Jig, Rock: "andesite"));
        Assert.True(mill.Takes(middlings));
        mill.FillWater(mill.ReservoirCapacity);
        int groundBefore = MillChestCount(chest, GroundCode);
        Assert.Null(CutterClick(player, pos, middlings));
        await MillUntil(mill, () => MillChestCount(chest, GroundCode) > groundBefore);
        var freed = chest.Inventory.Select(s => s.Itemstack).OfType<ItemStack>()
            .Select(OreStreamAttributes.Read).OfType<OreStream>().Single(s => s.Locked is not null);
        Assert.Equal(0, freed.Locked!.Value, 9);
        Assert.Equal(WorkedBy.None, freed.Worked);
        Assert.Equal(2.4, freed.Units, 9);

        // a chute laid into the feed box feeds the infeed
        var (inCell, inFace) = MachineChutes.World(pos, mill.Side, mill.Rig.Anchor("infeed")!.Value);
        var chutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-" + (inFace.Axis == EnumAxis.X ? "we" : "ns"), chutePos);
        await World.Ticks(2);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        chute.Inventory[0].Itemstack = PoorCrushed(1);
        chute.Inventory[0].MarkDirty();
        await UntilChuteFed(mill, chute);

        // broken: the frame and every stage's items, the ball charge's ten ingots among them
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == MillFrame + "north");
        Assert.Equal(10, drops.Where(d => d.Collectible.Code.ToString() == "game:ingot-iron").Sum(d => d.StackSize));
        Assert.Equal(21, drops.Where(d => d.Collectible.Code.ToString() == "game:metalplate-iron").Sum(d => d.StackSize));
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-lead").Sum(d => d.StackSize));
        foreach (var p in new[] { chestPos, chutePos })
            W.BlockAccessor.SetBlock(0, p);
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
