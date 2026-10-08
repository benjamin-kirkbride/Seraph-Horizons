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
    public void LiquidIngredientsCostPerItemNotPerLitre()
    {
        // Honey at 2 gears per litre, 100 portions a litre: 0.02 a portion. Fifty portions and a
        // stick make a jar worth 0.5, under the 1.05 they cost (not 100.05, as per-litre would say).
        var table = new ItemValues(new Dictionary<string, double>
        {
            ["game:honeyportion"] = 2,
            ["game:stick"] = 0.05,
            ["game:honeyjar"] = 0.5,
        }, [], perLitre: new Dictionary<string, int> { ["game:honeyportion"] = 100 });
        var recipes = new[] { new RecipeRow("jar", "game:honeyjar", 1, [("game:honeyportion", 50), ("game:stick", 1)]) };
        var jar = Assert.Single(ValueChecks.Below(table, recipes));
        Assert.Equal(1.05, jar.Ingredients, 6);
        // A liquid output is compared per item too: 0.004 a portion is under the 0.01 a portion costs.
        var cheap = new ItemValues(new Dictionary<string, double> { ["game:juiceportion"] = 0.4, ["game:fruit"] = 0.1 }, [],
            perLitre: new Dictionary<string, int> { ["game:juiceportion"] = 100 });
        var juice = Assert.Single(ValueChecks.Below(cheap, [new RecipeRow("juice", "game:juiceportion", 10, [("game:fruit", 1)])]));
        Assert.Equal(0.004, juice.Value, 9);
        Assert.Equal(0.01, juice.Ingredients, 9);
    }

    [Fact]
    public void FamilyValuedOutputsAreNotFlagged()
    {
        var recipes = new[] { new RecipeRow("birch", "game:plank-birch", 1, [("game:ingot-iron", 1)]) };
        Assert.Empty(ValueChecks.Below(Table, recipes));
    }
}
