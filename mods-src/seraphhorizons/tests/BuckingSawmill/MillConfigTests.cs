using SeraphHorizons.Mod.BuckingSawmill.Core;

namespace SeraphHorizons.Mod.Tests;

public class MillConfigTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var config = new MillConfig();
        Assert.Equal(0.17f, config.Resistance);
        Assert.Equal(0.05f, config.MinSpeed);
        Assert.Equal(8f, config.RevolutionsPerStoredLog);
        Assert.Equal(6f, config.RaiseRevolutions);
        Assert.Equal(2f, config.LogsPerStoredLog);
        Assert.Equal(1f, config.BladeWearPerStoredLog);
        Assert.Equal(0.35f, config.BladeSpeedPerTier);
        Assert.True(config.AutoPullFromRack);
        Assert.Empty(config.Sanitise());
    }

    [Fact]
    public void Out_of_range_values_fall_back_to_the_defaults()
    {
        var config = new MillConfig
        {
            Resistance = -1,
            MinSpeed = float.NaN,
            RevolutionsPerStoredLog = 0,
            RaiseRevolutions = -1,
            LogsPerStoredLog = -2,
            BladeWearPerStoredLog = float.PositiveInfinity,
            BladeSpeedPerTier = -0.1f,
        };
        var fixes = config.Sanitise();
        Assert.Equal(7, fixes.Count);
        Assert.Contains(fixes, f => f.StartsWith("RevolutionsPerStoredLog"));
        Assert.Contains(fixes, f => f.StartsWith("RaiseRevolutions"));
        Assert.Equal(6f, config.RaiseRevolutions);
        Assert.Equal(0.17f, config.Resistance);
        Assert.Equal(0.05f, config.MinSpeed);
        Assert.Equal(8f, config.RevolutionsPerStoredLog);
        Assert.Equal(2f, config.LogsPerStoredLog);
        Assert.Equal(1f, config.BladeWearPerStoredLog);
        Assert.Equal(0.35f, config.BladeSpeedPerTier);
    }

    [Fact]
    public void Valid_edges_are_kept()
    {
        var config = new MillConfig { Resistance = 0, MinSpeed = 0, LogsPerStoredLog = 0, BladeWearPerStoredLog = 0, BladeSpeedPerTier = 0, RevolutionsPerStoredLog = 0.01f, RaiseRevolutions = 0.01f };
        Assert.Empty(config.Sanitise());
        Assert.Equal(0.01f, config.RaiseRevolutions);
        Assert.Equal(0.01f, config.RevolutionsPerStoredLog);
    }
}
