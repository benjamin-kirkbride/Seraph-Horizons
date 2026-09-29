using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// Server-side copy of the client rule that decides which collectibles get a handbook page
/// (CollectibleObject.GetHandBookStacks, called for every collectible by the survival mod's
/// ModSystemSurvivalHandbook). See docs/recipe-browser/item-data.md for what it misses.
/// </summary>
internal static class HandbookRule
{
    /// <summary>
    /// The stacks that get a page: the collectible itself, or its creative inventory stacks
    /// (which may carry attributes, or be of other collectibles) when it lists those.
    /// </summary>
    public static IEnumerable<ItemStack> PagesFor(CollectibleObject c)
    {
        if (c.Code == null) yield break;
        var handbook = c.Attributes?["handbook"];
        // "excludeByType" is resolved into "exclude" per variant when the type is loaded.
        if (handbook != null && handbook["exclude"].AsBool()) yield break;

        var hasTabs = c.CreativeInventoryTabs is { Length: > 0 };
        var hasStacks = c.CreativeInventoryStacks is { Length: > 0 };
        if (!hasTabs && !hasStacks && handbook?["include"].AsBool() != true) yield break;

        if (hasStacks && (handbook == null || !handbook["ignoreCreativeInvStacks"].AsBool()))
        {
            foreach (var list in c.CreativeInventoryStacks!)
            foreach (var js in list.Stacks ?? [])
            {
                var stack = js?.ResolvedItemstack;
                if (stack?.Collectible?.Code != null) yield return stack;
            }
        }
        else
        {
            yield return new ItemStack(c);
        }
    }

    /// <summary>True when the class replaces the engine rule with its own code.</summary>
    public static bool OverridesRule(CollectibleObject c)
    {
        var method = c.GetType().GetMethod(nameof(CollectibleObject.GetHandBookStacks));
        var declaring = method?.DeclaringType;
        return declaring != null && declaring != typeof(CollectibleObject) &&
               declaring != typeof(Block) && declaring != typeof(Item);
    }
}
