using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>Butchering's raw blood sausage and raw black pudding as these scenarios read them: the
/// grid recipes that make them, and the enabled recipes of A Culinary Artillery's registries (its
/// mixing bowl's kneading) that do. Shared with <see cref="SwitchesOffScenarios"/>.</summary>
internal static class BloodSausages
{
    public static readonly string[] Outputs = [BloodSausage.BloodSausageRaw, BloodSausage.BlackPuddingRaw];

    public static List<GridRecipe> GridRecipes(IWorldAccessor world) =>
        world.GridRecipes.Where(r => Outputs.Contains(r.Output?.Code?.ToString())).ToList();

    /// <summary>The output codes of every enabled recipe in A Culinary Artillery's registries, found
    /// and read as the recipe exporter does.</summary>
    public static List<string> MixingBowlOutputs(ICoreServerAPI api)
    {
        var codes = new List<string>();
        foreach (var reg in Registries.Find(api).Where(r => r.Mod == "aculinaryartillery"))
        {
            var list = Registries.RecipeList(reg.Registry);
            if (list == null || Registries.ElementType(list) is not { } element)
                continue;
            var reader = Readers.For(element);
            foreach (var recipe in list)
                if (recipe != null && reader.Read(recipe) is { Enabled: true } form)
                    codes.AddRange(form.Outputs.Select(o => o.Stack.Code?.ToString()).OfType<string>());
        }
        return codes;
    }
}

/// <summary>
/// mods-src/seraphhorizons, BloodSausage: Butchering's four grid recipes for raw blood sausage and
/// raw black pudding are gone, and A Culinary Artillery's mixing bowl still makes both (Butchering's
/// kneading recipes, which it enables with Expanded Foods).
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Blood_sausage_and_black_pudding_are_made_only_in_the_mixing_bowl()
    {
        foreach (var code in BloodSausages.Outputs)
            Assert.NotNull(W.GetItem(new AssetLocation(code)));
        Assert.Empty(BloodSausages.GridRecipes(W));
        var bowl = BloodSausages.MixingBowlOutputs((ICoreServerAPI)World.Api);
        Assert.Contains(BloodSausage.BloodSausageRaw, bowl);
        Assert.Contains(BloodSausage.BlackPuddingRaw, bowl);
    }
}
