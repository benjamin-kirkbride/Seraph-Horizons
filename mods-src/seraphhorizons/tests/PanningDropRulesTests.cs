using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class PanningDropRulesTests
{
    // What Wool 1.9.6, Tailor's Delight 2.2.2 and Expanded Matter 3.8.1 add to the pan's table.
    [Theory]
    [InlineData("wool:fibers-generic-brown")]
    [InlineData("wool:fibers-generic-plain")]
    [InlineData("tailorsdelight:awl-flint")]
    [InlineData("tailorsdelight:awl-obsidian")]
    [InlineData("tailorsdelight:awlthorn-copper")]
    [InlineData("tailorsdelight:buttons-horn")]
    [InlineData("tailorsdelight:buttons-lapislazuli")]
    [InlineData("game:nugget-uranium")]
    [InlineData("nugget-uranium")]
    [InlineData(" Game:Nugget-Uranium ")]
    public void TakesOut(string code) => Assert.True(PanningDropRules.IsRemoved(code));

    // The rest of those mods' drops, and the game's own, stay.
    [Theory]
    [InlineData("tailorsdelight:twine-brown")]
    [InlineData("tailorsdelight:needle-bone")]
    [InlineData("game:ore-fluorite")]
    [InlineData("game:nugget-rhodochrosite")]
    [InlineData("nugget-nativecopper")]
    [InlineData("stone-{rocktype}")]
    [InlineData("flaxfibers")]
    [InlineData("betterruins:locatormap-huaca")]
    [InlineData("")]
    [InlineData(null)]
    public void Keeps(string? code) => Assert.False(PanningDropRules.IsRemoved(code));
}
