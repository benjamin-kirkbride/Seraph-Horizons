using Atlas.XUnit;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>Better Ruins' blueprint chain recipes, as the loaded server has them. Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class SupportChainRecipes
{
    /// <summary>Each chain's grid recipes from Better Ruins' mechanical blueprint file.</summary>
    public static List<GridRecipe> Of(IWorldAccessor world, string code) =>
        world.GridRecipes.Where(r => r.Output?.Code?.ToString() == code
                                     && r.Name?.ToString() == SupportChains.RecipeAsset.ToString()).ToList();
}

/// <summary>
/// mods-src/seraphhorizons, FewerSupportChains: Better Ruins' blueprint makes 4 support chains a
/// craft, not 64.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void The_blueprint_makes_4_support_chains_a_craft()
    {
        Assert.True(SeraphHorizonsSystem.ConfigFor(World.Api).FewerSupportChains);
        foreach (var code in SupportChains.Codes)
        {
            var recipes = SupportChainRecipes.Of(W, code);
            Assert.Single(recipes);
            Assert.Equal(SupportChains.Yield, recipes[0].Output.Quantity);
        }
    }
}
