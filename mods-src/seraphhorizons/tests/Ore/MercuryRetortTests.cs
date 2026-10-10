using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Retorting in the still (#726): what retorts, and one portion at a time adding up exactly.</summary>
public class MercuryRetortTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    /// <summary>Retorts a stack to the end: the portions out and the residue items left.</summary>
    private static (int Portions, int Residue) Run(int items, double perItem)
    {
        double? owed = null;
        int portions = 0, residue = 0;
        while (items > 0)
        {
            var (next, done) = MercuryRetort.Portion(owed, perItem);
            portions++;
            owed = next;
            if (done)
            {
                items--;
                residue++;
            }
        }
        return (portions, residue);
    }

    [Fact]
    public void Shipped_figures()
    {
        Assert.Empty(R.Problems);
        Assert.Equal("em:mercuryportion", R.Retort.Mercury);
        // the amalgam pan's 10 portions an amalgam, 90 % back
        Assert.Equal(9, MercuryRetort.AmalgamPortions(R.Retort), 9);
        Assert.Equal(1.0, R.SmeltShare(R.Ore("quartz_nativegold"), OreForm.Sponge));
    }

    [Fact]
    public void Amalgams_and_cinnabar_retort()
    {
        var inputs = MercuryRetort.Inputs(R).ToDictionary(i => i.Code);
        Assert.Equal("seraphhorizons:sponge-quartz_nativegold", inputs["seraphhorizons:amalgam-quartz_nativegold"].Residue);
        Assert.Equal("seraphhorizons:sponge-quartz_nativesilver", inputs["seraphhorizons:amalgam-quartz_nativesilver"].Residue);
        Assert.Equal(9, inputs["seraphhorizons:amalgam-quartz_nativegold"].Portions, 9);
        Assert.Null(inputs["game:powder-cinnabar"].Residue);
        Assert.Equal(10, inputs["game:powder-cinnabar"].Portions);
        Assert.Equal(10, inputs["game:crushed-cinnabar"].Portions);
        Assert.Equal(4, inputs.Count);
    }

    [Theory]
    [InlineData(1, 9, 9)]
    [InlineData(10, 9, 90)]
    [InlineData(64, 10, 640)]
    [InlineData(2, 7.5, 15)]
    [InlineData(4, 7.5, 30)]
    [InlineData(3, 7.5, 22)] // the last item's half portion is lost
    [InlineData(5, 1, 5)]
    public void A_stack_gives_its_mercury_and_leaves_one_residue_an_item(int items, double perItem, int portions)
    {
        var run = Run(items, perItem);
        Assert.Equal(portions, run.Portions);
        Assert.Equal(items, run.Residue);
    }

    [Fact]
    public void An_item_is_done_only_after_its_last_whole_portion()
    {
        double? owed = null;
        for (int i = 1; i < 9; i++)
        {
            var (next, done) = MercuryRetort.Portion(owed, 9);
            Assert.False(done);
            owed = next;
            Assert.Equal(9 - i, owed!.Value, 9);
        }
        Assert.True(MercuryRetort.Portion(owed, 9).ItemDone);
    }

    [Theory]
    [InlineData(2, 0.4f)]
    [InlineData(10, 2f)]
    [InlineData(50, 2f)]
    [InlineData(0, 0.4f)]
    [InlineData(double.NaN, 0.4f)]
    public void Pace_is_the_games_ratio(double perSecond, float ratio) => Assert.Equal(ratio, MercuryRetort.Pace(perSecond), 6);

    [Fact]
    public void Bad_figures_are_problems()
    {
        var config = new OreProcessingConfig { Retort = new() { MercuryReturn = 1.5, AmalgamMercury = 0.5, Cinnabar = new() { ["x"] = 0 }, PortionsPerSecond = 0 } };
        var problems = new OreRecovery(config).Problems;
        Assert.Contains(problems, p => p.StartsWith("retort.mercuryReturn"));
        Assert.Contains(problems, p => p.StartsWith("retort.cinnabar.x"));
        Assert.Contains(problems, p => p.StartsWith("retort.portionsPerSecond"));
    }
}
