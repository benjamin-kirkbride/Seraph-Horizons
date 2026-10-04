using System.Runtime.CompilerServices;
using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class SawhorseWorkTests
{
    [Theory]
    [InlineData(SawhorseTier.Primitive, 2)]
    [InlineData(SawhorseTier.Standard, 2)]
    [InlineData(SawhorseTier.Advanced, 3)]
    public void BeamsPerLogByTier(SawhorseTier tier, int beams) => Assert.Equal(beams, tier.BeamsPerLog());

    [Theory]
    // The spud debarks whatever the offhand holds and whether or not the player sneaks.
    [InlineData(SawhorseTool.BarkSpud, false, false, SawhorseWork.SpudDebark)]
    [InlineData(SawhorseTool.BarkSpud, true, true, SawhorseWork.SpudDebark)]
    [InlineData(SawhorseTool.Axe, true, false, SawhorseWork.AxeAndHammerDebark)]
    [InlineData(SawhorseTool.Axe, true, true, SawhorseWork.AxeAndHammerDebark)]
    [InlineData(SawhorseTool.Axe, false, true, SawhorseWork.LoggingExpanded)]
    [InlineData(SawhorseTool.Saw, false, true, SawhorseWork.Beams)]
    [InlineData(SawhorseTool.Saw, true, true, SawhorseWork.Beams)]
    [InlineData(SawhorseTool.Saw, false, false, SawhorseWork.LoggingExpanded)]
    [InlineData(SawhorseTool.Other, true, true, SawhorseWork.LoggingExpanded)]
    public void ToolSetPicksTheWork(SawhorseTool tool, bool hammer, bool sneaking, SawhorseWork work) =>
        Assert.Equal(work, SawhorseWorks.Classify(tool, hammer, sneaking));

    [Fact]
    public void OnlyTheTwoDebarksDropBark()
    {
        Assert.True(SawhorseWork.SpudDebark.IsDebark());
        Assert.True(SawhorseWork.AxeAndHammerDebark.IsDebark());
        Assert.False(SawhorseWork.Beams.IsDebark());
        Assert.False(SawhorseWork.LoggingExpanded.IsDebark());
    }

    [Theory]
    [InlineData(16, 15, 1)]   // loose logs, or a trunk on the primitive and standard tiers
    [InlineData(5, 3, 2)]     // the advanced tier's 2 stored -> 3 debarked: 2 rolls
    [InlineData(1, 0, 1)]     // the last log: the inventory reads 0 once empty
    [InlineData(4, 4, 0)]     // nothing taken (no output for the wood)
    [InlineData(2, -1, 2)]
    public void OneBarkRollPerLogTaken(int before, int after, int rolls) =>
        Assert.Equal(rolls, SawhorseWorks.LogsTaken(before, after));

    [Theory]
    [InlineData("oak", "oak")]
    [InlineData("grown-oak", "oak")]
    [InlineData("Grown-BaldCypress", "baldcypress")]
    [InlineData("resin-pine", "pine")]
    [InlineData("resinharvested-pine", "pine")]
    [InlineData("aged", "aged")]
    [InlineData("grown-", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SpeciesIsTheBareLowerCaseWood(string? woodType, string? species) =>
        Assert.Equal(species, SawhorseWorks.Species(woodType));

    [Fact]
    public void HelpKeysFollowLoggingExpandedsNaming()
    {
        Assert.Equal("seraphhorizons:wi-sawhorse-beams", SawhorseTier.Primitive.BeamsHelpKey());
        Assert.Equal("seraphhorizons:wi-sawhorse-beams", SawhorseTier.Standard.BeamsHelpKey());
        Assert.Equal("seraphhorizons:wi-sawhorseadvanced-beams", SawhorseTier.Advanced.BeamsHelpKey());
        Assert.Equal("seraphhorizons:wi-sawhorse-debark-spud", SawhorseTier.Standard.SpudHelpKey());
        Assert.Equal("seraphhorizons:wi-sawhorseadvanced-debark-spud", SawhorseTier.Advanced.SpudHelpKey());
    }

    [Fact]
    public void EveryHelpKeyIsInTheEnglishLangFile()
    {
        string en = File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "lang", "en.json"));
        foreach (var tier in Enum.GetValues<SawhorseTier>())
        foreach (string key in new[] { tier.BeamsHelpKey(), tier.SpudHelpKey() })
            Assert.Contains($"\"{key["seraphhorizons:".Length..]}\":", en);
    }

    private static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
