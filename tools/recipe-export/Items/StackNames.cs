using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// Gives a recipe's concrete stacks ($defs/stack: each variant's ingredients and outputs, an
/// ingredient's returned stack) the <c>name</c> the game shows for that exact stack, when its
/// attributes make it differ from its item's. A clutter block's identity is its
/// <c>type</c> attribute (BetterRuins' crate schematic makes <c>game:clutter</c> with
/// <c>type: crate/crate-large-empty</c>), and the bare code's name is the untranslated
/// <c>game:clutter-</c>; the stack's own name is "Large crate (Empty)".
/// Part of the recipe section, so a recipe fill alone writes the same records.
/// </summary>
internal static class StackNames
{
    public static void Fill(ICoreServerAPI api, JObject root)
    {
        if (root["recipes"] is not JArray recipes) return;
        using var english = new EnglishLocale();
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        int named = 0;
        foreach (var recipe in recipes.OfType<JObject>())
        {
            foreach (var variant in (recipe["variants"] as JArray ?? new JArray()).OfType<JObject>())
            {
                foreach (var slot in (variant["ingredients"] as JArray ?? new JArray()).OfType<JArray>())
                    foreach (var stack in slot.OfType<JObject>()) named += Name(api, cache, stack);
                foreach (var stack in (variant["outputs"] as JArray ?? new JArray()).OfType<JObject>())
                    named += Name(api, cache, stack);
            }
            foreach (var ingredient in (recipe["ingredients"] as JArray ?? new JArray()).OfType<JObject>())
                if (ingredient["returned"] is JObject returned) named += Name(api, cache, returned);
        }
        api.Logger.Notification("[seraphexport] {0} recipe stacks named by their attributes", named);
    }

    private static int Name(ICoreServerAPI api, Dictionary<string, string?> cache, JObject stack)
    {
        if (stack["name"] != null || stack["attributes"] is not JObject attributes || !attributes.HasValues) return 0;
        var code = (string?)stack["code"];
        var kind = (string?)stack["kind"];
        if (code == null || kind == null) return 0;
        var key = kind + "|" + code + "|" + attributes.ToString(Newtonsoft.Json.Formatting.None);
        if (!cache.TryGetValue(key, out var name))
            cache[key] = name = Resolve(api, code, kind, attributes);
        if (name == null) return 0;
        stack["name"] = name;
        return 1;
    }

    /// <summary>
    /// The stack's name, or null when it is the bare collectible's own or has no translation.
    /// (An item whose bare code is untranslated may borrow its variants' name in the item
    /// section; a stack then repeats it, which is harmless.)
    /// </summary>
    private static string? Resolve(ICoreServerAPI api, string code, string kind, JObject attributes)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = kind == "block" ? api.World.GetBlock(loc) : api.World.GetItem(loc);
        if (c?.Code == null || c.IsMissing) return null;
        var stack = new ItemStack(c);
        try
        {
            if (new JsonObject(attributes).ToAttribute() is ITreeAttribute tree) stack.Attributes.MergeTree(tree);
        }
        catch (Exception) { return null; }
        var name = ItemRecords.Name(stack);
        if (ItemRecords.IsLangKey(name, c)) return null;
        return name == ItemRecords.Name(c) ? null : name;
    }
}
