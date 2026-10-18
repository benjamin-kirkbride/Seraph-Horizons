using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>The handbook's smelting counts: inputs for one output, abbreviated from 1,000.</summary>
public class SmeltCountTests
{
    [Theory]
    [InlineData(20000, 51, 393)]   // poor firepit-roasted tetrahedrite: 51 bullion per 20,000
    [InlineData(20, 1, 20)]        // a nugget, a concentrate
    [InlineData(200, 3, 67)]       // medium hematite rock: 3 blooms per 200
    [InlineData(250, 7, 36)]       // rich 7-unit rock at 40 %
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 1)]          // one input, two outputs: still one makes one
    [InlineData(21, 1, 21)]        // litharge
    [InlineData(0, 0, 1)]          // nonsense counts as 1 for 1
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    public void Per_one_rounds_up(int ratio, int output, int expected) =>
        Assert.Equal(expected, SmeltCount.PerOne(ratio, output));

    [Theory]
    [InlineData(100, 20, 1, 20)]       // vanilla nugget into an ingot mold: the game's own 20
    [InlineData(100, 1, 1, 1)]         // a bar of bullion is an ingot's 100 units
    [InlineData(100, 20000, 51, 393)]  // the same as one output when the mold takes 100
    [InlineData(400, 20, 1, 80)]       // a 400-unit mold
    [InlineData(100, 20, 17, 2)]       // doré smelted plainly: 17 gold per 20 bars
    public void For_units_counts_what_a_mold_takes(int units, int ratio, int output, int expected) =>
        Assert.Equal(expected, SmeltCount.ForUnits(units, ratio, output));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(393, "393")]
    [InlineData(999, "999")]
    [InlineData(1000, "1K")]
    [InlineData(1049, "1K")]
    [InlineData(1050, "1.1K")]
    [InlineData(1600, "1.6K")]
    [InlineData(20000, "20K")]
    [InlineData(20049, "20K")]
    [InlineData(393400, "393.4K")]
    [InlineData(999949, "999.9K")]
    [InlineData(999950, "1M")]
    [InlineData(1000000, "1M")]
    [InlineData(2500000, "2.5M")]
    [InlineData(2147483647, "2147.5M")]
    [InlineData(-1600, "-1.6K")]
    public void Abbreviated_with_si_suffixes(long count, string expected) =>
        Assert.Equal(expected, SmeltCount.Abbreviated(count));
}
