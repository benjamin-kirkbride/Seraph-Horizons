using SeraphHorizons.OreSurvey.Core;

namespace SeraphHorizons.OreSurvey.Tests;

public class ColumnFloodTests
{
    // Runs a flood to the end over a set of columns holding the metal; returns it and the batches.
    static (ColumnFlood Flood, int Batches) Run(Column anchor, ISet<Column> ore, int margin = 2, int cap = 16)
    {
        var flood = new ColumnFlood(anchor, margin, cap);
        int batches = 0;
        while (!flood.Done)
        {
            var batch = flood.NextBatch();
            Assert.NotEmpty(batch);
            batches++;
            foreach (var c in batch) flood.Report(c, ore.Contains(c));
            Assert.True(batches < 100, "the flood does not end");
        }
        return (flood, batches);
    }

    static HashSet<Column> Line(int x1, int x2, int z) =>
        Enumerable.Range(x1, x2 - x1 + 1).Select(x => new Column(x, z)).ToHashSet();

    [Fact]
    public void No_ore_asks_only_the_anchors_margin()
    {
        var (flood, batches) = Run(new Column(10, 10), new HashSet<Column>());
        Assert.Equal(25, flood.Asked.Count);
        Assert.Equal(1, batches);
        Assert.Empty(flood.Found);
        Assert.False(flood.Capped);
    }

    [Fact]
    public void Ore_is_surrounded_by_an_empty_margin()
    {
        var ore = Line(10, 15, 10);
        var (flood, _) = Run(new Column(10, 10), ore);
        Assert.Equal(ore, flood.Found.ToHashSet());
        // Every column within 2 of any ore column was asked for, and nothing further.
        var want = ore.SelectMany(c => from dx in Enumerable.Range(-2, 5) from dz in Enumerable.Range(-2, 5)
                                       select new Column(c.X + dx, c.Z + dz)).ToHashSet();
        Assert.Equal(want, flood.Asked.ToHashSet());
        Assert.False(flood.Capped);
    }

    [Fact]
    public void A_gap_of_one_empty_column_is_crossed_and_two_are_not()
    {
        var one = Line(10, 12, 10);
        one.UnionWith(Line(14, 15, 10)); // 13 empty
        Assert.Equal(5, Run(new Column(10, 10), one).Flood.Found.Count);

        var two = Line(10, 12, 10);
        two.UnionWith(Line(15, 16, 10)); // 13 and 14 empty: the margin
        Assert.Equal(3, Run(new Column(10, 10), two).Flood.Found.Count);
    }

    [Fact]
    public void Ore_reaching_the_cap_is_flagged_and_never_passed()
    {
        var ore = Line(0, 40, 0);
        var (flood, _) = Run(new Column(0, 0), ore, cap: 16);
        Assert.True(flood.Capped);
        Assert.All(flood.Asked, c => Assert.True(c.Chebyshev(new Column(0, 0)) <= 16));
        Assert.Equal(Line(0, 16, 0), flood.Found.ToHashSet());
    }

    [Fact]
    public void Ore_short_of_the_caps_margin_is_not_capped()
    {
        var ore = Line(0, 14, 0);
        Assert.False(Run(new Column(0, 0), ore, cap: 16).Flood.Capped);
        Assert.True(Run(new Column(0, 0), Line(0, 15, 0), cap: 16).Flood.Capped);
    }

    [Fact]
    public void Next_batch_waits_for_every_report()
    {
        var flood = new ColumnFlood(new Column(0, 0));
        var batch = flood.NextBatch();
        flood.Report(batch[0], true);
        Assert.Empty(flood.NextBatch());
        Assert.False(flood.Done);
        foreach (var c in batch.Skip(1)) flood.Report(c, false);
        Assert.NotEmpty(flood.NextBatch());
    }

    [Fact]
    public void Reporting_a_column_not_asked_for_throws() =>
        Assert.Throws<InvalidOperationException>(() => new ColumnFlood(new Column(0, 0)).Report(new Column(9, 9), true));
}

public class OreNamesTests
{
    [Theory]
    [InlineData("ore-medium-malachite-limestone", "copper")]
    [InlineData("game:ore-poor-galena_nativesilver-granite", "lead")]
    [InlineData("ore-*-cassiterite-granite", "tin")]
    [InlineData("ore-bituminouscoal-shale", "coal")]
    [InlineData("saltpeterore-limestone", "saltpeter")]
    [InlineData("ore-rich-quartz_nativegold-granite", "gold")]
    public void Metal_of_a_block(string path, string metal) => Assert.Equal(metal, OreNames.MetalOfPath(path));

    [Theory]
    [InlineData("rock-granite")]
    [InlineData("ore-quartz-granite")]
    [InlineData("ore-poor-olivine-peridotite")]
    [InlineData("looseores-malachite-granite-free")]
    public void Not_a_metal(string path) => Assert.Null(OreNames.MetalOfPath(path));
}
