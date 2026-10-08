using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkCarryPoseTests
{
    // Carry On's first-person frame for the hands: x forward, y up, z right.
    private static (float Forward, float Up, float Right) Moved(float x, float y, float z)
    {
        var m = TrunkCarryPose.FirstPersonAdjust();
        var (ox, oy, oz) = TrunkCarryPose.Apply(m, 0, 0, 0);
        var (px, py, pz) = TrunkCarryPose.Apply(m, x, y, z);
        return (px - ox, py - oy, pz - oz);
    }

    [Fact]
    public void The_first_person_frame_becomes_the_shoulder_frame()
    {
        // As in third person on the raised forearm: x up, y back, z right.
        Near((0f, 1f, 0f), Moved(1, 0, 0));
        Near((-1f, 0f, 0f), Moved(0, 1, 0));
        Near((0f, 0f, 1f), Moved(0, 0, 1));
    }

    private static void Near((float, float, float) expected, (float, float, float) actual)
    {
        Assert.Equal(expected.Item1, actual.Item1, 5);
        Assert.Equal(expected.Item2, actual.Item2, 5);
        Assert.Equal(expected.Item3, actual.Item3, 5);
    }

    [Fact]
    public void A_trunk_tipped_by_rotation_x_90_then_lies_front_to_back()
    {
        // rotationX 90 takes the block's z (the trunk's length) to the frame's -y: forward.
        float c = MathF.Cos(MathF.PI / 2), s = MathF.Sin(MathF.PI / 2);
        var (fx, fy, fz) = (0f, -s, c);
        var (forward, up, right) = Moved(fx, fy, fz);
        Assert.Equal(1f, forward, 5);
        Assert.Equal(0f, up, 5);
        Assert.Equal(0f, right, 5);
    }

    [Fact]
    public void The_point_moves_back_up_and_left()
    {
        var (x, y, z) = TrunkCarryPose.Apply(TrunkCarryPose.FirstPersonAdjust(), 0, 0, 0);
        Assert.Equal(-TrunkCarryPose.FirstPersonBack, x);
        Assert.Equal(TrunkCarryPose.FirstPersonUp, y);
        Assert.Equal(TrunkCarryPose.FirstPersonRight, z);
        Assert.True(TrunkCarryPose.FirstPersonRight < 0);
    }
}
