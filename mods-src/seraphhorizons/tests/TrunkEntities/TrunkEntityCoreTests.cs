using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkEntityConfigTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var c = new TrunkEntityConfig();
        Assert.Equal(8f, c.WeightPerLog);
        Assert.Equal(1f, c.CarrySpeedAtOneLog);
        Assert.Equal(0.5f, c.CarrySpeedAtMaxLogs);
        Assert.Equal(0.5f, c.SpudSecondsPerLog);
        Assert.Empty(c.Sanitise());
    }

    [Fact]
    public void Out_of_range_values_fall_back_to_the_defaults()
    {
        var c = new TrunkEntityConfig
        {
            WeightPerLog = -1, CarrySpeedAtOneLog = 2, CarrySpeedAtMaxLogs = float.NaN,
            SpudSecondsPerLog = 100,
        };
        var fixes = c.Sanitise();
        Assert.Equal(4, fixes.Count);
        Assert.Contains(fixes, f => f.StartsWith("WeightPerLog -1 is out of range"));
        Assert.Equal(TrunkEntityConfig.Defaults.WeightPerLog, c.WeightPerLog);
        Assert.Equal(TrunkEntityConfig.Defaults.CarrySpeedAtOneLog, c.CarrySpeedAtOneLog);
        Assert.Equal(TrunkEntityConfig.Defaults.CarrySpeedAtMaxLogs, c.CarrySpeedAtMaxLogs);
        Assert.Equal(TrunkEntityConfig.Defaults.SpudSecondsPerLog, c.SpudSecondsPerLog);
        Assert.Empty(c.Sanitise());
    }

    [Fact]
    public void Edge_values_in_range_are_kept()
    {
        var c = new TrunkEntityConfig { WeightPerLog = 0, CarrySpeedAtOneLog = 1,CarrySpeedAtMaxLogs = 0, SpudSecondsPerLog = 0 };
        Assert.Empty(c.Sanitise());
        Assert.Equal(0, c.SpudSecondsPerLog);
    }
}

public class TrunkWeightTests
{
    private static readonly TrunkEntityConfig C = new();

    [Theory]
    [InlineData(0, 10f)]
    [InlineData(10, 90f)]
    [InlineData(48, 394f)]
    [InlineData(-3, 10f)]
    public void Weight_is_ten_plus_eight_per_log(int logs, float weight) => Assert.Equal(weight, TrunkWeight.Weight(logs, C));

    [Fact]
    public void Weight_follows_the_setting() =>
        Assert.Equal(10f + 5 * 20f, TrunkWeight.Weight(5, new TrunkEntityConfig { WeightPerLog = 20 }));

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(1, 1f)]
    [InlineData(25, 0.7447f)]
    [InlineData(48, 0.5f)]
    [InlineData(100, 0.5f)]
    public void Carry_speed_runs_linearly_from_a_normal_walk_at_one_log_to_half_at_forty_eight(int logs, float speed) =>
        Assert.Equal(speed, TrunkWeight.CarrySpeed(logs, C), 4);

    [Fact]
    public void Carry_speed_never_rises_with_logs()
    {
        for (int logs = 0; logs < 60; logs++)
            Assert.True(TrunkWeight.CarrySpeed(logs + 1, C) <= TrunkWeight.CarrySpeed(logs, C));
    }

    [Theory]
    [InlineData(0, 2f)]
    [InlineData(3, 2f)]
    [InlineData(4, 2f)]
    [InlineData(10, 5f)]
    [InlineData(48, 24f)]
    public void Spud_hold_is_half_a_second_per_log_two_at_least(int logs, float seconds) =>
        Assert.Equal(seconds, TrunkWeight.SpudSeconds(logs, C), 4);
}

public class TrunkBoxesTests
{
    [Fact]
    public void Thin_collision_is_four_cubes_along_z()
    {
        var boxes = TrunkBoxes.Collision(TrunkClass.Thin);
        Assert.Equal(
            [new Box(-0.5f, 0, -2, 0.5f, 1, -1), new Box(-0.5f, 0, -1, 0.5f, 1, 0), new Box(-0.5f, 0, 0, 0.5f, 1, 1), new Box(-0.5f, 0, 1, 0.5f, 1, 2)],
            boxes);
        Assert.Equal(new Box(-0.5f, 0, -2, 0.5f, 1, 2), TrunkBoxes.Selection(TrunkClass.Thin));
        Assert.Equal((1f, 1f), TrunkBoxes.Hitbox(TrunkClass.Thin));
    }

    [Fact]
    public void Thick_collision_is_four_overlapping_two_block_cubes_along_z()
    {
        var boxes = TrunkBoxes.Collision(TrunkClass.Thick);
        Assert.Equal(4, boxes.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(new Box(-1, 0, -2.5f + i, 1, 2, -0.5f + i), boxes[i]);
        Assert.Equal(new Box(-1, 0, -2.5f, 1, 2, 2.5f), TrunkBoxes.Selection(TrunkClass.Thick));
        Assert.Equal((2f, 2f), TrunkBoxes.Hitbox(TrunkClass.Thick));
    }

    [Fact]
    public void None_has_no_boxes()
    {
        Assert.Empty(TrunkBoxes.Collision(TrunkClass.None));
        Assert.Empty(TrunkBoxes.Turned(TrunkClass.None, 1f));
    }

    [Fact]
    public void Radius_reaches_the_ends() =>
        Assert.Equal(MathF.Sqrt(16 + 1 + 1) / 2, TrunkBoxes.Radius(TrunkClass.Thin), 5);

    [Theory]
    [InlineData(TrunkClass.Thin)]
    [InlineData(TrunkClass.Thick)]
    public void At_yaw_zero_and_a_half_turn_the_row_stays_along_z(TrunkClass trunk)
    {
        foreach (float yaw in new[] { 0f, MathF.PI })
        {
            var turned = TrunkBoxes.Turned(trunk, yaw).OrderBy(b => b.Z1).ToList();
            var plain = TrunkBoxes.Collision(trunk);
            Assert.Equal(plain.Count, turned.Count);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(plain[i].X1, turned[i].X1, 4);
                Assert.Equal(plain[i].Z1, turned[i].Z1, 4);
                Assert.Equal(plain[i].Z2, turned[i].Z2, 4);
                Assert.Equal(plain[i].Y2, turned[i].Y2, 4);
            }
        }
    }

    [Fact]
    public void A_quarter_turn_lays_the_row_along_x_one_box_wide()
    {
        var turned = TrunkBoxes.Turned(TrunkClass.Thin, MathF.PI / 2).OrderBy(b => b.X1).ToList();
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(-2 + i, turned[i].X1, 4);
            Assert.Equal(-1 + i, turned[i].X2, 4);
            Assert.Equal(-0.5f, turned[i].Z1, 4);
            Assert.Equal(0.5f, turned[i].Z2, 4);
        }
    }

    [Fact]
    public void A_quarter_turn_lays_a_thick_trunk_along_x_two_blocks_wide()
    {
        var turned = TrunkBoxes.Turned(TrunkClass.Thick, MathF.PI / 2).OrderBy(b => b.X1).ToList();
        Assert.Equal(4, turned.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(-2.5f + i, turned[i].X1, 4);
            Assert.Equal(-0.5f + i, turned[i].X2, 4);
            Assert.Equal(-1f, turned[i].Z1, 4);
            Assert.Equal(1f, turned[i].Z2, 4);
        }
        // the same footprint as along z, turned: 5 × 2, not 6 × 1
        Assert.Equal(-2.5f, turned.Min(b => b.X1), 4);
        Assert.Equal(2.5f, turned.Max(b => b.X2), 4);
    }

    [Fact]
    public void Turning_keeps_the_boxes_middles_at_their_distance()
    {
        var plain = TrunkBoxes.Collision(TrunkClass.Thick);
        var turned = TrunkBoxes.Turned(TrunkClass.Thick, 0.7f);
        for (int i = 0; i < plain.Count; i++)
        {
            float r0 = MathF.Abs((plain[i].Z1 + plain[i].Z2) / 2);
            float mx = (turned[i].X1 + turned[i].X2) / 2, mz = (turned[i].Z1 + turned[i].Z2) / 2;
            Assert.Equal(r0, MathF.Sqrt(mx * mx + mz * mz), 4);
        }
    }
}
