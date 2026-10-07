using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Tests;

public class FellingWearTests
{
    [Fact]
    public void A_tree_is_thick_when_any_block_is_a_log_section()
    {
        Assert.False(FellingWearRules.IsThick(["log-grown-oak-ud", "leaves-grown-oak", "leavesbranchy-grown-oak"]));
        Assert.True(FellingWearRules.IsThick(["log-grown-redwood-ud", "logsection-grown-redwood-ne-ud", "leaves-grown-redwood"]));
        Assert.False(FellingWearRules.IsThick(["lognarrow-grown-baldcypress-ud"]));
        Assert.False(FellingWearRules.IsThick([]));
    }

    [Fact]
    public void The_cost_is_the_thin_or_thick_figure()
    {
        var config = new FellingWearConfig();
        Assert.Equal(4, FellingWearRules.Cost(false, config));
        Assert.Equal(8, FellingWearRules.Cost(true, config));
        Assert.Equal(1, FellingWearRules.Cost(true, new FellingWearConfig { ThickTree = 1 }));
    }

    [Fact]
    public void Defaults_are_in_range_and_bad_values_fall_back()
    {
        Assert.Empty(new FellingWearConfig().Sanitise());
        var config = new FellingWearConfig { ThinTree = -1, ThickTree = 20_000 };
        var fixes = config.Sanitise();
        Assert.Equal(2, fixes.Count);
        Assert.Equal((4, 8), (config.ThinTree, config.ThickTree));
        Assert.Contains(fixes, f => f.StartsWith("ThinTree -1"));
        var zero = new FellingWearConfig { ThinTree = 0, ThickTree = 0 };
        Assert.Empty(zero.Sanitise());
    }
}
