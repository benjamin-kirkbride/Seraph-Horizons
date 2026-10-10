using SeraphHorizons.Mod.Eidolon.Core;
using Xunit;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Eidolon/Core/EidolonCarrying.cs (#676): the lift and set-down timings, where the eidolon
/// stands beside a block, and that standing there its box is clear of the block's cell.</summary>
public class EidolonCarryingTests
{
    private sealed class Voxels : IWideSpace
    {
        public readonly HashSet<(int, int, int)> Solid = [];

        public Voxels Floor(int reach, int y = -1)
        {
            for (int x = -reach; x <= reach; x++)
            for (int z = -reach; z <= reach; z++)
                Solid.Add((x, y, z));
            return this;
        }

        public bool Free(double x, double y, double z)
        {
            const double eps = 1e-6, half = EidolonCarrying.HalfWidth, height = 3.75;
            for (int bx = (int)Math.Floor(x - half); bx <= (int)Math.Floor(x + half - eps); bx++)
            for (int by = (int)Math.Floor(y); by <= (int)Math.Floor(y + height - eps); by++)
            for (int bz = (int)Math.Floor(z - half); bz <= (int)Math.Floor(z + half - eps); bz++)
                if (Solid.Contains((bx, by, bz)))
                    return false;
            return true;
        }

        public float Cost(PathCell cell) => 0;
    }

    [Fact]
    public void Event_frames_fall_inside_their_one_shots()
    {
        Assert.Equal(1.5, EidolonCarrying.LiftSeconds, 6);
        Assert.Equal(22 / 30.0, EidolonCarrying.GrabSeconds, 6);
        Assert.Equal(28 / 30.0, EidolonCarrying.ReleaseSeconds, 6);
        Assert.True(EidolonCarrying.GrabSeconds < EidolonCarrying.LiftSeconds);
        Assert.True(EidolonCarrying.ReleaseSeconds < EidolonCarrying.SetDownSeconds);
    }

    [Fact]
    public void A_block_on_open_ground_has_four_stands_nearest_first_each_clear_of_its_cell_and_facing_it()
    {
        var space = new Voxels().Floor(8);
        space.Solid.Add((0, 0, 0));   // the block
        var stands = EidolonCarrying.Stands(0, 0, 0, fromX: 6, fromZ: 0.5, space);
        Assert.Equal(4, stands.Count);
        Assert.Equal(0.5 + EidolonCarrying.Reach, stands[0].X, 6);   // the east side, nearest (6, 0.5)
        Assert.Equal(0.5, stands[0].Z, 6);
        foreach (var s in stands)
        {
            Assert.Equal(0, s.Y);
            Assert.False(EidolonCarrying.Overlaps(s.X, s.Z, 0, 0));
            // Facing the block's centre: one step along the yaw lands on it.
            double toX = s.X + Math.Sin(s.Yaw) * EidolonCarrying.Reach, toZ = s.Z + Math.Cos(s.Yaw) * EidolonCarrying.Reach;
            Assert.Equal(0.5, toX, 6);
            Assert.Equal(0.5, toZ, 6);
        }
    }

    [Fact]
    public void A_side_against_a_wall_or_over_a_hole_is_no_stand_and_a_step_down_is()
    {
        var space = new Voxels().Floor(8);
        // A wall on the west, a hole on the north (no floor), the south a block lower.
        for (int y = 0; y < 5; y++)
        for (int z = -3; z <= 3; z++)
            space.Solid.Add((-2, y, z));
        for (int x = -2; x <= 2; x++)
        for (int z = -4; z <= -1; z++)
            space.Solid.Remove((x, -1, z));
        for (int x = -2; x <= 2; x++)
        for (int z = 1; z <= 4; z++)
        {
            space.Solid.Remove((x, -1, z));
            space.Solid.Add((x, -2, z));
        }
        var stands = EidolonCarrying.Stands(0, 0, 0, 0, 0, space);
        Assert.DoesNotContain(stands, s => s.X < 0);              // west: the wall
        Assert.DoesNotContain(stands, s => s.Z < 0);              // north: nothing to stand on
        Assert.Contains(stands, s => s.Z > 1 && s.Y == -1);       // south: a block down
        Assert.Contains(stands, s => s.X > 1 && s.Y == 0);        // east: level
    }

    [Fact]
    public void Near_takes_a_traverser_stop_and_not_a_wrong_level()
    {
        var stand = new CarryStand(10, 4, 10, 0);
        Assert.True(EidolonCarrying.Near(stand, 10.5, 4, 10.4));
        Assert.False(EidolonCarrying.Near(stand, 11, 4, 11));
        Assert.False(EidolonCarrying.Near(stand, 10, 6, 10));
    }

    [Fact]
    public void Its_box_overlaps_a_cell_it_stands_in_or_reaches_into()
    {
        Assert.True(EidolonCarrying.Overlaps(0.5, 0.5, 0, 0));
        Assert.True(EidolonCarrying.Overlaps(1.8, 0.5, 0, 0));    // 0.95 from the cell's edge: 0.85 wide
        Assert.False(EidolonCarrying.Overlaps(1.9, 0.5, 0, 0));
    }
}
