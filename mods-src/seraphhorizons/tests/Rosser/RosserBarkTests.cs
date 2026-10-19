using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The drip: its reservoir (the shared machine base's, at the rosser's figures) and the bark a log gives
/// wet or dry.</summary>
public class RosserBarkTests
{
    private static readonly RosserConfig Config = new();

    /// <summary>A random source that hands out the given values and fails if asked for more.</summary>
    private static Func<double> Rolls(params double[] values)
    {
        var queue = new Queue<double>(values);
        return () => queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("rolled too often");
    }

    private static double Capacity => Config.ReservoirLitres;
    private static double Intake => Config.WaterIntakeLitresPerSecond;

    [Fact]
    public void The_reservoir_fills_at_the_intake_rate_up_to_full()
    {
        var water = MachineReservoir.Empty;
        Assert.False(water.Holds(Config.WaterPerLog));
        Assert.Equal(10, water.Wanted(Capacity, Intake, 1), 9);
        Assert.Equal(0.5, water.Wanted(Capacity, Intake, 0.05), 9);
        Assert.Equal(0, water.Wanted(Capacity, Intake, -1));
        water = water.Fill(10, Capacity);
        Assert.Equal(10, water.Wanted(Capacity, Intake, 5), 9);   // only the room left
        water = water.Fill(15, Capacity);
        Assert.Equal(20, water.Litres, 9);
        Assert.Equal(0, water.Wanted(Capacity, Intake, 1));
        Assert.Equal(20, water.Fill(double.NaN, Capacity).Litres, 9);
        Assert.Equal(20, water.Fill(-3, Capacity).Litres, 9);
    }

    [Fact]
    public void Ten_logs_per_full_reservoir_then_dry()
    {
        var water = new MachineReservoir(20);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(water.Holds(Config.WaterPerLog));
            (water, bool wet) = water.Spend(Config.WaterPerLog);
            Assert.True(wet);
        }
        Assert.Equal(0, water.Litres, 9);
        var (after, dry) = water.Spend(Config.WaterPerLog);
        Assert.False(dry);
        Assert.Equal(water, after);
        Assert.False(new MachineReservoir(1.9).Spend(Config.WaterPerLog).Spent);
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
        var (water, logs) = RosserBark.Scrape(new MachineReservoir(5), 4, 3, Config, Rolls(0.1, 0.9));
        Assert.Equal(1, water.Litres, 9);
        Assert.Equal(new[]
        {
            new LogBark(1.5, 5, true), new LogBark(1.5, 4, true), new LogBark(1.0, 3, false), new LogBark(1.0, 3, false),
        }, logs);
    }

    [Fact]
    public void Dry_scraping_never_rolls()
    {
        var (water, logs) = RosserBark.Scrape(MachineReservoir.Empty, 48, 3, Config, Rolls());
        Assert.Equal(48, logs.Count);
        Assert.All(logs, l => Assert.Equal(new LogBark(1.0, 3, false), l));
        Assert.Equal(MachineReservoir.Empty, water);
    }

    [Fact]
    public void The_multipliers_follow_the_settings()
    {
        var config = new RosserConfig { DryBarkMultiplier = 0.8f, WetBarkMultiplier = 2f, WetBarkCountMultiplier = 2f, WaterPerLog = 1 };
        var (_, logs) = RosserBark.Scrape(new MachineReservoir(1), 2, 3, config, Rolls());
        Assert.Equal(new[] { new LogBark(2.0, 6, true), new LogBark(0.800000011920929, 3, false) }, logs);
    }
}
