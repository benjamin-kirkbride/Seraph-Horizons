using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Hand-tier roasting in the firepit (#720): 85 % with the fraction carried, so nothing
/// rounds away.</summary>
public class OreRoastingTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static int RoastRun(UnitCarry carry, string key, int items, double share)
    {
        int roasted = 0;
        for (int i = 0; i < items; i++)
            roasted += OreRoasting.Roast(carry, key, 5, share, 5);
        return roasted;
    }

    [Fact]
    public void Firepit_keeps_85_percent_of_every_sulfide()
    {
        foreach (var ore in R.Ores.Where(o => o.IsSulfide))
            Assert.Equal(0.85, R.Roasting(ore, Roaster.Firepit), 9);
    }

    [Theory]
    [InlineData(20, 17)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(128, 108)]
    [InlineData(640, 544)]
    public void A_run_gives_its_share_in_whole_items(int items, int roasted)
    {
        var carry = new UnitCarry();
        Assert.Equal(roasted, RoastRun(carry, "k", items, 0.85));
        // what is not yet an item is held for the next, not lost
        Assert.Equal(items * 5 * 0.85 - roasted * 5, carry.HeldFor("k"), 9);
    }

    [Fact]
    public void Runs_split_anyhow_add_up_to_one_run()
    {
        var carry = new UnitCarry();
        int total = RoastRun(carry, "k", 3, 0.85) + RoastRun(carry, "k", 7, 0.85) + RoastRun(carry, "k", 10, 0.85);
        Assert.Equal(17, total);
        Assert.Equal(0, carry.HeldFor("k"), 9);
    }

    [Fact]
    public void Each_firepit_and_output_carries_its_own_fraction()
    {
        var carry = new UnitCarry();
        Assert.Equal(0, OreRoasting.Roast(carry, "a|galena", 5, 0.85, 5));
        Assert.Equal(0, OreRoasting.Roast(carry, "b|galena", 5, 0.85, 5));
        Assert.Equal(0, OreRoasting.Roast(carry, "a|pyrite", 5, 0.85, 5));
        Assert.Equal(1, OreRoasting.Roast(carry, "a|galena", 5, 0.85, 5));
    }

    [Theory]
    [InlineData(10, 10f)]
    [InlineData(2.5, 2.5f)]
    [InlineData(0, (float)OreRoasting.DefaultSeconds)]
    [InlineData(-3, (float)OreRoasting.DefaultSeconds)]
    [InlineData(double.NaN, (float)OreRoasting.DefaultSeconds)]
    public void Cook_time_falls_back_to_the_default(double configured, float seconds) =>
        Assert.Equal(seconds, OreRoasting.Seconds(configured));
}
