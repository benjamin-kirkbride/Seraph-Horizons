using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/allowedvariantsfix: an unnamed wildcard ingredient keeps its allowedVariants and
/// skipVariants when the recipe also has a named one
/// (https://github.com/anegostudios/VintageStory-Issues/issues/9256). Without the mod every
/// scenario here fails; when the game fixes the bug they pass without it, and the mod can go.
/// </summary>
public partial class PackFixScenarios
{
    private ItemStack BlockStack(string code) =>
        new(W.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no block {code}"));

    private ItemStack ItemStackOf(string code) =>
        new(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no item {code}"));

    private static CraftingRecipeIngredient Cell(GridRecipe recipe, string key) =>
        recipe.ResolvedIngredients!.First(i => i?.Id == key)!;

    // The recipe from the issue, built here so the check needs no mod: P is named, I is not.
    [AtlasScenario]
    public void Unnamed_ingredient_keeps_its_filters_in_every_generated_recipe()
    {
        var recipe = JsonUtil.FromString<GridRecipe>("""
            {
              "ingredientPattern": "PIS",
              "ingredients": {
                "P": { "type": "item", "code": "game:plank-*", "name": "wood", "allowedVariants": ["birch", "oak"] },
                "I": { "type": "item", "code": "game:metalnailsandstrips-*", "allowedVariants": ["meteoriciron", "steel"] },
                "S": { "type": "item", "code": "game:plank-*", "skipVariants": ["birch"] }
              },
              "width": 3,
              "height": 1,
              "output": { "type": "item", "code": "game:stick" }
            }
            """)!;

        var generated = recipe.GenerateRecipesForAllIngredientCombinations(W).Cast<GridRecipe>().ToList();
        Assert.Equal(new[] { "game:plank-birch", "game:plank-oak" },
            generated.Select(g => g.Ingredients!["P"].Code?.ToString()).Order(StringComparer.Ordinal));
        foreach (var g in generated)
        {
            Assert.Equal(new[] { "meteoriciron", "steel" }, g.Ingredients!["I"].AllowedVariants);
            Assert.Equal(new[] { "birch" }, g.Ingredients["S"].SkipVariants);
        }
    }

    // betterruins:recipes/grid/nonschematicrelated/moregravel/dirtygravel-dry.json, entry 0:
    // G = gravel-* named gravel (14 rocks), S = soil-*-none allowing only "low".
    [AtlasScenario]
    public void BetterRuins_dirty_gravel_takes_only_low_fertility_soil()
    {
        var recipes = W.GridRecipes
            .Where(g => g.Name?.ToString() == "betterruins:recipes/grid/nonschematicrelated/moregravel/dirtygravel-dry.json"
                        && g.Output?.Code?.ToString() == "game:dirtygravel-dry-plain")
            .ToList();
        Assert.Equal(14, recipes.Count);
        foreach (var r in recipes)
        {
            var soil = Cell(r, "S");
            Assert.True(soil.SatisfiesAsIngredient(BlockStack("game:soil-low-none")));
            Assert.False(soil.SatisfiesAsIngredient(BlockStack("game:soil-high-none")), "terra preta accepted");
            Assert.False(soil.SatisfiesAsIngredient(BlockStack("game:soil-medium-none")));
        }
    }

    // butchering:recipes/grid/butcherhook.json, entry 3: H = hook-* named material, F = nails
    // and strips allowing only copper and the bronzes.
    [AtlasScenario]
    public void Butcher_hook_takes_only_the_listed_nails()
    {
        var recipes = W.GridRecipes
            .Where(g => g.Name?.ToString() == "butchering:recipes/grid/butcherhook.json"
                        && g.ResolvedIngredients?.Any(i => i?.Id == "F" && i.Code?.ToString() == "game:metalnailsandstrips-*") == true)
            .ToList();
        Assert.Equal(4, recipes.Count);
        foreach (var r in recipes)
        {
            var nails = Cell(r, "F"); // needs 2; only the variant matters here
            Assert.True(nails.SatisfiesAsIngredient(ItemStackOf("game:metalnailsandstrips-copper"), checkStackSize: false));
            Assert.False(nails.SatisfiesAsIngredient(ItemStackOf("game:metalnailsandstrips-iron"), checkStackSize: false), "iron nails accepted");
            Assert.False(nails.SatisfiesAsIngredient(ItemStackOf("game:metalnailsandstrips-steel"), checkStackSize: false));
        }
    }
}
