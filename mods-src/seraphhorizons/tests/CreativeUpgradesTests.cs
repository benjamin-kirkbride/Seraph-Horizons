using System.Runtime.CompilerServices;
using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class CreativeUpgradesTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)]
    public void OnlyCtrlInCreativeAndNotWithShift(bool creative, bool ctrl, bool shift, bool applies) =>
        Assert.Equal(applies, CreativeUpgrades.Applies(creative, ctrl, shift));

    [Fact]
    public void ASplittingBlockGoesUpOneTierAtOnceForNothing()
    {
        foreach (var tier in new[] { SplittingBlockTier.Primitive, SplittingBlockTier.Debarked, SplittingBlockTier.Bound })
        {
            var upgrade = SplittingBlockRules.Creative(tier);
            Assert.NotNull(upgrade);
            Assert.Equal(SplittingBlockRules.NextStep(tier), upgrade!.Step);
            Assert.Equal(tier + 1, upgrade.To);
            Assert.True(upgrade.Creative);
            Assert.False(upgrade.IsHold);
            Assert.Equal((0, 0, 0), (upgrade.Consumes, upgrade.MainWear, upgrade.HammerWear));
        }
        Assert.Null(SplittingBlockRules.Creative(SplittingBlockTier.Advanced));
    }

    [Fact]
    public void AnUpgradeFromTheHandsIsNotTheCreativeOne() =>
        Assert.False(SplittingBlockRules.For(SplittingBlockTier.Debarked, SplittingBlockHeld.IronHoops, 2, false, 0, 0)!.Creative);

    [Theory]
    [InlineData("sawhorseframe-north", "sawhorse")]
    [InlineData("plankframe-oak-north", "standardsawhorseframe")]
    [InlineData("standardsawhorseframe-oak-east", "sawhorsestandard")]
    [InlineData("advancedsawhorseframea-oak-north", "advancedsawhorseframeb")]
    [InlineData("advancedsawhorseframeb-birch-south", "sawhorseadvanced")]
    [InlineData("storagerackframe-oak-north", "trunkstorage")]
    [InlineData("heatingrackframe-fire-west", "resinrack")]
    [InlineData("stickstorageframe", "stickstorage")]
    public void EachFrameMakesItsNextStage(string path, string makes) =>
        Assert.Equal(makes, CreativeUpgrades.Frame("loggingmod", path)?.Makes);

    [Theory]
    [InlineData("loggingmod", "sawhorse-north")]
    [InlineData("loggingmod", "sawhorsestandard-oak-copper-north")]
    [InlineData("loggingmod", "sawhorseadvanced-oak-north")]
    [InlineData("loggingmod", "trunkstorage-oak-empty-north")]
    [InlineData("game", "sawhorseframe-north")]
    [InlineData("loggingmod", "")]
    [InlineData(null, null)]
    public void FinishedStationsAndOtherBlocksAreNoFrame(string? domain, string? path) =>
        Assert.Null(CreativeUpgrades.Frame(domain, path));

    // Logging Expanded's own costs, which its code checks (0.3.6): the staged items must pass them.
    [Fact]
    public void EachFrameStageHasLoggingExpandedsItems()
    {
        var frames = CreativeUpgrades.Frames.ToDictionary(f => f.FrameCode);
        Assert.Equal(("game:rope", 4, false, 3f), Items(frames["sawhorseframe"]));
        Assert.Equal(("game:debarkedlog-oak-ud", 2, false, 0f), Items(frames["plankframe"]));
        Assert.Equal(("game:metalnailsandstrips-copper", 20, true, 3f), Items(frames["standardsawhorseframe"]));
        Assert.Equal(("game:rod-iron", 4, false, 0f), Items(frames["advancedsawhorseframea"]));
        Assert.Equal(("game:metalnailsandstrips-iron", 20, true, 3f), Items(frames["advancedsawhorseframeb"]));
        Assert.Equal(("game:metalnailsandstrips-iron", 10, true, 3f), Items(frames["storagerackframe"]));
        Assert.Equal(("game:rope", 6, false, 3f), Items(frames["heatingrackframe"]));
        Assert.Equal(("game:cattailtops", 4, false, 3f), Items(frames["stickstorageframe"]));
        Assert.Equal(CreativeUpgrades.Frames.Count, CreativeUpgrades.Frames.Select(f => f.FrameClass).Distinct().Count());

        static (string, int, bool, float) Items(FrameStage f) => (f.MainCode, f.Count, f.HammerInOffhand, f.HoldSeconds);
    }

    [Fact]
    public void TheHelpLineIsInTheLangFile()
    {
        string en = File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "lang", "en.json"));
        Assert.Contains($"\"{CreativeUpgrades.HelpKey["seraphhorizons:".Length..]}\":", en);
    }

    private static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
