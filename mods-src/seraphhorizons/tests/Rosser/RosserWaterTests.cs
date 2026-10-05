using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserWaterTests
{
    private static readonly RosserConfig Config = new();

    /// <summary>A random source that hands out the given values and fails if asked for more.</summary>
    private static Func<double> Rolls(params double[] values)
    {
        var queue = new Queue<double>(values);
        return () => queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("rolled too often");
    }

    [Fact]
    public void The_reservoir_fills_at_the_intake_rate_up_to_full()
    {
        var water = RosserWater.Dry;
        Assert.False(water.Wet(Config));
        Assert.Equal(10, water.Wanted(Config, 1), 9);
        Assert.Equal(0.5, water.Wanted(Config, 0.05), 9);
        Assert.Equal(0, water.Wanted(Config, -1));
        water = water.Fill(10, Config);
        Assert.Equal(10, water.Wanted(Config, 5), 9);   // only the room left
        water = water.Fill(15, Config);
        Assert.Equal(20, water.Litres, 9);
        Assert.Equal(0, water.Wanted(Config, 1));
        Assert.Equal(20, water.Fill(double.NaN, Config).Litres, 9);
        Assert.Equal(20, water.Fill(-3, Config).Litres, 9);
    }

    [Fact]
    public void Ten_logs_per_full_reservoir_then_dry()
    {
        var water = new RosserWater(20);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(water.Wet(Config));
            (water, bool wet) = water.SpendForLog(Config);
            Assert.True(wet);
        }
        Assert.Equal(0, water.Litres, 9);
        var (after, dry) = water.SpendForLog(Config);
        Assert.False(dry);
        Assert.Equal(water, after);
        Assert.False(new RosserWater(1.9).SpendForLog(Config).Wet);
    }

    [Fact]
    public void Restore_clamps_to_the_reservoir()
    {
        Assert.Equal(20, RosserWater.Restore(99, Config).Litres);
        Assert.Equal(0, RosserWater.Restore(-1, Config).Litres);
        Assert.Equal(0, RosserWater.Restore(double.NaN, Config).Litres);
        Assert.Equal(5, RosserWater.Restore(99, new RosserConfig { ReservoirLitres = 5 }).Litres);
    }

    [Theory]
    [InlineData(3, 1.5f, 0.49, 5)]   // 4.5: one more with chance 0.5
    [InlineData(3, 1.5f, 0.5, 4)]
    [InlineData(3, 1.5f, 0.99, 4)]
    [InlineData(4, 1.5f, -1, 6)]     // 6.0: no roll
    [InlineData(3, 1f, -1, 3)]
    [InlineData(3, 0f, -1, 0)]
    [InlineData(0, 1.5f, -1, 0)]
    [InlineData(3, 1.1f, 0.29, 4)]   // 3.3
    [InlineData(3, 1.1f, 0.31, 3)]
    public void Wet_count(int baseCount, float factor, double roll, int expected) =>
        Assert.Equal(expected, RosserBark.WetCount(baseCount, factor, roll < 0 ? Rolls() : Rolls(roll)));

    [Fact]
    public void Scraping_spends_water_per_log_and_switches_to_dry()
    {
        // 5 L: two wet logs, then dry
        var (water, logs) = RosserBark.Scrape(new RosserWater(5), 4, 3, Config, Rolls(0.1, 0.9));
        Assert.Equal(1, water.Litres, 9);
        Assert.Equal(new[]
        {
            new LogBark(1.5, 5, true), new LogBark(1.5, 4, true), new LogBark(1.0, 3, false), new LogBark(1.0, 3, false),
        }, logs);
    }

    [Fact]
    public void Dry_scraping_never_rolls()
    {
        var (water, logs) = RosserBark.Scrape(RosserWater.Dry, 48, 3, Config, Rolls());
        Assert.Equal(48, logs.Count);
        Assert.All(logs, l => Assert.Equal(new LogBark(1.0, 3, false), l));
        Assert.Equal(RosserWater.Dry, water);
    }

    [Fact]
    public void The_multipliers_follow_the_settings()
    {
        var config = new RosserConfig { DryBarkMultiplier = 0.8f, WetBarkMultiplier = 2f, WetBarkCountMultiplier = 2f, WaterPerLog = 1 };
        var (_, logs) = RosserBark.Scrape(new RosserWater(1), 2, 3, config, Rolls());
        Assert.Equal(new[] { new LogBark(2.0, 6, true), new LogBark(0.800000011920929, 3, false) }, logs);
    }
}
