using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The crew order's rules (#679): which trunk lying may be the one a felled tree threw, and
/// its timings in order.</summary>
public class EidolonCrewTests
{
    [Theory]
    // Beside the stump, and a tall tree's length away along its fall.
    [InlineData(1.5, 64, 0.5, true)]
    [InlineData(13.5, 64, 0.5, true)]
    [InlineData(0.5, 64, -12.5, true)]
    // Lower down a slope, within the drop.
    [InlineData(4.5, 56, 4.5, true)]
    // Too far, or far below.
    [InlineData(15.5, 64, 0.5, false)]
    [InlineData(10.5, 64, 10.5, false)]
    [InlineData(2.5, 52, 0.5, false)]
    public void A_trunk_near_the_stump_may_be_the_trees(double x, double y, double z, bool near) =>
        Assert.Equal(near, EidolonCrew.NearStump(0, 64, 0, x, y, z));

    [Fact]
    public void It_looks_for_the_trunk_after_it_settles_and_gives_up_well_before_looking_again()
    {
        Assert.True(EidolonCrew.SettleSeconds < EidolonCrew.LookSeconds);
        Assert.True(EidolonCrew.LookSeconds < EidolonCrew.RelookSeconds);
        Assert.True(EidolonCrew.Tries >= 2);
    }
}
