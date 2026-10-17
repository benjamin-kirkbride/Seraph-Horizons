using Atlas.XUnit;
using SeraphHorizons.Mod.AmalgamationPlates;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, AmalgamationPlates (#917) on the ore machines' shared game code (Machines/Game, #711): the plates
// in the pack's world, placed, built stage by stage with real items, run with no power on the virtual clock: gold
// quartz over undressed plates goes by unchanged; dressed from a bucket of mercury, the plates catch its free gold
// as amalgam and send the rock on amalgamated into a chest at the outlet; the amalgam is scraped off by hand;
// with the outlet left open the tailings are dumped as sand; broken, everything back. Built on granite floors 40
// above spawn at x 740 to 760, z 740; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string PlatesFrame = "seraphhorizons:amalgamationplates-";

    private async Task<BEAmalgamationPlates> PlacePlates(BlockPos pos, string side = "south")
    {
        World.SetBlock(PlatesFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEAmalgamationPlates ?? throw new Xunit.Sdk.XunitException($"no amalgamation plates at {pos}");
        be.PlaceCells();
        return be;
    }

    private ItemStack QuartzCrushed(string ore, string grade, int size, string rock = "andesite")
    {
        var stack = CutterItem(OreProducts.CrushedCode(ore, grade), size);
        stack.Attributes.SetString(OreStreamAttributes.Rock, rock);
        return stack;
    }

    /// <summary>A burned bucket holding <paramref name="portions"/> of mercury.</summary>
    private ItemStack MercuryBucket(int portions)
    {
        var bucket = CutterItem("game:bucket-burned");
        var mercury = CutterItem(BEAmalgamationPlates.MercuryCode, portions);
        ((BlockLiquidContainerBase)bucket.Block).SetContent(bucket, mercury);
        return bucket;
    }

    private async Task PlatesUntil(BEAmalgamationPlates plates, Func<bool> done, int ticks = 120000)
    {
        try
        {
            using (VirtualClock.Start(World))
                await World.Until(done, ticks);
        }
        catch (Exception e) when (e.GetType().Name.Contains("Timeout"))
        {
            string held = string.Join(", ", plates.OutputSlots("output").Where(s => !s.Empty).Select(s => $"{s.StackSize}x {s.Itemstack.Collectible.Code}"));
            throw new Xunit.Sdk.XunitException($"the plates never got there: {plates.State}, infeed {plates.InfeedSlot.StackSize}, " +
                $"water {plates.Water.Litres} of {plates.ReservoirCapacity}, dressing {plates.Dressing}, amalgam {plates.AmalgamUnits}, held [{held}]");
        }
    }

    [AtlasScenario]
    public void Amalgamation_plates_load_with_their_frame_build_recipe_and_figures()
    {
        Assert.True(AmalgamationPlatesSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(PlatesFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(PlatesFrame + "north"));
        Assert.Equal("amalgamationplates", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.Null(block.Rig!.Anchor(OreMachineRig.Power));
        var build = OreMachinesSystem.Of(World.Api).Build("amalgamationplates")!;
        Assert.Equal(["plates", "trap", "services"], build.Stages.Select(s => s.Id).ToArray());
        Assert.Equal("Amalgamation plates", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == PlatesFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "plank-*" && i.Quantity == 12);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the copper half plate the plates stage takes, and the mercury that dresses them, are in the game
        Assert.True(W.GetItem(new AssetLocation("seraphhorizons:halfplate-copper")) is { Id: > 0 });
        Assert.True(W.GetItem(new AssetLocation(BEAmalgamationPlates.MercuryCode)) is { Id: > 0 });
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.AmalgamationPlates);
        Assert.Equal(8, spec.ItemsPerHour, 6);
        Assert.Equal(5, spec.WaterPerItem, 6);
        Assert.Equal(0.65, spec.Recovery, 6);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Amalgamation_plates_build_dress_catch_gold_scrape_dump_tailings_and_break_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(740, 740);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 5) / size; cx <= (pos.X + 5) / size; cx++)
            for (int cz = (pos.Z - 5) / size; cz <= (pos.Z + 5) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var plates = await PlacePlates(pos);
        var assembly = plates.Assembly!;
        // out of order: the trap's iron before the plates
        Assert.Equal(2, CutterClick(player, pos, CutterItem("game:metalplate-iron", 2))?.StackSize);
        Assert.Equal(0, assembly.Build.StagesDone);
        // mercury before the plates are fitted is refused, and kept
        var early = CutterClick(player, pos, MercuryBucket(50));
        Assert.True(_cutterHandled);
        Assert.Equal(0, plates.Dressing);
        Assert.Equal(50, ((BlockLiquidContainerBase)early!.Block).GetContent(early)!.StackSize);

        var foot = plates.CellPos(new Int3(0, 0, 2));
        int i = 0;
        while (assembly.Build.Next is { } _)
        {
            var (material, missing) = assembly.Build.NextNeeds().First();
            string code = material.Codes[0].Replace("chute-straight-*", "chute-straight-ns");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? pos : foot, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
        }
        Assert.True(plates.Built);
        Assert.True(plates.ShowsPart("plates") && plates.ShowsPart("trap") && plates.ShowsPart("pipecopper"));
        Assert.False(plates.ShowsPart("dressing") || plates.ShowsPart("amalgam1"));
        Assert.Equal(0f, plates.Resistance, 6);

        var (outCell, outFace) = MachineChutes.World(pos, plates.Side, plates.Rig!.Anchor("output")!.Value);
        var chestPos = outCell.AddCopy(outFace);
        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;
        string goldCode = OreProducts.CrushedCode("quartz_nativegold", "rich");
        int ChestGold() => chest.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == goldCode).Sum(s => s.StackSize);

        // raw ore and a stick are not taken
        Assert.False(plates.Takes(CutterItem("game:stick")));
        // undressed, dry: it waits for water; with water, gold quartz goes by unchanged
        Assert.Null(CutterClick(player, pos, QuartzCrushed("quartz_nativegold", "rich", 1)));
        Assert.Equal(MachineState.Dry, await MillStateSoon(plates));
        plates.FillWater(plates.ReservoirCapacity);
        await PlatesUntil(plates, () => ChestGold() >= 1);
        var passed = OreStreamAttributes.Read(chest.Inventory.First(s => !s.Empty).Itemstack)!;
        Assert.False(passed.State.HasFlag(OreState.Amalgamated));
        Assert.Equal(0, plates.AmalgamUnits);

        // dressed from a bucket of mercury
        var bucket = CutterClick(player, pos, MercuryBucket(150));
        Assert.True(_cutterHandled);
        Assert.Equal(100, plates.Dressing, 6);
        Assert.Equal(50, ((BlockLiquidContainerBase)bucket!.Block).GetContent(bucket)!.StackSize);
        Assert.True(plates.ShowsPart("dressing"));

        // four rich gold quartz: 4 units each, unclassified (× 0.85), 0.65 of that caught: 2.21 a rock
        Assert.Null(CutterClick(player, pos, QuartzCrushed("quartz_nativegold", "rich", 4)));
        plates.FillWater(plates.ReservoirCapacity);
        await PlatesUntil(plates, () =>
        {
            if (plates.Water.Litres < 10) plates.FillWater(plates.ReservoirCapacity);
            return ChestGold() >= 5;
        });
        Assert.Equal(4 * 2.21, plates.AmalgamUnits, 6);
        Assert.Equal(100 - 4 * 2.21 * 2, plates.Dressing, 6);
        Assert.True(plates.ShowsPart("amalgam1"));
        var caught = chest.Inventory.Select(s => s.Itemstack).OfType<ItemStack>().Select(OreStreamAttributes.Read).OfType<OreStream>()
            .First(s => s.State.HasFlag(OreState.Amalgamated));
        Assert.Equal(4 - 2.21, caught.Units, 6);
        Assert.Equal("andesite", caught.Rock);

        // scraped by hand: one whole amalgam (5 units), the rest left on the plates
        Assert.Null(CutterClick(player, pos, null));
        Assert.True(_cutterHandled);
        int amalgam = player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName)!
            .Concat(player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) ?? Enumerable.Empty<ItemSlot>())
            .Where(s => s.Itemstack?.Collectible.Code.ToString() == "seraphhorizons:amalgam-quartz_nativegold").Sum(s => s.StackSize);
        Assert.Equal(1, amalgam);
        Assert.Equal(4 * 2.21 - 5, plates.AmalgamUnits, 6);

        // the outlet left open: two galena rocks run over unchanged and are dumped as two layers of sand
        W.BlockAccessor.SetBlock(0, chestPos);
        Assert.Null(CutterClick(player, pos, QuartzCrushed("galena", "rich", 2)));
        plates.FillWater(plates.ReservoirCapacity);
        await PlatesUntil(plates, () => plates.InfeedSlot.Empty && plates.OutputSlots("output").All(s => s.Empty));
        Assert.Equal("game:sand-andesite-2", W.BlockAccessor.GetBlock(chestPos).Code.ToString());

        // broken: the frame, every stage's items and the whole amalgam left (none: 3.84 units)
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == PlatesFrame + "north");
        Assert.Equal(3, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:halfplate-copper").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-copper").Sum(d => d.StackSize));
        W.BlockAccessor.SetBlock(0, chestPos);
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
