using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkPushTests
{
    // A player's box (0.6 wide, 1.85 tall) with its feet's middle at (x, y, z).
    private static Box Player(double x, double y, double z) =>
        new((float)(x - 0.3), (float)y, (float)(z - 0.3), (float)(x + 0.3), (float)(y + 1.85), (float)(z + 0.3));

    private static IReadOnlyList<Box> Thin => TrunkBoxes.Collision(TrunkClass.Thin);
    private static IReadOnlyList<Box> Thick => TrunkBoxes.Collision(TrunkClass.Thick);

    [Fact]
    public void Nothing_to_do_outside_or_just_touching()
    {
        Assert.Null(TrunkPush.Out(Thin, Player(2, 0, 0)));
        Assert.Null(TrunkPush.Out(Thin, Player(0.8, 0, 0)));    // touching x = 0.5
        Assert.Null(TrunkPush.Out(Thin, Player(0, 0, 2.3)));    // touching the end at z = 2
    }

    [Fact]
    public void In_the_middle_box_the_way_out_is_across_not_along()
    {
        var exit = TrunkPush.Out(Thin, Player(0.1, 0, 0.5))!.Value;
        Assert.Equal((1, 0, 0), (exit.X, exit.Y, exit.Z));
        Assert.Equal(0.7, exit.Distance, 2);              // 0.5 - (0.1 - 0.3) + skin
        var other = TrunkPush.Out(Thin, Player(-0.2, 0, -1))!.Value;
        Assert.Equal(-1, other.X);
    }

    [Fact]
    public void Near_an_end_the_way_out_is_past_the_end()
    {
        var exit = TrunkPush.Out(Thin, Player(0, 0, 1.9))!.Value;
        Assert.Equal((0, 0, 1), (exit.X, exit.Y, exit.Z));
        Assert.Equal(0.4, exit.Distance, 2);
    }

    [Fact]
    public void Overlapping_boxes_are_cleared_together()
    {
        // thick boxes overlap; the way out along z from the middle clears every box at once
        double along = TrunkPush.Clear(Thick, Player(0, 0, 0), 0, 0, 1);
        Assert.Equal(2.8, along, 2);                      // 2.5 + 0.3
        var exit = TrunkPush.Out(Thick, Player(0, 0, 0))!.Value;
        Assert.NotEqual(0, exit.X);                        // across: 1.3, not 2.8
        Assert.Equal(1.3, exit.Distance, 2);
        var moved = Player(0, 0, 0);
        var (dx, dy, dz) = exit.Step(0);
        moved = TrunkPush.Shift(moved, dx, dy, dz);
        Assert.DoesNotContain(Thick, b => TrunkPush.Overlaps(b, moved));
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

    [Fact]
    public void Motion_into_the_trunk_is_stopped_and_away_kept()
    {
        var exit = new TrunkPush.Exit(1, 0, 0, 0.1);
        Assert.Equal((0.0, 0.0, 0.05), TrunkPush.Stop(exit, -0.07, 0, 0.05));
        Assert.Equal((0.07, 0.0, 0.0), TrunkPush.Stop(exit, 0.07, 0, 0));
        var up = new TrunkPush.Exit(0, 1, 0, 0.1);
        Assert.Equal((0.01, 0.0, 0.0), TrunkPush.Stop(up, 0.01, -0.2, 0));
    }
}
