using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ChuteEntryTests
{
    // The game's face indices: north 0, east 1, south 2, west 3, up 4, down 5.
    private const int North = 0, South = 2, Up = 4;

    [Fact]
    public void A_chute_refuses_the_face_its_item_came_in_by() =>
        Assert.True(ChuteEntry.Refuses(entry: South, face: South, holding: true, slots: 1));

    [Fact]
    public void Every_other_face_is_the_games() =>
        Assert.False(ChuteEntry.Refuses(entry: South, face: North, holding: true, slots: 1));

    // Put in by hand, or there before the switch: as the game moves it.
    [Fact]
    public void An_item_with_no_recorded_entry_moves_as_the_game_moves_it() =>
        Assert.False(ChuteEntry.Refuses(entry: null, face: South, holding: true, slots: 1));

    [Fact]
    public void An_empty_chute_refuses_nothing() =>
        Assert.False(ChuteEntry.Refuses(entry: South, face: South, holding: false, slots: 1));

    [Fact]
    public void A_hopper_is_left_alone() =>
        Assert.False(ChuteEntry.Refuses(entry: South, face: South, holding: true, slots: 4));

    [Fact]
    public void A_machine_tags_its_item_with_its_face_and_up_into_a_screw()
    {
        Assert.Equal(North, ChuteEntry.Tag(North, screw: false, up: Up));
        Assert.Equal(Up, ChuteEntry.Tag(North, screw: true, up: Up));
    }
}
