using System.Runtime.CompilerServices;
using System.Text;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/constructionhelpfix: RightClickConstruction.GenInteractionHelp, patched, builds the
/// same construction hint as the game's own method. Every scenario runs the original (a Harmony
/// reverse patch: the method's unpatched body) and the patched method on identical, freshly
/// built input, and compares the hints: the action, the mouse button and every stack, in order.
/// A method that returns early leaves the hint untouched, and that is compared too.
/// </summary>
public partial class PackFixScenarios
{
    private const string HarmonyId = "constructionhelpfix";

    private ICoreAPI Api => World.Api;
    private static readonly System.Reflection.MethodInfo Target =
        AccessTools.DeclaredMethod(typeof(RightClickConstruction), "GenInteractionHelp", Type.EmptyTypes)
        ?? throw new InvalidOperationException("RightClickConstruction.GenInteractionHelp not found");

    private static readonly AccessTools.FieldRef<RightClickConstruction, WorldInteraction[]?> NextWis =
        AccessTools.FieldRefAccess<RightClickConstruction, WorldInteraction[]?>("nextConstructWis");

    // Set before each run, so a run that returns before assigning the hint shows as such.
    private static readonly WorldInteraction[] Untouched = [new WorldInteraction { ActionLangCode = "(untouched)" }];

    /// <summary>The game's GenInteractionHelp, unpatched.</summary>
    private static class Vanilla
    {
        private static readonly object Gate = new();
        private static bool _ready;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void GenInteractionHelp(RightClickConstruction instance) =>
            throw new InvalidOperationException("reverse patch not applied");

        public static void Run(RightClickConstruction instance)
        {
            lock (Gate)
            {
                if (!_ready)
                {
                    Harmony.ReversePatch(Target, new HarmonyMethod(typeof(Vanilla), nameof(GenInteractionHelp)));
                    _ready = true;
                }
            }
            GenInteractionHelp(instance);
        }
    }

    private static void Patched(RightClickConstruction instance) => Target.Invoke(instance, null);

    /// <summary>Runs one GenInteractionHelp on a new RightClickConstruction and describes the hint.
    /// Stages are parsed afresh by the caller for each run: the method fills placeholders into
    /// the ingredients it reads.</summary>
    private string Run(ConstructionStage[] stages, int completed, IReadOnlyDictionary<string, string> wildcards,
                       Action<RightClickConstruction> gen)
    {
        var rcc = new RightClickConstruction();
        rcc.LateInit(stages, Api, () => new Vec3d(), "constructionhelpfix scenario");
        rcc.CurrentCompletedStage = completed;
        foreach (var (key, value) in wildcards)
            rcc.StoredWildCards[key] = value;
        NextWis(rcc) = Untouched;
        gen(rcc);
        return Describe(NextWis(rcc));
    }

    private static string Describe(WorldInteraction[]? wis)
    {
        if (wis == null)
            return "(null)";
        if (ReferenceEquals(wis, Untouched))
            return "(untouched)";
        var sb = new StringBuilder();
        foreach (var wi in wis)
        {
            sb.Append(wi.ActionLangCode).Append(' ').Append(wi.MouseButton).Append(':');
            AppendStacks(sb, wi.Itemstacks);
            sb.Append(" | matching:");
            AppendStacks(sb, wi.GetMatchingStacks?.Invoke(wi, null!, null!));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void AppendStacks(StringBuilder sb, ItemStack[]? stacks)
    {
        if (stacks == null)
        {
            sb.Append(" (null)");
            return;
        }
        foreach (var s in stacks)
            sb.Append(' ').Append(s.Class).Append(' ').Append(s.Collectible?.Code).Append(" x").Append(s.StackSize)
              .Append(s.Attributes.Count > 0 ? " " + s.Attributes.ToJsonToken() : "");
    }

    private static int StackCount(string description) =>
        description.Split('\n').Sum(line => line.Split(" | ")[0].Split(" x").Length - 1);

    /// <summary>Asserts both methods give the same hint; returns the hint.</summary>
    private string AssertSame(Func<ConstructionStage[]> stages, int completed, IReadOnlyDictionary<string, string> wildcards,
                              string what)
    {
        // Without the patch both runs are the game's method, and every comparison passes.
        AssertPatched();
        string vanilla = Run(stages(), completed, wildcards, Vanilla.Run);
        string patched = Run(stages(), completed, wildcards, Patched);
        Assert.True(vanilla == patched, $"{what}: the hints differ\n--- game\n{vanilla}\n--- patched\n{patched}");
        return vanilla;
    }

    private static readonly IReadOnlyDictionary<string, string> NoWildcards = new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> Wildcards(string pairs) =>
        pairs.Length == 0
            ? NoWildcards
            : pairs.Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);

    private static void AssertPatched()
    {
        var owners = Harmony.GetPatchInfo(Target)?.Transpilers.Select(p => p.owner).ToList() ?? [];
        Assert.True(owners.Contains(HarmonyId),
            $"GenInteractionHelp is not patched by {HarmonyId} (transpilers: [{string.Join(", ", owners)}]); did the mod load?");
    }

    [AtlasScenario]
    public void The_mod_patches_GenInteractionHelp() => AssertPatched();

    // The next stage's requireStacks, one case per way an ingredient can match. Stage 0 is
    // always complete, so the hint is built for stage 1 unless `completed` says otherwise.
    // expectStacks: whether the game's hint lists any stack, so a case can't pass by matching
    // nothing on both sides by accident.
    public static TheoryData<string, string, int, string, bool> Ingredients() => new()
    {
        { "exact item", """[{ "type": "item", "code": "game:resin", "quantity": 4 }]""", 0, "", true },
        { "exact block", """[{ "type": "block", "code": "game:woodenaxle-ud" }]""", 0, "", true },
        { "exact, domain left out", """[{ "type": "item", "code": "resin" }]""", 0, "", true },
        { "exact, wrong type", """[{ "type": "block", "code": "game:resin" }]""", 0, "", false },
        { "exact, missing code", """[{ "type": "item", "code": "game:nosuchitem-at-all" }]""", 0, "", false },
        { "exact, with attributes", """[{ "type": "item", "code": "game:resin", "attributes": { "x": 1 } }]""", 0, "", false },
        { "wildcard item", """[{ "type": "item", "code": "game:plank-*", "quantity": 48 }]""", 0, "", true },
        { "wildcard block", """[{ "type": "block", "code": "game:supportbeam-*", "quantity": 16 }]""", 0, "", true },
        { "wildcard, wrong type", """[{ "type": "block", "code": "game:plank-*" }]""", 0, "", false },
        { "wildcard, domain left out", """[{ "type": "item", "code": "metalnailsandstrips-*" }]""", 0, "", true },
        { "wildcard, any domain", """[{ "type": "item", "code": "*:ingot-*" }]""", 0, "", true },
        { "wildcard at the start", """[{ "type": "block", "code": "game:*-granite" }]""", 0, "", true },
        { "wildcard in the middle", """[{ "type": "block", "code": "game:rock-*" }]""", 0, "", true },
        { "two wildcards", """[{ "type": "block", "code": "game:log-*-*" }]""", 0, "", true },
        { "everything of a type", """[{ "type": "item", "code": "game:*" }]""", 0, "", true },
        { "allowed variants", """[{ "type": "block", "code": "game:supportbeam-*", "allowedVariants": ["oak", "maple", "ebony"] }]""", 0, "", true },
        { "skipped variants", """[{ "type": "item", "code": "game:plank-*", "skipVariants": ["oak", "birch"] }]""", 0, "", true },
        { "allowed variants, none exist", """[{ "type": "item", "code": "game:plank-*", "allowedVariants": ["nosuchwood"] }]""", 0, "", false },
        { "named wildcard", """[{ "type": "item", "code": "game:plank-*", "name": "wood" }]""", 0, "", true },
        { "stores its wildcard", """[{ "type": "block", "code": "game:supportbeam-*", "storeWildCard": "wood" }]""", 0, "", true },
        { "placeholder, filled", """[{ "type": "item", "code": "game:plank-{wood}" }]""", 0, "wood=oak", true },
        { "placeholder, not filled", """[{ "type": "item", "code": "game:plank-{wood}" }]""", 0, "", false },
        { "placeholder, filled with an unknown value", """[{ "type": "item", "code": "game:plank-{wood}" }]""", 0, "wood=nosuchwood", false },
        { "advanced wildcard", """[{ "type": "item", "code": "game:plank-{oak|birch}" }]""", 0, "", false },
        { "regex", """[{ "type": "item", "code": "@game:plank-(oak|birch)" }]""", 0, "", false },
        { "tags only, no code", """[{ "type": "item" }]""", 0, "", true },
        { "tags only, any code", """[{ "type": "block", "code": "*:*" }]""", 0, "", true },
        { "several ingredients", """[{ "type": "item", "code": "game:plank-*" }, { "type": "item", "code": "game:resin" }, { "type": "block", "code": "game:supportbeam-*" }]""", 0, "", true },
        { "several, one fails to resolve", """[{ "type": "item", "code": "game:plank-*" }, { "type": "item", "code": "game:nosuchitem-at-all" }]""", 0, "", false },
        { "empty requireStacks", "[]", 0, "", false },
        { "construction complete", """[{ "type": "item", "code": "game:resin" }]""", 1, "", false },
    };

    [AtlasTheory, MemberData(nameof(Ingredients))]
    public void Hint_is_the_same_for(string what, string requireStacks, int completed, string wildcards, bool expectStacks)
    {
        ConstructionStage[] Stages() => JsonUtil.FromString<ConstructionStage[]>(
            $$"""[{ "addElements": ["a"] }, { "requireStacks": {{requireStacks}}, "addElements": ["b"] }]""")!;
        string hint = AssertSame(Stages, completed, Wildcards(wildcards), what);
        Assert.True((StackCount(hint) > 0) == expectStacks, $"{what}: expected {(expectStacks ? "some" : "no")} stacks, got\n{hint}");
    }

    [AtlasScenario]
    public void Hint_is_the_same_when_a_stage_has_no_requireStacks()
    {
        ConstructionStage[] Stages() => JsonUtil.FromString<ConstructionStage[]>(
            """[{ "addElements": ["a"] }, { "addElements": ["b"] }]""")!;
        Assert.Equal("(null)", AssertSame(Stages, 0, NoWildcards, "no requireStacks"));
    }

    /// <summary>Every block whose block entity has a right-click construction behavior, one per
    /// distinct stage list.</summary>
    private List<(string Code, Func<ConstructionStage[]> Stages)> ConstructableBlocks()
    {
        var found = new List<(string, Func<ConstructionStage[]>)>();
        var seen = new HashSet<string>();
        foreach (var block in W.Blocks)
        {
            if (block?.Code == null)
                continue;
            foreach (var behavior in block.BlockEntityBehaviors)
            {
                var cls = Api.ClassRegistry.GetBlockEntityBehaviorClass(behavior.Name);
                if (cls == null || !typeof(BEBehaviorRightClickConstructable).IsAssignableFrom(cls)
                    || behavior.properties?["stages"] is not { Exists: true } stages)
                    continue;
                if (seen.Add(stages.Token.ToString()))
                    found.Add((block.Code.ToString(), () => stages.AsObject<ConstructionStage[]>()!));
            }
        }
        return found;
    }

    /// <summary>Values the construction could have stored for each wildcard by
    /// <paramref name="stage"/>: the variant a matching stack fills in, as tryConsumeIngredients
    /// stores it. At most <paramref name="perKey"/> per key.</summary>
    private Dictionary<string, List<string>> StorableWildcards(ConstructionStage[] stages, int stage, int perKey)
    {
        var values = new Dictionary<string, List<string>>();
        for (int s = 0; s <= stage && s < stages.Length; s++)
            foreach (var ingredient in stages[s].RequireStacks ?? [])
            {
                if (ingredient.StoreWildCard == null || !ingredient.Resolve(W, "constructionhelpfix scenario"))
                    continue;
                values[ingredient.StoreWildCard] = W.Collectibles
                    .Where(c => c?.Code != null && ingredient.SatisfiesAsIngredient(new ItemStack(c), checkStackSize: false))
                    .Select(c => WildcardUtil.GetWildcardValue(ingredient.Code, c.Code))
                    .Where(v => v != null)
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .Take(perKey)
                    .ToList();
            }
        return values;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public void Hint_is_the_same_for_every_constructable_block_at_every_stage()
    {
        var blocks = ConstructableBlocks();
        Assert.Contains(blocks, b => b.Code.StartsWith("game:waterwheel", StringComparison.Ordinal));

        int cases = 0, withStacks = 0;
        foreach (var (code, stages) in blocks)
        {
            int count = stages().Length;
            for (int completed = 0; completed < count; completed++)
            {
                // No wildcards stored, then each value a stored wildcard could have.
                var sets = new List<IReadOnlyDictionary<string, string>> { NoWildcards };
                foreach (var (key, values) in StorableWildcards(stages(), completed, perKey: 3))
                    foreach (var value in values)
                        sets.Add(new Dictionary<string, string> { [key] = value });

                foreach (var wildcards in sets)
                {
                    string what = $"{code}, {completed} of {count - 1} stages done, wildcards "
                                  + string.Join(",", wildcards.Select(kv => $"{kv.Key}={kv.Value}"));
                    if (StackCount(AssertSame(stages, completed, wildcards, what)) > 0)
                        withStacks++;
                    cases++;
                }
            }
        }
        Assert.True(withStacks > 0, $"{cases} cases, none with a stack in the hint");
    }

    /// <summary>A construction ingredient with a recipe ingredient's matching fields.</summary>
    private static ConstructionIngredient AsConstruction(CraftingRecipeIngredient r) => new()
    {
        Type = r.Type,
        Code = r.Code?.Clone(),
        Name = r.Name,
        Quantity = r.Quantity,
        AllowedVariants = r.AllowedVariants?.ToArray(),
        SkipVariants = r.SkipVariants?.ToArray(),
        Attributes = r.Attributes?.Clone(),
    };

    // The pack's grid recipes hold thousands of real ingredients, in every shape mods write:
    // each distinct one becomes the only ingredient of a construction stage.
    [AtlasScenario(TimeoutMs = 600_000)]
    public void Hint_is_the_same_for_every_grid_recipe_ingredient_in_the_pack()
    {
        var ingredients = W.GridRecipes
            .SelectMany(g => g.ResolvedIngredients ?? [])
            .OfType<CraftingRecipeIngredient>()
            .GroupBy(i => string.Join("|", i.Type, i.Code, i.Name, i.Quantity,
                string.Join(",", i.AllowedVariants ?? []), string.Join(",", i.SkipVariants ?? []), i.Attributes?.Token?.ToString()))
            .Select(g => g.First())
            .ToList();
        Assert.True(ingredients.Count > 100, $"only {ingredients.Count} distinct grid ingredients");

        int withStacks = 0;
        foreach (var ingredient in ingredients)
        {
            ConstructionStage[] Stages() =>
            [
                new ConstructionStage { AddElements = ["a"] },
                new ConstructionStage { RequireStacks = [AsConstruction(ingredient)], AddElements = ["b"] },
            ];
            string what = $"grid ingredient {ingredient.Type} {ingredient.Code}"
                          + (ingredient.AllowedVariants != null ? $" allowed [{string.Join(",", ingredient.AllowedVariants)}]" : "")
                          + (ingredient.SkipVariants != null ? $" skip [{string.Join(",", ingredient.SkipVariants)}]" : "");
            if (StackCount(AssertSame(Stages, 0, NoWildcards, what)) > 0)
                withStacks++;
        }
        Assert.True(withStacks > ingredients.Count / 2, $"{withStacks} of {ingredients.Count} ingredients matched anything");
    }

    // The point of the patch: the loop sees far fewer collectibles than the game has.
    [AtlasScenario]
    public void The_waterwheel_hint_tests_a_small_part_of_the_collectibles()
    {
        var candidates = AccessTools.TypeByName("SeraphHorizons.ConstructionHelpFix.GenInteractionHelpPatch")
            ?.GetMethod("Candidates") ?? throw new InvalidOperationException("the mod's Candidates is not loaded");
        var beams = JsonUtil.FromString<ConstructionIngredient>(
            """{ "type": "block", "code": "game:supportbeam-*", "allowedVariants": ["oak", "maple"] }""")!;
        Assert.True(beams.Resolve(W, "constructionhelpfix scenario"));

        var all = W.Collectibles;
        var some = (List<CollectibleObject>)candidates.Invoke(null, [all, beams])!;
        Assert.NotEmpty(some);
        Assert.True(some.Count * 100 < all.Count, $"{some.Count} of {all.Count} collectibles left to test");
    }
}
