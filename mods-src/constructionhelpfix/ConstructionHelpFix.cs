using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.ConstructionHelpFix;

/// <summary>
/// RightClickConstruction.GenInteractionHelp builds the "add these to continue" hint of an
/// unfinished right-click-constructed block. For each ingredient of the next stage it creates an
/// ItemStack for every collectible in the game and asks the ingredient whether it fits. The
/// client runs it whenever the player looks at a new cell of the block, and client and server
/// both run it on every construction step: with a large mod list it is a visible frame hitch.
///
/// The patch hands that loop a shorter list: the collectibles that pass the checks
/// SatisfiesAsIngredient makes first (item class, then code). The loop itself is unchanged, so
/// it still builds and tests a stack for each candidate, and the hint is the same, in the same
/// order.
/// </summary>
public class ConstructionHelpFixSystem : ModSystem
{
    public const string HarmonyId = "constructionhelpfix";

    // Singleplayer runs the client and the server in one process, and Harmony patches are
    // process wide: patch on the first start, unpatch on the last dispose.
    private static readonly object Gate = new();
    private static int _users;
    private static Harmony? _harmony;
    private bool _counted;

    public override void StartPre(ICoreAPI api)
    {
        lock (Gate)
        {
            _counted = true;
            if (_users++ > 0)
                return;

            var target = GenInteractionHelpPatch.Target();
            if (target == null || GenInteractionHelpPatch.IngredientLocal(target) == null)
            {
                api.Logger.Warning("[constructionhelpfix] RightClickConstruction.GenInteractionHelp not found as "
                                   + "expected; the game changed, so the construction hint is left as the game builds it");
                return;
            }
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target,
                transpiler: new HarmonyMethod(typeof(GenInteractionHelpPatch), nameof(GenInteractionHelpPatch.Transpiler)));
            if (!GenInteractionHelpPatch.Applied)
                api.Logger.Warning("[constructionhelpfix] RightClickConstruction.GenInteractionHelp does not read "
                                   + "World.Collectibles once; the game changed, so the construction hint is left as the game builds it");
        }
    }

    public override void Dispose()
    {
        lock (Gate)
        {
            if (!_counted)
                return;
            _counted = false;
            if (--_users > 0)
                return;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }
    }
}

public static class GenInteractionHelpPatch
{
    /// <summary>Whether the last transpile found the one World.Collectibles read and rerouted it.</summary>
    public static bool Applied { get; private set; }

    private static readonly MethodInfo CollectiblesGetter =
        AccessTools.PropertyGetter(typeof(IWorldAccessor), nameof(IWorldAccessor.Collectibles));

    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(RightClickConstruction), "GenInteractionHelp", Type.EmptyTypes);

    /// <summary>The index of the method's only ConstructionIngredient local: the loop's current
    /// ingredient. Null unless there is exactly one.</summary>
    public static int? IngredientLocal(MethodBase method)
    {
        var locals = method.GetMethodBody()?.LocalVariables
            .Where(l => l.LocalType == typeof(ConstructionIngredient))
            .ToList();
        return locals is [var only] ? only.LocalIndex : null;
    }

    /// <summary>Follows the one World.Collectibles read with Candidates(collectibles, ingredient).
    /// Leaves the method as it is unless there is exactly one such read.</summary>
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var code = instructions.ToList();
        Applied = false;
        if (IngredientLocal(original) is not int ingredient || code.Count(i => i.Calls(CollectiblesGetter)) != 1)
            return code;

        int at = code.FindIndex(i => i.Calls(CollectiblesGetter));
        code.InsertRange(at + 1, new[]
        {
            CodeInstruction.LoadLocal(ingredient),
            CodeInstruction.Call(typeof(GenInteractionHelpPatch), nameof(Candidates)),
        });
        Applied = true;
        return code;
    }

    /// <summary>
    /// The collectibles <paramref name="ingredient"/> may accept, in their order in
    /// <paramref name="all"/>: every one the ingredient's own SatisfiesAsIngredient could return
    /// true for. Returns <paramref name="all"/> itself when that can't be decided cheaply.
    /// </summary>
    public static List<CollectibleObject> Candidates(List<CollectibleObject> all, ConstructionIngredient ingredient)
    {
        var mayAccept = Prefilter(ingredient);
        if (mayAccept == null)
            return all;

        var candidates = new List<CollectibleObject>();
        foreach (var collectible in all)
            // A null entry is kept, so the game fails on it exactly as it did before.
            if (collectible == null || mayAccept(collectible))
                candidates.Add(collectible!);
        return candidates;
    }

    /// <summary>
    /// A cheap test no collectible the ingredient accepts can fail, or null for "test them all".
    /// It repeats the checks CraftingRecipeIngredient.SatisfiesAsIngredient makes before anything
    /// that needs the stack itself.
    /// </summary>
    internal static System.Func<CollectibleObject, bool>? Prefilter(ConstructionIngredient ingredient)
    {
        // A subclass may match differently.
        if (ingredient.GetType() != typeof(ConstructionIngredient))
            return null;

        if (ingredient.MatchingType != EnumRecipeMatchType.Exact)
        {
            // Wildcard, named, advanced, regex and tags-only ingredients: the stack's class must
            // equal Type, and a set Code must match with AllowedVariants.
            var type = ingredient.Type;
            var code = ingredient.Code;
            var allowed = ingredient.AllowedVariants;
            return c => ClassOf(c) == type && (code == null || WildcardUtil.Match(code, c.Code, allowed));
        }

        // Exact: the resolved stack's collectible decides, through CollectibleObject.Satisfies.
        // Unless its class overrides that, only the same class and id can pass.
        var resolved = ingredient.ResolvedItemStack;
        if (resolved?.Collectible == null)
            return null;
        if (OverridesSatisfies(resolved.Collectible.GetType()))
            return null;
        var cls = resolved.Class;
        var id = resolved.Id;
        return c => ClassOf(c) == cls && c.Id == id;
    }

    // What new ItemStack(collectible) sets as its Class.
    private static EnumItemClass ClassOf(CollectibleObject c) => c is Block ? EnumItemClass.Block : EnumItemClass.Item;

    private static readonly ConcurrentDictionary<Type, bool> SatisfiesOverridden = new();

    private static bool OverridesSatisfies(Type type) =>
        SatisfiesOverridden.GetOrAdd(type, t =>
            t.GetMethod(nameof(CollectibleObject.Satisfies), new[] { typeof(ItemStack), typeof(ItemStack) })
                ?.DeclaringType != typeof(CollectibleObject));
}
