using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Working together: a job by owner, kind and area, joined late; reservations exclusive across
/// jobs, freed on leaving or going away, lapsing after 30 s; the job's level its present members'
/// highest; done only with every present member finished and none still to load; the footing rule
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

    // ---- the same layer together ----

    [Fact]
    public void TheJobWorksItsPresentMembersHighestLevel()
    {
        var board = Board();
        var job = board.Join(1, "1", Key());
        board.Join(2, "2", Key());
        Assert.Null(job.Level);
        board.SetLevel(1, 12);
        board.SetLevel(2, 11);
        // 2 has nothing on 12 it can reach: it waits until 12 is done
        Assert.Equal(12, job.Level);
        board.SetLevel(1, 11);
        Assert.Equal(11, job.Level);
        // one gone away does not hold the others to its layer
        board.SetLevel(1, 12);
        board.Away(1);
        Assert.Equal(11, job.Level);
        board.Leave(2);
        Assert.Null(job.Level);
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
        Assert.All(rim, s => Assert.True((s.X - 3) * (s.X - 3) + (s.Z + 3) * (s.Z + 3) >= EidolonTeamwork.RimSpacing * EidolonTeamwork.RimSpacing));
        Assert.DoesNotContain((3, -3), rim);
        Assert.DoesNotContain((4, -3), rim);
    }
}
