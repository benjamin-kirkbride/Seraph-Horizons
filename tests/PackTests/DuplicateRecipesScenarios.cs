using Atlas.XUnit;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The recipes seraphhorizons' <c>DuplicateRecipes</c> switch turns off, and the ones it
/// keeps, as these scenarios read them from the loaded server. Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class Duplicates
{
    public const string Offal = "butchering:offal-clean";
    public const string ScrapBrazier = "hqzlights:brazier-scrap";
    public const string CottageCheese = "game:cottagecheeseportion";
    public const string YellowDaub = "game:daubraw-yellow";
    public const string Sandstone = "game:sand-sandstone";
    public const string EfBrazierFile = "expandedfoods:recipes/grid/braziers/brazier.json";
    public const string GameDaubFile = "game:recipes/grid/daub-raw.json";

    /// <summary>Expanded Foods' kneading sausages without offal, four recipes each.</summary>
    public static readonly string[] EfSausageFiles =
    [
        "expandedfoods:recipes/kneading/sausage.json",
        "expandedfoods:recipes/kneading/sausagefish.json",
    ];

    /// <summary>What those three files make, all of which the game's own recipes make too.</summary>
    public static readonly string[] AgedOutputs =
    [
        "game:slantedroofingbottom-aged-east-free", "game:slantedroofingtop-aged-east-free",
        "game:slantedroofinghalfleft-aged-east-free", "game:slantedroofinghalfright-aged-east-free",
        "game:slantedroofingridgeend-aged-east-free", "game:slantedroofingridgehalfleft-aged-east-free",
        "game:slantedroofingridgehalfright-aged-east-free", "game:oar-crude-aged", "game:boat-raft-aged",
    ];

    public static string? Out(GridRecipe r) => r.Output?.Code?.ToString();

    public static string From(GridRecipe r) => r.Name?.ToString() ?? "";

    /// <summary>Whether a grid recipe is Material Needs' (its file's path, or for its raft and oar the
    /// recipe's own <c>name</c>, in its domain).</summary>
    public static bool FromMaterialNeeds(GridRecipe r) => r.Name?.Domain == "materialneeds";

    /// <summary>Whether a grid recipe makes the iron round shield of very aged planks.</summary>
    public static bool VeryAgedIronShield(GridRecipe r) =>
        Out(r) == "game:shield-woodmetal"
        && r.Output?.ResolvedItemStack?.Attributes is { } a
        && a.GetString("wood") == "veryaged" && a.GetString("metal") == "iron";

    /// <summary>Firewood a grid recipe takes (0 when none).</summary>
    public static int Firewood(GridRecipe r) =>
        r.ResolvedIngredients?.Where(i => i?.Code?.ToString() == "game:firewood").Sum(i => i!.Quantity) ?? 0;

    /// <summary>Whether a grid recipe makes yellow daub from sandstone sand.</summary>
    public static bool SandstoneDaub(GridRecipe r) =>
        Out(r) == YellowDaub && r.ResolvedIngredients?.Any(i => i?.Code?.ToString() == Sandstone) == true;

    /// <summary>Whether an output is one of Expanded Foods' meat or fish sausages.</summary>
    public static bool EfSausage(string code) =>
        code.StartsWith("expandedfoods:sausage-") || code.StartsWith("expandedfoods:sausagefish-");

    /// <summary>Every enabled recipe of A Culinary Artillery's registries, read as the recipe exporter
    /// does.</summary>
    public static List<RecipeForm> MixingBowl(ICoreServerAPI api)
    {
        var forms = new List<RecipeForm>();
        foreach (var reg in Registries.Find(api).Where(r => r.Mod == "aculinaryartillery"))
        {
            var list = Registries.RecipeList(reg.Registry);
            if (list == null || Registries.ElementType(list) is not { } element)
                continue;
            var reader = Readers.For(element);
            foreach (var recipe in list)
                if (recipe != null && reader.Read(recipe) is { Enabled: true } form)
                    forms.Add(form);
        }
        return forms;
    }

    public static IEnumerable<string> Outputs(RecipeForm form) =>
        form.Outputs.Select(o => o.Stack.Code?.ToString()).OfType<string>();

    public static bool TakesOffal(RecipeForm form) =>
        form.Slots.Any(s => s.Accepts.Any(a => a.Code?.ToString() == Offal));

    /// <summary>The enabled mixing bowl (kneading) recipes making one of Expanded Foods' sausages raw
    /// or to cure.</summary>
    public static List<RecipeForm> SausageRecipes(ICoreServerAPI api) =>
        MixingBowl(api).Where(f => f.Name?.Path.StartsWith("recipes/kneading/") == true && Outputs(f).Any(EfSausage)).ToList();
}

/// <summary>
/// mods-src/seraphhorizons, DuplicateRecipes: the duplicate recipes are gone, and what replaces each
/// is still there.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Expanded_foods_sausages_all_take_clean_offal()
    {
        var sausages = Duplicates.SausageRecipes((ICoreServerAPI)World.Api);
        Assert.DoesNotContain(sausages, f => Duplicates.EfSausageFiles.Contains(f.Name?.ToString()));
        Assert.All(sausages, f => Assert.True(Duplicates.TakesOffal(f), $"{f.Name}: {string.Join(", ", Duplicates.Outputs(f))} takes no offal"));
        // Butchering's four meat and four fish sausages, which it enables with Expanded Foods.
        foreach (var output in new[] { "-raw", "-curing", "cheese-raw", "cheese-curing" })
            Assert.Contains(sausages, f => Duplicates.Outputs(f).Any(o => o.StartsWith("expandedfoods:sausage-") && o.EndsWith(output)));
        foreach (var output in new[] { "normal-raw", "normal-curing", "cheese-raw", "cheese-curing" })
            Assert.Contains(sausages, f => Duplicates.Outputs(f).Contains("expandedfoods:sausagefish-" + output));
    }

    [AtlasScenario]
    public void The_scrap_brazier_is_made_only_hqz_lights_way()
    {
        var braziers = W.GridRecipes.Where(r => Duplicates.Out(r) == Duplicates.ScrapBrazier).ToList();
        Assert.DoesNotContain(braziers, r => Duplicates.From(r) == Duplicates.EfBrazierFile);
        Assert.DoesNotContain(braziers, r => Duplicates.Firewood(r) is > 0 and < 10);
        Assert.Contains(braziers, r => Duplicates.Firewood(r) == 16);
    }

    [AtlasScenario]
    public void Aged_roofing_rafts_and_shields_come_from_the_games_recipes_only()
    {
        foreach (var output in Duplicates.AgedOutputs)
        {
            Assert.DoesNotContain(W.GridRecipes, r => Duplicates.Out(r) == output && Duplicates.FromMaterialNeeds(r));
            Assert.Contains(W.GridRecipes, r => Duplicates.Out(r) == output && r.Name?.Domain == "game");
        }
        Assert.DoesNotContain(W.GridRecipes, r => Duplicates.VeryAgedIronShield(r) && Duplicates.FromMaterialNeeds(r));
        Assert.Contains(W.GridRecipes, r => Duplicates.VeryAgedIronShield(r) && r.Name?.Domain == "game");
    }

    [AtlasScenario]
    public void Cottage_cheese_comes_from_the_mixing_bowl()
    {
        Assert.DoesNotContain(World.Api.GetBarrelRecipes(), r => r.Output?.Code?.ToString() == Duplicates.CottageCheese);
        var bowl = Duplicates.MixingBowl((ICoreServerAPI)World.Api);
        Assert.Contains(bowl, f => Duplicates.Outputs(f).Contains(Duplicates.CottageCheese));
    }

    [AtlasScenario]
    public void Sandstone_daub_gives_twelve()
    {
        var daub = W.GridRecipes.Where(Duplicates.SandstoneDaub).ToList();
        Assert.NotEmpty(daub);
        Assert.All(daub, r => Assert.Equal(12, r.Output.Quantity));
        // The game's other daub recipes stay.
        Assert.Contains(W.GridRecipes, r => Duplicates.From(r) == Duplicates.GameDaubFile);
    }
}
