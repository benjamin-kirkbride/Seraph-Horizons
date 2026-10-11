using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Rock and earth stack to 16 (#863, <see cref="RockStackSystem"/>): rock, earth and ore-bearing
/// rock blocks stack to 16 and their building shapes to 32, each carrying the 64 it stacked to
/// before to trade by, in the block type packet clients get too; support beams, wooden shapes,
/// every other block and the rock items keep their stacks. The switch off is in
/// <see cref="SwitchesOffScenarios"/>.
/// </summary>
public partial class SharedWorldScenarios
{
    private Block RockStackBlock(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } b ? b : throw new Xunit.Sdk.XunitException($"no {code}");

    /// <summary>The block stacks to <paramref name="stack"/>, trades by 64, and a client gets both.</summary>
    private void AssertRockStack(string code, int stack)
    {
        var block = RockStackBlock(code);
        Assert.True(stack == block.MaxStackSize, $"{code} stacks to {block.MaxStackSize}");
        Assert.Equal(64, RockStackSystem.TradeStack(block));
        // What a client gets: the block type packet carries the stack and the trade stack.
        var packet = BlockTypeNet.GetBlockTypePacket(block, World.Api.ClassRegistry);
        Assert.Equal(stack, packet.MaxStackSize);
        Assert.Equal(64, JToken.Parse(packet.Attributes)[RockStackSystem.TradeStackAttr]?.Value<int>());
    }

    [AtlasScenario]
    public void Rock_and_earth_stack_to_16_and_trade_by_their_old_stack()
    {
        foreach (var code in new[]
                 {
                     "game:rock-granite", "game:cobblestone-granite", "game:stonebricks-granite", "game:ore-medium-nativecopper-granite",
                     "game:sand-granite", "game:gravel-granite", "game:muddygravel", "game:soil-medium-none", "game:rawclay-blue-none", "game:peat-none",
                 })
            AssertRockStack(code, RockStackRules.Cap);
        Assert.True(RockStackSystem.Of(World.Api)!.Cut > 1000, $"only {RockStackSystem.Of(World.Api)!.Cut} blocks cut");
    }

    [AtlasScenario]
    public void Rock_and_earth_slabs_stairs_roads_and_roofing_stack_to_32()
    {
        foreach (var code in new[]
                 {
                     "game:cobblestoneslab-granite-north-free", "game:stonebrickstairs-granite-up-north-free",
                     "game:drystonefence-mixed-empty-snow", "moreroads:ancientroad-basic-granite-free",
                     "game:slantedroofing-blackclay-north-free", "game:slantedroofing-slate-north-free",
                 })
            AssertRockStack(code, RockStackRules.ShapeCap);
        Assert.True(RockStackSystem.Of(World.Api)!.ShapesCut > 1000, $"only {RockStackSystem.Of(World.Api)!.ShapesCut} shapes cut");
    }

    /// <summary>Muddy gravel stacks to 16, and Stone Quarry's rubble storage still makes 16 of it
    /// from a bucket of water (<see cref="StoneQuarryDrench"/>), not a quarter of 16. At (70, 12, -70).</summary>
    [AtlasScenario]
    public async Task A_bucket_of_water_on_rubble_storage_still_makes_16_muddy_gravel()
    {
        Assert.Equal(RockStackRules.Cap, RockStackBlock("game:muddygravel").MaxStackSize);
        Assert.True(StoneQuarryDrench.Patched, "TryDrench is not patched");

        var player = (await World.JoinPlayer("drencher")).Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var pos = World.Spawn.AddCopy(70, 12, -70);
        World.SetBlock("stonequarry:rubblestorage-north", pos);
        await World.Ticks(2);
        player.Entity.TeleportTo(pos.ToVec3d().Add(0.5, 0, 2.5));
        var be = W.BlockAccessor.GetBlockEntity(pos) ?? throw new Xunit.Sdk.XunitException($"no rubble storage entity: {W.BlockAccessor.GetBlock(pos).Code} at {pos}");
        var inventory = (InventoryBase)be.GetType().GetProperty("Inventory")!.GetValue(be)!;
        var gravelSlot = inventory[1];
        gravelSlot.Itemstack = new ItemStack(RockStackBlock("game:gravel-granite"), 40);

        var bucket = (BlockLiquidContainerBase)RockStackBlock("game:woodbucket");
        var hand = player.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = new ItemStack(bucket);
        bucket.SetContent(hand.Itemstack, new ItemStack(W.GetItem(new AssetLocation("game:waterportion")), 1000));
        try
        {
            var sel = new BlockSelection { Position = pos, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
            Assert.True((bool)StoneQuarryDrench.Target!.Invoke(be, [W, sel, player])!);
            Assert.Equal(40 - 16, gravelSlot.StackSize);
            await World.Ticks(2);
            int made = W.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 4, 4)
                .OfType<EntityItem>().Where(e => e.Itemstack?.Collectible.Code.ToString() == "game:muddygravel")
                .Sum(e => e.Itemstack.StackSize);
            Assert.Equal(16, made);
        }
        finally
        {
            hand.Itemstack = null;
            foreach (var e in W.GetEntitiesAround(pos.ToVec3d(), 6, 6).OfType<EntityItem>()) e.Die(EnumDespawnReason.Removed);
            World.SetBlock("game:air", pos);
        }
    }

    [AtlasScenario]
    public void Support_beams_wooden_shapes_other_blocks_and_rock_items_keep_their_stacks()
    {
        foreach (var (code, stack) in new[]
                 {
                     ("game:supportbeam-oak", 32), ("game:torch-basic-lit-up", 64),
                     ("game:claybricks-good-fire", 64), ("game:plankslab-oak-north-free", 64), ("game:plankstairs-oak-up-north-free", 64),
                     ("game:woodenfence-oak-empty-free", 64), ("game:slantedroofing-oak-north-free", 64),
                     ("game:slantedroofing-thatch-north-free", 64), ("moreroads:edgewood-oak-straight-north-free", 64),
                 })
        {
            var block = RockStackBlock(code);
            Assert.True(stack == block.MaxStackSize, $"{code} stacks to {block.MaxStackSize}");
            Assert.Equal(stack, RockStackSystem.TradeStack(block));
        }
        foreach (var code in new[] { "game:stone-granite", "game:clay-blue", "game:burnedbrick-fire" })
        {
            var item = W.GetItem(new AssetLocation(code));
            Assert.True(item is { Id: > 0, MaxStackSize: 64 }, $"{code} stacks to {item?.MaxStackSize}");
        }
    }
}
