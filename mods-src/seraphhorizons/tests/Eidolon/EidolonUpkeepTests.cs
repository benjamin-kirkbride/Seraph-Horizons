using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Charge, stops, the slump pose and ownership.</summary>
public class EidolonUpkeepTests
{
    // The pack's calendar: 9 days a month, 12 months.
    private const double DaysPerYear = 108;

    [Fact]
    public void AGearRunsItAQuarterYear() =>
        Assert.Equal(27, EidolonCharge.DaysPerGear(DaysPerYear, new EidolonConfig().ChargeYearsPerGear), 9);

    [Fact]
    public void ChargeRunsOutOverDaysAndNeverBelowZero()
    {
        double charge = 27;
        for (int day = 0; day < 26; day++)
            charge = EidolonCharge.Drain(charge, 1);
        Assert.Equal(1, charge, 9);
        charge = EidolonCharge.Drain(charge, 5);
        Assert.Equal(0, charge);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void TimeGoingBackDrainsNothing(double elapsed) => Assert.Equal(10, EidolonCharge.Drain(10, elapsed));

    [Fact]
    public void AGearIsTakenUpToTheCapAndRefusedPastIt()
    {
        Assert.True(EidolonCharge.TryAddGear(0, 27, 2, out double one));
        Assert.Equal(27, one);
        Assert.True(EidolonCharge.TryAddGear(one, 27, 2, out double two));
        Assert.Equal(54, two);
        Assert.False(EidolonCharge.TryAddGear(two - 1, 27, 2, out double same));
        Assert.Equal(53, same);
        Assert.Equal(2, EidolonCharge.Gears(54, 27), 9);
    }

    [Fact]
    public void ASlumpingStopCountsBeforeOneThatWaitsAndLowerRanksFirst()
    {
        var dry = new EidolonStop("dry", false, 20);
        Assert.Null(EidolonStop.First([null, null]));
        Assert.Equal(dry, EidolonStop.First([null, dry]));
        Assert.Equal(EidolonStop.NoCharge, EidolonStop.First([dry, EidolonStop.NoCharge]));
        Assert.Equal(EidolonStop.Damaged, EidolonStop.First([EidolonStop.NoCharge, EidolonStop.Damaged, dry]));
    }

    [Fact]
    public void ItSlumpsHoldsAndStandsUpOverTheAnimation()
    {
        var pose = new EidolonPose();
        Assert.True(pose.CanAct);
        Assert.Equal(EidolonPoseAction.None, pose.Update(false, 0, 2));
        Assert.Equal(EidolonPoseAction.Slump, pose.Update(true, 1, 2));
        Assert.False(pose.CanAct);
        Assert.Equal(EidolonPoseAction.None, pose.Update(true, 5, 2));
        Assert.Equal(EidolonPoseAction.StandUp, pose.Update(false, 10, 2));
        Assert.Equal(EidolonPoseState.Rising, pose.State);
        Assert.False(pose.CanAct);
        Assert.Equal(EidolonPoseAction.None, pose.Update(false, 11.9, 2));
        Assert.Equal(EidolonPoseAction.Stood, pose.Update(false, 12, 2));
        Assert.True(pose.CanAct);
    }

    [Fact]
    public void ASlumpInterruptsStandingUpAndWaking()
    {
        var pose = new EidolonPose();
        pose.Rise(3);
        Assert.False(pose.CanAct);
        Assert.Equal(EidolonPoseAction.Slump, pose.Update(true, 1, 2));
        Assert.Equal(EidolonPoseState.Slumped, pose.State);
    }

    [Theory]
    [InlineData(null, "p", null, null, false, true)]       // no owner: anyone
    [InlineData("o", "o", null, null, false, true)]         // the owner
    [InlineData("o", "p", null, null, false, false)]        // a stranger
    [InlineData("o", "p", 7, 7, false, true)]               // the owner's company
    [InlineData("o", "p", 7, 8, true, false)]               // another company, whatever groups they share
    [InlineData("o", "p", 7, null, true, false)]            // the owner has a company the asker is not in
    [InlineData("o", "p", null, null, true, true)]          // standing off: a shared group
    public void OnlyTheOwnerAndTheirCompanyCommand(string? owner, string player, int? ownerCompany, int? playerCompany, bool share, bool may) =>
        Assert.Equal(may, EidolonOwnership.MayCommand(owner, player, ownerCompany, playerCompany, share));

    [Fact]
    public void SettingsOutOfRangeFallBack()
    {
        var c = new EidolonConfig { ChargeYearsPerGear = -1, MaxChargeGears = 0.5, WalkSpeed = float.NaN, PathSearchNodes = 1 };
        Assert.Equal(4, c.Sanitise().Count);
        Assert.Equal(EidolonConfig.Defaults.ChargeYearsPerGear, c.ChargeYearsPerGear);
        Assert.Equal(EidolonConfig.Defaults.PathSearchNodes, c.PathSearchNodes);
        Assert.Empty(new EidolonConfig().Sanitise());
    }
}
