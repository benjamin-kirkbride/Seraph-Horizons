using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Vanner;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Vanner (#923) on the ore machines' shared game code (Machines/Game, #711): the vanner in the
// pack's world, placed, built stage by stage with real items (the belt its wearing part, fitted as stage 4),
// driven by a vanilla creative rotor on its power face, fed crushed ore by hand and by a vanilla chute into its
// feed box, its concentrate pushed into a chest at its concentrate face and its tailings discarded beside its
// foot as sand, its worn belt renewed by right-click, and broken back into everything that went in. Built on
// granite floors 40 above spawn at x 920, z 920; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string VannerFrame = "seraphhorizons:vanner-";

    private async Task<BEVanner> PlaceVanner(BlockPos pos, string side = "south")
    {
        World.SetBlock(VannerFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEVanner ?? throw new Xunit.Sdk.XunitException($"no vanner at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>Every stage by right-clicks with real items, from the frame and a cell in turn.</summary>
    private void BuildVanner(BEVanner vanner, IPlayer player, string pipe = "seraphhorizons:pipesection-copper")
    {
        var cell = vanner.CellPos(new Int3(1, 0, 1));
        int i = 0;
        while (vanner.Assembly!.Build.Next is { } _)
        {
            var (material, missing) = vanner.Assembly.Build.NextNeeds().First();
            string code = material.Codes.Contains(pipe) ? pipe : material.Codes[0].Replace("plank-*", "plank-oak").Replace("chute-straight-*", "chute-straight-we");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? vanner.Pos : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
            Assert.True(_cutterHandled);
            // the belt is drawn as soon as its leather is in
            if (code == "game:leather-normal-plain")
                Assert.True(vanner.ShowsPart("belt") && !vanner.ShowsPart("feedbox"));
        }
        Assert.True(vanner.Assembly.Ready);
    }

    private async Task VannerUntil(BEVanner vanner, Func<bool> done, int ticks = 200000)
    {
        try
        {
            using (VirtualClock.Start(World))
                await World.Until(done, ticks);
        }
        catch (Exception e) when (e.GetType().Name.Contains("Timeout"))
        {
            throw new Xunit.Sdk.XunitException($"the vanner never got there: {vanner.State}, speed {vanner.ShaftSpeed}, infeed {vanner.InfeedSlot.StackSize}, " +
                $"water {vanner.Water.Litres} of {vanner.ReservoirCapacity}, turns {vanner.Turns.Turns}");
        }
    }

    [AtlasScenario]
    public void Vanner_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(VannerSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(VannerFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(VannerFrame + "north"));
        Assert.Equal("vanner", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(block.Rig!.Anchor("concentrate"));
        Assert.NotNull(block.Rig.Anchor("tailings"));
        Assert.NotNull(block.Rig.Anchor(OreMachineRig.Infeed));
        var build = OreMachinesSystem.Of(World.Api).Build("vanner")!;
        Assert.Equal(["countershaft", "shaker", "rollers", "belt", "services"], build.Stages.Select(s => s.Id).ToArray());
        Assert.Equal("belt", Assert.Single(build.Wearing).Id);
        Assert.Equal("Vanner frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == VannerFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "plank-*" && i.Quantity == 5);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // every material of its build is an item or block the game has
        foreach (var m in build.Stages.SelectMany(s => s.Materials))
            Assert.True(m.Codes.Any(c => c.Contains('*') ? W.SearchItems(new AssetLocation(c)).Length + W.SearchBlocks(new AssetLocation(c)).Length > 0
                : W.GetItem(new AssetLocation(c)) is { Id: > 0 } || W.GetBlock(new AssetLocation(c)) is { Id: > 0 }), $"{string.Join(",", m.Codes)} is not in the game");
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.Vanner);
        Assert.Equal(3, spec.ItemsPerHour, 6);
        Assert.Equal(15, spec.WaterPerItem, 6);
        Assert.Equal(0.8, spec.Recovery, 6);
    }

    // Built stage by stage; then fed and run: concentrate into a chest at the concentrate face, the tailings
    // (nothing beyond the foot) on a heap of sand; a steady load; dry it stops; a chute into the feed box
    // feeds it; worn out its belt is renewed by right-click; broken, everything back.
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Vanner_builds_in_stages_vans_crushed_ore_and_renews_its_belt()
    {
        var hand = await CutterHand();
        var pos = await CutterSite(920, 920);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 5) / size; cx <= (pos.X + 5) / size; cx++)
            for (int cz = (pos.Z - 5) / size; cz <= (pos.Z + 5) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
        var player = hand.Player;

        var vanner = await PlaceVanner(pos);
        var assembly = vanner.Assembly!;
        Assert.Equal(MachineLoad.Unassembled, vanner.Resistance);

        // out of order: the belt's leather before the countershaft
        Assert.Equal(2, CutterClick(player, pos, CutterItem("game:leather-normal-plain", 2))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.StagesDone);
        BuildVanner(vanner, player);
        Assert.True(vanner.Built);
        Assert.True(vanner.ShowsPart("pipecopper") && !vanner.ShowsPart("pipelead"));
        Assert.Equal(0.08f, vanner.Resistance, 4);

        // a chest at the concentrate face; nothing at the tailings face
        var (concCell, concFace) = MachineChutes.World(pos, vanner.Side, vanner.Rig!.Anchor("concentrate")!.Value);
        var chestPos = concCell.AddCopy(concFace);
        World.SetBlock("game:chest-east", chestPos);
        var (tailCell, tailFace) = MachineChutes.World(pos, vanner.Side, vanner.Rig.Anchor("tailings")!.Value);
        var heapPos = tailCell.AddCopy(tailFace);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;

        // not what it washes: raw ore and concentrate stay in hand
        Assert.False(vanner.Takes(new ItemStack(W.SearchItems(new AssetLocation("game:ore-rich-hematite-*")).First())));
        Assert.False(vanner.Takes(CutterItem(OreProducts.ConcentrateCode("hematite"), 1)));
        const string feed = "seraphhorizons:crushedore-hematite-rich";
        var rocks = CutterItem(feed, 4);
        rocks.Attributes.SetString(OreStreamAttributes.Rock, "granite");
        Assert.Null(CutterClick(player, pos, rocks));
        Assert.Equal(4, vanner.InfeedSlot.StackSize);
        Assert.Equal(MachineState.NoPower, await MillStateSoon(vanner));

        // powered, but dry: it stops until water comes
        var power = vanner.CellPos(vanner.Rig.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(vanner.PowerFace);
        World.SetBlock($"game:creativerotor-{vanner.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(vanner.ShaftSpeed) >= 0.3f, 15000);
        Assert.Equal(MachineState.Dry, await MillStateSoon(vanner));
        Assert.Equal(4, vanner.InfeedSlot.StackSize);
        vanner.FillWater(vanner.ReservoirCapacity);

        await VannerUntil(vanner, () => vanner.InfeedSlot.Empty);
        await World.Ticks(5);
        // 4 rock × units × 0.85 (unclassified) × r, in whole concentrates
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);
        double units = OreProducts.RawUnits("hematite", "rich")!.Value;
        double share = recovery.PassShare(recovery.Ore("hematite"), OreMachine.Vanner);
        int expected = (int)Math.Floor(4 * units * 0.85 * share / recovery.ConcentrateUnits + 1e-9);
        Assert.True(expected > 0);
        Assert.Equal(expected, ChestCount(chestPos, OreProducts.ConcentrateCode("hematite")));
        // its tailings, with nothing beyond the foot, on a heap of granite sand: half a layer a rock
        Assert.Equal(TailingsBlocks.Code(TailingsForm.Sand, "granite", 2), W.BlockAccessor.GetBlock(heapPos).Code.ToString());
        // the belt wore a use a rock
        Assert.Equal(1 - 4 / 4000.0, assembly.Wearing.ShareLeft("belt"), 9);

        // a chute laid down into the feed box feeds the infeed
        var (inCell, inFace) = MachineChutes.World(pos, vanner.Side, vanner.Rig.Anchor("infeed")!.Value);
        var chutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-ud", chutePos);
        await World.Ticks(2);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        chute.Inventory[0].Itemstack = CutterItem(feed, 1);
        chute.Inventory[0].MarkDirty();
        await World.Until(() => chute.Inventory.Empty, 20000);

        // worn out, it stops; two plain leather renew the belt
        assembly.Wearing.WearAll(4000);
        Assert.Equal(MachineState.Worn, await MillStateSoon(vanner));
        Assert.Null(CutterClick(player, pos, CutterItem("game:leather-normal-plain", 2)));
        Assert.True(_cutterHandled);
        Assert.True(assembly.Ready);

        // broken: the frame, every stage's items and the belt with use left
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == VannerFrame + "north");
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "game:leather-normal-plain").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:gear-stainless").Sum(d => d.StackSize));
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-copper").Sum(d => d.StackSize));
        foreach (var p in new[] { chestPos, chutePos, heapPos })
            W.BlockAccessor.SetBlock(0, p);
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
