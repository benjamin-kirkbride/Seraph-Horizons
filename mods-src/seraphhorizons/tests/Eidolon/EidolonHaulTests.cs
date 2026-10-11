using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Hauling's rules: marking an area then a machine, where it stands for a trunk and lays one,
/// the area test and the animations' timing.</summary>
public class EidolonHaulTests
{
    private static readonly MarkPos A = new(10, 64, 10);

    [Fact]
    public void AnAreaThenABlockTakesThreeClicksAndAFourthStartsAgain()
    {
        var (marks, step) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, Marks.None, A, 48);
        Assert.Equal(MarkStep.FirstCorner, step);
        (marks, step) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, marks, new MarkPos(20, 64, 4), 48);
        Assert.Equal(MarkStep.AreaThenBlock, step);
        Assert.Equal(MarkArea.Between(A, new MarkPos(20, 64, 4)), marks.Area);
        Assert.Null(marks.Third);
        var machine = new MarkPos(40, 65, 40);
        (marks, step) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, marks, machine, 48);
        Assert.Equal(MarkStep.Target, step);
        Assert.Equal(machine, marks.Third);
        Assert.Equal(MarkArea.Between(A, new MarkPos(20, 64, 4)), marks.Area);
        (marks, step) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, marks, new MarkPos(1, 1, 1), 48);
        Assert.Equal(MarkStep.FirstCorner, step);
        Assert.Equal(new Marks(new MarkPos(1, 1, 1), null), marks);
    }

    [Fact]
    public void AnAreaThenABlockRefusesATooLargeAreaAndKeepsTheFirstCorner()
    {
        var (marks, _) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, Marks.None, A, 16);
        var (after, step) = EidolonMarking.Click(EidolonMarkKind.AreaThenBlock, marks, new MarkPos(A.X + 16, A.Y, A.Z), 16);
        Assert.Equal(MarkStep.TooLarge, step);
        Assert.Equal(marks, after);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.7f, false)]
    [InlineData(2.1f, true)]
    [InlineData(-1.3f, true)]
    public void ItStandsSquareToTheTrunkAtItsReachFacingIt(float trunkYaw, bool thick)
    {
        var stands = HaulPlan.PickupStands(5.5, 7.25, trunkYaw, thick, 0, 0);
        Assert.Equal(2, stands.Length);
        foreach (var stand in stands)
        {
            double dx = stand.X - 5.5, dz = stand.Z - 7.25;
            Assert.Equal(HaulPlan.Reach(thick), Math.Sqrt(dx * dx + dz * dz), 6);
            // square to the length (sin, cos)
            Assert.Equal(0, dx * Math.Sin(trunkYaw) + dz * Math.Cos(trunkYaw), 6);
            // facing the trunk: forward (sin yaw, cos yaw) points at it
            Assert.Equal(-dx / HaulPlan.Reach(thick), Math.Sin(stand.Yaw), 6);
            Assert.Equal(-dz / HaulPlan.Reach(thick), Math.Cos(stand.Yaw), 6);
        }
        // the nearer side first
        double Dist(HaulStand s) => s.X * s.X + s.Z * s.Z;
        Assert.True(Dist(stands[0]) <= Dist(stands[1]));
    }

    [Fact]
    public void ItsReachClearsTheTrunk()
    {
        Assert.True(HaulPlan.Reach(false) > 0.5 + HaulPlan.HalfWidth);
        Assert.True(HaulPlan.Reach(true) > 1 + HaulPlan.HalfWidth);
    }

    [Fact]
    public void ATrunkIsLaidInTheMiddleInfeedCellAcrossTheLineWithTheEidolonOutside()
    {
        // The rosser's three infeed cells across its line, the way out towards -x.
        MarkPos[] cells = [new(100, 70, 49), new(100, 70, 50), new(100, 70, 51)];
        var drop = HaulPlan.Drop(cells, -1, 0, thick: false)!.Value;
        Assert.Equal((100.5, 70.0, 50.5), (drop.X, drop.Y, drop.Z));
        // lying across the line: its length along z
        Assert.Equal(0, Math.Sin(drop.TrunkYaw), 6);
        Assert.Equal(100.5 - HaulPlan.Reach(false), drop.Stand.X, 6);
        Assert.Equal(50.5, drop.Stand.Z, 6);
        // facing the machine, +x
        Assert.Equal(1, Math.Sin(drop.Stand.Yaw), 6);
    }

    [Fact]
    public void WithTwoInfeedCellsTheTrunkLiesInOneOfThemNotOnTheirSeam()
    {
        MarkPos[] cells = [new(0, 5, -1), new(1, 5, -1)];
        var drop = HaulPlan.Drop(cells, 0, -1, thick: true)!.Value;
        Assert.Contains(cells, c => Math.Floor(drop.X) == c.X && Math.Floor(drop.Z) == c.Z);
        Assert.Equal(drop.Z - HaulPlan.Reach(true), drop.Stand.Z, 6);
        Assert.Null(HaulPlan.Drop([], 1, 0, false));
    }

    [Fact]
    public void ATrunkIsInTheAreaByItsMiddleColumnUpToTheHeadroom()
    {
        var area = MarkArea.Between(new MarkPos(0, 60, 0), new MarkPos(9, 60, 9));
        Assert.True(HaulPlan.InArea(area, 4.5, 61, 4.5));
        Assert.True(HaulPlan.InArea(area, 9.99, 60 + HaulPlan.AreaHeadroom, 0));
        Assert.False(HaulPlan.InArea(area, 10.01, 61, 4.5));
        Assert.False(HaulPlan.InArea(area, 4.5, 61, -0.01));
        Assert.False(HaulPlan.InArea(area, 4.5, 59, 4.5));
        Assert.False(HaulPlan.InArea(area, 4.5, 61 + HaulPlan.AreaHeadroom, 4.5));
    }

    [Fact]
    public void TheTrunkChangesHandsOnTheShapesEventFrames()
    {
        Assert.Equal(("trunk-pickup", 0.8, 2.0), (HaulPlan.PickUp(false).Animation, HaulPlan.PickUp(false).EventAt, HaulPlan.PickUp(false).Ends));
        Assert.Equal(26 / 30.0, HaulPlan.PickUp(true).EventAt, 6);
        Assert.Equal(36 / 30.0, HaulPlan.SetDown(false).EventAt, 6);
        Assert.Equal(32 / 30.0, HaulPlan.SetDown(true).EventAt, 6);
        Assert.Equal("trunk-thick-setdown", HaulPlan.SetDown(true).Animation);
        Assert.Equal(("Trunk", "ThickTrunk"), (HaulPlan.AttachmentPoint(false), HaulPlan.AttachmentPoint(true)));
    }
}
