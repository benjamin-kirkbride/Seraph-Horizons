using SeraphHorizons.Mod.Trading.Values.Core;

namespace SeraphHorizons.Tests.Trading.Values;

public class ValueChecksTests
{
    private static readonly ItemValues Table = new(new Dictionary<string, double>
    {
        ["game:ingot-iron"] = 4,
        ["game:plank-oak"] = 0.2,
        ["game:plank-pine"] = 0.4,
        ["game:stick"] = 0.05,
        ["game:axe-iron"] = 5,       // cheaper than its ingredients: a hand price to review
        ["game:chest-east"] = 12,
    }, []);

    [Fact]
    public void MissingIsWhatHasNoValueNorFamily()
    {
        var missing = ValueChecks.Missing(Table, ["game:ingot-iron", "game:plank-birch", "game:unknownthing", "unknownthing", "game:unknownthing"]);
        // plank-birch falls back to the plank family; unknownthing is game:unknownthing.
        Assert.Equal(["game:unknownthing", "unknownthing"], missing);
    }

    [Fact]
    public void BelowTakesTheCheapestRecipeWithKnownIngredients()
    {
        var recipes = new[]
        {
            new RecipeRow("axe", "game:axe-iron", 1, [("game:ingot-iron", 2), ("game:stick", 1)]),
            new RecipeRow("axe-unknown", "game:axe-iron", 1, [("game:mystery", 1)]),
            // Eight oak planks (wildcard: the family's 0.3 average) make a chest worth 12: fine.
            new RecipeRow("chest", "game:chest-east", 1, [("game:plank-*", 8)]),
            new RecipeRow("planks", "game:plank-oak", 4, [("game:log-oak", 1)]),
        };
        var below = ValueChecks.Below(Table, recipes);
        var axe = Assert.Single(below);
        Assert.Equal("game:axe-iron", axe.Code);
        Assert.Equal(8.05, axe.Ingredients, 6);
        Assert.Equal("axe", axe.Recipe);
    }

    [Fact]
    public void FamilyValuedOutputsAreNotFlagged()
    {
        var recipes = new[] { new RecipeRow("birch", "game:plank-birch", 1, [("game:ingot-iron", 1)]) };
        Assert.Empty(ValueChecks.Below(Table, recipes));
    }
}
