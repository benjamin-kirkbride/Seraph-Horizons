using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class RockStackRulesTests
{
    // Codes and materials as the pack's 1.22.7 world has them.
    [Theory]
    [InlineData("game:rock-granite", "Stone")]
    [InlineData("game:crackedrock-andesite", "Stone")]
    [InlineData("game:cobblestone-andesite", "Stone")]
    [InlineData("game:mossycobblestone-chert", "Stone")]
    [InlineData("game:stonebricks-suevite", "Stone")]
    [InlineData("game:rockpolished-whitemarble", "Stone")]
    [InlineData("game:drystone-mixed", "Stone")]
    [InlineData("game:ore-poor-nativecopper-andesite", "Ore")]
    [InlineData("game:ore-anthracite-chalk", "Ore")]
    [InlineData("game:soil-verylow-none", "Soil")]
    [InlineData("game:forestfloor-3", "Soil")]
    [InlineData("game:bonysoil", "Soil")]
    [InlineData("game:sand-andesite", "Sand")]
    [InlineData("game:sandwavy-andesite", "Sand")]
    [InlineData("game:gravel-andesite", "Gravel")]
    [InlineData("game:richgravel-granite", "Gravel")]
    [InlineData("game:dirtygravel-dry-cracked", "Gravel")]
    [InlineData("game:sludgygravel", "Soil")]
    [InlineData("game:packeddirt", "Soil")]
    [InlineData("game:packeddirt-trampledearth", "Soil")]
    [InlineData("game:drypackeddirt", "Soil")]
    [InlineData("game:rawclay-blue-none", "Soil")]
    [InlineData("game:peat-none", "Soil")]
    [InlineData("game:cob-none", "Soil")]
    [InlineData("game:rammed-light-plain", "Soil")]
    [InlineData("game:mudbrick-light", "Soil")]
    [InlineData("materialneeds:rammed-light-ashen-plain", "Soil")]
    [InlineData("materialneeds:oxbloodmudbrick-light", "Soil")]
    [InlineData("rock-granite", "Stone")]
    public void Rock_and_earth(string code, string material) =>
        Assert.True(RockStackRules.IsRockOrEarth(code, material, hasBlockEntity: false));

    // Building shapes, fired clay, fuel, muddy gravel, machines and decor keep their stacks.
    [Theory]
    [InlineData("game:cobblestoneslab-andesite-north-free", "Stone")]
    [InlineData("game:cobblestonestairs-andesite-up-north-free", "Stone")]
    [InlineData("game:stonebrickslab-suevite-north-free", "Stone")]
    [InlineData("game:drystonefence-mixed-empty-snow", "Stone")]
    [InlineData("game:rockpolishedslab-whitemarble-north-free", "Stone")]
    [InlineData("game:stonepath-free", "Gravel")]
    [InlineData("moreroads:stonepath-andesite-free", "Gravel")]
    [InlineData("game:mudbrickslab-dark-north-free", "Soil")]
    [InlineData("materialneeds:redmudbrickslab-dark-north-free", "Soil")]
    [InlineData("game:claybricks-good-fire", "Ceramic")]
    [InlineData("game:peatbrick", "Soil")]
    [InlineData("game:muddygravel", "Soil")]
    [InlineData("game:supportbeam-oak", "Wood")]
    [InlineData("game:torch-basic-lit-up", "Wood")]
    [InlineData("game:chest-east", "Wood")]
    [InlineData("game:looseboulders-andesite-free", "Stone")]
    [InlineData("game:charcoalpile-1", "Soil")]
    [InlineData("game:rockpolished-whitemarble", "Wood")]
    [InlineData("game:rockpolished-whitemarble", null)]
    [InlineData("", "Stone")]
    [InlineData(null, "Stone")]
    public void Not_rock_or_earth(string? code, string? material) =>
        Assert.False(RockStackRules.IsRockOrEarth(code, material, hasBlockEntity: false));

    [Fact]
    public void A_block_with_a_block_entity_is_not_rock_or_earth() =>
        Assert.False(RockStackRules.IsRockOrEarth("game:soil-verylow-none", "Soil", hasBlockEntity: true));

    [Theory]
    [InlineData(64, 16)]
    [InlineData(32, 16)]
    [InlineData(16, 16)]
    [InlineData(8, 8)]
    [InlineData(1, 1)]
    public void Cut_is_at_most_16_and_never_raises(int before, int after) => Assert.Equal(after, RockStackRules.Cut(before));

    [Fact]
    public void Trade_stack_is_the_four_sell_slots() => Assert.Equal(64, RockStackRules.TradeStack(RockStackRules.Cap));
}
