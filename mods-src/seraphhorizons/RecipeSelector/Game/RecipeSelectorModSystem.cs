using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.RecipeSelector.Game;

/// <summary>
/// Recipe selection has a search and a scrollbar (README, "Recipe selection has a search and a scrollbar";
/// switch <see cref="SeraphHorizonsConfig.RecipeSelectorSearch"/>): <see cref="SearchableSelector"/>, through a
/// postfix on the constructor of the game's <c>GuiDialogBlockEntityRecipeSelector</c>, the one dialog
/// knapping, clay forming and smithing open. Applied by hand here with its own id, on the client only and
/// only with the switch on; never <c>PatchAll</c>.
/// </summary>
public sealed class RecipeSelectorModSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.recipeselector";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).RecipeSelectorSearch || !SearchableSelector.Init(api)) return;
        // public GuiDialogBlockEntityRecipeSelector(string DialogTitle, ItemStack[] recipeOutputs, Action<int> onSelectedRecipe,
        //     Action onCancelSelect, BlockPos blockEntityPos, ICoreClientAPI capi), verified against the decompiled 1.22.7 code.
        var ctor = AccessTools.DeclaredConstructor(typeof(GuiDialogBlockEntityRecipeSelector),
            [typeof(string), typeof(ItemStack[]), typeof(Action<int>), typeof(Action), typeof(BlockPos), typeof(ICoreClientAPI)]);
        if (ctor is null)
        {
            api.Logger.Warning("[seraphhorizons] Recipe selector search: the game's recipe selector constructor was not found; off");
            SearchableSelector.Reset();
            return;
        }
        try
        {
            (_harmony = new Harmony(HarmonyId)).Patch(ctor, postfix: new HarmonyMethod(typeof(RecipeSelectorModSystem), nameof(CtorPostfix)));
            api.Logger.Notification("[seraphhorizons] Recipe selector search: patch applied");
        }
        catch (Exception ex)
        {
            api.Logger.Error("[seraphhorizons] Recipe selector search: could not patch the recipe selector; it stays vanilla: {0}", ex);
            SearchableSelector.Reset();
        }
    }

    // After the constructor's SetupDialog has composed the game's grid: compose ours in its place. The
    // parameter is bound by position (__args), so a rename in the game cannot unbind it.
    public static void CtorPostfix(GuiDialogBlockEntityRecipeSelector __instance, object[] __args)
    {
        try { SearchableSelector.Rebuild(__instance, __args.Length > 1 ? __args[1] as ItemStack[] : null); }
        catch (Exception ex) { SearchableSelector.Fail("the constructor postfix", ex); }
    }

    public override void Dispose()
    {
        try { _harmony?.UnpatchAll(HarmonyId); }
        catch { /* shutting down */ }
        _harmony = null;
        SearchableSelector.Reset();
    }
}
