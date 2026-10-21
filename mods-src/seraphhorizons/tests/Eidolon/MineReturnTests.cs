using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>
/// The walk back to where it was ordered (README "Eidolon", mining): set off again a few times when it stops short
/// of its place, and a few more when it stops short of another in its way, then given up, so that one standing in
/// its way for good (a member waiting on a job the walker reported done) never keeps the order going for ever.
/// </summary>
public class MineReturnTests
{
    [Fact]
    public void Stopped_short_of_its_place_it_sets_off_again_three_times()
    {
        var back = new MineReturn();
        Assert.All(Enumerable.Range(0, MineReturn.Tries), _ => Assert.True(back.StoppedShort()));
        Assert.False(back.StoppedShort());
    }

    [Fact]
    public void Stopped_short_of_one_in_its_way_for_good_it_gives_up_its_walk()
    {
        // one that never moves: the walker bumps it every few seconds; it used to set off again for ever
        var back = new MineReturn();
        int setOff = 0;
        for (int bump = 0; bump < 1000 && back.Bumped(); bump++)
            setOff++;
        Assert.Equal(MineReturn.Bumps, setOff);
        Assert.False(back.Bumped());
    }

    [Fact]
    public void Its_bumps_spend_none_of_its_tries_and_its_tries_none_of_its_bumps()
    {
        // members still at work cross its way home many times: those stops leave it its tries for being stuck
        var back = new MineReturn();
        for (int i = 0; i < MineReturn.Bumps; i++)
            Assert.True(back.Bumped());
        Assert.True(back.StoppedShort());
        var other = new MineReturn();
        for (int i = 0; i < MineReturn.Tries; i++)
            Assert.True(other.StoppedShort());
        Assert.True(other.Bumped());
        Assert.Equal($"{MineReturn.Tries}/{MineReturn.Tries} tries, 1/{MineReturn.Bumps} bumps", other.ToString());
    }

    [Fact]
    public void A_walk_that_gets_no_nearer_is_stopped_short_and_one_getting_nearer_is_not()
    {
        // pressing against one it cannot pass: walking all the while, as the game's walk sees it, never nearer
        var back = new MineReturn();
        back.SetOff(100);
        Assert.False(back.Stalled(12, 100));
        Assert.False(back.Stalled(12.3, 100 + MineReturn.StallSeconds - 0.1));
        Assert.True(back.Stalled(11.8, 100 + MineReturn.StallSeconds));
        // a slow walk home, a block nearer every few seconds, never is
        var slow = new MineReturn();
        slow.SetOff(0);
        for (int t = 0; t <= 60; t++)
            Assert.False(slow.Stalled(40 - t / 3.0, t));
        // set off again, the walk is measured afresh
        back.SetOff(200);
        Assert.False(back.Stalled(11.8, 200 + MineReturn.StallSeconds - 0.1));
    }
}
