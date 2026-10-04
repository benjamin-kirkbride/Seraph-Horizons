using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class SplittingBlockRulesTests
{
    private const float Debark = 1.5f;
    private const int Wear = 1;

    private static SplittingBlockUpgrade? For(SplittingBlockTier tier, SplittingBlockHeld main, int count = 1,
        bool hammer = false) => SplittingBlockRules.For(tier, main, count, hammer, Debark, Wear);

    [Theory]
    [InlineData("immersivewoodworking:barkspud-iron", false, SplittingBlockHeld.BarkSpud)]
    [InlineData("immersivewoodworking:barkspud-steel", false, SplittingBlockHeld.BarkSpud)]
    [InlineData("game:hoop-iron", false, SplittingBlockHeld.IronHoops)]
    [InlineData("game:metalnailsandstrips-iron", false, SplittingBlockHeld.IronNails)]
    [InlineData("game:axe-felling-iron", true, SplittingBlockHeld.Axe)]
    [InlineData("immersivewoodworking:maul-iron", true, SplittingBlockHeld.Axe)]
    [InlineData("game:hoop-copper", false, SplittingBlockHeld.Other)]
    [InlineData("game:metalnailsandstrips-copper", false, SplittingBlockHeld.Other)]
    [InlineData("game:barkspudding", false, SplittingBlockHeld.Other)]
    [InlineData(null, false, SplittingBlockHeld.Other)]
    public void ClassifiesHeldItems(string? code, bool isAxe, SplittingBlockHeld held) =>
        Assert.Equal(held, SplittingBlockRules.Classify(code, isAxe));

    [Fact]
    public void ABarkSpudDebarksAPrimitiveBlockWithOrWithoutAHammer()
    {
        foreach (bool hammer in new[] { false, true })
        {
            var upgrade = For(SplittingBlockTier.Primitive, SplittingBlockHeld.BarkSpud, hammer: hammer);
            Assert.Equal(new SplittingBlockUpgrade(SplittingBlockStep.Debark, SplittingBlockTier.Primitive, 0, Debark, Wear, 0),
                upgrade);
            Assert.Equal(SplittingBlockTier.Debarked, upgrade!.To);
        }
    }

    [Fact]
    public void AnAxeDebarksOnlyWithAHammerInTheOffhandAndBothWear()
    {
        Assert.Null(For(SplittingBlockTier.Primitive, SplittingBlockHeld.Axe));
        Assert.Equal(new SplittingBlockUpgrade(SplittingBlockStep.Debark, SplittingBlockTier.Primitive, 0, Debark, Wear, Wear),
            For(SplittingBlockTier.Primitive, SplittingBlockHeld.Axe, hammer: true));
    }

    [Fact]
    public void TwoIronHoopsBindADebarkedBlockAtOnce()
    {
        Assert.Null(For(SplittingBlockTier.Debarked, SplittingBlockHeld.IronHoops, 1));
        var upgrade = For(SplittingBlockTier.Debarked, SplittingBlockHeld.IronHoops, 2);
        Assert.Equal(new SplittingBlockUpgrade(SplittingBlockStep.Bind, SplittingBlockTier.Debarked, 2, 0, 0, 0), upgrade);
        Assert.False(upgrade!.IsHold);
        Assert.Equal(SplittingBlockTier.Bound, upgrade.To);
    }

    [Fact]
    public void EightIronNailsWithAHammerFinishABoundBlockInAThreeSecondHold()
    {
        Assert.Null(For(SplittingBlockTier.Bound, SplittingBlockHeld.IronNails, 8));
        Assert.Null(For(SplittingBlockTier.Bound, SplittingBlockHeld.IronNails, 7, hammer: true));
        var upgrade = For(SplittingBlockTier.Bound, SplittingBlockHeld.IronNails, 64, hammer: true);
        Assert.Equal(new SplittingBlockUpgrade(SplittingBlockStep.Nail, SplittingBlockTier.Bound, 8, 3f, 0, 1), upgrade);
        Assert.True(upgrade!.IsHold);
        Assert.Equal(SplittingBlockTier.Advanced, upgrade.To);
    }

    [Theory]
    [InlineData(SplittingBlockTier.Primitive, SplittingBlockHeld.IronHoops)]
    [InlineData(SplittingBlockTier.Primitive, SplittingBlockHeld.IronNails)]
    [InlineData(SplittingBlockTier.Debarked, SplittingBlockHeld.BarkSpud)]
    [InlineData(SplittingBlockTier.Debarked, SplittingBlockHeld.Axe)]
    [InlineData(SplittingBlockTier.Debarked, SplittingBlockHeld.IronNails)]
    [InlineData(SplittingBlockTier.Bound, SplittingBlockHeld.BarkSpud)]
    [InlineData(SplittingBlockTier.Bound, SplittingBlockHeld.IronHoops)]
    [InlineData(SplittingBlockTier.Advanced, SplittingBlockHeld.BarkSpud)]
    [InlineData(SplittingBlockTier.Advanced, SplittingBlockHeld.Axe)]
    [InlineData(SplittingBlockTier.Advanced, SplittingBlockHeld.IronHoops)]
    [InlineData(SplittingBlockTier.Advanced, SplittingBlockHeld.IronNails)]
    [InlineData(SplittingBlockTier.Primitive, SplittingBlockHeld.Other)]
    public void NothingElseUpgrades(SplittingBlockTier tier, SplittingBlockHeld held) =>
        Assert.Null(For(tier, held, 64, hammer: true));

    [Fact]
    public void EachTierOffersTheStepToTheNextAndAdvancedNone()
    {
        Assert.Equal(SplittingBlockStep.Debark, SplittingBlockRules.NextStep(SplittingBlockTier.Primitive));
        Assert.Equal(SplittingBlockStep.Bind, SplittingBlockRules.NextStep(SplittingBlockTier.Debarked));
        Assert.Equal(SplittingBlockStep.Nail, SplittingBlockRules.NextStep(SplittingBlockTier.Bound));
        Assert.Null(SplittingBlockRules.NextStep(SplittingBlockTier.Advanced));
    }

    [Theory]
    [InlineData(1.5f, 1f, 1.5f)]
    [InlineData(1.5f, 0.3f, 5f)]
    [InlineData(1.5f, 0f, 30f)]
    public void DebarkingTakesTheBaseTimeOverTheToolsSpeed(float baseSeconds, float speed, float seconds) =>
        Assert.Equal(seconds, SplittingBlockRules.DebarkSeconds(baseSeconds, speed), 3);

    [Theory]
    [InlineData(2.95f, true)]
    [InlineData(2.9f, true)]
    [InlineData(2.8f, false)]
    public void AHoldIsDoneJustBeforeItsFullTime(float secondsUsed, bool done) =>
        Assert.Equal(done, SplittingBlockRules.IsHoldDone(secondsUsed, 3f));

    [Fact]
    public void TwoHalfLogsYieldWhatTheWholeLogDoesOnEveryTier()
    {
        foreach (var tier in Enum.GetValues<SplittingBlockTier>())
            Assert.Equal(tier.FirewoodPerLog(), 2 * tier.FirewoodPerHalfLog());
        Assert.Equal([3, 3, 3, 4], Enum.GetValues<SplittingBlockTier>().Select(tier => tier.FirewoodPerHalfLog()));
    }

    [Theory]
    [InlineData("immersivewoodworking", "choppingblock", true)]
    [InlineData("immersivewoodworking", "choppingblock-oak", true)]
    [InlineData("game", "choppingblock", false)]
    [InlineData("immersivewoodworking", "choppingblocks", false)]
    [InlineData("game", "log-placed-oak-ud", false)]
    [InlineData(null, null, false)]
    public void TellsSplittingBlocksByCode(string? domain, string? path, bool isBlock) =>
        Assert.Equal(isBlock, SplittingBlockRules.IsSplittingBlock(domain, path));
}
