using Atlas.XUnit;
using SeraphHorizons.Mod.AmalgamPan;
using SeraphHorizons.Mod.AmalgamPan.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, AmalgamPan (mods-src/seraphhorizons/AmalgamPan/, #718): the amalgam pan in the plain
// world. Built by right-clicks with real items, charged from real buckets, worked by right-click held
// (the block's own interaction calls, as the game makes them) and then finished through
// BEAmalgamPan.Work, as the press brake's scenarios finish a plate through Fold; turned from a creative
// rotor below in one scenario. Every scenario builds on a granite floor of its own 40 above spawn at
// x -520 to -560, z -520, and they share the gear cutter's player, gearcutterhand.
public partial class SharedWorldScenarios
{
    private AmalgamPanSystem PanMod => AmalgamPanSystem.Of(World.Api);
    private const string PanFrame = "seraphhorizons:amalgampan-frame-north";
    private const string GoldQuartz = "seraphhorizons:crushedore-quartz_nativegold-medium";

    private async Task<BEAmalgamPan> PlacePan(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:amalgampan-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BEAmalgamPan ?? throw new Xunit.Sdk.XunitException($"no amalgam pan at {pos}");
    }

    private ItemStack PanBucket(string liquid, int portions)
    {
        var bucket = Assert.IsAssignableFrom<BlockLiquidContainerBase>(W.GetBlock(new AssetLocation("game:woodbucket")));
        var stack = new ItemStack(bucket);
        bucket.SetContent(stack, CutterItem(liquid, portions));
        return stack;
    }

    private static int BucketPortions(ItemStack? bucket) =>
        bucket?.Collectible is BlockLiquidContainerBase c ? c.GetContent(bucket)?.StackSize ?? 0 : -1;

    private static string PanInfo(BEAmalgamPan pan, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        pan.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private async Task<(bool Started, int Steps)> PanHold(IPlayer player, BlockPos at, int ticks)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        if (!W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel))
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

    private void BuildPan(BEAmalgamPan pan, IPlayer player, string rod = "game:rod-iron", string plate = "game:metalplate-iron")
    {
        Assert.Null(CutterClick(player, pan.Pos, CutterItem(rod)));
        Assert.Null(CutterClick(player, pan.Pos, CutterItem(plate)));
        Assert.True(pan.Complete);
    }

    [AtlasScenario]
    public void Amalgam_pan_loads_with_its_block_build_and_recipe()
    {
        Assert.True(AmalgamPanSystem.Applies(World.Api));
        Assert.NotNull(PanMod.Rig);
        var build = Assert.IsType<SeraphHorizons.Mod.Machines.Core.MachineBuild>(PanMod.Build);
        Assert.Equal(["muller", "shoes"], build.Stages.Select(s => s.Id));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockAmalgamPan>(W.GetBlock(new AssetLocation($"seraphhorizons:amalgampan-frame-{side}")));
        Assert.Equal("Amalgam pan frame", new ItemStack(W.GetBlock(AmalgamPanSystem.FrameCode)).GetName());
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == PanFrame && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("steel"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 2);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:plank-oak");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // what it works and makes exist: gold and silver quartz crushed, their amalgam, the mercury
        Assert.True(W.GetItem(new AssetLocation(GoldQuartz)) is { IsMissing: false });
        Assert.True(W.GetItem(new AssetLocation(OreProducts.AmalgamCode("quartz_nativesilver"))) is { IsMissing: false });
        Assert.True(W.GetItem(new AssetLocation(BEAmalgamPan.MercuryCode)) is { IsMissing: false });
        // the export mark on the frame: a charge per free ore and grade
        var mark = W.GetBlock(AmalgamPanSystem.FrameCode)!.Attributes?[AmalgamPanSystem.ExportKey];
        Assert.Equal(8, mark?["charges"].AsArray()?.Length);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Amalgam_pan_is_built_in_order_and_takes_nothing_before()
    {
        var pos = await CutterSite(-520, -520);
        var player = await CutterPlayer();
        var pan = await PlacePan(pos, "east");
        Assert.Equal(SeraphHorizons.Mod.Machines.Core.Side.East, pan.Side);
        Assert.Contains("Next part: muller", PanInfo(pan, player));
        // the shoes before the muller, a copper rod, mercury and quartz on a bare frame are all refused
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalplate-iron"))?.StackSize);
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper"))?.StackSize);
        Assert.Equal(200, BucketPortions(CutterClick(player, pos, PanBucket(BEAmalgamPan.MercuryCode, 200))));
        Assert.Equal(4, CutterClick(player, pos, CutterItem(GoldQuartz, 4))?.StackSize);
        Assert.Equal(0, pan.Batch.Mercury);
        // the muller from a stack of two, then the shoes in steel
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-steel", 2))?.StackSize);
        Assert.Contains("Next part: shoes", PanInfo(pan, player));
        Assert.False(pan.Complete);
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-steel")));
        Assert.True(pan.Complete);
        Assert.Equal("steel", pan.ShoeMetal);
        Assert.Equal("Amalgam pan", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        // a save keeps the build; breaking returns it with the frame
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        pan.ToTreeAttributes(tree);
        pan.FromTreeAttributes(tree, W);
        Assert.True(pan.Complete);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        var near = CutterItemsNear(pos);
        Assert.Equal(1, near.GetValueOrDefault(PanFrame));
        Assert.Equal(1, near.GetValueOrDefault("game:rod-steel"));
        Assert.Equal(1, near.GetValueOrDefault("game:metalplate-steel"));
        CutterKillItems(pos);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Amalgam_pan_works_a_charge_by_hand_into_amalgam_and_amalgamated_quartz()
    {
        var pos = await CutterSite(-535, -520);
        var player = await CutterPlayer();
        var pan = await PlacePan(pos);
        BuildPan(pan, player);
        var recovery = OreProcessingSystem.Of(World.Api).Recovery!;

        // mercury from a bucket, the pan taking what fits
        var left = CutterClick(player, pos, PanBucket(BEAmalgamPan.MercuryCode, 1000));
        Assert.Equal(1000 - pan.Config.MercuryCapacityPortions, BucketPortions(left));
        Assert.Equal(pan.Config.MercuryCapacityPortions, pan.Batch.Mercury);
        Assert.True(pan.Batch.Shows(AmalgamPanRequires.Mercury));
        // quartz with no free metal, and amalgamated quartz, stay in hand
        Assert.Equal(4, CutterClick(player, pos, CutterItem("seraphhorizons:crushedore-hematite-medium", 4))?.StackSize);
        var done = CutterItem(GoldQuartz, 2);
        OreStreamAttributes.AddState(done, OreState.Amalgamated);
        Assert.Equal(2, CutterClick(player, pos, done)?.StackSize);
        // a stack and a half of gold quartz: a charge takes 16
        Assert.Equal(8, CutterClick(player, pos, CutterItem(GoldQuartz, 24))?.StackSize);
        Assert.Equal(16, pan.Batch.Items);
        // with no water it does not work
        var (started, _) = await PanHold(player, pos, 4);
        Assert.False(started);
        Assert.Contains("Water: 0 of 4 litres", PanInfo(pan, player));
        // its water from a bucket of 10 litres: 4 go in
        left = CutterClick(player, pos, PanBucket("game:waterportion", 1000));
        Assert.Equal(600, BucketPortions(left));
        Assert.Equal(4, pan.Batch.Water, 3);
        Assert.True(pan.Batch.Shows(AmalgamPanRequires.Water));

        // held, the muller turns the charge
        (started, int steps) = await PanHold(player, pos, 20);
        Assert.True(started);
        Assert.True(steps >= 15, $"held for only {steps} steps");
        Assert.True(pan.Batch.Turns > 0.3, $"turned {pan.Batch.Turns}");
        double turnsAfterHold = pan.Batch.Turns;
        await World.Ticks(10);
        Assert.Equal(turnsAfterHold, pan.Batch.Turns, 6);
        Assert.Equal(PanRefusal.Busy, pan.Batch.CanLoad(true, true, false, true, pan.Config));

        // finished: the amalgam its recovery says, the mercury it took, the quartz amalgamated
        var stream = OreStreamAttributes.Read(CutterItem(GoldQuartz))!;
        var pass = recovery.Amalgamate(HandStation.AmalgamPan, stream);
        int expected = (int)Math.Floor(pass.Amalgam * 16 / recovery.ConcentrateUnits + 1e-9);
        int made = pan.Work(16);
        Assert.Equal(expected, made);
        Assert.True(made > 0);
        Assert.True(pan.Batch.Worked);
        Assert.Equal(pan.Config.MercuryCapacityPortions - made * (int)recovery.Retort.AmalgamMercury, pan.Batch.Mercury);
        Assert.Equal(0, pan.Batch.Water);
        Assert.True(pan.Batch.Shows(AmalgamPanRequires.Amalgam));
        // taken out with an empty hand
        CutterClick(player, pos, null);
        Assert.True(_cutterHandled);
        Assert.Equal(made, CutterHeldCount(player, OreProducts.AmalgamCode("quartz_nativegold")));
        var quartz = PanSlots(player)
            .First(s => s.Itemstack?.Collectible.Code.ToString() == GoldQuartz && s.StackSize == 16).Itemstack!;
        Assert.True(OreStreamAttributes.StateOf(quartz).HasFlag(OreState.Amalgamated));
        Assert.Equal(pass.Rest.Units, OreStreamAttributes.Read(quartz)!.Units, 6);
        Assert.False(pan.Batch.Worked);
        Assert.Equal(0, pan.Batch.Items);
        // and the rest of the mercury comes back into an empty bucket
        var empty = CutterClick(player, pos, new ItemStack(W.GetBlock(new AssetLocation("game:woodbucket"))));
        Assert.Equal(pan.Config.MercuryCapacityPortions - made * (int)recovery.Retort.AmalgamMercury, BucketPortions(empty));
        Assert.Equal(0, pan.Batch.Mercury);
    }

    private static IEnumerable<ItemSlot> PanSlots(IPlayer player) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is Vintagestory.API.Config.GlobalConstants.hotBarInvClassName or Vintagestory.API.Config.GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv);

    private static int CutterHeldCount(IPlayer player, string code) => PanSlots(player).Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Amalgam_pan_turns_from_an_axle_below_with_no_player()
    {
        var pos = await CutterSite(-550, -520);
        var player = await CutterPlayer();
        var pan = await PlacePan(pos);
        BuildPan(pan, player);
        CutterClick(player, pos, PanBucket(BEAmalgamPan.MercuryCode, 500));
        Assert.Null(CutterClick(player, pos, CutterItem(GoldQuartz, 4)));
        CutterClick(player, pos, PanBucket("game:waterportion", 1000));
        Assert.Equal(PanRefusal.None, pan.CanWork());

        // a creative rotor turning an angled gear under the pan, which turns up into its down face
        var gearPos = pos.DownCopy();
        var rotorPos = gearPos.NorthCopy();
        World.SetBlock("game:angledgears-nu", gearPos);
        await World.Ticks(2);
        World.SetBlock("game:creativerotor-north", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 5);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => pan.Powered, 15000);
        await World.Until(() => pan.Batch.Turns > 0.5 || pan.Batch.Worked, 30000);
        Assert.True(pan.Batch.Turns > 0.5 || pan.Batch.Worked);
        W.BlockAccessor.SetBlock(0, rotorPos);
        W.BlockAccessor.SetBlock(0, gearPos);
        CutterKillItems(pos);
    }
}
