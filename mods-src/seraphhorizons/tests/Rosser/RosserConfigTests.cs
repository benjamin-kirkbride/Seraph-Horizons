using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserConfigTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var c = new RosserConfig();
        Assert.Equal(0.2f, c.Resistance);
        Assert.Equal(0.05f, c.MinSpeed);
        Assert.Equal(6f, c.RevolutionsPerStoredLog);
        Assert.Equal(0.316f, c.RevolutionsPerBranch);
        Assert.Equal(10, c.TypicalThinLogs);
        Assert.Equal(25, c.TypicalThinBranches);
        Assert.Equal(25, c.TypicalThickLogs);
        Assert.Equal(61, c.TypicalThickBranches);
        Assert.Equal(0.35f, c.HeadSpeedPerTier);
        Assert.Equal(0.5f, c.StickFraction);
        Assert.Equal(1f, c.DryBarkMultiplier);
        Assert.Equal(1.5f, c.WetBarkMultiplier);
        Assert.Equal(1.5f, c.WetBarkCountMultiplier);
        Assert.Equal(4f, c.HeadWearMultiple);
        Assert.Equal(1f, c.HeadWearPerStoredLog);
        Assert.Equal(2f, c.WaterPerLog);
        Assert.Equal(20f, c.ReservoirLitres);
        Assert.Equal(10f, c.WaterIntakeLitresPerSecond);
        Assert.True(c.AutoPullFromRack);
        Assert.True(c.AutoPushToRack);
        Assert.Empty(c.Sanitise());
        // 10 × 6 + 25 × 0.316 and 25 × 6 + 61 × 0.316: within 0.2 % of the gears' 68 and 169
        Assert.Equal(67.9, c.TypicalTurns(1), 4);
        Assert.Equal(169.276, c.TypicalTurns(2), 3);
        Assert.Equal(0, c.TypicalTurns(0));
    }

    [Fact]
    public void Out_of_range_values_fall_back_to_the_defaults()
    {
        var c = new RosserConfig
        {
            Resistance = -1,
            MinSpeed = float.NaN,
            RevolutionsPerStoredLog = 0,
            RevolutionsPerBranch = -1,
            TypicalThinLogs = 0,
            TypicalThinBranches = -1,
            TypicalThickLogs = 49,
            TypicalThickBranches = -5,
            HeadSpeedPerTier = -0.1f,
            StickFraction = float.PositiveInfinity,
            DryBarkMultiplier = -1,
            WetBarkMultiplier = float.NaN,
            WetBarkCountMultiplier = -2,
            HeadWearMultiple = 0,
            HeadWearPerStoredLog = -1,
            WaterPerLog = 0,
            ReservoirLitres = -1,
            WaterIntakeLitresPerSecond = float.NegativeInfinity,
        };
        var fixes = c.Sanitise();
        Assert.Equal(18, fixes.Count);
        Assert.Contains(fixes, f => f.StartsWith("TypicalThickLogs 49"));
        var d = new RosserConfig();
        Assert.Equal(d.Resistance, c.Resistance);
        Assert.Equal(d.MinSpeed, c.MinSpeed);
        Assert.Equal(d.RevolutionsPerStoredLog, c.RevolutionsPerStoredLog);
        Assert.Equal(d.RevolutionsPerBranch, c.RevolutionsPerBranch);
        Assert.Equal(d.TypicalThinLogs, c.TypicalThinLogs);
        Assert.Equal(d.TypicalThinBranches, c.TypicalThinBranches);
        Assert.Equal(d.TypicalThickLogs, c.TypicalThickLogs);
        Assert.Equal(d.TypicalThickBranches, c.TypicalThickBranches);
        Assert.Equal(d.HeadSpeedPerTier, c.HeadSpeedPerTier);
        Assert.Equal(d.StickFraction, c.StickFraction);
        Assert.Equal(d.DryBarkMultiplier, c.DryBarkMultiplier);
        Assert.Equal(d.WetBarkMultiplier, c.WetBarkMultiplier);
        Assert.Equal(d.WetBarkCountMultiplier, c.WetBarkCountMultiplier);
        Assert.Equal(d.HeadWearMultiple, c.HeadWearMultiple);
        Assert.Equal(d.HeadWearPerStoredLog, c.HeadWearPerStoredLog);
        Assert.Equal(d.WaterPerLog, c.WaterPerLog);
        Assert.Equal(d.ReservoirLitres, c.ReservoirLitres);
        Assert.Equal(d.WaterIntakeLitresPerSecond, c.WaterIntakeLitresPerSecond);
        Assert.Empty(c.Sanitise());
    }

    [Fact]
    public void Valid_edges_are_kept()
    {
        var c = new RosserConfig
        {
            Resistance = 0, MinSpeed = 0, RevolutionsPerStoredLog = 0.01f, RevolutionsPerBranch = 0,
            TypicalThinLogs = 1, TypicalThinBranches = 0, TypicalThickLogs = 48, TypicalThickBranches = 0,
            HeadSpeedPerTier = 0, StickFraction = 0, DryBarkMultiplier = 0, WetBarkMultiplier = 0, WetBarkCountMultiplier = 0,
            HeadWearMultiple = 0.01f, HeadWearPerStoredLog = 0, WaterPerLog = 0.01f, ReservoirLitres = 0, WaterIntakeLitresPerSecond = 0,
        };
        Assert.Empty(c.Sanitise());
        Assert.Equal(0.01, c.TypicalTurns(1), 6);
        Assert.Equal(0.48, c.TypicalTurns(2), 6);
    }

    [Fact]
    public void A_sanitised_config_always_gives_a_moving_feed()
    {
        // RevolutionsPerStoredLog > 0 and typical logs >= 1 keep both class speeds finite and above 0.
        var c = new RosserConfig { RevolutionsPerStoredLog = -3, TypicalThinLogs = -2 };
        c.Sanitise();
        var pace = RosserFixture.Pace(c);
        Assert.True(pace.BlocksPerTurn(1) > 0 && double.IsFinite(pace.BlocksPerTurn(1)));
        Assert.True(pace.BlocksPerTurn(2) > 0 && double.IsFinite(pace.BlocksPerTurn(2)));
    }
}
