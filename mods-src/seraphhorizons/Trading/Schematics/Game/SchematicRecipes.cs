using SeraphHorizons.Mod.Trading.Schematics.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Trading.Schematics;

/// <summary>What <see cref="SchematicRecipes.Apply"/> did, for the log and the scenarios.</summary>
public sealed class SchematicRecipeReport
{
    /// <summary>Recipes taken out: they made or copied a sold schematic.</summary>
    public List<string> Removed { get; } = [];
    /// <summary>Recipe slots whose schematic is now kept on crafting.</summary>
    public int Kept { get; set; }
    /// <summary>Gated recipes as <c>output ← schematic</c>.</summary>
    public List<string> Gated { get; } = [];
    /// <summary>Recipes whose output is gated but which could not take the schematic.</summary>
    public List<string> Failed { get; } = [];
}

/// <summary>
/// The grid recipe side of the schematics (#468, #469), run once on the server at ModsAndConfigReady (LoadGamePre): after
/// every mod's AssetsFinalize (Immersive Woodworking registers its frames' recipes there), before
/// the recipes are sent to clients (after WorldReady) and while they still have their ingredient
/// tables (the server drops them once sent). Clients and the handbook get the recipes as changed.
///
/// <list type="bullet">
/// <item>TraderSchematics: a recipe making a sold schematic is removed unless it is a one-slot
/// conversion (<see cref="SchematicRecipeRules"/>): the game's glider copy, Abyssal Depths' copy and
/// craft, Cartwright's carts and signs from parchment and charcoal, and BetterRuins' copy should its
/// setting turn it on. A sold schematic in any other recipe is kept on crafting (<c>consume:
/// false</c>; a returned stack is dropped, it would come on top): Cartwright's and Abyssal Depths'
/// recipes consumed and returned it, the walking stick's hidden gun ate it once Scrolled took the
/// item's obsolete noConsumeOnCrafting away.</item>
/// <item>MachineSchematics: every recipe making a gated output (<see cref="SchematicTable.GateFor"/>),
/// other than a one-slot conversion, takes the machine's schematic in a slot <see cref="GridGate"/>
/// finds.</item>
/// </list>
/// </summary>
public static class SchematicRecipes
{
    public static SchematicRecipeReport Apply(IWorldAccessor world, ICoreAPI api, SchematicTable table, bool traderSchematics, bool machineSchematics)
    {
        var report = new SchematicRecipeReport();
        bool ModLoaded(string mod) => mod == "game" || api.ModLoader.IsModEnabled(mod);
        foreach (var recipe in world.GridRecipes.ToList())
        {
            string? output = recipe.Output?.Code?.ToString();
            if (output is null || recipe.ResolvedIngredients is null) continue;
            var filled = recipe.ResolvedIngredients.OfType<CraftingRecipeIngredient>().ToList();
            bool conversion = SchematicRecipeRules.IsConversion(filled.Count, filled.Count == 1 && filled[0].Consume && !filled[0].IsTool, recipe.Output!.Quantity);

            if (traderSchematics)
            {
                if (SchematicRecipeRules.RemoveRecipe(table.IsSold(output), conversion))
                {
                    world.GridRecipes.Remove(recipe);
                    report.Removed.Add($"{recipe.Name} ({output})");
                    continue;
                }
                foreach (var ingredient in (recipe.Ingredients?.Values ?? Enumerable.Empty<CraftingRecipeIngredient>()).Concat(filled))
                {
                    if (ingredient.Code is null || !SchematicRecipeRules.KeepIngredient(table.IsSold(ingredient.Code.ToString()), conversion)) continue;
                    if (ingredient.Consume || ingredient.ReturnedStack != null) report.Kept++;
                    ingredient.Consume = false;
                    ingredient.ReturnedStack = null;
                }
            }

            if (machineSchematics && !conversion && table.GateFor(output, ModLoaded) is { } gate)
            {
                if (Gate(world, recipe, gate.Schematic)) report.Gated.Add($"{output} ← {gate.Schematic}");
                else report.Failed.Add($"{recipe.Name} ({output}): pattern {recipe.IngredientPattern}");
            }
        }
        return report;
    }

    /// <summary>Puts the schematic in the recipe; false (the recipe unchanged) when there is no room
    /// or the recipe no longer resolves.</summary>
    public static bool Gate(IWorldAccessor world, GridRecipe recipe, string schematic)
    {
        if (recipe.Ingredients is null || recipe.IngredientPattern is null) return false;
        if (recipe.Ingredients.Values.Any(i => i.Code?.ToString() == schematic)) return true;
        var ingredients = recipe.Ingredients;
        var placement = GridGate.Place(recipe.IngredientPattern, recipe.Width, recipe.Height,
            ingredients.Keys.Where(k => k.Length == 1).Select(k => k[0]), key => CanDouble(world, ingredients.GetValueOrDefault(key.ToString())));
        if (placement is null) return false;

        var (oldPattern, oldWidth, oldHeight) = (recipe.IngredientPattern, recipe.Width, recipe.Height);
        var newIngredients = new Dictionary<string, CraftingRecipeIngredient>(ingredients)
        {
            [placement.SchematicKey.ToString()] = new CraftingRecipeIngredient
            {
                Type = EnumItemClass.Item,
                Code = new AssetLocation(schematic),
                Quantity = 1,
                Consume = false,
            },
        };
        if (placement is { DoubledFrom: { } from, DoubledKey: { } doubled })
        {
            var twice = ingredients[from.ToString()].Clone();
            twice.Quantity *= 2;
            newIngredients[doubled.ToString()] = twice;
        }
        recipe.Ingredients = newIngredients;
        recipe.IngredientPattern = placement.Pattern;
        recipe.Width = placement.Width;
        recipe.Height = placement.Height;
        recipe.OnParsed(world);
        if (recipe.Resolve(world, "seraphhorizons schematic gate")) return true;

        recipe.Ingredients = ingredients;
        recipe.IngredientPattern = oldPattern;
        recipe.Width = oldWidth;
        recipe.Height = oldHeight;
        recipe.Resolve(world, "seraphhorizons schematic gate (restored)");
        return false;
    }

    /// <summary>Two slots of this ingredient can become one: consumed, not a tool, returns nothing,
    /// and a stack of twice the quantity fits every collectible it matches.</summary>
    private static bool CanDouble(IWorldAccessor world, CraftingRecipeIngredient? ingredient)
    {
        if (ingredient is null || ingredient.IsTool || !ingredient.Consume || ingredient.ReturnedStack != null || ingredient.Code is null) return false;
        int quantity = ingredient.Quantity * 2;
        if (ingredient.ResolvedItemStack is { } stack) return stack.Collectible.MaxStackSize >= quantity;
        string pattern = ingredient.Code.ToString();
        var matches = world.Collectibles.Where(c => c.Code != null && c.ItemClass == ingredient.Type && CodePattern.Matches(pattern, c.Code.ToString())).ToList();
        return matches.Count > 0 && matches.All(c => c.MaxStackSize >= quantity);
    }
}
