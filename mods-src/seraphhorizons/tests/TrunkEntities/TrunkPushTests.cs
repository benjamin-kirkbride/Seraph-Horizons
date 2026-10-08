using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkPushTests
{
    // A player's box (0.6 wide, 1.85 tall) with its feet's middle at (x, y, z).
    private static Box Player(double x, double y, double z) =>
        new((float)(x - 0.3), (float)y, (float)(z - 0.3), (float)(x + 0.3), (float)(y + 1.85), (float)(z + 0.3));

    private static TrunkPush.Footprint Thin => TrunkPush.Footprint.Of(TrunkClass.Thin, 0);
    private static TrunkPush.Footprint Thick => TrunkPush.Footprint.Of(TrunkClass.Thick, 0);

    private static void Direction(TrunkPush.Exit exit, double x, double z)
    {
        Assert.Equal(x, exit.X, 6);
        Assert.Equal(0, exit.Y, 6);
        Assert.Equal(z, exit.Z, 6);
    }

    [Fact]
    public void Nothing_to_do_outside_or_just_touching()
    {
        Assert.Null(TrunkPush.Out(Thin, Player(2, 0, 0)));
        Assert.Null(TrunkPush.Out(Thin, Player(0.8, 0, 0)));    // touching x = 0.5
        Assert.Null(TrunkPush.Out(Thin, Player(0, 0, 2.3)));    // touching the end at z = 2
        Assert.Null(TrunkPush.Out(Thin, Player(0, 1, 0)));      // standing on it
        // off a corner diagonally: the box's corner would touch, the round player does not
        Assert.Null(TrunkPush.Out(Thin, Player(0.75, 0, 2.25)));
    }

    [Fact]
    public void In_the_middle_the_way_out_is_across_not_along()
    {
        var exit = TrunkPush.Out(Thin, Player(0.1, 0, 0.5))!.Value;
        Direction(exit, 1, 0);
        Assert.Equal(0.7, exit.Distance, 2);              // 0.5 - (0.1 - 0.3) + skin
        var other = TrunkPush.Out(Thin, Player(-0.2, 0, -1))!.Value;
        Direction(other, -1, 0);
        // a thick trunk too: 1.3 across, not 2.8 along
        var thick = TrunkPush.Out(Thick, Player(0, 0, 0))!.Value;
        Assert.Equal(0, thick.Z, 6);
        Assert.Equal(1.3, thick.Distance, 2);
    }

    [Fact]
    public void Near_an_end_the_way_out_is_past_the_end()
    {
        var exit = TrunkPush.Out(Thin, Player(0, 0, 1.9))!.Value;
        Direction(exit, 0, 1);
        Assert.Equal(0.4, exit.Distance, 2);
    }

    [Fact]
    public void Beside_the_trunk_the_way_out_is_straight_away_from_it()
    {
        // grazing the side: pushed square to it by the overlap
        var exit = TrunkPush.Out(Thin, Player(0.7, 0, 1))!.Value;
        Direction(exit, 1, 0);
        Assert.Equal(0.1, exit.Distance, 2);
        // grazing a corner: pushed away from the corner, diagonally
        var corner = TrunkPush.Out(Thin, Player(0.6, 0, 2.1))!.Value;
        Assert.Equal(Math.Sqrt(0.5), corner.X, 3);
        Assert.Equal(Math.Sqrt(0.5), corner.Z, 3);
    }

    [Fact]
    public void Feet_near_the_top_go_up_onto_it()
    {
        var exit = TrunkPush.Out(Thin, Player(0, 0.8, 0))!.Value;
        Assert.True(exit.Up);
        Assert.Equal(0.2, exit.Distance, 2);
        // deeper than the margin: off the side instead
        Assert.False(TrunkPush.Out(Thin, Player(0, 0.3, 0))!.Value.Up);
    }

    [Fact]
    public void A_step_is_capped_and_repeated_steps_get_out()
    {
        var p = Player(0, 0, 0.5);
        int steps = 0;
        while (TrunkPush.Out(Thin, p) is { } exit && steps < 20)
        {
            var (dx, dy, dz) = exit.Step();
            Assert.True(Math.Sqrt(dx * dx + dy * dy + dz * dz) <= TrunkPush.MaxStep + 1e-9);
            p = TrunkPush.Shift(p, dx, dy, dz);
            steps++;
        }
        Assert.InRange(steps, 1, 4);
        Assert.True(TrunkPush.MaxStep > 0.07 * 2, "a walking player would sink in");
    }

    // A point at `across` from the axis and `along` it, in a trunk at `yaw`'s frame, in the world.
    private static (double X, double Z) At(TrunkPush.Footprint t, double across, double along) => t.ToWorld(across, along);

    [Fact]
    public void A_turned_trunks_side_is_a_smooth_wall_square_to_its_axis()
    {
        // The first build pushed along ±x/±z out of the turned axis-aligned boxes, a staircase
        // along a diagonal: the depth and the direction jumped along the side.
        var t = TrunkPush.Footprint.Of(TrunkClass.Thin, 0.6);
        var (ox, oz) = t.ToWorld(1, 0);   // the side's outward normal in the world
        double? last = null;
        for (double along = -1.8; along <= 1.8; along += 0.05)
        {
            var (x, z) = At(t, 0.75, along);   // 0.05 into the side (0.5 + 0.3 - 0.75)
            var exit = TrunkPush.Out(t, Player(x, 0, z))!.Value;
            double angle = Math.Acos(Math.Clamp(exit.X * ox + exit.Z * oz, -1, 1));
            Assert.True(angle < 5 * Math.PI / 180, $"at {along:F2} along, the way out is {angle * 180 / Math.PI:F1}° off square");
            Assert.Equal(0.05, exit.Distance, 2);
            if (last is { } l)
                Assert.True(Math.Abs(exit.Distance - l) < 0.05, $"at {along:F2} along, the depth jumped {l:F3} -> {exit.Distance:F3}");
            last = exit.Distance;
        }
    }

    [Fact]
    public void A_turned_trunk_is_left_across_from_its_middle_and_along_from_near_an_end()
    {
        var t = TrunkPush.Footprint.Of(TrunkClass.Thin, 0.6);
        var (mx, mz) = At(t, 0.1, 0.3);
        var middle = TrunkPush.Out(t, Player(mx, 0, mz))!.Value;
        var (ax, az) = t.ToWorld(1, 0);
        Assert.Equal(ax, middle.X, 6);
        Assert.Equal(az, middle.Z, 6);
        Assert.Equal(0.7, middle.Distance, 2);
        var (ex, ez) = At(t, 0.1, -1.9);
        var end = TrunkPush.Out(t, Player(ex, 0, ez))!.Value;
        var (bx, bz) = t.ToWorld(0, -1);
        Assert.Equal(bx, end.X, 6);
        Assert.Equal(bz, end.Z, 6);
        Assert.Equal(0.4, end.Distance, 2);
        // stepped out the whole way, the player is clear
        var p = Player(mx, 0, mz);
        var (dx, dy, dz) = middle.Step(0);
        Assert.False(TrunkPush.Inside(t, TrunkPush.Shift(p, dx, dy, dz)));
    }

    [Fact]
    public void A_quarter_turned_trunk_lies_along_x()
    {
        var t = TrunkPush.Footprint.Of(TrunkClass.Thin, Math.PI / 2);
        Assert.NotNull(TrunkPush.Out(t, Player(1.9, 0, 0)));
        Assert.Null(TrunkPush.Out(t, Player(0, 0, 1.9)));
        var exit = TrunkPush.Out(t, Player(1.0, 0, 0.1))!.Value;
        Assert.Equal(0, exit.X, 6);
        Assert.Equal(1, exit.Z, 6);
    }

    [Fact]
    public void Motion_into_the_trunk_is_stopped_and_away_kept()
    {
        var exit = new TrunkPush.Exit(1, 0, 0, 0.1);
        Assert.Equal((0.0, 0.0, 0.05), TrunkPush.Stop(exit, -0.07, 0, 0.05));
        Assert.Equal((0.07, 0.0, 0.0), TrunkPush.Stop(exit, 0.07, 0, 0));
        var up = new TrunkPush.Exit(0, 1, 0, 0.1);
        Assert.Equal((0.01, 0.0, 0.0), TrunkPush.Stop(up, 0.01, -0.2, 0));
        // a slanted wall takes away only the part into it
        double s = Math.Sqrt(0.5);
        var (x, _, z) = TrunkPush.Stop(new TrunkPush.Exit(s, 0, s, 0.1), -0.1, 0, 0);
        Assert.Equal(-0.05, x, 6);
        Assert.Equal(0.05, z, 6);
    }
}
