using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class CampPondsTests
{
    // A basement camp two blocks tall: floor layer at y 100, footprint x 512..524, z 640..648 (max exclusive).
    private static readonly CampBox Cellar = new(512, 100, 640, 525, 102, 649);

    [Fact]
    public void TheWindowIsTheGeneratorsSearchArea()
    {
        // GenPonds: cells strictly between -31 and 63 blocks from the chunk's corner.
        Assert.Equal((16 * 32 - 30, 20 * 32 - 30, 16 * 32 + 63, 20 * 32 + 63), CampPonds.Window(16, 20));
    }

    [Fact]
    public void AFloodedCellarFloorIsCaught()
    {
        // The save's case: the floor layer flooded, every cell of it inside the camp.
        var floor = Enumerable.Range(514, 4).SelectMany(x => Enumerable.Range(642, 3).Select(z => (x, z)));
        Assert.True(CampPonds.Floods([Cellar], 100, floor));
    }

    [Fact]
    public void APondSpreadingInFromOutsideIsCaught()
    {
        // Started outside, reaching the camp through its doorway: one cell inside is enough.
        Assert.True(CampPonds.Floods([Cellar], 101, [(530, 644), (526, 644), (524, 644)]));
    }

    [Theory]
    [InlineData(511, 644, true)]   // against the west wall
    [InlineData(525, 644, true)]   // against the east wall
    [InlineData(510, 644, false)]
    [InlineData(526, 644, false)]
    [InlineData(518, 639, true)]
    [InlineData(518, 649, true)]
    [InlineData(518, 638, false)]
    [InlineData(518, 650, false)]
    public void TheCampIsGrownByOneBlock(int x, int z, bool touches) =>
        Assert.Equal(touches, CampPonds.Touches(Cellar, x, 100, z));

    [Theory]
    [InlineData(98, false)]
    [InlineData(99, true)]    // water just under the floor layer
    [InlineData(100, true)]
    [InlineData(102, true)]   // water on the layer above, against the roof
    [InlineData(103, true)]   // its lake bed is the camp's margin
    [InlineData(104, false)]
    public void TheWaterAndItsLakeBedLayersCount(int pondY, bool touches) =>
        Assert.Equal(touches, CampPonds.Touches(Cellar, 518, pondY, 644));

    [Fact]
    public void PondsElsewhereAreLeftAlone()
    {
        Assert.False(CampPonds.Floods([], 100, [(518, 644)]));
        Assert.False(CampPonds.Floods([Cellar], 100, [(600, 644), (601, 644)]));
        Assert.False(CampPonds.Floods([Cellar], 120, [(518, 644)]));
    }

    [Fact]
    public void OnlyCampsThePondsWindowAndLayersMeetAreNear()
    {
        Assert.True(CampPonds.Near(Cellar, 100, CampPonds.Window(16, 20)));
        Assert.True(CampPonds.Near(Cellar, 100, CampPonds.Window(15, 19)));
        Assert.False(CampPonds.Near(Cellar, 100, CampPonds.Window(19, 20)));
        Assert.False(CampPonds.Near(Cellar, 110, CampPonds.Window(16, 20)));
    }
}
