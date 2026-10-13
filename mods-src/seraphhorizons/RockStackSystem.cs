using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod;

/// <summary>
/// Rock and earth stack to 16 (#863, switch <see cref="SeraphHorizonsConfig.SmallerRockStacks"/>,
/// README "Rock and earth stack to 16"): every rock and earth block (<see cref="RockStackRules"/>)
/// stacks to at most 16, and their building shapes (slabs, stairs, fences, paths, roads, roofing)
/// to at most 32. Most of them stack to 64 only because the game's block type defaults to it and
/// their files say nothing, so a JSON patch cannot do it: this sets the size on the loaded blocks
/// instead, in <c>AssetsFinalize</c>, server side. Clients get it in the block type packet, which
/// the server builds after that phase.
///
/// Each block it cuts also carries <see cref="StackBeforeCutAttr"/> (the stack it had before the
/// cut, 64) in its attributes, which reach clients the same way, for what still works by that stack
/// (<see cref="StackBeforeCut"/>): Stone Quarry's drench (<see cref="StoneQuarryDrench"/>). Trading
/// no longer reads it: every item is traded by what the four sell slots hold of it (#910).
///
/// ExecuteOrder 0.15: after MorePiles (0.1), which gives these blocks their ground piles in its own
/// <c>AssetsFinalize</c>; the pile's capacity is not the stack size, so it is unchanged. Recipes are
/// changed later (the schematic gate, ModsAndConfigReady), so they see the new sizes.
/// </summary>
public class RockStackSystem : ModSystem
{
    /// <summary>The block attribute holding a cut block's stack before the cut.</summary>
    public const string StackBeforeCutAttr = "seraphhorizonsStackBeforeCut";

    /// <summary>How many blocks the pass cut to 16 (server side; 0 with the switch off).</summary>
    public int Cut { get; private set; }

    /// <summary>How many building shapes the pass cut to 32 (server side; 0 with the switch off).</summary>
    public int ShapesCut { get; private set; }

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
            if (RockStackRules.CapOf(block.Code.ToString(), block.BlockMaterial.ToString(), block.EntityClass != null) is not int cap
                || block.MaxStackSize <= cap) continue;
            int before = block.MaxStackSize;
            block.MaxStackSize = RockStackRules.Cut(before, cap);
            // A copy: variants of one block type may share their attributes.
            var attributes = block.Attributes?.Token is JObject own ? (JObject)own.DeepClone() : new JObject();
            attributes[StackBeforeCutAttr] = before;
            block.Attributes = new JsonObject(attributes);
            if (cap == RockStackRules.Cap) Cut++;
            else ShapesCut++;
        }
        api.Logger.Notification("[seraphhorizons] Rock and earth stacks: {0} blocks stack to {1}, {2} building shapes to {3}",
            Cut, RockStackRules.Cap, ShapesCut, RockStackRules.ShapeCap);
    }

    /// <summary>A collectible's stack before the cut: a cut rock or earth block's or shape's
    /// <see cref="StackBeforeCutAttr"/>, else its stack size. Both sides: the attribute comes with
    /// the block type.</summary>
    public static int StackBeforeCut(CollectibleObject collectible)
    {
        int max = Math.Max(1, collectible.MaxStackSize);
        return collectible.Attributes?[StackBeforeCutAttr].AsInt(0) is int before && before > max ? before : max;
    }
}
