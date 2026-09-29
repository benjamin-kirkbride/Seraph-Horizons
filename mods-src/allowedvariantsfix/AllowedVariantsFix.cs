using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.AllowedVariantsFix;

/// <summary>
/// A recipe with a named wildcard ingredient is expanded into one recipe per value of that
/// name. For each copy, RecipeBase.FillPlaceHolder fills in the named ingredient and then
/// clears AllowedVariants and SkipVariants on every ingredient, including the ones it did not
/// fill. An unnamed wildcard ingredient then accepts every variant: BetterRuins' dry dirty
/// gravel takes any soil-*-none, not only soil-low-none
/// (https://github.com/anegostudios/VintageStory-Issues/issues/9256).
///
/// The patch keeps the filters of every ingredient the call leaves unfilled. Filled
/// ingredients are still cleared as before: their code is exact by then.
/// </summary>
public class AllowedVariantsFixSystem : ModSystem
{
    public const string HarmonyId = "allowedvariantsfix";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    // Recipes are expanded when the server loads assets, after every mod's StartPre.
    public override void StartPre(ICoreAPI api)
    {
        var target = FillPlaceHolderPatch.Target();
        if (target == null)
        {
            api.Logger.Warning("[allowedvariantsfix] RecipeBase.FillPlaceHolder(string, string) not found; "
                               + "the game changed, so recipes are left as the game builds them");
            return;
        }
        _harmony = new Harmony(HarmonyId);
        _harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(FillPlaceHolderPatch), nameof(FillPlaceHolderPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(FillPlaceHolderPatch), nameof(FillPlaceHolderPatch.Postfix)));
    }

    public override void Dispose() => _harmony?.UnpatchAll(HarmonyId);
}

public static class FillPlaceHolderPatch
{
    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(RecipeBase), "FillPlaceHolder", new[] { typeof(string), typeof(string) });

    /// <summary>
    /// Whether FillPlaceHolder fills `ingredient` for `variantCode`, following
    /// RecipeBase.FillIngredientPlaceHolders: a named wildcard of that name, or any advanced
    /// wildcard.
    /// </summary>
    public static bool Fills(IRecipeIngredient ingredient, string variantCode) => ingredient.MatchingType switch
    {
        EnumRecipeMatchType.NamedWildcard => ingredient.Name == variantCode,
        EnumRecipeMatchType.AdvancedWildcard => true,
        _ => false,
    };

    public static void Prefix(RecipeBase __instance, string variantCode,
                              out List<(IRecipeIngredient Ingredient, string[]? Allowed, string[]? Skip)> __state)
    {
        __state = new();
        foreach (var ingredient in __instance.RecipeIngredients)
            if (!Fills(ingredient, variantCode))
                __state.Add((ingredient, ingredient.AllowedVariants, ingredient.SkipVariants));
    }

    public static void Postfix(List<(IRecipeIngredient Ingredient, string[]? Allowed, string[]? Skip)> __state)
    {
        foreach (var (ingredient, allowed, skip) in __state)
        {
            ingredient.AllowedVariants = allowed;
            ingredient.SkipVariants = skip;
        }
    }
}
