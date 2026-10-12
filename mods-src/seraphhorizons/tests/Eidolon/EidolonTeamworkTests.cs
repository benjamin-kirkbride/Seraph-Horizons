using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Working together: a job by owner, kind and area, joined late; reservations exclusive across
/// jobs, freed on leaving or going away, lapsing after 30 s; the job's level its present members'
/// highest, and none mining more than a layer under it; done only with every present member finished and none still to load; the footing rule
/// against other eidolons; the rim's places; the report's shares.</summary>
public class EidolonTeamworkTests
{
    private static readonly MarkArea Area = MarkArea.Between(new MarkPos(0, 10, 0), new MarkPos(7, 12, 7));

    private static JobKey Key(string owner = "alice", string kind = "mine", MarkArea? area = null) => new(owner, kind, area ?? Area);

    private static JobBoard<(int X, int Z), MineJobCells> Board() => new();

    // ---- the job ----

    [Fact]
    public void TheSameOwnerKindAndAreaIsOneJobAndALateJoinerJoinsIt()
    {
        var board = Board();
        var a = board.Join(1, "1", Key());
        var b = board.Join(2, "2", Key());
        Assert.Same(a, b);
        Assert.Equal([1L, 2L], a.Members);
        // ordered later, while it runs
        a.Tally(1, 5);
        var c = board.Join(3, "3", Key());
        Assert.Same(a, c);
        Assert.Equal(3, a.Members.Count);
        Assert.Same(a, board.JobOf(3));
    }

    [Fact]
    public void AnotherOwnerAnOverlappingAreaOrAnotherKindIsAnotherJob()
    {
        var board = Board();
        var a = board.Join(1, "1", Key());
        Assert.NotSame(a, board.Join(2, "2", Key(owner: "bob")));
        Assert.NotSame(a, board.Join(3, "3", Key(area: MarkArea.Between(new MarkPos(0, 10, 0), new MarkPos(7, 12, 8)))));
        Assert.NotSame(a, board.Join(4, "4", Key(kind: "fell")));
        Assert.Equal(4, board.Jobs.Count);
    }

    [Fact]
    public void JoiningAnotherJobLeavesTheFirstAndAnEmptyJobIsDropped()
    {
        var board = Board();
        var a = board.Join(1, "1", Key());
        var other = Key(area: MarkArea.Between(new MarkPos(20, 10, 0), new MarkPos(27, 12, 7)));
        var b = board.Join(1, "1", other);
        Assert.NotSame(a, b);
        Assert.DoesNotContain(a, board.Jobs);
        Assert.Same(b, board.JobOf(1));
    }

    [Fact]
    public void AClosedJobIsNotJoinedAndItsKeyMakesANewOne()
    {
        var board = Board();
        var a = board.Join(1, "1", Key());
        board.SetFinished(1, true);
        Assert.True(board.TryClose(1, 0));
        Assert.True(a.Closed);
        var b = board.Join(2, "2", Key());
        Assert.NotSame(a, b);
        // and a member of the closed one starting again makes or joins the new one
        Assert.Same(b, board.Join(1, "1", Key()));
    }

    [Fact]
    public void OneAwayWhenItsJobClosedRejoinsItClosedUnlessItsOrderIsNew()
    {
        var board = Board();
        var job = board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        // 2 dry (or unloaded) while 1 finishes and reports
        board.Away(2);
        board.SetFinished(1, true);
        Assert.True(board.TryClose(1, 0));
        board.Leave(1);
        // 2 back: its job, closed, so it goes back with no report of its own
        var back = board.Join(2, "2", Key(), rejoinClosed: true);
        Assert.Same(job, back);
        Assert.True(back.Closed);
        // given the same area again by its player: a new job
        var fresh = board.Join(2, "2", Key(), rejoinClosed: false);
        Assert.NotSame(job, fresh);
        Assert.False(fresh.Closed);
    }

    // ---- reservations ----

    [Fact]
    public void AReservationIsExclusiveAllOrNothingAndAcrossJobs()
    {
        var board = Board();
        board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        board.Join(3, "3", Key(area: MarkArea.Between(new MarkPos(1, 10, 1), new MarkPos(9, 12, 9))));
        Assert.True(board.TryReserve(1, [(0, 0), (1, 0)], 0));
        Assert.False(board.Free(2, (1, 0), 1));
        Assert.True(board.Free(1, (1, 0), 1));
        // one of them held: none taken
        Assert.False(board.TryReserve(2, [(1, 0), (2, 0)], 1));
        Assert.Empty(board.Held(2));
        Assert.True(board.Free(3, (2, 0), 1));
        // another job over the same place
        Assert.False(board.TryReserve(3, [(1, 0)], 1));
        Assert.True(board.TryReserve(3, [(2, 0), (3, 0)], 1));
        Assert.Equal(3, board.Holder((2, 0), 1));
    }

    [Fact]
    public void AMemberHoldsOneReservationAtATime()
    {
        var board = Board();
        board.Join(1, "1", Key());
        Assert.True(board.TryReserve(1, [(0, 0)], 0));
        Assert.True(board.TryReserve(1, [(4, 4)], 1));
        Assert.Null(board.Holder((0, 0), 1));
        Assert.Equal([(4, 4)], board.Held(1));
    }

    [Fact]
    public void LeavingOrGoingAwayFreesItsReservationAtOnce()
    {
        var board = Board();
        var job = board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        Assert.True(board.TryReserve(1, [(0, 0)], 0));
        board.Away(1);
        Assert.True(board.Free(2, (0, 0), 0.1));
        Assert.Equal([2L], job.Present);
        Assert.Equal(2, job.Members.Count);
        board.Join(1, "1", Key());
        Assert.Equal(2, job.Present.Count);
        Assert.True(board.TryReserve(1, [(0, 0)], 1));
        board.Leave(1);
        Assert.True(board.Free(2, (0, 0), 1.1));
        Assert.Null(board.JobOf(1));
        Assert.Equal([2L], job.Members);
    }

    [Fact]
    public void AReservationNotStruckWithin30SecondsLapses()
    {
        Assert.Equal(30, EidolonTeamwork.ReserveSeconds);
        var board = Board();
        board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        Assert.True(board.TryReserve(1, [(0, 0)], 0));
        Assert.False(board.TryReserve(2, [(0, 0)], 29.9));
        // struck at 20: held to 50
        board.Struck(1, 20);
        Assert.False(board.TryReserve(2, [(0, 0)], 49.9));
        Assert.True(board.TryReserve(2, [(0, 0)], 50));
        Assert.Equal(2, board.Holder((0, 0), 50));
        Assert.DoesNotContain((0, 0), board.Held(1));
        // tidied once lapsed
        board.Expire(80);
        Assert.Empty(board.Held(2));
    }

    // ---- the floors within a climb ----

    [Fact]
    public void TheJobWorksDownFromItsPresentMembersHighestLevelAndNoMoreThanOneLayerUnder()
    {
        var board = Board();
        var job = board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        Assert.Null(job.Level);
        Assert.Null(job.Lowest);
        board.SetLevel(1, 12);
        board.SetLevel(2, 11);
        // 12 is left somewhere: none mines under 11, so where 12 is left a floor stands on it (feet in
        // 13), and where 11 is mined on 10 (feet in 11), a climb of two
        Assert.Equal(12, job.Level);
        Assert.Equal(11, job.Lowest);
        Assert.Equal(EidolonTeamwork.FloorStep, 13 - (job.Lowest!.Value));
        board.SetLevel(1, 11);
        Assert.Equal(11, job.Level);
        Assert.Equal(10, job.Lowest);
        // one gone away does not hold the others to its layer
        board.SetLevel(1, 12);
        board.Away(1);
        Assert.Equal(11, job.Level);
        board.Leave(2);
        Assert.Null(job.Level);
        Assert.Null(job.Lowest);
    }

    [Fact]
    public void ItIsDoneOnlyWhenEveryPresentMemberIsFinished()
    {
        var board = Board();
        var job = board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        board.SetFinished(1, true);
        Assert.False(job.Done(0));
        Assert.False(board.TryClose(1, 0));
        // 2 interrupted (dry): it does not hold up the report
        board.Away(2);
        Assert.True(job.Done(0));
        board.Join(2, "2", Key());
        Assert.False(job.Done(0));
        board.SetFinished(2, true);
        Assert.True(board.TryClose(2, 0));
        // once only
        Assert.False(board.TryClose(1, 0));
    }

    [Fact]
    public void AJobLoadedAgainWaitsForTheMembersItHadUntilTheyLoadOrTimeRunsOut()
    {
        var board = Board();
        var job = board.Join(1, "1", Key(), expect: [2, 3], expectUntil: EidolonTeamwork.ExpectSeconds);
        board.SetFinished(1, true);
        Assert.False(job.Done(10));
        // 2 loads and joins, and is finished too; 3 never loads
        board.Join(2, "2", Key());
        board.SetFinished(2, true);
        Assert.False(job.Done(60));
        Assert.True(job.Done(EidolonTeamwork.ExpectSeconds));
        Assert.Equal([3L], job.Expected.Keys);
    }

    // ---- the report ----

    [Fact]
    public void TheReportCountsEveryMembersShareThoseThatLeftToo()
    {
        var board = Board();
        var job = board.Join(11, "11", Key());
        board.Join(12, "12", Key());
        board.Join(13, "13", Key());
        job.Tally(11, 20);
        job.Tally(12, 36);
        job.Tally(13, 4);
        board.Leave(13);
        Assert.Equal(60, job.Total);
        Assert.Equal([12L, 11L, 13L], job.Shares.Select(s => s.Member));
        Assert.Equal("36 by eidolon 12, 20 by eidolon 11, 4 by eidolon 13", EidolonTeamwork.ShareText(job.Shares, "{1} by eidolon {0}"));
    }

    [Fact]
    public void FellersReserveStumpsAcrossJobsShareWhatTheyPassOverAndCountTreesEach()
    {
        var fell = new JobBoard<MarkPos, FellJobCells>();
        var a = fell.Join(1, "1", Key(kind: "fell"));
        Assert.Same(a, fell.Join(2, "2", Key(kind: "fell")));
        var overlapping = fell.Join(3, "3", Key(kind: "fell", area: MarkArea.Between(new MarkPos(4, 10, 4), new MarkPos(12, 12, 12))));
        Assert.NotSame(a, overlapping);
        var oak = new MarkPos(5, 11, 5);
        Assert.True(fell.TryReserve(1, [oak], 0));
        // a mate and an eidolon of the job over the overlapping area pass it over
        Assert.False(fell.TryReserve(2, [oak], 1));
        Assert.False(fell.TryReserve(3, [oak], 1));
        Assert.Equal(1L, fell.Holder(oak, 1));
        // where a spare one must not wait: every tree held by another, in any job
        Assert.Equal([oak], fell.HeldByOthers(2, 1));
        Assert.Empty(fell.HeldByOthers(1, 1));
        Assert.Empty(fell.HeldByOthers(2, 31));
        // a swing keeps it; felled, it is freed
        fell.Struck(1, 25);
        Assert.False(fell.Free(2, oak, 40));
        fell.Release(1);
        Assert.True(fell.TryReserve(2, [oak], 41));
        // a young tree one passes over, all of its job do
        a.Shared.Skip.Add(new MarkPos(1, 11, 1));
        Assert.Contains(new MarkPos(1, 11, 1), fell.JobOf(2)!.Shared.Skip);
        Assert.Empty(overlapping.Shared.Skip);
        a.Tally(1, 1);
        a.Tally(2, 1);
        a.Tally(1, 1);
        Assert.Equal(3, a.Total);
        Assert.Equal("2 by eidolon 1, 1 by eidolon 2", EidolonTeamwork.ShareText(a.Shares, "{1} by eidolon {0}"));
    }

    [Fact]
    public void EqualSharesKeepTheOrderTheyJoinedIn()
    {
        var board = Board();
        var job = board.Join(5, "five", Key());
        board.Join(3, "three", Key());
        job.Tally(5, 8);
        job.Tally(3, 8);
        Assert.Equal("five: 8; three: 8", EidolonTeamwork.ShareText(job.Shares, "{0}: {1}", "; "));
    }

    // ---- footing ----

    [Fact]
    public void ItNeverBreaksABlockUnderAnotherEidolonsBoxAtOrBelowItsFeet()
    {
        // Another eidolon centred on the corner (4, 4), its feet in block 11: its box is over columns 3
        // and 4 each way.
        var other = (X: 4.0, Feet: 11, Z: 4.0);
        Assert.True(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(3, 10, 3)));
        Assert.True(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(4, 9, 4)));
        Assert.True(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(4, 11, 3)));
        // beside it, or above its feet (its own height is no footing)
        Assert.False(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(5, 10, 4)));
        Assert.False(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(2, 10, 3)));
        Assert.False(EidolonMining.Undermines(other.X, other.Feet, other.Z, new MarkPos(3, 12, 3)));
        // any of several
        var others = new[] { other, (X: 10.5, Feet: 11, Z: 0.5) };
        Assert.True(EidolonMining.Undermines(others, new MarkPos(10, 10, 0)));
        Assert.True(EidolonMining.Undermines(others, new MarkPos(3, 10, 4)));
        Assert.False(EidolonMining.Undermines(others, new MarkPos(7, 10, 7)));
        Assert.False(EidolonMining.Undermines([], new MarkPos(3, 10, 3)));
    }

    // ---- the rim ----

    [Fact]
    public void ItWaitsOnTheRimOutOfTheAreaAndOutOfTheMinersWay()
    {
        var rim = EidolonTeamwork.Rim(Area, 3.5, -5, []);
        Assert.NotEmpty(rim);
        foreach (var (x, z) in rim)
        {
            var f = EidolonMining.Footprint(x, z);
            // its box is over no column of the area
            bool overArea = f.MaxX >= Area.Min.X && f.MinX <= Area.Max.X && f.MaxZ >= Area.Min.Z && f.MinZ <= Area.Max.Z;
            Assert.False(overArea, $"({x}, {z}) over the area");
        }
        // first a place with two columns between its box and the area, nearest it: north of the area
        var (fx, fz) = rim[0];
        var first = EidolonMining.Footprint(fx, fz);
        Assert.Equal(EidolonTeamwork.RimGap, Area.Min.Z - first.MaxZ - 1);
        Assert.Equal(-3, fz);
        Assert.InRange(fx, 3, 4);
        // and every ring, out to the edge
        Assert.Contains((Area.Min.X - 1, 4), rim);
    }

    [Fact]
    public void TwoWaitAtTheRimApart()
    {
        var rim = EidolonTeamwork.Rim(Area, 3.5, -5, [(3, -3)]);
        Assert.All(rim, s => Assert.True(Math.Max(Math.Abs(s.X - 3), Math.Abs(s.Z + 3)) >= EidolonTeamwork.RimSpacing));
        Assert.DoesNotContain((3, -3), rim);
        Assert.DoesNotContain((4, -3), rim);
        // apart along x or z, as their square boxes are: two places diagonal 2 and 2 (2.8 apart) are a
        // box apart only 0.3, and refused (the Euclidean 2.5 it was took them)
        Assert.DoesNotContain((5, -5), EidolonTeamwork.Rim(Area, 3.5, -5, [(3, -3)]));
    }

    [Fact]
    public void PlacesToWaitAreABoxAndMoreApartAlongXOrZ()
    {
        // its box 1.7 wide: chosen places leave it clear of another's with room to drift
        Assert.True(EidolonTeamwork.RimSpacing >= 1.7 + 2 * EidolonTeamwork.WaitArrive + 0.3);
        Assert.True(EidolonTeamwork.KeepSpacing < EidolonTeamwork.RimSpacing - 2 * EidolonTeamwork.WaitArrive);
        Assert.True(EidolonTeamwork.KeepSpacing > 1.7);
        Assert.True(EidolonTeamwork.Apart(0, 0, [(2.7, 0), (0, -2.7), (-3, 3)]));
        Assert.False(EidolonTeamwork.Apart(0, 0, [(2.5, 0)]));
        // 1.69 along z and 0.74 along x (the CI contact's two): too near
        Assert.False(EidolonTeamwork.Apart(511286.76, 514103.27, [(511286.02, 514104.96)]));
        Assert.False(EidolonTeamwork.Apart(0, 0, [(2, 2)]));
        Assert.True(EidolonTeamwork.Apart(0, 0, [(2, 2)], 2));
        Assert.True(EidolonTeamwork.Apart(0, 0, []));
    }

    [Fact]
    public void OneWaitingMovesOnWhenAnotherComesToStandByItOrItsWalkEndsShort()
    {
        var place = (10.0, 10.0);
        // walking there: kept while the place is clear, whatever it passes on the way
        Assert.True(EidolonTeamwork.KeepsPlace(place, (4, 4), walking: true, [(5, 5)]));
        // another came to stand at it, or chose a place by it, while it walked there
        Assert.False(EidolonTeamwork.KeepsPlace(place, (4, 4), walking: true, [(11.5, 10.5)]));
        // there, a little off it: kept
        Assert.True(EidolonTeamwork.KeepsPlace(place, (10.15, 9.85), walking: false, [(12.7, 10)]));
        // two that stopped each WaitArrive off places RimSpacing apart, toward each other, stay
        double near = EidolonTeamwork.RimSpacing - 2 * EidolonTeamwork.WaitArrive;
        Assert.True(EidolonTeamwork.KeepsPlace(place, (10 + EidolonTeamwork.WaitArrive, 10), walking: false,
            [(10 + EidolonTeamwork.WaitArrive + near, 10)]));
        // its walk ended short of its place (stuck, or another stood in its way): it chooses again
        Assert.False(EidolonTeamwork.KeepsPlace(place, (9, 10), walking: false, []));
        // another came to stand by it there
        Assert.False(EidolonTeamwork.KeepsPlace(place, (10, 10), walking: false, [(11, 11.6)]));
    }
}
