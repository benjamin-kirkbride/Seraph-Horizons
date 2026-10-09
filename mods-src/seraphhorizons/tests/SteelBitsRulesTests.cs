using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class SteelBitsRulesTests
{
    [Theory]
    [InlineData("game:metalbit-iron,game:metalbit-steel")] // Steelmaking Expanded 0.10.1's default
    [InlineData("game:metalbit-steel")]
    [InlineData(" game:metalbit-iron , game:metalbit-steel ")]
    [InlineData("metalbit-steel")]
    [InlineData("GAME:MetalBit-Steel")]
    public void A_list_naming_the_steel_bit_is_left_alone(string codes)
    {
        Assert.True(SteelBitsRules.ListsSteelBit(codes));
        Assert.Null(SteelBitsRules.WithSteelBit(codes));
    }

    [Theory]
    [InlineData("game:metalbit-iron", "game:metalbit-iron,game:metalbit-steel")]
    [InlineData("game:metalbit-iron,", "game:metalbit-iron,game:metalbit-steel")]
    [InlineData("", "game:metalbit-steel")]
    [InlineData(null, "game:metalbit-steel")]
    [InlineData("game:metalbit-steelx,othermod:metalbit-steel", "game:metalbit-steelx,othermod:metalbit-steel,game:metalbit-steel")]
    public void A_list_without_it_gets_it_at_the_end(string? codes, string expected)
    {
        Assert.False(SteelBitsRules.ListsSteelBit(codes));
        Assert.Equal(expected, SteelBitsRules.WithSteelBit(codes));
    }

    [Theory]
    [InlineData("game:metalbit-iron,game:metalbit-steel", "game:metalbit-iron,game:metalbit-steel,seraphhorizons:ferrochrome")]
    [InlineData("game:metalbit-iron,SeraphHorizons:Ferrochrome", null)]
    [InlineData("", "seraphhorizons:ferrochrome")]
    public void Any_other_code_goes_in_the_same_way(string codes, string? expected)
    {
        Assert.Equal(expected == null, SteelBitsRules.Lists(codes, "seraphhorizons:ferrochrome"));
        Assert.Equal(expected, SteelBitsRules.With(codes, "seraphhorizons:ferrochrome"));
    }

    [Fact]
    public void A_full_coffin_takes_sixteen_ingots_worth_of_bits()
    {
        Assert.Equal(320, SteelBitsRules.BitsPerCharge * SteelBitsRules.CoffinPlaces);
    }
}
