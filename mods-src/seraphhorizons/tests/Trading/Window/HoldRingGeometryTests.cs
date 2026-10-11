using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

public class HoldRingGeometryTests
{
    private static double SweptAngle(float[] xy)
    {
        // The angle (clockwise from the top) of the last outer vertex.
        int last = HoldRingGeometry.Steps * 4;
        double a = Math.Atan2(xy[last], -xy[last + 1]);
        return a < 0 ? a + Math.PI * 2 : a;
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void TheMeshIsTheSameSizeAtEveryProgress(float progress)
    {
        // The renderer uploads once and updates in place, which keeps the first upload's sizes.
        Assert.Equal(HoldRingGeometry.VertexCount * 2, HoldRingGeometry.Vertices(progress).Length);
        Assert.Equal(HoldRingGeometry.IndexCount, HoldRingGeometry.Indices().Length);
        Assert.All(HoldRingGeometry.Indices(), i => Assert.InRange(i, 0, HoldRingGeometry.VertexCount - 1));
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(0.73f)]
    public void TheRingEndsAtTheProgress(float progress)
    {
        Assert.Equal(progress * Math.PI * 2, SweptAngle(HoldRingGeometry.Vertices(progress)), 4);
    }

    [Fact]
    public void AFullRingClosesAtTheTop()
    {
        var xy = HoldRingGeometry.Vertices(1);
        int last = HoldRingGeometry.Steps * 4;
        Assert.Equal(xy[0], xy[last], 4);
        Assert.Equal(xy[1], xy[last + 1], 4);
        Assert.Equal(0, xy[0], 4);
        Assert.Equal(-1, xy[1], 4);
        Assert.Equal(-HoldRingGeometry.Inner, xy[3], 4);
    }

    [Fact]
    public void ItFillsClockwiseOnScreen()
    {
        // A quarter in (screen y down): the right-hand side.
        var xy = HoldRingGeometry.Vertices(0.25f);
        int quarter = HoldRingGeometry.Steps / 4 * 4;
        Assert.Equal(1, xy[quarter], 4);
        Assert.Equal(0, xy[quarter + 1], 4);
    }
}
