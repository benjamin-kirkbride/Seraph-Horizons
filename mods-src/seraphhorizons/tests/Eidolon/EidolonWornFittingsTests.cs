using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>What a woken eidolon does with its fittings outside the game: the build a command spells
/// out, the health it keeps when its most changes, and how its fittings read.</summary>
public class EidolonWornFittingsTests
{
    private const string Stainless = EidolonFittings.Stainless;

    [Fact]
    public void ASpecChangesOnlyTheLayersItNames()
    {
        var b = EidolonBuildSpec.Parse(["plates=brass", "sash=cloth-red"], EidolonBuild.OldBuild, out var error);
        Assert.Null(error);
        Assert.Equal(EidolonBuild.Of("oak", "steel", "brass", "cloth-red", "steel"), b);
    }

    [Fact]
    public void AnEmptySpecIsTheBuildItStartsFrom() =>
        Assert.Equal(EidolonBuild.OldBuild, EidolonBuildSpec.Parse([], EidolonBuild.OldBuild, out _));

    [Fact]
    public void StainlessIsShortForStainlessSteelAndAllSetsTheThreeMetalLayers()
    {
        var b = EidolonBuildSpec.Parse(["all=stainless", "bones=aged"], EidolonBuild.OldBuild, out _);
        Assert.Equal(EidolonBuild.Of("aged", Stainless, Stainless, "leather-plain", Stainless), b);
    }

    [Theory]
    [InlineData("plates=copper", "plates does not take 'copper'")]
    [InlineData("joints=brass", "joints does not take 'brass'")]
    [InlineData("all=brass", "joints does not take 'brass'")]
    [InlineData("bones=rotten", "bones does not take 'rotten'")]
    [InlineData("arms=steel", "no layer 'arms' (bones, joints, plates, sash, skirt, all)")]
    [InlineData("=steel", "'=steel' is not layer=material")]
    public void ABadSpecSaysWhy(string word, string why)
    {
        Assert.Null(EidolonBuildSpec.Parse([word], EidolonBuild.OldBuild, out var error));
        Assert.Equal(why, error);
    }

    [Fact]
    public void OnlyWordsWithAnEqualsAreSpecWords()
    {
        Assert.True(EidolonBuildSpec.IsSpecWord("plates=iron"));
        Assert.False(EidolonBuildSpec.IsSpecWord("Tyron"));
    }

    [Theory]
    [InlineData(300, 300, 400, 400)] // full stays full
    [InlineData(150, 300, 400, 200)] // half stays half
    [InlineData(0, 300, 400, 0)]     // down stays down
    [InlineData(300, 300, 300, 300)]
    [InlineData(500, 300, 200, 200)] // never above the most
    [InlineData(10, 0, 200, 200)]    // no old most: full
    public void HealthKeepsItsShareOfTheMost(double health, double oldMax, double newMax, double kept) =>
        Assert.Equal(kept, EidolonStats.KeepShare(health, oldMax, newMax), 6);

    [Fact]
    public void FittingsReadAsTheGamesMaterialAndColourNames()
    {
        var names = EidolonFittingText.Layers(EidolonBuild.Of("oak", Stainless, "brass", "cloth-red", "iron"));
        Assert.Equal(
        [
            (new FittingName("game:material-oak"), "seraphhorizons:eidolon-layer-bones"),
            (new FittingName("game:material-stainlesssteel"), "seraphhorizons:eidolon-layer-joints"),
            (new FittingName("game:material-brass"), "seraphhorizons:eidolon-layer-plates"),
            (new FittingName("seraphhorizons:eidolon-fitting-cloth", "game:color-red"), "seraphhorizons:eidolon-layer-sash"),
            (new FittingName("game:material-iron"), "seraphhorizons:eidolon-layer-skirt"),
        ], names.Select(n => (n.Material, n.Layer)).ToList(), new NameComparer());
    }

    [Fact]
    public void OnlyTheLayersOnAreNamed()
    {
        var b = EidolonBuild.Bare.With(FittingLayer.Bones, "pine")!;
        var only = Assert.Single(EidolonFittingText.Layers(b));
        Assert.Equal("seraphhorizons:eidolon-layer-bones", only.Layer);
        Assert.Equal("seraphhorizons:eidolon-fitting-leather", EidolonFittingText.Material(FittingLayer.Sash, "leather-plain")!.Key);
        Assert.Null(EidolonFittingText.Material(FittingLayer.Plates, "copper"));
    }

    // A record's array member compares by reference; these compare its keys.
    private sealed class NameComparer : IEqualityComparer<(FittingName Material, string Layer)>
    {
        public bool Equals((FittingName Material, string Layer) x, (FittingName Material, string Layer) y) =>
            x.Layer == y.Layer && x.Material.Key == y.Material.Key && x.Material.ArgKeys.SequenceEqual(y.Material.ArgKeys);

        public int GetHashCode((FittingName Material, string Layer) obj) => obj.Layer.GetHashCode();
    }
}
