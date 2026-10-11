using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
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
                     "game:sand-granite", "game:gravel-granite", "game:soil-medium-none", "game:rawclay-blue-none", "game:peat-none",
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

    [AtlasScenario]
    public void Support_beams_wooden_shapes_other_blocks_and_rock_items_keep_their_stacks()
    {
        foreach (var (code, stack) in new[]
                 {
                     ("game:supportbeam-oak", 32), ("game:torch-basic-lit-up", 64), ("game:muddygravel", 64),
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
