using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Clutter does not pile (switch <see cref="SeraphHorizonsConfig.NoClutterPiles"/>, README "Clutter
/// does not pile"): takes away the ground pile More Piles' catch-all cube rule gives the game's
/// clutter and the other blocks drawn from their attributes (<see cref="ClutterPileRules"/>), so
/// they are placed one at a time as the game ships them. Run by <see cref="StorageTweaksSystem"/>
/// after More Piles and before <see cref="BigPiles"/>, on both sides: More Piles runs on a client
/// too and, the server having sent the block with no ground storage, gives it the pile again there.
///
/// A clutter pile already in a world keeps its items; with no pile behavior it holds what it holds
/// and takes no more, and breaking it gives them back.
/// </summary>
public static class ClutterPiles
{
    /// <summary>The creative tab More Piles adds every block it gives a pile to.</summary>
    public const string GroundStorableTab = "groundstorable";

    /// <summary>Takes the piles away on this side. Returns how many blocks lost one.</summary>
    public static int Apply(ICoreAPI api)
    {
        int changed = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code == null || block.CollectibleBehaviors == null) continue;
            var piles = block.CollectibleBehaviors.OfType<CollectibleBehaviorGroundStorable>().ToList();
            if (piles.Count == 0) continue;
            var chain = ClassChain(block.GetType());
            var entityBehaviors = block.BlockEntityBehaviors?.Select(b => b.Name) ?? [];
            var gone = piles.Where(p => ClutterPileRules.TakesNoPile(chain, entityBehaviors, p.propertiesAtString)).ToList();
            if (gone.Count == 0) continue;
            block.CollectibleBehaviors = block.CollectibleBehaviors.Except(gone).ToArray();
            if (!block.HasBehavior<CollectibleBehaviorGroundStorable>()) DropGroundStorableTab(block);
            changed++;
        }
        return changed;
    }

    private static List<string?> ClassChain(Type? type)
    {
        var names = new List<string?>();
        for (; type != null; type = type.BaseType) names.Add(type.Name);
        return names;
    }

    /// <summary>Undoes More Piles' <c>AddToCreativeInventory</c>: the block is not ground storable.</summary>
    private static void DropGroundStorableTab(Block block)
    {
        if (block.CreativeInventoryTabs is { Length: > 0 } tabs)
            block.CreativeInventoryTabs = tabs.Where(t => t != GroundStorableTab).ToArray();
        if (block.CreativeInventoryStacks != null)
            foreach (var list in block.CreativeInventoryStacks)
                if (list?.Tabs != null)
                    list.Tabs = list.Tabs.Where(t => t != GroundStorableTab).ToArray();
    }
}
