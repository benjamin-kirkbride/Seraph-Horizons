using Atlas.XUnit;
using SeraphHorizons.Mod.CreativeItems;
using SeraphHorizons.Mod.CreativeItems.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, CreativeItemSource: the creative item source feeds the game's hopper
/// under it at its rate, to its limit and not while paused, and nothing else; the creative item
/// sink takes what a chute or hopper pushes into it, from any face, counts it by item and voids
/// it. A line of source, hopper, chute and sink runs on the server's own item flow, and the two
/// counts agree.
/// </summary>
public partial class SharedWorldScenarios
{
    private const string ItemSource = "seraphhorizons:" + CreativeItemBlocks.SourceCode;
    private const string ItemSink = "seraphhorizons:" + CreativeItemBlocks.SinkCode;
    private const string StickKey = "item:game:stick";

    private ItemStack Stick(int size = 1) =>
        new(W.GetItem(new AssetLocation("game:stick")) ?? throw new Xunit.Sdk.XunitException("game:stick is not an item"), size);

    /// <summary>Source over hopper over a vertical chute over sink, at <paramref name="top"/> and
    /// down; returns the source and the sink.</summary>
    private async Task<(BlockEntityCreativeItemSource Source, BlockEntityCreativeItemSink Sink)> BuildItemLine(BlockPos top)
    {
        World.SetBlock(ItemSource, top);
        World.SetBlock("game:hopper", top.DownCopy());
        World.SetBlock("game:chute-straight-ud", top.DownCopy(2));
        World.SetBlock(ItemSink, top.DownCopy(3));
        await World.Ticks(2);
        return (Assert.IsType<BlockEntityCreativeItemSource>(W.BlockAccessor.GetBlockEntity(top)),
            Assert.IsType<BlockEntityCreativeItemSink>(W.BlockAccessor.GetBlockEntity(top.DownCopy(3))));
    }

    [AtlasScenario]
    public void Item_source_and_sink_are_in_the_creative_inventory_only()
    {
        foreach (var code in new[] { ItemSource, ItemSink })
        {
            var block = W.GetBlock(new AssetLocation(code));
            Assert.NotNull(block);
            Assert.IsType<BlockCreativeItems>(block);
            Assert.Contains("general", block.CreativeInventoryTabs);
            Assert.Empty(block.Drops ?? []);
            Assert.True(block.Attributes["handbook"]["exclude"].AsBool());
            Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code == block.Code);
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Item_source_feeds_its_rate_through_a_hopper_and_chute_into_the_sink()
    {
        var (source, sink) = await BuildItemLine(World.Spawn.AddCopy(30, 24, -30));
        var held = Stick(7);
        source.SetItem(held);
        Assert.Equal(7, held.StackSize);   // copied, not taken
        Assert.Equal(1, source.Item!.StackSize);

        using var clock = VirtualClock.Start(World);
        source.Configure(rate: 2, unlimited: false, paused: false, limit: 0);
        long start = W.ElapsedMilliseconds;
        await World.Until(() => W.ElapsedMilliseconds - start >= 10_000, 400);
        // 2 a second for ten seconds, give or take a tick at each end.
        Assert.InRange(source.Feed.Total, 18, 21);
        Assert.InRange(source.PerMinute, 90, 150);

        // Paused: nothing more is given, and what is on its way still arrives.
        source.Configure(rate: 2, unlimited: false, paused: true, limit: 0);
        long given = source.Feed.Total;
        await World.Until(() => sink.Tally.Total == given, 300);
        await World.Ticks(60);
        Assert.Equal(given, source.Feed.Total);
        Assert.Equal(given, sink.Tally.Total);
        Assert.Equal(given, sink.Tally[StickKey]);
        Assert.Equal(1, sink.Tally.Kinds);
        Assert.True(sink.PerMinute > 0);
        // Voided, never stored: the sink's slot is empty to every pusher.
        Assert.True(sink.Inventory[0].Empty);
        Assert.True(source.Item!.Equals(W, Stick(), GlobalConstants.IgnoredStackAttributes));

        source.ResetCounts();
        sink.ResetCounts();
        Assert.Equal(0, source.Feed.Total);
        Assert.Equal(0, sink.Tally.Total);
        Assert.Equal(0, sink.Tally.Kinds);
        Assert.Equal(0, source.PerMinute);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Item_source_unlimited_stops_at_its_limit_and_the_sink_gets_every_item()
    {
        var (source, sink) = await BuildItemLine(World.Spawn.AddCopy(34, 24, -30));
        source.SetItem(Stick());

        using var clock = VirtualClock.Start(World);
        source.Configure(rate: 1, unlimited: true, paused: false, limit: 40);
        await World.Until(() => source.Feed.LimitReached, 60);
        Assert.Equal(40, source.Feed.Total);
        await World.Until(() => sink.Tally.Total == 40, 900);
        await World.Ticks(60);
        Assert.Equal(40, source.Feed.Total);
        Assert.Equal(40, sink.Tally[StickKey]);

        // A reset starts it again, to the same limit.
        source.ResetCounts();
        await World.Until(() => source.Feed.LimitReached, 60);
        Assert.Equal(40, source.Feed.Total);
    }

    [AtlasScenario]
    public async Task Item_source_feeds_only_a_hopper_and_the_sink_takes_from_every_face()
    {
        // Over a chute, not a hopper: nothing is given.
        var top = World.Spawn.AddCopy(38, 24, -30);
        World.SetBlock(ItemSource, top);
        World.SetBlock("game:chute-straight-ud", top.DownCopy());
        World.SetBlock(ItemSink, top.DownCopy(2));
        await World.Ticks(2);
        var source = Assert.IsType<BlockEntityCreativeItemSource>(W.BlockAccessor.GetBlockEntity(top));
        var sink = Assert.IsType<BlockEntityCreativeItemSink>(W.BlockAccessor.GetBlockEntity(top.DownCopy(2)));
        source.SetItem(Stick());
        source.Configure(rate: ItemFeed.MaxRate, unlimited: false, paused: false, limit: 0);
        await World.Ticks(30);
        Assert.Equal(0, source.Feed.Total);
        Assert.True(((BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(top.DownCopy())!).Inventory.Empty);

        // Every face hands a pusher the same empty slot, whatever it holds; nothing pulls.
        foreach (var face in BlockFacing.ALLFACES)
        {
            var from = new DummySlot(Stick(3));
            var slot = sink.Inventory.GetAutoPushIntoSlot(face, from);
            Assert.NotNull(slot);
            Assert.True(slot.Empty);
            var op = new ItemStackMoveOperation(W, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 3);
            Assert.Equal(3, from.TryPutInto(slot, ref op));
            Assert.Null(sink.Inventory.GetAutoPullFromSlot(face));
        }
        sink.Drain();
        Assert.Equal(18, sink.Tally[StickKey]);
    }

    [AtlasScenario]
    public async Task Item_source_settings_are_clamped_and_kept()
    {
        var top = World.Spawn.AddCopy(42, 24, -30);
        World.SetBlock(ItemSource, top);
        await World.Ticks(2);
        var source = Assert.IsType<BlockEntityCreativeItemSource>(W.BlockAccessor.GetBlockEntity(top));
        source.SetItem(Stick());
        source.Configure(rate: 1e9, unlimited: false, paused: true, limit: -5);
        Assert.Equal(ItemFeed.MaxRate, source.Feed.Rate);
        Assert.Equal(0, source.Feed.Limit);

        var tree = new TreeAttribute();
        source.ToTreeAttributes(tree);
        tree.SetDouble("rate", double.NaN);
        tree.SetLong("limit", long.MaxValue);
        tree.SetLong("total", -12);
        source.FromTreeAttributes(tree, W);
        Assert.Equal(ItemFeed.DefaultRate, source.Feed.Rate);
        Assert.Equal(ItemFeed.MaxLimit, source.Feed.Limit);
        Assert.Equal(0, source.Feed.Total);
        Assert.True(source.Feed.Paused);
        Assert.Equal("game:stick", source.Item?.Collectible.Code.ToString());

        source.SetItem(null);
        Assert.Null(source.Item);
    }
}
