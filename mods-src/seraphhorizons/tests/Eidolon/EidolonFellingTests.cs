using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The fell order's rules (#677): wild logs and stumps, grown trees, where it stands, the
/// swing's timing and what replants a tree.</summary>
public class EidolonFellingTests
{
    [Theory]
    [InlineData("log-grown-oak-ud", true, true, true)]
    [InlineData("logsection-grown-redwood-ne-ud", true, true, true)]
    [InlineData("lognarrow-grown-cypress-ud", true, true, true)]
    [InlineData("log-resin-pine-ud", true, true, true)]
    [InlineData("log-resinharvested-acacia-ud", true, true, true)]
    // A player's log building: placed logs have no felling group.
    [InlineData("log-placed-oak-ud", false, true, false)]
    [InlineData("log-placed-oak-ud", true, true, false)]
    // Leaves and fruit trees are never a tree's log here.
    [InlineData("leavesbranchy-grown-oak", true, false, false)]
    [InlineData("fruittree-branch", true, true, false)]
    public void WildLogsAreGrownLogsOfATree(string path, bool group, bool wood, bool wild) =>
        Assert.Equal(wild, EidolonFelling.IsWildLog(path, group, wood));

    [Fact]
    public void AStumpIsAnUprightLogOnTheGround()
    {
        Assert.True(EidolonFelling.IsStump("log-grown-oak-ud", belowSameGroup: false));
        Assert.False(EidolonFelling.IsStump("log-grown-oak-ud", belowSameGroup: true));
        Assert.False(EidolonFelling.IsStump("log-grown-oak-we", belowSameGroup: false));
        Assert.True(EidolonFelling.IsStump("logsection-grown-redwood-sw-ud", belowSameGroup: false));
    }

    [Fact]
    public void ATreeIsGrownFromTheConfiguredLogs()
    {
        Assert.False(EidolonFelling.Mature(4, EidolonConfig.Defaults.FellMinLogs));
        Assert.True(EidolonFelling.Mature(5, EidolonConfig.Defaults.FellMinLogs));
        Assert.True(EidolonFelling.Mature(1, 1));
    }

    [Fact]
    public void ItStandsBesideTheTrunkInReachNearestItFirst()
    {
        var corners = EidolonFelling.StandCorners(10, 20, 0, 20.5);
        Assert.NotEmpty(corners);
        foreach (var (x, z) in corners)
        {
            double d = Math.Sqrt((x - 10.5) * (x - 10.5) + (z - 20.5) * (z - 20.5));
            Assert.InRange(d, EidolonFelling.StandNear, EidolonFelling.StandFar);
            Assert.True(EidolonFelling.InReach(x, z, 10, 20));
            // Its box (1.7 wide) centred on the corner never takes in the trunk's block.
            Assert.True(Math.Abs(x - 10.5) >= 1.35 || Math.Abs(z - 20.5) >= 1.35, $"({x}, {z})");
        }
        // Coming from the west: the first place is a best-distance one on the west side.
        var (fx, _) = corners[0];
        Assert.True(fx < 10.5);
        double first = Math.Sqrt((corners[0].X - 10.5) * (corners[0].X - 10.5) + (corners[0].Z - 20.5) * (corners[0].Z - 20.5));
        Assert.True(first <= EidolonFelling.StandBest);
    }

    [Fact]
    public void ReachIsHorizontalFromTheTrunksCentre()
    {
        Assert.True(EidolonFelling.InReach(10.5 + EidolonFelling.Reach - 0.01, 20.5, 10, 20));
        Assert.False(EidolonFelling.InReach(10.5 + EidolonFelling.Reach + 0.01, 20.5, 10, 20));
    }

    [Fact]
    public void ItFacesTheTreeAsTheGamesCreaturesTurn()
    {
        Assert.Equal(0f, EidolonFelling.Yaw(0, 0, 0, 5), 4);
        Assert.Equal((float)(Math.PI / 2), EidolonFelling.Yaw(0, 0, 5, 0), 4);
    }

    [Fact]
    public void TheLastSwingsCutFellsOnFrameFifteen()
    {
        Assert.Equal(0.5, EidolonFelling.CutAt(0), 6);
        Assert.Equal(2 * 40 / 30.0 + 0.5, EidolonFelling.CutAt(EidolonFelling.Swings - 1), 6);
    }

    [Theory]
    [InlineData("game:sapling-oak-free", "oak", true)]
    [InlineData("game:sapling-oak-snow", "oak", true)]
    [InlineData("game:treeseed-oak", "oak", true)]
    [InlineData("game:sapling-pine-free", "oak", false)]
    [InlineData("game:sapling-darkpine-free", "pine", false)]
    [InlineData("game:treeseed-darkpine", "pine", false)]
    [InlineData("game:log-placed-oak-ud", "oak", false)]
    public void ItReplantsWithThatTreesSaplingOrSeed(string code, string wood, bool replants)
    {
        Assert.Equal(replants, EidolonFelling.Replants(code, wood));
        Assert.Equal("game:sapling-oak-free", EidolonFelling.SaplingCode("oak"));
    }
}
