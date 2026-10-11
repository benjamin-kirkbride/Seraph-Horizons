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

    // #847: every grade is the same lump of rock, so the same blows.
    [Fact]
    public void Every_grade_takes_the_same_blows()
    {
        var config = new SpallingConfig();
        foreach (var grade in OreProducts.Grades)
            Assert.Equal(6, config.BlowsFor(grade));
        Assert.Equal(0, config.BlowsFor("lignite"));
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

    // With no loss (#847): a raw ore breaks into one crushed ore of its grade, which holds its units;
    // crystallised ore, twice its grade's units, into two.
    [Theory]
    [InlineData("poor", 3, 1)]
    [InlineData("medium", 5, 1)]
    [InlineData("rich", 7, 1)]
    [InlineData("bountiful", 10, 1)]
    [InlineData("bountiful", 20, 2)]
    public void It_breaks_into_crushed_ore_one_for_one(string grade, int units, int crushed) =>
        Assert.Equal(crushed, OreProducts.CrushedCount(units, OreProducts.RawUnits("galena", grade)!.Value));

    [Fact]
    public void Settings_out_of_range_fall_back()
    {
        var config = new SpallingConfig { BlowsRawOre = 0, HammerWearPerBlow = -1 };
        Assert.Equal(2, config.Sanitise().Count);
        Assert.Equal((6, 1), (config.BlowsRawOre, config.HammerWearPerBlow));
        Assert.Empty(new SpallingConfig { BlowsRawOre = 1, HammerWearPerBlow = 0 }.Sanitise());
    }
}
