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
    [InlineData("game:muddygravel", "Soil")]
    [InlineData("game:claybricks-good-fire", "Ceramic")]
    [InlineData("game:brickcourse-four-header-black", "Ceramic")]
    [InlineData("game:brickruin-irregular-gray", "Ceramic")]
    [InlineData("game:tile-2x2-plain-travertine", "Ceramic")]
    [InlineData("game:clayshingleblock-black", "Ceramic")]
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

    [Fact]
    public void Rock_and_earth_go_to_16() => Assert.Equal(16, RockStackRules.CapOf("game:rock-granite", "Stone", false));

    // Their building shapes go to 32, not 16.
    [Theory]
    [InlineData("game:cobblestoneslab-andesite-north-free", "Stone")]
    [InlineData("game:cobblestonestairs-andesite-up-north-free", "Stone")]
    [InlineData("game:stonebrickslab-suevite-north-free", "Stone")]
    [InlineData("game:stonebrickstairs-suevite-up-north-free", "Stone")]
    [InlineData("game:polishedrockslab-whitemarble-north-free", "Stone")]
    [InlineData("game:brickslabs-fire-north-free", "Ceramic")]
    [InlineData("game:brickstairs-fire-up-north-free", "Ceramic")]
    [InlineData("game:tileslab-2x2-plain-travertine-north-free", "Ceramic")]
    [InlineData("game:mudbrickslab-dark-north-free", "Soil")]
    [InlineData("materialneeds:redmudbrickslab-dark-north-free", "Soil")]
    [InlineData("game:drystonefence-mixed-empty-snow", "Stone")]
    [InlineData("game:stonepath-free", "Gravel")]
    [InlineData("game:stonepathfullblock", "Gravel")]
    [InlineData("splittablestonepaths:stonepathquarter-up-north-free", "Gravel")]
    [InlineData("smex:slagpathslab-free", "Gravel")]
    [InlineData("moreroads:stonepath-andesite-free", "Gravel")]
    [InlineData("moreroads:stonefootpath-andesite-ns", "Stone")]
    [InlineData("moreroads:ancientroadstairs-basic-andesite-up-north-free", "Stone")]
    [InlineData("moreroads:clayroad-tan-free", "Stone")]
    [InlineData("moreroads:stoneroad3slab-andesite-free", "Stone")]
    [InlineData("moreroads:edgebrick-andesite-straight-north-free", "Stone")]
    [InlineData("game:clayshinglelabs-black-north-free", "Ceramic")]
    [InlineData("game:slantedroofing-blackclay-north-free", "Ceramic")]
    [InlineData("game:slantedroofingridge-slate-ns-free", "Stone")]
    public void Building_shapes_go_to_32(string code, string material)
    {
        Assert.True(RockStackRules.IsRockOrEarthShape(code, material, hasBlockEntity: false));
        Assert.False(RockStackRules.IsRockOrEarth(code, material, hasBlockEntity: false));
        Assert.Equal(RockStackRules.ShapeCap, RockStackRules.CapOf(code, material, hasBlockEntity: false));
    }

    // Chimneys, refractory bricks, pots, fuel, wooden, thatch and metal shapes, machines and decor
    // keep their stacks.
    [Theory]
    [InlineData("game:slantedroofing-oak-north-free", "Wood")]
    [InlineData("game:slantedroofing-thatch-north-free", "Plant")]
    [InlineData("game:slantedroofing-copper-north-free", "Metal")]
    [InlineData("moreroads:edgewood-acacia-straight-north-free", "Wood")]
    [InlineData("materialneeds:woodenpath-darkaged-ns", "Wood")]
    [InlineData("industrialdeco:industrialroad-iron-free", "Stone")]
    [InlineData("game:cobblestonefan-granite-north", "Stone")]
    [InlineData("game:quartzslab-north-free", "Stone")]
    [InlineData("game:shingleblock-copper", "Ceramic")]
    [InlineData("game:claybrickchimney-four-black-ns", "Ceramic")]
    [InlineData("game:refractorybricks-good-tier1", "Ceramic")]
    [InlineData("game:claypot-blue-fired", "Ceramic")]
    [InlineData("game:peatbrick", "Soil")]
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
        Assert.Null(RockStackRules.CapOf(code, material, hasBlockEntity: false));

    [Fact]
    public void A_block_with_a_block_entity_is_not_rock_or_earth()
    {
        Assert.Null(RockStackRules.CapOf("game:soil-verylow-none", "Soil", hasBlockEntity: true));
        Assert.Null(RockStackRules.CapOf("vsroofing:roof-bare-north", "Stone", hasBlockEntity: true));
    }

    [Theory]
    [InlineData(64, 16, 16)]
    [InlineData(32, 16, 16)]
    [InlineData(16, 16, 16)]
    [InlineData(8, 16, 8)]
    [InlineData(64, 32, 32)]
    [InlineData(32, 32, 32)]
    [InlineData(20, 32, 20)]
    [InlineData(1, 32, 1)]
    public void Cut_is_at_most_the_cap_and_never_raises(int before, int cap, int after) =>
        Assert.Equal(after, RockStackRules.Cut(before, cap));

    [Fact]
    public void Trade_stack_is_the_stack_before_the_cut() => Assert.Equal(64, RockStackRules.TradeStack(64));
}
