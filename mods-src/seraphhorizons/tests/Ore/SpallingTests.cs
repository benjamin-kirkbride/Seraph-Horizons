using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Spalling (#747): which ore items spall, the blows each form takes, the count to the last
/// blow and the settings' ranges.</summary>
public class SpallingTests
{
    [Theory]
    [InlineData("ore", "poor", true)]
    [InlineData("ore", "medium", true)]
    [InlineData("ore", "rich", true)]
    [InlineData("ore", "bountiful", true)]
    [InlineData("crystalizedore", "medium", true)]
    [InlineData("ore", null, false)]          // ungraded ore (coal, sulfur, ...)
    [InlineData("ore", "lignite", false)]
    [InlineData("nugget", "medium", false)]
    [InlineData("crushed", "rich", false)]
    public void Graded_ore_and_crystallised_ore_spall(string firstPart, string? grade, bool spalls) =>
        Assert.Equal(spalls, Spalling.Spalls(firstPart, grade));

    [Fact]
    public void Raw_ore_takes_more_blows_than_a_chunk()
    {
        var config = new SpallingConfig();
        Assert.Equal(6, config.BlowsFor(OreProducts.FormOfGrade("poor")));
        Assert.Equal(6, config.BlowsFor(OreProducts.FormOfGrade("medium")));
        Assert.Equal(3, config.BlowsFor(OreProducts.FormOfGrade("rich")));
        Assert.Equal(3, config.BlowsFor(OreProducts.FormOfGrade("bountiful")));
        Assert.Equal(0, config.BlowsFor(OreForm.Crushed));
        Assert.Equal(0, config.BlowsFor(null));
        Assert.Equal(1, config.HammerWearPerBlow);
    }

    [Fact]
    public void The_last_blow_breaks_it()
    {
        int blows = 0;
        for (int i = 1; i < 6; i++)
        {
            (blows, bool breaks) = Spalling.Strike(blows, 6);
            Assert.Equal(i, blows);
            Assert.False(breaks);
        }
        Assert.Equal((6, true), Spalling.Strike(blows, 6));
        // A single blow is enough when one is needed, and a count past the need still breaks.
        Assert.Equal((1, true), Spalling.Strike(0, 1));
        Assert.Equal((9, true), Spalling.Strike(8, 3));
        Assert.Equal((1, true), Spalling.Strike(-4, 0));
    }

    // With no loss: a raw ore and a chunk give their units in crushed ore, fine from poor.
    [Theory]
    [InlineData("poor", 15, 3, OreGrain.Fine)]
    [InlineData("medium", 20, 4, OreGrain.Coarse)]
    [InlineData("rich", 25, 5, OreGrain.Coarse)]
    [InlineData("bountiful", 35, 7, OreGrain.Coarse)]
    public void It_breaks_into_its_units_of_crushed_ore(string grade, int units, int crushed, OreGrain grain)
    {
        Assert.Equal(crushed, OreProducts.CrushedCount(units, 5));
        Assert.Equal(grain, OreProducts.GrainOfGrade(grade));
    }

    [Fact]
    public void Settings_out_of_range_fall_back()
    {
        var config = new SpallingConfig { BlowsRawOre = 0, BlowsChunk = 5000, HammerWearPerBlow = -1 };
        Assert.Equal(3, config.Sanitise().Count);
        Assert.Equal((6, 3, 1), (config.BlowsRawOre, config.BlowsChunk, config.HammerWearPerBlow));
        Assert.Empty(new SpallingConfig { BlowsRawOre = 1, BlowsChunk = 1, HammerWearPerBlow = 0 }.Sanitise());
    }
}
