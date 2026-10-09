using Atlas.XUnit;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.PicklingTub;
using SeraphHorizons.Mod.PicklingTub.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;
using C = SeraphHorizons.Mod.PicklingTub.Core.PicklingTubConfig;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/PicklingTub: the pickling tub (#476), which pickles degreased gears and
/// passivates pickled ones in nitric acid, with the default <c>PicklingTubSettings</c>. A player pours liquids in and gears in and takes them out by
/// right-clicks, through the block's <c>OnBlockInteractStart</c> as the game calls it. The clock is
/// the world's, which a scenario must not move, so a batch is aged by moving its start back
/// (<see cref="BEPicklingTub.Batch"/>): the tub reads its batch from the time alone.
/// <para>Its own class on the plain world rather than a part of <see cref="SharedWorldScenarios"/>:
/// it joins seven players, and the shared world already has fourteen of the server's sixteen.</para>
/// </summary>
[AtlasWorld]
public class PicklingTubScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const string Tub = "seraphhorizons:picklingtub";
    private const string LargeGear = GearCodes.LargeStainless;
    private IWorldAccessor W => World.Api.World;

    private sealed class Site(PicklingTubScenarios s, BlockPos pos, IPlayer player)
    {
        public BlockPos Pos { get; } = pos;
        public IPlayer Player { get; } = player;
        private IWorldAccessor W => s.W;

        public BEPicklingTub Tub => Assert.IsType<BEPicklingTub>(W.BlockAccessor.GetBlockEntity(Pos));

        public ItemSlot Hand => Player.InventoryManager.ActiveHotbarSlot;

        public ItemStack Stack(string code, int size = 1) =>
            W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
            : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

        public ItemStack BucketOf(string liquid, int items)
        {
            var bucket = Stack("game:woodbucket");
            ((BlockLiquidContainerBase)bucket.Block).SetContent(bucket, Stack(liquid, items));
            return bucket;
        }

        public bool RightClick(ItemStack? held)
        {
            Hand.Itemstack = held;
            Hand.MarkDirty();
            var sel = new BlockSelection { Position = Pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.8, 0.5) };
            return W.BlockAccessor.GetBlock(Pos).OnBlockInteractStart(W, Player, sel);
        }

        public string Info()
        {
            var sb = new System.Text.StringBuilder();
            Tub.GetBlockInfo(Player, sb);
            return sb.ToString();
        }

        /// <summary>Moves the batch's start <paramref name="hours"/> back, as if that much time had passed.</summary>
        public void Age(double hours)
        {
            var batch = Tub.Batch!;
            Tub.Batch = batch with { StartHours = batch.StartHours!.Value - hours };
        }

        private IEnumerable<ItemSlot> Slots() =>
            Player.InventoryManager.Inventories.Values
                .Where(i => i.ClassName is GlobalConstants_HotBar or GlobalConstants_Backpack)
                .SelectMany(i => i);

        private const string GlobalConstants_HotBar = "hotbar";
        private const string GlobalConstants_Backpack = "backpack";

        public int Count(string code) =>
            Slots().Where(slot => slot.Itemstack?.Collectible?.Code?.ToString() == code).Sum(slot => slot.StackSize);

        public void EmptyInventory()
        {
            foreach (var slot in Slots())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        /// <summary>Takes the batch out with an empty hand into an empty inventory.</summary>
        public void TakeOut()
        {
            EmptyInventory();
            Assert.True(RightClick(null));
            Assert.Null(Tub.Batch);
        }
    }

    private async Task<Site> Build(string player, int dx)
    {
        var p = await World.JoinPlayer(player);
        var pos = World.Spawn.AddCopy(dx, 20, 60);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(pos) != null, 30000);
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        {
            World.SetBlock("game:rock-granite", pos.AddCopy(x, -1, z));
            for (int y = 0; y < 3; y++)
                World.SetBlock("game:air", pos.AddCopy(x, y, z));
        }
        World.SetBlock(Tub, pos);
        await World.Ticks(2);
        var site = new Site(this, pos, p.Player);
        site.EmptyInventory();
        return site;
    }

    private static TubRuleConfig RuleFor(BEPicklingTub tub) => tub.Rule ?? throw new Xunit.Sdk.XunitException("the batch has no rule");

    [AtlasScenario]
    public void The_tub_and_its_recipe_exist_and_brine_and_the_bare_gear_do_not()
    {
        Assert.True(W.GetBlock(new AssetLocation(Tub)) is { Id: > 0 }, "no tub");
        var recipe = Assert.Single(W.GridRecipes, r => r.Output.Code?.ToString() == Tub && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients, i => i?.Code?.ToString() == "immersivewoodworking:barktar" && i.Quantity == 2);
        // The liquids and items the default rules name exist in the pack (nitric acid by Expanded Matter).
        foreach (var code in new[] { C.Vinegar, C.Sulfuric, C.Hydrochloric, C.Nitric, C.Bits, C.Degreased, C.Pickled, C.Passivated })
            Assert.True(W.GetItem(new AssetLocation(code)) != null, $"no {code}");
        Assert.Empty(PicklingTubSystem.Of(World.Api).Config.Sanitise());
        // Stainless does not rust: no brine bath and no bare steel gear (#484's stainless rework).
        Assert.False(PicklingTubSystem.Of(World.Api).Rules.IsLiquid("game:brineportion"));
        Assert.Null(W.GetItem(new AssetLocation("seraphhorizons:gear-steel-bare")));
    }

    [AtlasScenario]
    public async Task Each_acid_pickles_degreased_gears_and_early_take_out_gives_them_back()
    {
        int dx = 0;
        foreach (var (acid, hours) in new[] { (C.Vinegar, 24.0), (C.Sulfuric, 8.0), (C.Hydrochloric, 2.0) })
        {
            var site = await Build("pickler" + dx, 300 + dx);
            dx += 6;
            // A full bucket fills the tub to its 10 litres; what is left stays in the bucket.
            Assert.True(site.RightClick(site.BucketOf(acid, 1500)));
            Assert.Equal(10, site.Tub.FreeLitres, 3);
            var bucket = site.Hand.Itemstack!;
            Assert.Equal(5, ((BlockLiquidContainerBase)bucket.Block).GetCurrentLitres(bucket), 3);
            Assert.Contains("10 of 10 litres", site.Info());

            // Eight degreased gears go in from a stack of ten: the batch draws its litre and starts.
            Assert.True(site.RightClick(site.Stack(C.Degreased, 10)));
            Assert.Equal(2, site.Hand.StackSize);
            var tub = site.Tub;
            Assert.Equal(8, tub.Batch!.Count);
            Assert.True(tub.Batch.Started);
            Assert.Equal(hours, RuleFor(tub).Hours);
            Assert.Equal(9, tub.FreeLitres, 3);
            Assert.Equal(10, tub.TotalLitres, 3);
            Assert.Contains("pickling", site.Info());
            // Full: no more go in.
            Assert.True(site.RightClick(site.Stack(C.Degreased, 1)));
            Assert.Equal(1, site.Hand.StackSize);

            // Early: the degreased gears back, and the litre back into the tub.
            site.Age(hours * 0.99);
            Assert.Equal(TubPhase.Soaking, tub.Stage!.Value.Phase);
            site.TakeOut();
            Assert.Equal(8, site.Count(C.Degreased));
            Assert.Equal(0, site.Count(C.Pickled));
            Assert.Equal(10, tub.FreeLitres, 3);

            // Again, to done: the metal looks grey and clean, and eight pickled gears come out.
            Assert.True(site.RightClick(site.Stack(C.Degreased, 8)));
            site.Age(hours + 0.01);
            await World.Ticks(1);
            Assert.Equal(TubPhase.Done, tub.Stage!.Value.Phase);
            Assert.Contains("grey and clean", site.Info());
            site.TakeOut();
            Assert.Equal(8, site.Count(C.Pickled));
            Assert.Equal(0, site.Count(C.Degreased));
            Assert.Equal(9, tub.FreeLitres, 3);
            output.WriteLine($"{acid}: {hours} h, a litre used");
        }
    }

    [AtlasScenario]
    public async Task Left_too_long_the_acid_eats_the_batch_to_stainless_bits_a_gear_at_a_time()
    {
        var site = await Build("overpickler", 330);
        Assert.True(site.RightClick(site.BucketOf(C.Sulfuric, 500)));
        Assert.True(site.RightClick(site.Stack(C.Degreased, 8)));
        var tub = site.Tub;
        var rule = RuleFor(tub); // 8 h, grace 4 h, a gear an hour
        site.Age(rule.Hours + rule.GraceHours + 3);
        Assert.Equal(new TubStage(TubPhase.Eating, 1, 0, 4, 4), tub.Stage);
        Assert.Contains("eating", site.Info());
        site.TakeOut();
        Assert.Equal(4, site.Count(C.Pickled));
        Assert.Equal(4, site.Count(C.Bits));

        Assert.True(site.RightClick(site.Stack(C.Degreased, 8)));
        site.Age(100);
        Assert.Equal(TubPhase.Dissolved, tub.Stage!.Value.Phase);
        Assert.Contains("Nothing left but stainless bits", site.Info());
        site.TakeOut();
        Assert.Equal(0, site.Count(C.Pickled));
        Assert.Equal(8, site.Count(C.Bits));
        Assert.Equal(3, tub.FreeLitres, 3);
    }

    [AtlasScenario]
    public async Task Nitric_acid_passivates_pickled_gears_and_eats_them_left_too_long()
    {
        var site = await Build("passivator", 340);
        Assert.True(site.RightClick(site.BucketOf(C.Nitric, 1000)));
        var tub = site.Tub;
        Assert.Equal(10, tub.FreeLitres, 3);

        // Degreased gears are not for nitric acid: they are pickled first.
        Assert.True(site.RightClick(site.Stack(C.Degreased, 4)));
        Assert.Equal(4, site.Hand.StackSize);
        Assert.Null(tub.Batch);

        Assert.True(site.RightClick(site.Stack(C.Pickled, 8)));
        var rule = RuleFor(tub);
        Assert.Equal(C.Passivated, rule.Output);
        Assert.Equal(TubRuleKind.Passivate, rule.Kind);
        Assert.Equal(6, rule.Hours);
        Assert.Equal(3, rule.GraceHours);
        Assert.Equal(1, rule.LossEveryHours);
        Assert.Equal(C.Bits, rule.Failure);
        site.Age(3);
        Assert.Contains("passivating: 50%", site.Info());
        site.Age(3);
        Assert.Equal(TubPhase.Done, tub.Stage!.Value.Phase);
        Assert.Contains("gone passive", site.Info());
        site.TakeOut();
        Assert.Equal(8, site.Count(C.Passivated));
        Assert.Equal(9, tub.FreeLitres, 3);

        // Past done and the grace, a gear an hour goes to a stainless bit: 2 hours on, three are gone.
        Assert.True(site.RightClick(site.Stack(C.Pickled, 8)));
        site.Age(rule.Hours + rule.GraceHours + 2);
        Assert.Equal(new TubStage(TubPhase.Eating, 1, 0, 5, 3), tub.Stage);
        Assert.Contains("gone to stainless bits", site.Info());
        site.TakeOut();
        Assert.Equal(5, site.Count(C.Passivated));
        Assert.Equal(3, site.Count(C.Bits));
    }

    [AtlasScenario]
    public async Task The_tub_refuses_large_gears_wrong_liquids_and_mixed_batches()
    {
        var site = await Build("refuser", 370);
        // A large stainless gear: refused with a word, the click is the tub's, the gear stays in hand.
        Assert.True(site.RightClick(site.Stack(LargeGear, 1)));
        Assert.Equal(1, site.Hand.StackSize);
        Assert.Null(site.Tub.Batch);

        // Degreased gears wait without liquid, and nitric acid does nothing for them.
        Assert.True(site.RightClick(site.Stack(C.Degreased, 4)));
        Assert.False(site.Tub.Batch!.Started);
        Assert.Contains("waiting", site.Info());
        Assert.True(site.RightClick(site.BucketOf(C.Nitric, 500)));
        Assert.Equal(0, site.Tub.TotalLitres);
        Assert.Equal(5, ((BlockLiquidContainerBase)site.Hand.Itemstack!.Block).GetCurrentLitres(site.Hand.Itemstack), 3);

        // Vinegar starts them; pickled gears are another batch; nitric acid on vinegar is refused.
        Assert.True(site.RightClick(site.BucketOf(C.Vinegar, 300)));
        Assert.True(site.Tub.Batch!.Started);
        Assert.Equal(2, site.Tub.FreeLitres, 3);
        Assert.True(site.RightClick(site.Stack(C.Pickled, 2)));
        Assert.Equal(2, site.Hand.StackSize);
        Assert.Equal(4, site.Tub.Batch!.Count);
        Assert.True(site.RightClick(site.BucketOf(C.Nitric, 100)));
        Assert.Equal(2, site.Tub.FreeLitres, 3);

        // Water, brine and other liquids are not the tub's at all; nor is a rusty gear or a stainless one.
        Assert.False(site.RightClick(site.BucketOf("game:waterportion", 100)));
        Assert.False(site.RightClick(site.BucketOf("game:brineportion", 100)));
        Assert.False(site.RightClick(site.Stack(GearCodes.Rusty, 3)));
        Assert.False(site.RightClick(site.Stack(GearCodes.Stainless, 3)));

        // More degreased gears start the batch's clock again.
        site.Age(10);
        Assert.True(site.RightClick(site.Stack(C.Degreased, 2)));
        Assert.Equal(6, site.Tub.Batch!.Count);
        Assert.True(site.Tub.Stage!.Value.Progress < 0.01);

        // An empty bucket takes the free liquid back out; the batch keeps its litre.
        Assert.True(site.RightClick(site.Stack("game:woodbucket")));
        Assert.Equal(0, site.Tub.FreeLitres, 3);
        Assert.Equal(1, site.Tub.TotalLitres, 3);
        var bucket = site.Hand.Itemstack!;
        Assert.Equal(2, ((BlockLiquidContainerBase)bucket.Block).GetCurrentLitres(bucket), 3);
        Assert.Equal(C.Vinegar, ((BlockLiquidContainerBase)bucket.Block).GetContent(bucket)!.Collectible.Code.ToString());
    }

    [AtlasScenario]
    public async Task A_running_batch_is_saved_with_its_rule_and_read_back()
    {
        var site = await Build("tubsaver", 380);
        Assert.True(site.RightClick(site.BucketOf(C.Nitric, 300)));
        Assert.True(site.RightClick(site.Stack(C.Pickled, 5)));
        site.Age(1);
        var tree = new TreeAttribute();
        site.Tub.ToTreeAttributes(tree);
        var before = site.Tub.Stage;
        site.Tub.FromTreeAttributes(tree, W);
        Assert.Equal(5, site.Tub.Batch!.Count);
        Assert.Equal(C.Nitric, site.Tub.Batch.Liquid);
        Assert.Equal(C.Passivated, RuleFor(site.Tub).Output);
        Assert.Equal(6, RuleFor(site.Tub).Hours);
        Assert.Equal(TubRuleKind.Passivate, RuleFor(site.Tub).Kind);
        Assert.Equal(before!.Value.Phase, site.Tub.Stage!.Value.Phase);
        Assert.Equal(2, site.Tub.FreeLitres, 3);
        Assert.Equal(C.Nitric, site.Tub.Liquid!.Collectible.Code.ToString());

        // Broken, the tub drops its batch as it is.
        W.BlockAccessor.BreakBlock(site.Pos, site.Player);
        await World.Ticks(2);
        int dropped = W.GetEntitiesAround(site.Pos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3,
                e => e is EntityItem item && item.Itemstack?.Collectible?.Code?.ToString() == C.Pickled)
            .Sum(e => ((EntityItem)e).Itemstack.StackSize);
        Assert.Equal(5, dropped);
    }
}
