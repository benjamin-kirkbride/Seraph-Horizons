using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class SplittingBlockTierTests
{
    [Fact]
    public void TiersAreNamedInUpgradeOrder() =>
        Assert.Equal(["primitive", "debarked", "bound", "advanced"],
            Enum.GetValues<SplittingBlockTier>().Select(tier => tier.Name()));

    [Theory]
    [InlineData("primitive", SplittingBlockTier.Primitive)]
    [InlineData("debarked", SplittingBlockTier.Debarked)]
    [InlineData("bound", SplittingBlockTier.Bound)]
    [InlineData("advanced", SplittingBlockTier.Advanced)]
    public void ParsesEachName(string name, SplittingBlockTier tier) => Assert.Equal(tier, SplittingBlockTiers.Parse(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Advanced")]
    [InlineData("iron")]
    public void MissingOrUnknownIsPrimitive(string? name) =>
        Assert.Equal(SplittingBlockTier.Primitive, SplittingBlockTiers.Parse(name));

    [Fact]
    public void EachTierUpgradesToTheNextAndAdvancedIsLast()
    {
        Assert.Equal(SplittingBlockTier.Debarked, SplittingBlockTier.Primitive.Next());
        Assert.Equal(SplittingBlockTier.Bound, SplittingBlockTier.Debarked.Next());
        Assert.Equal(SplittingBlockTier.Advanced, SplittingBlockTier.Bound.Next());
        Assert.Null(SplittingBlockTier.Advanced.Next());
    }

    [Fact]
    public void FirewoodPerLogIsSixUntilAdvancedThenEight() =>
        Assert.Equal([6, 6, 6, 8], Enum.GetValues<SplittingBlockTier>().Select(tier => tier.FirewoodPerLog()));

    [Fact]
    public void OnlyTheAdvancedTierIsAChopperBed() =>
        Assert.Equal([SplittingBlockTier.Advanced],
            Enum.GetValues<SplittingBlockTier>().Where(tier => tier.IsChopperBed()));

    [Fact]
    public void NameKeysAreInTheModsDomain()
    {
        Assert.Equal("seraphhorizons:splittingblock-bound", SplittingBlockTier.Bound.NameKey());
        Assert.Equal("seraphhorizons:splittingblock-bound-name", SplittingBlockTier.Bound.WoodNameKey());
    }

    [Fact]
    public void AttributeKeyIsNamespaced() => Assert.Equal("seraphhorizons:tier", SplittingBlockTiers.AttributeKey);
}
