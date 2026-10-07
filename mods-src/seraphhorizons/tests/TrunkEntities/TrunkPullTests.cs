using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkPullTests
{
    private const double Eps = 1e-9;

    [Fact]
    public void End_plus_one_is_where_Turned_puts_local_plus_z()
    {
        // TrunkBoxes.Turned maps local (0, z) to (z sin(yaw+pi), z cos(yaw+pi)).
        foreach (float yaw in new[] { 0f, 0.7f, MathF.PI / 2, -2.1f })
        {
            var (x, z) = TrunkPull.EndPos(0, 0, yaw, 4, 1);
            Assert.Equal(2 * Math.Sin(yaw + Math.PI), x, 6);
            Assert.Equal(2 * Math.Cos(yaw + Math.PI), z, 6);
            var (ox, oz) = TrunkPull.EndPos(0, 0, yaw, 4, -1);
            Assert.Equal(-x, ox, 9);
            Assert.Equal(-z, oz, 9);
        }
    }

    [Fact]
    public void End_matches_the_turned_collision_boxes_extent()
    {
        foreach (float yaw in new[] { 0f, MathF.PI / 2 })
        {
            var boxes = TrunkBoxes.Turned(TrunkClass.Thin, yaw);
            var (x, z) = TrunkPull.EndPos(0, 0, yaw, TrunkBox.Size(TrunkClass.Thin).Length, 1);
            Assert.Contains(boxes, b => b.X1 - 0.01 <= x && x <= b.X2 + 0.01 && b.Z1 - 0.01 <= z && z <= b.Z2 + 0.01);
        }
    }

    [Fact]
    public void Nearer_end_is_the_side_the_point_is_on()
    {
        var (x, z) = TrunkPull.EndPos(10, 20, 0.3, 5, 1);
        Assert.Equal(1, TrunkPull.NearerEnd(10, 20, 0.3, x + 0.2, z));
        var (ox, oz) = TrunkPull.EndPos(10, 20, 0.3, 5, -1);
        Assert.Equal(-1, TrunkPull.NearerEnd(10, 20, 0.3, ox, oz - 0.1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Yaw_facing_points_that_end_along_the_direction(int end)
    {
        double yaw = TrunkPull.YawFacing(3, -4, end);
        var (ax, az) = TrunkPull.Axis(yaw, end);
        Assert.Equal(0.6, ax, 9);
        Assert.Equal(-0.8, az, 9);
    }

    [Fact]
    public void Step_yaw_takes_the_short_way_round_and_is_capped()
    {
        // From 170 deg to -170 deg is 20 deg forwards, across the wrap.
        double from = 170 * Math.PI / 180, to = -170 * Math.PI / 180;
        double step = TrunkPull.StepYaw(from, to, 0.1);
        Assert.Equal(TrunkPull.Wrap(from + 0.1), step, 9);
        Assert.Equal(to, TrunkPull.StepYaw(from, to, 1), 9);
        Assert.Equal(0.0, TrunkPull.StepYaw(0.2, 0, 0.5), 9);
    }

    [Fact]
    public void Wrap_lands_in_minus_pi_to_pi()
    {
        Assert.Equal(-Math.PI, TrunkPull.Wrap(Math.PI), 9);
        Assert.Equal(0.5, TrunkPull.Wrap(0.5 + 4 * Math.PI), 9);
        Assert.Equal(0.5, TrunkPull.Wrap(0.5 - 6 * Math.PI), 9);
    }

    [Fact]
    public void Afloat_a_rope_sees_a_lighter_trunk()
    {
        Assert.Equal(400 / TrunkPull.WaterLightening, TrunkPull.EffectiveWeight(400, true), 9);
        Assert.Equal(400, TrunkPull.EffectiveWeight(400, false), 9);
    }

    // A thin trunk along z at yaw 0, standing on the ground at y 64 (cells below 64 solid).
    private static IReadOnlyList<Box> Thin => new[] { new Box(-0.5f, 0, -2, 0.5f, 1, 2) };

    private static Func<int, int, int, bool> Ground(params (int X, int Y, int Z)[] extra) =>
        (x, y, z) => y < 64 || extra.Contains((x, y, z));

    [Fact]
    public void A_trunk_pulled_against_a_one_block_rise_lifts()
    {
        // Pulled along +z, a block at z 2 (just ahead of the end at 2.0 - the probe reaches it).
        var solid = Ground((0, 64, 2));
        // The whole block in one go (plus a skin), not a part of it.
        double lift = TrunkStep.Lift(Thin, 0.5, 64, 0, 0, 0.05, solid);
        Assert.True(lift > 1 && lift < 1.05, $"lift {lift}");
        // Left part way up, it finishes the rise; at the top it stops.
        double rest = TrunkStep.Lift(Thin, 0.5, 64.9, 0, 0, 0.05, solid);
        Assert.True(rest > 0.1 && rest < 0.15, $"lift {rest}");
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 65.02, 0, 0, 0.05, solid));
    }

    [Fact]
    public void No_step_on_flat_ground_without_pull_up_a_cliff_or_under_a_ceiling()
    {
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 64, 0, 0, 0.05, Ground()));
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 64, 0, 0, 0.001, Ground((0, 64, 2))));
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 64, 0, 0, 0.05, Ground((0, 64, 2), (0, 65, 2))));
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 64, 0, 0, 0.05, Ground((0, 64, 2), (0, 65, 0))));
        // Moving away from the block: no step.
        Assert.Equal(0, TrunkStep.Lift(Thin, 0.5, 64, 0, 0, -0.05, Ground((0, 64, 2))));
    }
}
