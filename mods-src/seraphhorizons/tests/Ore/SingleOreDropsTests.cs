using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class SingleOreDropsTests
{
    private static OreDropView Item(string path, double avg, bool metalUnits = false) => new(true, path, metalUnits, avg);

    [Fact]
    public void AGradedOreBlockDropsOneOreAndNoCrystallisedOre()
    {
        var plan = SingleOreDrops.Plan([Item("crystalizedore-medium-galena-shale", 0.01, true), Item("ore-medium-galena-shale", 1.25, true)]);
        Assert.Equal([OreDropChange.Remove, OreDropChange.One], plan);
    }

    [Fact]
    public void QuartzKeepsItsClearQuartz()
    {
        var plan = SingleOreDrops.Plan([Item("clearquartz", 0.2), Item("ore-quartz", 1.25)]);
        Assert.Equal([OreDropChange.Keep, OreDropChange.One], plan);
    }

    // Any mod's ore item: a code starting ore- or an item with metalUnits.
    [Fact]
    public void AModdedOreBlockIsAnOreBlock()
    {
        Assert.Equal([OreDropChange.One], SingleOreDrops.Plan([Item("chunk-bauxite", 2, metalUnits: true)]));
        Assert.Equal([OreDropChange.One], SingleOreDrops.Plan([Item("ore-uraninite", 1.5)]));
    }

    [Theory]
    [InlineData("flint", 1.5)]
    [InlineData("ore-sylvite", 2.5)]
    [InlineData("stone-granite", 1)]
    public void BlocksWhoseDropIsNotAnOreKeepTheirs(string path, double avg) =>
        Assert.Null(SingleOreDrops.Plan([Item(path, avg)]));

    // Below one an ore drop is a chance, as a bonus from some other block: left alone.
    [Fact]
    public void AChanceOfOreIsNotAnOreBlock() =>
        Assert.Null(SingleOreDrops.Plan([Item("gravel-granite", 1), Item("ore-poor-nativecopper-granite", 0.1, true)]));

    [Fact]
    public void ABlockThatAlreadyDropsOneIsLeftAsItIs()
    {
        Assert.Null(SingleOreDrops.Plan([Item("ore-medium-galena-shale", 1, true)]));
        Assert.Null(SingleOreDrops.Plan([new OreDropView(false, "ore-medium-galena-shale", false, 2)]));
    }

    [Fact]
    public void TheBasisCountsOneOreAndNoCrystallisedOre()
    {
        Assert.Equal(5, DepositBasis.UnitsAtOneOre([(Item("crystalizedore-medium-galena-shale", 0.01, true), 10), (Item("ore-medium-galena-shale", 1.25, true), 5)]));
        Assert.Equal(5, DepositBasis.UnitsAtOneOre([(Item("ore-medium-galena-shale", 1, true), 5)]));
        Assert.Equal(0.5, DepositBasis.UnitsAtOneOre([(Item("ore-poor-galena-shale", 0.1, true), 5)]), 9);
    }
}
