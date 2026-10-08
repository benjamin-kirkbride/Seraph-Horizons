using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Tests;

public class RuinSurfaceHeightTests
{
    [Fact]
    public void The_median_of_an_odd_count_is_the_middle_sample()
    {
        Assert.Equal(136, RuinSurfaceHeight.Median([137, 134, 136, 137, 136], 0));
        Assert.Equal(5, RuinSurfaceHeight.Median([5], 0));
    }

    [Fact]
    public void The_median_of_an_even_count_is_the_lower_middle_sample()
    {
        Assert.Equal(135, RuinSurfaceHeight.Median([134, 136, 135, 137], 0));
        Assert.Equal(134, RuinSurfaceHeight.Median([137, 134], 0));
    }

    [Fact]
    public void No_samples_give_the_fallback() => Assert.Equal(99, RuinSurfaceHeight.Median([], 99));

    [Fact]
    public void A_ruin_on_a_slope_sits_on_the_median_not_the_lowest()
    {
        // BetterRuins' tinkers bakery (25 x 25): the game's eleven samples, the start twice, on
        // ground mostly at 136 to 137 with the south edge at 134. The game seats it at 134.
        int[] samples = [137, 137, 136, 134, 134, 137, 136, 134, 136, 137, 136];
        Assert.Equal(134, samples.Min());
        Assert.Equal(136, RuinSurfaceHeight.Base(samples, 134));
    }

    [Fact]
    public void Flat_ground_is_unchanged() => Assert.Equal(120, RuinSurfaceHeight.Base([120, 120, 120, 120, 120], 120));

    [Fact]
    public void The_median_never_leaves_the_sampled_range()
    {
        var random = new Random(7);
        for (int n = 0; n < 500; n++)
        {
            var samples = Enumerable.Range(0, random.Next(5, 20)).Select(_ => 100 + random.Next(4)).ToArray();
            int median = RuinSurfaceHeight.Base(samples, samples.Min());
            Assert.InRange(median, samples.Min(), samples.Max());
            Assert.Contains(median, samples);
        }
    }

    [Fact]
    public void Samples_that_do_not_hold_the_games_minimum_are_ignored()
    {
        Assert.Equal(130, RuinSurfaceHeight.Base([136, 137, 136], 130));
        Assert.Equal(130, RuinSurfaceHeight.Base([], 130));
    }
}
