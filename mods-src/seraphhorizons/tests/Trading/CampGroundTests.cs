using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class CampGroundTests
{
    [Fact]
    public void TheSamplesAreTheGamesCentreAndCornersOnePastTheFootprint()
    {
        // WorldGenStructure.TryGenerateAtSurface: centre at start + ceil(size / 2), corners at
        // start, +SizeX, +SizeZ.
        Assert.Equal([(108, 207), (100, 200), (115, 200), (100, 214), (115, 214)], CampGround.SamplePoints(100, 200, 15, 14));
        Assert.Equal((5, 5), CampGround.SamplePoints(0, 0, 10, 9)[0]);
    }

    [Theory]
    [InlineData(0, 0, 15, 16, true)]
    [InlineData(31, 31, 32, 32, true)]
    [InlineData(31, 0, 33, 10, false)]
    [InlineData(0, 40, 10, 24, false)]
    public void OnlyTheChunkAndItsPlusNeighboursAreRead(int lx, int lz, int sx, int sz, bool inside) =>
        Assert.Equal(inside, CampGround.InNeighbourhood(lx, lz, sx, sz));

    [Fact]
    public void ExactlyLevelGroundIsTheGamesOwnHeight()
    {
        var fit = CampGround.Surface([120, 120, 120, 120, 120], 0);
        Assert.Equal(new GroundFit(120, 120, 0), fit);
        Assert.Null(CampGround.Surface([120, 121, 120, 120, 120], 0));
    }

    [Fact]
    public void UnevenGroundWithinTheToleranceSitsOnTheMedian()
    {
        // Ground rising 2 across the footprint: the game rejects it, a camp takes it at the median.
        var fit = CampGround.Surface([121, 120, 122, 120, 122], 2)!.Value;
        Assert.Equal((121, 121, 2), (fit.Base, fit.Centre, fit.Slope));
        Assert.Null(CampGround.Surface([121, 120, 123, 120, 122], 2));
        Assert.Null(CampGround.Surface([120, 120, 120, 120], 2));
        // A negative tolerance is the game's rule.
        Assert.Null(CampGround.Surface([120, 121, 120, 120, 120], -1));
    }

    [Fact]
    public void ShallowWaterKeepsTheGamesRule()
    {
        Assert.Equal(new GroundFit(101, 100, 1), CampGround.ShallowWater([100, 100, 101, 100, 101]));
        Assert.Null(CampGround.ShallowWater([100, 100, 100, 100, 100]));
        Assert.Null(CampGround.ShallowWater([100, 100, 102, 100, 101]));
        Assert.Null(CampGround.Fit(CampPlacement.ShallowWater, [100, 100, 102, 100, 101], 5));
        Assert.NotNull(CampGround.Fit(CampPlacement.Surface, [100, 100, 102, 100, 101], 2));
    }

    [Fact]
    public void TheSeaDepthLimitIsTheGames()
    {
        Assert.False(CampGround.TooDeep(109, 110, 1));
        Assert.True(CampGround.TooDeep(108, 110, 1));
        Assert.False(CampGround.TooDeep(90, 110, 20));
    }

    [Fact]
    public void LevellingFillsLowColumnsAndCutsHighOnes()
    {
        Assert.Equal(new ColumnWork(119, 120, 0, -1), CampGround.Level(118, 120));
        Assert.True(CampGround.Level(118, 120).Fills);
        var cut = CampGround.Level(123, 120);
        Assert.Equal((121, 123, false, true), (cut.CutFrom, cut.CutTo, cut.Fills, cut.Cuts));
        var none = CampGround.Level(120, 120);
        Assert.False(none.Fills || none.Cuts);
    }

    [Fact]
    public void AFootprintFarOffItsBaseIsNotLevelled()
    {
        Assert.True(CampGround.Levellable([118, 120, 126], 120));
        Assert.False(CampGround.Levellable([118, 120, 127], 120));
        Assert.False(CampGround.Levellable([113, 120], 120));
    }

    [Fact]
    public void TheChecksSeeTheLevelledGroundInsideTheFootprint()
    {
        Assert.True(CampGround.InFootprint(100, 200, 100, 200, 15, 14));
        Assert.False(CampGround.InFootprint(115, 200, 100, 200, 15, 14));
        Assert.False(CampGround.InFootprint(100, 214, 100, 200, 15, 14));
        Assert.Equal(121, CampGround.LevelledHeight(119, true, 121));
        Assert.Equal(119, CampGround.LevelledHeight(119, false, 121));
        // Filled: ground. Cut: not. Below the old ground, or outside: the block decides.
        Assert.True(CampGround.LevelledGround(121, 119, true, 121));
        Assert.False(CampGround.LevelledGround(122, 125, true, 121));
        Assert.Null(CampGround.LevelledGround(118, 119, true, 121));
        Assert.Null(CampGround.LevelledGround(121, 119, false, 121));
    }

    [Fact]
    public void TheLiquidChecksAreTheGamesPositions()
    {
        var surface = CampGround.SurfaceLiquidChecks(100, 200, 10, 12, 120);
        Assert.Contains((105, 120, 206), surface);
        Assert.Contains((110, 121, 212), surface);
        Assert.Contains((100, 122, 212), surface);
        Assert.Equal(surface.Length, surface.Distinct().Count());
        Assert.All(surface, p => Assert.InRange(p.Y, 120, 122));
        Assert.Equal([(105, 99, 206), (100, 99, 200), (110, 99, 200), (100, 99, 212), (110, 99, 212)],
            CampGround.ShallowLiquidChecks(100, 200, 10, 12, 99));
    }
}
