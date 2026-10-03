using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ChopperEjectTests
{
    // The four output sides, as (x, z) unit directions: north-facing frames output south (+z).
    public static TheoryData<int, int> Outputs => new() { { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 } };

    [Theory]
    [InlineData(12, 4, new[] { 3, 3, 3, 3 })]
    [InlineData(10, 4, new[] { 3, 3, 2, 2 })]
    [InlineData(3, 8, new[] { 1, 1, 1 })]
    [InlineData(5, 0, new[] { 5 })]
    [InlineData(0, 4, new int[0])]
    public void SplitsLikeImmersiveWoodworking(int amount, int dropCount, int[] expected) =>
        Assert.Equal(expected, ChopperEject.SplitEvenly(amount, dropCount));

    [Theory]
    [MemberData(nameof(Outputs))]
    public void EveryPileIsWellInsideTheCellInFront(int outX, int outZ)
    {
        const int x = -7, y = 110, z = 1234;
        int cellX = x + outX, cellZ = z + outZ;
        // The jitter at both of its extremes, and many piles: the widest spread there is.
        foreach (double r in new[] { 0.0, 0.999999 })
        foreach (int piles in new[] { 1, 2, 4, 16 })
        {
            var drops = ChopperEject.Plan(x, y, z, outX, outZ, 64, piles, 64, () => r);
            Assert.Equal(piles, drops.Count);
            Assert.All(drops, d =>
            {
                Assert.InRange(d.X - cellX, 0.3, 0.7);
                Assert.InRange(d.Z - cellZ, 0.3, 0.7);
                Assert.Equal(y + ChopperEject.Height, d.Y);
            });
        }
    }

    [Theory]
    [MemberData(nameof(Outputs))]
    public void PilesSpreadAcrossTheOutputSideOnly(int outX, int outZ)
    {
        var drops = ChopperEject.Plan(0, 0, 0, outX, outZ, 4, 4, 64, () => 0.5);
        // Along the output side, every pile is at the middle of the cell in front.
        Assert.All(drops, d => Assert.Equal(0.5 + outX + outZ, outX != 0 ? d.X : d.Z, 9));
        var across = drops.Select(d => outX != 0 ? d.Z : d.X).ToArray();
        Assert.Equal([0.3875, 0.4625, 0.5375, 0.6125], across.Select(a => Math.Round(a, 9)).OrderBy(a => a));
    }

    [Fact]
    public void ALargePileIsSeveralEntitiesAtOnePoint()
    {
        var drops = ChopperEject.Plan(0, 0, 0, 0, 1, 80, 1, 32, () => 0.5);
        Assert.Equal([32, 32, 16], drops.Select(d => d.Count));
        Assert.Single(drops.Select(d => (d.X, d.Z)).Distinct());
    }

    [Fact]
    public void NothingToDropDropsNothing() =>
        Assert.Empty(ChopperEject.Plan(0, 0, 0, 0, 1, 0, 4, 64, () => 0.5));
}
