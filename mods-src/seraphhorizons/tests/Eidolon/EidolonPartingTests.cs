using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Two of a crew whose boxes touch part (<see cref="EidolonParting"/>): who steps away, changing
/// over while they stay touching, and a step away left to go on long enough for a climb out (#971).</summary>
public class EidolonPartingTests
{
    [Fact]
    public void TheOneThatGivesWayStepsAwayAtFirst()
    {
        Assert.True(EidolonParting.Moves(givesWay: true, touching: 0));
        Assert.False(EidolonParting.Moves(givesWay: false, touching: 0));
        Assert.True(EidolonParting.Moves(true, EidolonParting.SwapSeconds - 0.01));
        Assert.False(EidolonParting.Moves(false, EidolonParting.SwapSeconds - 0.01));
    }

    [Fact]
    public void StillTouchingTheyChangeOverAndBackAgain()
    {
        double swap = EidolonParting.SwapSeconds;
        Assert.False(EidolonParting.Moves(true, swap));
        Assert.True(EidolonParting.Moves(false, swap));
        Assert.True(EidolonParting.Moves(false, 2 * swap - 0.01));
        Assert.True(EidolonParting.Moves(true, 2 * swap));
        Assert.False(EidolonParting.Moves(false, 2 * swap));
        Assert.False(EidolonParting.Moves(true, 3 * swap + 1));
    }

    [Fact]
    public void AtEveryMomentExactlyOneOfTwoStepsAway()
    {
        for (double t = 0; t < 5 * EidolonParting.SwapSeconds; t += 0.05)
            Assert.NotEqual(EidolonParting.Moves(true, t), EidolonParting.Moves(false, t));
    }

    [Fact]
    public void AStepAwayGoesOnWhileItWalksUpToItsTime()
    {
        Assert.True(EidolonParting.KeepsStepping(100, walking: true, now: 100));
        Assert.True(EidolonParting.KeepsStepping(100, true, 100 + EidolonParting.StepSeconds - 0.01));
        // walked its time and still touching: it looks again
        Assert.False(EidolonParting.KeepsStepping(100, true, 100 + EidolonParting.StepSeconds));
        // there (or stopped): it looks again
        Assert.False(EidolonParting.KeepsStepping(100, walking: false, now: 101));
        // none set off on
        Assert.False(EidolonParting.KeepsStepping(null, true, 101));
    }

    [Fact]
    public void AStepAwayOutlastsAClimbAndAClimbOutlastsAGlance()
    {
        // #971: a climb out keeps its box over its foot until it moves onto the top; looked at again every
        // TouchSeconds it was stopped and begun again for ever
        double overFoot = EidolonClimb.TopFrame / EidolonClimb.FramesPerSecond;
        Assert.True(EidolonParting.TouchSeconds < overFoot);
        Assert.True(EidolonParting.StepSeconds > EidolonClimb.Seconds);
        // a step away is looked at again before the two change over
        Assert.True(EidolonParting.StepSeconds < EidolonParting.SwapSeconds);
    }

    [Fact]
    public void Two_unparted_for_three_changes_over_stop_parting()
    {
        // one walking home into a member already home: it steps away, finds nowhere, and the other never steps away
        Assert.False(EidolonParting.GivesUp(0));
        Assert.False(EidolonParting.GivesUp(EidolonParting.GiveUpSeconds - 0.01));
        Assert.True(EidolonParting.GivesUp(EidolonParting.GiveUpSeconds));
        Assert.True(EidolonParting.GivesUp(1000));
        // after three changes over: the one that gives way has had two turns to step away, the other one
        Assert.Equal(3, (int)(EidolonParting.GiveUpSeconds / EidolonParting.SwapSeconds));
    }

    [Fact]
    public void A_moment_apart_while_one_presses_against_another_is_not_parted()
    {
        // pressing against one it cannot pass, it rocks back and forth: clear of it for a tick now and then
        Assert.False(EidolonParting.Parted(0));
        Assert.False(EidolonParting.Parted(0.1));
        Assert.False(EidolonParting.Parted(EidolonParting.ApartSeconds - 0.01));
        Assert.True(EidolonParting.Parted(EidolonParting.ApartSeconds));
        // longer than the look for a touch, so a tick clear never begins the touch over
        Assert.True(EidolonParting.ApartSeconds > EidolonParting.TouchSeconds);
    }
}
