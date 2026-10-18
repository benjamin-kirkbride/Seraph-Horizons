using SeraphHorizons.Mod.Core;
using static SeraphHorizons.Mod.Core.ScrewPortRoute.Action;

namespace SeraphHorizons.Mod.Tests;

public class ScrewPortRouteTests
{
    private static bool Horizontal(string face) => face is "north" or "east" or "south" or "west";

    private static bool Air(string face) => false;

    private static bool ScrewAbove(string face) => face == "up";

    // The game's ported screw: up into the next screw or a chest, then the port, then out of the
    // port; never out of the top.
    [Fact]
    public void A_ported_screw_pushes_up_then_into_the_port_then_out_of_it() =>
        Assert.Equal([(PushInto, "up"), (PushInto, "north"), (SpitOut, "north")],
            ScrewPortRoute.Steps(["up", "north"], Horizontal, Air));

    [Fact]
    public void The_port_comes_after_up_whatever_the_order_of_the_faces() =>
        Assert.Equal([(PushInto, "up"), (PushInto, "west"), (SpitOut, "west")],
            ScrewPortRoute.Steps(["west", "up"], Horizontal, Air));

    // The bottom of a stack: while the screw above is full the item waits, rather than fly out of
    // an open port; a chest on the port still takes it.
    [Fact]
    public void Under_a_container_it_never_throws_out_of_the_port() =>
        Assert.Equal([(PushInto, "up"), (PushInto, "north")],
            ScrewPortRoute.Steps(["up", "north"], Horizontal, ScrewAbove));

    [Fact]
    public void A_straight_screw_is_left_to_the_game() =>
        Assert.Null(ScrewPortRoute.Steps(["up"], Horizontal, Air));

    [Fact]
    public void No_push_face_is_left_to_the_game() =>
        Assert.Null(ScrewPortRoute.Steps(Array.Empty<string>(), Horizontal, Air));

    [Fact]
    public void No_step_throws_out_of_a_vertical_face() =>
        Assert.DoesNotContain(ScrewPortRoute.Steps(["up", "down", "east"], Horizontal, Air)!,
            s => s.Action == SpitOut && !Horizontal(s.Face));
}
