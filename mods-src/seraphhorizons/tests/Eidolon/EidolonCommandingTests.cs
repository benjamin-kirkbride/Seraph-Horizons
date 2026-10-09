using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The command tool's rules: marking, binding, following and self-defence.</summary>
public class EidolonCommandingTests
{
    private static readonly MarkPos A = new(10, 64, 10);

    [Fact]
    public void ABlockModeMarksTheClickedBlockEachTime()
    {
        var (marks, step) = EidolonMarking.Click(EidolonMarkKind.Block, Marks.None, A, 48);
        Assert.Equal(MarkStep.Target, step);
        Assert.Equal(A, marks.First);
        var b = new MarkPos(1, 2, 3);
        (marks, step) = EidolonMarking.Click(EidolonMarkKind.Block, marks, b, 48);
        Assert.Equal(MarkStep.Target, step);
        Assert.Equal(b, marks.First);
        Assert.Null(marks.Area);
    }

    [Fact]
    public void AnAreaTakesTwoCornersAndAThirdClickStartsAgain()
    {
        var (marks, step) = EidolonMarking.Click(EidolonMarkKind.Area, Marks.None, A, 48);
        Assert.Equal(MarkStep.FirstCorner, step);
        Assert.Null(marks.Area);

        (marks, step) = EidolonMarking.Click(EidolonMarkKind.Area, marks, new MarkPos(4, 70, 20), 48);
        Assert.Equal(MarkStep.Area, step);
        var area = marks.Area!.Value;
        Assert.Equal(new MarkPos(4, 64, 10), area.Min);
        Assert.Equal(new MarkPos(10, 70, 20), area.Max);
        Assert.Equal(7, area.SizeX);
        Assert.Equal(11, area.SizeZ);
        Assert.Equal(11, area.LongestSide);
        Assert.True(area.Contains(4, 64, 20));
        Assert.False(area.Contains(3, 64, 20));

        (marks, step) = EidolonMarking.Click(EidolonMarkKind.Area, marks, new MarkPos(0, 0, 0), 48);
        Assert.Equal(MarkStep.FirstCorner, step);
        Assert.Equal(new MarkPos(0, 0, 0), marks.First);
        Assert.Null(marks.Second);
    }

    [Fact]
    public void ATooLargeAreaIsRefusedAndTheFirstCornerKept()
    {
        var (marks, _) = EidolonMarking.Click(EidolonMarkKind.Area, Marks.None, A, 16);
        var (after, step) = EidolonMarking.Click(EidolonMarkKind.Area, marks, new MarkPos(A.X + 16, A.Y, A.Z), 16);
        Assert.Equal(MarkStep.TooLarge, step);
        Assert.Equal(marks, after);
        (after, step) = EidolonMarking.Click(EidolonMarkKind.Area, marks, new MarkPos(A.X + 15, A.Y + 40, A.Z - 15), 16);
        Assert.Equal(MarkStep.Area, step);
        Assert.NotNull(after.Area);
    }

    [Fact]
    public void AModeWithoutMarksCannotBeClicked() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EidolonMarking.Click(EidolonMarkKind.None, Marks.None, A, 48));

    [Fact]
    public void BindingKeepsEachEidolonOnceUpToTheCap()
    {
        var bound = new List<long>();
        Assert.Equal(BindResult.Bound, EidolonBindings.Bind(bound, 7));
        Assert.Equal(BindResult.AlreadyBound, EidolonBindings.Bind(bound, 7));
        Assert.Single(bound);
        for (long id = 100; bound.Count < EidolonBindings.Max; id++)
            Assert.Equal(BindResult.Bound, EidolonBindings.Bind(bound, id));
        Assert.Equal(BindResult.Full, EidolonBindings.Bind(bound, 5));
        Assert.True(EidolonBindings.Unbind(bound, 7));
        Assert.False(EidolonBindings.Unbind(bound, 7));
        Assert.Equal(BindResult.Bound, EidolonBindings.Bind(bound, 5));
    }

    [Theory]
    // distance, gait now, gait after (near 4, run from 10)
    [InlineData(3, FollowGait.Walk, FollowGait.Stand)]
    [InlineData(4, FollowGait.Run, FollowGait.Stand)]
    [InlineData(5, FollowGait.Stand, FollowGait.Stand)]   // within the slack: it does not shuffle
    [InlineData(5, FollowGait.Walk, FollowGait.Walk)]
    [InlineData(6, FollowGait.Stand, FollowGait.Walk)]
    [InlineData(9, FollowGait.Walk, FollowGait.Walk)]
    [InlineData(11, FollowGait.Walk, FollowGait.Run)]
    [InlineData(11, FollowGait.Stand, FollowGait.Run)]
    [InlineData(8, FollowGait.Run, FollowGait.Run)]       // keeps running until well within
    [InlineData(6.5, FollowGait.Run, FollowGait.Walk)]
    public void FollowingWalksRunsAndStandsWithSlack(double distance, FollowGait now, FollowGait expected) =>
        Assert.Equal(expected, EidolonFollow.Gait(distance, 4, 10, now));

    [Theory]
    [InlineData(1.5, 5, false)]
    [InlineData(2.5, 5, true)]
    [InlineData(5, 40, false)]
    [InlineData(11, 40, true)]
    public void FollowingSearchesAgainWhenTheyMoveFarEnough(double moved, double distance, bool expected) =>
        Assert.Equal(expected, EidolonFollow.Repath(moved, distance));

    [Fact]
    public void SelfDefenceStrikesBackAtACreatureOnly()
    {
        Assert.True(EidolonDefence.Engages(false, false, true, 1, 5, 12, 24));
        Assert.False(EidolonDefence.Engages(true, false, true, 1, 5, 12, 24));   // a player: never
        Assert.False(EidolonDefence.Engages(false, true, true, 1, 5, 12, 24));   // another eidolon
        Assert.False(EidolonDefence.Engages(false, false, false, 1, 5, 12, 24)); // dead
        Assert.False(EidolonDefence.Engages(false, false, true, 13, 5, 12, 24)); // forgotten
        Assert.False(EidolonDefence.Engages(false, false, true, 1, 25, 12, 24)); // out of range
    }

    [Fact]
    public void BlowsAlternatePunchAndKickLandingWithinTheirAnimation()
    {
        Assert.Equal("punch", EidolonDefence.Blow(0).Animation);
        Assert.Equal("kick", EidolonDefence.Blow(1).Animation);
        Assert.Equal("punch", EidolonDefence.Blow(2).Animation);
        for (int n = 0; n < 2; n++)
        {
            var (_, seconds, hitAt) = EidolonDefence.Blow(n);
            Assert.InRange(hitAt, 0.1, seconds);
        }
    }

    [Fact]
    public void TheCommandSettingsHaveSaneDefaultsAndBadValuesAreReset()
    {
        var config = new EidolonConfig { CommandRange = -1, FollowDistance = 100, FollowRunDistance = double.NaN, DefenceDamage = -3 };
        Assert.Equal(4, config.Sanitise().Count);
        Assert.Equal(EidolonConfig.Defaults.CommandRange, config.CommandRange);
        Assert.Equal(EidolonConfig.Defaults.FollowDistance, config.FollowDistance);
        Assert.Equal(EidolonConfig.Defaults.FollowRunDistance, config.FollowRunDistance);
        Assert.Equal(EidolonConfig.Defaults.DefenceDamage, config.DefenceDamage);
        Assert.True(EidolonConfig.Defaults.FollowRunDistance > EidolonConfig.Defaults.FollowDistance + EidolonFollow.StartSlack);
    }
}
