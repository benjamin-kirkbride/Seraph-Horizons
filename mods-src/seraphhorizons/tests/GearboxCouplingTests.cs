using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class GearboxCouplingTests
{
    // A 1:5 gearbox at ratio 1 on its low side answers 5 at its high side. A rotor on the low side
    // was handed the high side's 5 by discovery; the touching face says 1.
    [Fact]
    public void A_source_on_the_low_side_takes_the_low_side_ratio() =>
        Assert.Equal(1f, GearboxCoupling.RatioToSet(true, false, true, stored: 5f, atTouchingFace: 1f));

    // Driving the high side: the gearbox's low side is at 0.2, the rotor's touching face at 1.
    [Fact]
    public void A_source_on_the_high_side_takes_the_high_side_ratio() =>
        Assert.Equal(1f, GearboxCoupling.RatioToSet(true, false, true, stored: 0.2f, atTouchingFace: 1f));

    [Fact]
    public void A_ratio_already_right_is_left_alone() =>
        Assert.Null(GearboxCoupling.RatioToSet(true, false, true, stored: 1f, atTouchingFace: 1f));

    [Fact]
    public void Any_other_neighbour_is_left_to_the_game() =>
        Assert.Null(GearboxCoupling.RatioToSet(false, false, true, stored: 5f, atTouchingFace: 1f));

    // A gearbox's own stored ratio is its low side's, not the ratio of the face it touches with.
    [Fact]
    public void A_gearbox_next_to_a_gearbox_is_left_to_the_game() =>
        Assert.Null(GearboxCoupling.RatioToSet(true, true, true, stored: 5f, atTouchingFace: 1f));

    [Fact]
    public void A_gearbox_on_another_network_is_not_read() =>
        Assert.Null(GearboxCoupling.RatioToSet(true, false, false, stored: 5f, atTouchingFace: 1f));

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void An_unusable_answer_changes_nothing(float answer) =>
        Assert.Null(GearboxCoupling.RatioToSet(true, false, true, stored: 5f, atTouchingFace: answer));
}
