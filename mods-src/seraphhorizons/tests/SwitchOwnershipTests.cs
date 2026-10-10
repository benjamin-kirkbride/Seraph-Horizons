using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class SwitchOwnershipTests
{
    private static readonly SwitchOwnership Registry = new(
    [
        new OwnedBySwitch("GearCutter", ["seraphhorizons:recipes/grid/gearcutter.json"],
            [.. SwitchOwnership.TypeCodePatterns("seraphhorizons", "gearcutter"),
             .. SwitchOwnership.TypeCodePatterns("seraphhorizons", "gearcutterkit")], ["gearcutter"]),
        new OwnedBySwitch("GearReclamation", ["seraphhorizons:recipes/barrel/gear-neutralize.json"],
            SwitchOwnership.TypeCodePatterns("seraphhorizons", "picklingtub"), ["picklingtub", "lottery"]),
        new OwnedBySwitch("Rosser", [], ["loggingmod:treetrunk-*-debarked-*"], []),
    ]);

    [Theory]
    [InlineData("seraphhorizons:gearcutter-frame-north", "GearCutter")]
    [InlineData("seraphhorizons:gearcutterkit-steel", "GearCutter")]
    [InlineData("seraphhorizons:picklingtub", "GearReclamation")]
    [InlineData("loggingmod:treetrunk-oak-thin-debarked-north", "Rosser")]
    [InlineData("SeraphHorizons:GearCutter-Frame-North", "GearCutter")]
    public void OwnedCodes(string code, string owner) => Assert.Equal(owner, Registry.SwitchForCode(code));

    [Theory]
    [InlineData("seraphhorizons:gear-stainless")]
    [InlineData("seraphhorizons:gearcutterspindle")]
    [InlineData("loggingmod:treetrunk-oak-thin-no-north")]
    [InlineData("gear-rusty")]
    public void UnownedCodes(string code) => Assert.Null(Registry.SwitchForCode(code));

    [Theory]
    [InlineData("grid|seraphhorizons:recipes/grid/gearcutter.json|3", "GearCutter")]
    [InlineData("barrel|seraphhorizons:recipes/barrel/gear-neutralize.json|0", "GearReclamation")]
    [InlineData("picklingtub|seraphhorizons:gear-degreased|game:vinegarportion", "GearReclamation")]
    [InlineData("lottery|seraphhorizons:gear-neutralized|0", "GearReclamation")]
    [InlineData("gearcutter|seraphhorizons:gearblank-stainlesssteel|0", "GearCutter")]
    // Keyed by the code it starts from: a transition of an owned item exists only with the item.
    [InlineData("perish|seraphhorizons:picklingtub|0", "GearReclamation")]
    public void OwnedRecipes(string id, string owner) => Assert.Equal(owner, Registry.SwitchForRecipe(id));

    [Theory]
    [InlineData("grid|game:recipes/grid/gearbox.json|0")]
    [InlineData("grid|code|r12")]
    [InlineData("perish|seraphhorizons:gear-stainless|0")]
    [InlineData("casting")]
    public void UnownedRecipes(string id) => Assert.Null(Registry.SwitchForRecipe(id));

    [Fact]
    public void OffSwitchesRoundTrip()
    {
        Assert.Equal("GearCutter,Rosser", SwitchOwnership.EncodeOff(["Rosser", "GearCutter", "Rosser", ""]));
        Assert.Equal(["GearCutter", "Rosser"], SwitchOwnership.DecodeOff("GearCutter,Rosser").Order());
        Assert.Empty(SwitchOwnership.DecodeOff(null));
        Assert.Empty(SwitchOwnership.DecodeOff(""));
    }

    [Fact]
    public void HandListNamesOnlyRealSwitchesWithSomethingToOwn()
    {
        foreach (var o in SwitchOwnership.HandListed)
        {
            Assert.Contains(o.Switch, new[] { "Rosser", "GearReclamation", "GearCutter", "DrawBench", "PressBrake", "SquaringShear", "MandrelStation", "CastPipes", "UnifiedPipes", "OreProcessing" });
            Assert.True(o.RecipeAssets.Count + o.CodePatterns.Count + o.RecipeTypes.Count > 0);
        }
    }
}
