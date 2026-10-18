using Atlas.XUnit;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>The game's fruit and its Dry transitions into Expanded Foods' dried fruit (seraphhorizons'
/// <c>FruitDries</c> switch).</summary>
internal static class FruitDryingCheck
{
    /// <summary>Each fruit and the hours it takes to dry, one for each of the patch's four byType keys.</summary>
    public static readonly (string Fruit, float Hours)[] Fruits =
        [("cranberry", 96), ("orange", 360), ("redapple", 960), ("blueberry", 48)];

    /// <summary>The fruit's Dry transition, or null.</summary>
    public static TransitionableProperties? Dry(IWorldAccessor world, string fruit) =>
        world.GetItem(new AssetLocation("game:fruit-" + fruit))?.TransitionableProps?.FirstOrDefault(t => t.Type == EnumTransitionType.Dry);
}

/// <summary>mods-src/seraphhorizons, FruitDries: fresh fruit dries into Expanded Foods' dried fruit.</summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Fruit_dries_into_expanded_foods_dried_fruit()
    {
        foreach (var (fruit, hours) in FruitDryingCheck.Fruits)
        {
            var dry = FruitDryingCheck.Dry(W, fruit);
            Assert.True(dry != null, $"game:fruit-{fruit} has no Dry transition");
            Assert.Equal(hours, dry.TransitionHours.avg);
            Assert.Equal(0, dry.FreshHours.avg);
            Assert.Equal(1, dry.TransitionRatio);
            dry.TransitionedStack.Resolve(W, "fruit drying test");
            Assert.Equal("expandedfoods:dryfruit-" + fruit, dry.TransitionedStack.ResolvedItemstack?.Collectible?.Code?.ToString());
            // The game's own perishing stays.
            Assert.Contains(W.GetItem(new AssetLocation("game:fruit-" + fruit))!.TransitionableProps, t => t.Type == EnumTransitionType.Perish);
        }
    }
}
