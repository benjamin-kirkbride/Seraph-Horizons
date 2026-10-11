using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Rock and earth stack to 16 (#863, <see cref="RockStackSystem"/>): rock, earth and ore-bearing
/// rock blocks stack to 16 and carry the four sell slots' 64 to trade by, in the block type packet
/// clients get too; support beams, every other block and the rock items keep their stacks. The
/// switch off is in <see cref="SwitchesOffScenarios"/>.
/// </summary>
public partial class SharedWorldScenarios
{
    private Block RockStackBlock(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } b ? b : throw new Xunit.Sdk.XunitException($"no {code}");

    [AtlasScenario]
    public void Rock_and_earth_stack_to_16_and_trade_by_the_four_sell_slots()
    {
        foreach (var code in new[]
                 {
                     "game:rock-granite", "game:cobblestone-granite", "game:stonebricks-granite", "game:ore-medium-nativecopper-granite",
                     "game:sand-granite", "game:gravel-granite", "game:soil-medium-none", "game:rawclay-blue-none", "game:peat-none",
                 })
        {
            var block = RockStackBlock(code);
            Assert.True(RockStackRules.Cap == block.MaxStackSize, $"{code} stacks to {block.MaxStackSize}");
            Assert.Equal(RockStackRules.TradeStack(RockStackRules.Cap), RockStackSystem.TradeStack(block));

            // What a client gets: the block type packet carries the stack and the trade stack.
            var packet = BlockTypeNet.GetBlockTypePacket(block, World.Api.ClassRegistry);
            Assert.Equal(RockStackRules.Cap, packet.MaxStackSize);
            Assert.Equal(64, JToken.Parse(packet.Attributes)[RockStackSystem.TradeStackAttr]?.Value<int>());
        }
        Assert.True(RockStackSystem.Of(World.Api)!.Cut > 1000, $"only {RockStackSystem.Of(World.Api)!.Cut} blocks cut");
    }

    [AtlasScenario]
    public void Support_beams_other_blocks_and_rock_items_keep_their_stacks()
    {
        foreach (var (code, stack) in new[]
                 {
                     ("game:supportbeam-oak", 32), ("game:torch-basic-lit-up", 64), ("game:muddygravel", 64),
                     ("game:cobblestoneslab-granite-north-free", 64), ("game:claybricks-good-fire", 64),
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
