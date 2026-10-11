using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod;

/// <summary>
/// Rock and earth stack to 16 (#863, switch <see cref="SeraphHorizonsConfig.SmallerRockStacks"/>,
/// README "Rock and earth stack to 16"): every rock and earth block (<see cref="RockStackRules"/>)
/// stacks to at most 16. Most of them stack to 64 only because the game's block type defaults to it
/// and their files say nothing, so a JSON patch cannot do it: this sets the size on the loaded
/// blocks instead, in <c>AssetsFinalize</c>, server side. Clients get it in the block type packet,
/// which the server builds after that phase.
///
/// Each block it cuts also carries <see cref="TradeStackAttr"/> (four times its new stack, what the
/// trade window's four sell slots hold of it) in its attributes, which reach clients the same way:
/// the economy judges whether the block is worth trading by that stack, not by one slot
/// (<see cref="TradeStack"/>), and the recipe export carries it to the item value table.
///
/// ExecuteOrder 0.15: after MorePiles (0.1), which gives these blocks their ground piles in its own
/// <c>AssetsFinalize</c>; the pile's capacity is not the stack size, so it is unchanged. Recipes are
/// changed later (the schematic gate, ModsAndConfigReady), so they see the new sizes.
/// </summary>
public class RockStackSystem : ModSystem
{
    /// <summary>The block attribute holding the stack trading judges a cut block by.</summary>
    public const string TradeStackAttr = "seraphhorizonsTradeStack";

    /// <summary>How many blocks the pass cut (server side; 0 with the switch off).</summary>
    public int Cut { get; private set; }

    public static RockStackSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<RockStackSystem>();

    public override double ExecuteOrder() => 0.15;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).SmallerRockStacks)
        {
            api.Logger.Notification("[seraphhorizons] Rock and earth stacks: switched off; blocks keep their stacks");
            return;
        }
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code is null || block.Id == 0 || block.MaxStackSize <= RockStackRules.Cap) continue;
            if (!RockStackRules.IsRockOrEarth(block.Code.ToString(), block.BlockMaterial.ToString(), block.EntityClass != null)) continue;
            block.MaxStackSize = RockStackRules.Cut(block.MaxStackSize);
            // A copy: variants of one block type may share their attributes.
            var attributes = block.Attributes?.Token is JObject own ? (JObject)own.DeepClone() : new JObject();
            attributes[TradeStackAttr] = RockStackRules.TradeStack(block.MaxStackSize);
            block.Attributes = new JsonObject(attributes);
            Cut++;
        }
        api.Logger.Notification("[seraphhorizons] Rock and earth stacks: {0} blocks stack to {1}", Cut, RockStackRules.Cap);
    }

    /// <summary>The stack trading judges a collectible's worth by: a cut rock or earth block's
    /// <see cref="TradeStackAttr"/>, else its stack size. Both sides: the attribute comes with the
    /// block type.</summary>
    public static int TradeStack(CollectibleObject collectible)
    {
        int max = Math.Max(1, collectible.MaxStackSize);
        return collectible.Attributes?[TradeStackAttr].AsInt(0) is int trade && trade > max ? trade : max;
    }
}
