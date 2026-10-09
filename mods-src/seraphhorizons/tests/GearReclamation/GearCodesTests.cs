using System.Reflection;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.PicklingTub.Core;

namespace SeraphHorizons.Mod.Tests.GearReclamation;

/// <summary>The gear item codes are spelled out once, in <see cref="GearCodes"/>; the pickling tub's
/// settings and the consumer patches (GearConsumers.cs, not game-independent, so read as text) take
/// them from there.</summary>
public class GearCodesTests
{
    private static IEnumerable<string> ItemCodes() =>
        typeof(GearCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.Contains(':'));

    [Fact]
    public void The_tub_names_the_gears_by_GearCodes()
    {
        Assert.Equal(
            [GearCodes.StainlessBit, GearCodes.Degreased, GearCodes.Pickled, GearCodes.Passivated],
            [PicklingTubConfig.Bits, PicklingTubConfig.Degreased, PicklingTubConfig.Pickled, PicklingTubConfig.Passivated]);
        Assert.Equal(GearCodes.StainlessBit, new TubRuleConfig().Failure);
    }

    [Theory]
    [InlineData("GearConsumers.cs")]
    [InlineData("TubConfig.cs")]
    public void The_readers_do_not_spell_the_codes_out_again(string file)
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gear-code-readers", file));
        Assert.Contains("GearCodes.", source);
        Assert.NotEmpty(ItemCodes());
        foreach (string code in ItemCodes())
            Assert.DoesNotContain($"\"{code}\"", source);
    }
}
