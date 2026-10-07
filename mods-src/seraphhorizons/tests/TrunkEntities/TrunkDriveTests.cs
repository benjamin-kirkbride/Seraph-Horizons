using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkDriveTests
{
    [Fact]
    public void Land_speed_runs_from_a_walk_at_one_log_to_half_at_forty_eight()
    {
        Assert.Equal(TrunkDrive.WalkBlocksPerSecond, TrunkDrive.Speed(1, false), 9);
        Assert.Equal(TrunkDrive.WalkBlocksPerSecond, TrunkDrive.Speed(0, false), 9);
        Assert.Equal(TrunkDrive.WalkBlocksPerSecond / 2, TrunkDrive.Speed(48, false), 9);
        Assert.Equal(TrunkDrive.WalkBlocksPerSecond / 2, TrunkDrive.Speed(200, false), 9);
        for (int logs = 0; logs < 60; logs++)
            Assert.True(TrunkDrive.Speed(logs + 1, false) <= TrunkDrive.Speed(logs, false));
        // linear: the midpoint of 1 and 47 is halfway between
        double mid = (TrunkDrive.Speed(1, false) + TrunkDrive.Speed(47, false)) / 2;
        Assert.Equal(mid, TrunkDrive.Speed(24, false), 9);
    }

    [Fact]
    public void Afloat_never_under_three_quarters_of_the_raft()
    {
        double floor = 0.75 * TrunkDrive.RaftBlocksPerSecond;
        Assert.Equal(floor, TrunkDrive.Speed(48, true), 9);
        Assert.True(TrunkDrive.Speed(48, true) > TrunkDrive.Speed(48, false));
        Assert.Equal(TrunkDrive.Speed(1, false), TrunkDrive.Speed(1, true), 9);
        for (int logs = 0; logs < 60; logs++)
            Assert.True(TrunkDrive.Speed(logs, true) >= floor - 1e-9);
    }

    [Fact]
    public void Turning_is_a_radian_a_second_light_and_half_heavy()
    {
        Assert.Equal(1.0, TrunkDrive.Turn(1, false), 9);
        Assert.Equal(0.5, TrunkDrive.Turn(48, false), 9);
        Assert.Equal(0.75, TrunkDrive.Turn(48, true), 9);
    }

    [Fact]
    public void Controls_ask_for_speed_and_turn()
    {
        Assert.Equal(3, TrunkDrive.Along(true, false, 3));
        Assert.Equal(-3, TrunkDrive.Along(false, true, 3));
        Assert.Equal(0, TrunkDrive.Along(true, true, 3));
        Assert.Equal(0, TrunkDrive.Along(false, false, 3));
        Assert.True(TrunkDrive.Turning(true, false, 1) > 0);
        Assert.True(TrunkDrive.Turning(false, true, 1) < 0);
        Assert.Equal(0, TrunkDrive.Turning(true, true, 1));
    }

    [Fact]
    public void A_steers_the_far_end_to_the_drivers_left()
    {
        // Driver of end +1 at yaw 0 stands at -z and faces +z (yaw 0). Facing +z (south), the
        // left hand is +x (east): A must move the far end (-1) towards +x, and so the driver's
        // own stand towards -x.
        Assert.Equal(0, TrunkDrive.FacingYaw(0, 1), 9);
        var (x0, _) = TrunkDrive.Stand(0, 0, 0, 4, 1);
        var (x1, _) = TrunkDrive.Stand(0, 0, TrunkDrive.Turning(true, false, 1) * 0.1, 4, 1);
        Assert.True(x1 < x0, $"{x0} -> {x1}");
        var (fx0, _) = TrunkPull.EndPos(0, 0, 0, 4, -1);
        var (fx1, _) = TrunkPull.EndPos(0, 0, TrunkDrive.Turning(true, false, 1) * 0.1, 4, -1);
        Assert.True(fx1 > fx0, $"far end {fx0} -> {fx1}");
        // the game's view at yaw y looks along (sin y, cos y): the driver looks over the middle
        foreach (double yaw in new[] { 0, 0.7, 2, -2.5 })
            foreach (int end in new[] { 1, -1 })
            {
                var (sx, sz) = TrunkDrive.Stand(10, 20, yaw, 4, end);
                double f = TrunkDrive.FacingYaw(yaw, end);
                double toMid = Math.Atan2(10 - sx, 20 - sz);
                Assert.Equal(0, TrunkPull.Wrap(f - toMid), 9);
            }
    }

    [Fact]
    public void The_driver_stands_just_beyond_the_taken_end_and_W_pushes_the_trunk_away()
    {
        var (ex, ez) = TrunkPull.EndPos(3, 4, 0.4, 5, -1);
        var (sx, sz) = TrunkDrive.Stand(3, 4, 0.4, 5, -1);
        Assert.Equal(TrunkDrive.StandOff, Math.Sqrt((sx - ex) * (sx - ex) + (sz - ez) * (sz - ez)), 9);
        Assert.True(Math.Pow(sx - 3, 2) + Math.Pow(sz - 4, 2) > Math.Pow(ex - 3, 2) + Math.Pow(ez - 4, 2));
        foreach (double yaw in new[] { 0, 0.4, 2, -2.5 })
            foreach (int end in new[] { 1, -1 })
            {
                var (dx, dz) = TrunkDrive.Stand(0, 0, yaw, 5, end);
                // W (positive along): away from the driver, the far end leading, blocks per 1/60 s
                var (mx, mz) = TrunkDrive.DriveMotion(yaw, end, 6);
                Assert.Equal(0.1, Math.Sqrt(mx * mx + mz * mz), 9);
                Assert.True(mx * dx + mz * dz < 0, $"W does not push away from the driver at {yaw}, {end}");
                // and the way the driver faces
                double f = TrunkDrive.FacingYaw(yaw, end);
                Assert.Equal(0, TrunkPull.Wrap(f - Math.Atan2(mx, mz)), 9);
                // S: back into them
                var (bx, bz) = TrunkDrive.DriveMotion(yaw, end, -6);
                Assert.True(bx * dx + bz * dz > 0);
            }
    }

    [Fact]
    public void Gaits_are_walk_and_walk_back_only()
    {
        Assert.Equal(EnumDriveGait.Idle, TrunkDrive.Gait(false, false, false, false));
        Assert.Equal(EnumDriveGait.Walk, TrunkDrive.Gait(true, false, false, false));
        Assert.Equal(EnumDriveGait.WalkBack, TrunkDrive.Gait(false, true, false, false));
        Assert.Equal(EnumDriveGait.Walk, TrunkDrive.Gait(true, false, true, false));
        Assert.Equal(EnumDriveGait.WalkBack, TrunkDrive.Gait(false, true, false, true));
        // turning on the spot steps, W and S together cancel
        Assert.Equal(EnumDriveGait.Walk, TrunkDrive.Gait(false, false, true, false));
        Assert.Equal(EnumDriveGait.Walk, TrunkDrive.Gait(true, true, false, true));
        Assert.Equal(EnumDriveGait.Idle, TrunkDrive.Gait(true, true, true, true));
    }

    [Fact]
    public void Ease_reaches_speed_in_a_few_tenths_and_never_overshoots()
    {
        double v = 0;
        int ticks = 0;
        while (v < 0.95 * 4 && ticks < 100)
        {
            v = TrunkDrive.Ease(v, 4, 1 / 30.0);
            Assert.True(v <= 4);
            ticks++;
        }
        Assert.InRange(ticks / 30.0, 0.1, 0.5);
        Assert.Equal(4, TrunkDrive.Ease(3.99995, 4, 0.03));
        Assert.Equal(1, TrunkDrive.Ease(1, 4, 0));
    }
}
