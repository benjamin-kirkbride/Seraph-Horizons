using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserPaceTests
{
    /// <summary>The model's drawn two-speed change agrees with the gameplay's dφ/dψ at the default
    /// settings and copper speed, within 3 % (whole tooth counts).</summary>
    internal static void AssertGearMatchesPace(RosserRig rig)
    {
        var pace = new RosserPace(rig, new RosserConfig());
        for (int k = 1; k <= 2; k++)
        {
            double want = pace.FeedRatio(k, 2, rig.Feed.BlocksPerRadian);
            Assert.InRange(rig.Feed.Gear[k] / want, 0.97, 1.03);
        }
    }

    [Fact]
    public void The_test_rigs_gear_matches_the_default_pace() => AssertGearMatchesPace(RosserFixture.Rig());

    [Fact]
    public void Class_speeds_come_from_the_typical_trunks()
    {
        var pace = RosserFixture.Pace();
        // thin: 10 logs × 6 + 25 branches × 0.316 = 67.9 turns over its 9.625-block trip; thick:
        // 25 × 6 + 61 × 0.316 = 169.276. Both within 0.2 % of the 68 and 169 the gears are drawn for.
        Assert.Equal(67.9, pace.Config.TypicalTurns(1), 4);
        Assert.Equal(169.276, pace.Config.TypicalTurns(2), 3);
        Assert.InRange(pace.Config.TypicalTurns(1) / 68 - 1, -0.002, 0.002);
        Assert.InRange(pace.Config.TypicalTurns(2) / 169 - 1, -0.002, 0.002);
        Assert.Equal(9.625 / pace.Config.TypicalTurns(1), pace.BlocksPerTurn(1), 9);
        Assert.Equal(10.625 / pace.Config.TypicalTurns(2), pace.BlocksPerTurn(2), 9);
        Assert.True(pace.BlocksPerTurn(1) > pace.BlocksPerTurn(2), "thin trunks feed faster");
        Assert.Equal(0, pace.BlocksPerTurn(0));
        Assert.Equal(0, pace.Rate(0, 5));
        Assert.Equal(pace.Config.TypicalTurns(1), pace.TripTurns(1, null), 6);
        Assert.Equal(pace.Config.TypicalTurns(2), pace.TripTurns(2, 2), 6);
    }

    [Theory]
    [InlineData(null, 1f)]
    [InlineData(2, 1f)]
    [InlineData(3, 1.35f)]
    [InlineData(4, 1.7f)]
    [InlineData(5, 2.05f)]
    public void Better_metal_feeds_faster(int? tier, float speed)
    {
        var pace = RosserFixture.Pace();
        Assert.Equal(speed, pace.HeadSpeed(tier), 5);
        Assert.Equal(pace.BlocksPerTurn(1) * speed / (2 * Math.PI), pace.Rate(1, tier), 6);
        Assert.Equal(pace.Config.TypicalTurns(1) / speed, pace.TripTurns(1, tier), 4);
    }

    [Fact]
    public void Head_speed_per_tier_zero_makes_every_metal_copper()
    {
        var pace = RosserFixture.Pace(new RosserConfig { HeadSpeedPerTier = 0 });
        Assert.Equal(1f, pace.HeadSpeed(5));
    }

    [Fact]
    public void The_pace_follows_the_settings()
    {
        // A short trip: 0.2 turns per log, 0.1 per branch.
        var pace = RosserFixture.Pace(new RosserConfig { RevolutionsPerStoredLog = 0.2f, RevolutionsPerBranch = 0.1f });
        Assert.Equal(10 * 0.2 + 25 * 0.1, pace.TripTurns(1, null), 4);
        Assert.Equal(25 * 0.2 + 61 * 0.1, pace.TripTurns(2, null), 4);
    }

    [Fact]
    public void The_feed_ratio_turns_phi_with_the_trunk()
    {
        // φ moves the trunk b blocks per radian, so dφ/dψ × b is the trunk's rate per shaft radian.
        var rig = RosserFixture.Rig();
        var pace = new RosserPace(rig, new RosserConfig());
        foreach (int k in new[] { 1, 2 })
            foreach (int? tier in new int?[] { null, 3, 5 })
                Assert.Equal(pace.Rate(k, tier), pace.FeedRatio(k, tier, rig.Feed.BlocksPerRadian) * rig.Feed.BlocksPerRadian, 9);
        Assert.Equal(0, pace.FeedRatio(1, 2, 0));
    }

    /// <summary>The mill's turns for a trunk of <paramref name="logs"/> at blade speed
    /// <paramref name="speed"/>: the cut, then the saws wound back up from the bed (BuckingSawmill/Core/SawDepth.cs).</summary>
    private static double MillTurns(int logs, float speed, MillConfig mill) =>
        logs * Cutting.CutRevolutions(mill.RevolutionsPerStoredLog, speed) + mill.RaiseRevolutions;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void One_rosser_keeps_one_mill_of_the_same_metal_fed(int tier)
    {
        // The rosser's trip for any trunk of a class takes as long as its typical trunk's; the
        // mill's grows with the logs. At the defaults the rosser is the faster for every thick trunk
        // (25-48 logs) and every thin one of 8 logs or more, at every metal.
        var pace = RosserFixture.Pace();
        var mill = new MillConfig();
        float millSpeed = Cutting.BladeSpeed(tier, mill.BladeSpeedPerTier);
        Assert.Equal(pace.HeadSpeed(tier), millSpeed);
        for (int logs = 25; logs <= 48; logs++)
            Assert.True(pace.TripTurns(2, tier) <= MillTurns(logs, millSpeed, mill), $"thick {logs} logs, tier {tier}");
        for (int logs = 8; logs <= 24; logs++)
            Assert.True(pace.TripTurns(1, tier) <= MillTurns(logs, millSpeed, mill), $"thin {logs} logs, tier {tier}");
        // and the typical trunks really are the edge for thin: a 7-log trunk at copper leaves the mill waiting
        if (tier == 2)
            Assert.True(pace.TripTurns(1, tier) > MillTurns(7, millSpeed, mill));
    }

    [Fact]
    public void A_path_without_the_stations_is_refused()
    {
        var path = RosserFixture.Rig().Path with { Stations = new Dictionary<string, float> { ["ring"] = 1 } };
        Assert.Throws<ArgumentException>(() => new RosserPace(path, new RosserConfig()));
    }

    [Theory]
    [InlineData("gold", 70, 2)]
    [InlineData("copper", 250, 2)]
    [InlineData("tinbronze", 400, 3)]
    [InlineData("blackbronze", 500, 3)]
    [InlineData("iron", 900, 4)]
    [InlineData("meteoriciron", 1200, 4)]
    [InlineData("steel", 2250, 5)]
    public void Head_metal_fallbacks(string metal, int durability, int tier)
    {
        Assert.Equal(durability, HeadMetals.SpudDurability(metal));
        Assert.Equal(tier, HeadMetals.SawTier(metal));
    }

    [Fact]
    public void Unknown_metals_have_no_fallback()
    {
        Assert.Null(HeadMetals.SpudDurability("cupronickel"));
        Assert.Null(HeadMetals.SawTier(null));
    }
}
