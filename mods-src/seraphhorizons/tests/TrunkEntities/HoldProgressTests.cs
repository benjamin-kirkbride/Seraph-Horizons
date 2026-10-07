using SeraphHorizons.Mod.TrunkEntities.Core;

namespace SeraphHorizons.Mod.Tests;

public class HoldProgressTests
{
    [Fact]
    public void A_fresh_hold_shows_nothing()
    {
        var h = new HoldProgress();
        Assert.False(h.Active);
        Assert.False(h.Done);
        Assert.Equal(0f, h.Fraction);
    }

    [Fact]
    public void Holding_fills_towards_the_total_and_stops_at_one()
    {
        var h = new HoldProgress();
        h.Advance(7, 0.8f, 0f);
        Assert.True(h.Active);
        Assert.Equal(0f, h.Fraction);
        h.Advance(7, 0.8f, 0.2f);
        Assert.Equal(0.25f, h.Fraction, 4);
        Assert.False(h.Done);
        h.Advance(7, 0.8f, 0.6f);
        Assert.Equal(1f, h.Fraction, 4);
        Assert.True(h.Done);
        h.Advance(7, 0.8f, 5f);
        Assert.Equal(1f, h.Fraction);
    }

    [Fact]
    public void Another_trunk_starts_over_and_a_reset_clears()
    {
        var h = new HoldProgress();
        h.Advance(7, 1f, 0.5f);
        h.Advance(8, 1f, 0.1f);
        Assert.Equal(8, h.Target);
        Assert.Equal(0.1f, h.Fraction, 4);
        h.Reset();
        Assert.False(h.Active);
        Assert.Equal(0f, h.Fraction);
        h.Advance(8, 1f, 0.1f);
        Assert.Equal(0.1f, h.Fraction, 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void A_hold_needing_no_time_is_done_at_once(float total)
    {
        var h = new HoldProgress();
        h.Advance(1, total, 0f);
        Assert.True(h.Done);
    }

    [Fact]
    public void Bad_frame_times_count_for_nothing()
    {
        var h = new HoldProgress();
        h.Advance(1, 1f, -2f);
        h.Advance(1, 1f, float.NaN);
        Assert.Equal(0f, h.Fraction);
    }
}
