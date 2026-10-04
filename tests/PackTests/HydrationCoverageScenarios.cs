using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Hydrate or Diedrate reads a food's hydration from its <c>hydration</c> attribute and counts a
/// missing one as 0, so a food nobody gave a value quietly quenches nothing (#319). The attribute
/// comes from HoD's own pattern lists (HoD.AddItemHydration.json, HoD.AddBlockHydration.json,
/// applied in its AssetsFinalize), from other mods' JSON patches, or from mods-src/seraphhorizons.
/// This checks it is there on every food once the server has loaded; an explicit 0 counts.
/// </summary>
public partial class SharedWorldScenarios
{
    // Deliberate gaps, with a reason each. Keep it short: a food missing here belongs in a patch.
    private static readonly Dictionary<string, string> Exempt = new();

    /// <summary>
    /// A food is any item or block that feeds the player in some form HoD's GetHydration is read
    /// for: eaten (<c>NutritionProps</c>), as a meal ingredient (<c>nutritionPropsWhenInMeal</c>:
    /// grain, dough, and so on), or drunk (<c>waterTightContainerProps.nutritionPropsPerLitre</c>).
    /// Not food: the NoNutrition category, which marks things like acid that go through the
    /// eating code only to hurt.
    /// </summary>
    private static bool IsFood(CollectibleObject c)
    {
        if (c.NutritionProps is { } props)
            return props.FoodCategory != EnumFoodCategory.NoNutrition;
        return IsFoodProps(c.Attributes?["nutritionPropsWhenInMeal"])
               || IsFoodProps(c.Attributes?["waterTightContainerProps"]?["nutritionPropsPerLitre"]);
    }

    private static bool IsFoodProps(JsonObject? props) =>
        props?.Exists == true
        && !string.Equals(props["foodcategory"].AsString(), nameof(EnumFoodCategory.NoNutrition),
            StringComparison.OrdinalIgnoreCase);

    // A code, marked when it is food only in a meal or as a drink, and as a block (whose list,
    // HoD.AddBlockHydration.json, is not the items').
    private static string Describe(CollectibleObject c) =>
        c.Code.Path
        + (c.NutritionProps != null ? "" : IsFoodProps(c.Attributes?["nutritionPropsWhenInMeal"]) ? " (in meals)" : " (drink)")
        + (c is Block ? " [block]" : "");

    private static bool HasHydration(CollectibleObject c) => c.Attributes?["hydration"].Exists == true;

    // Fails when a mod adds or renames a food and nothing gives it a hydration value: add one in
    // mods-src/seraphhorizons (or upstream), as #319's patches do. The list is every such code.
    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_food_has_a_hydration_value()
    {
        var collectibles = World.Api.World.Items.Cast<CollectibleObject>()
            .Concat(World.Api.World.Blocks)
            .Where(c => c?.Code != null)
            .ToList();
        var foods = collectibles.Where(IsFood).ToList();
        // A guard against the check passing vacuously (thirst off, or the props moved).
        Assert.True(foods.Count(HasHydration) > 100, $"only {foods.Count(HasHydration)} foods have hydration: is thirst on?");

        var missing = foods
            .Where(c => !HasHydration(c) && !Exempt.ContainsKey(c.Code.ToString()))
            .GroupBy(c => c.Code.Domain)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key} ({g.Count()}):\n  " + string.Join("\n  ", g.Select(Describe).Order()))
            .ToList();
        Assert.True(missing.Count == 0,
            $"Foods with no hydration value (HoD counts them as 0):\n{string.Join("\n", missing)}");
    }
}
