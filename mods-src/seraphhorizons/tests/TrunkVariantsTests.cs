using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkVariantsTests
{
    [Theory]
    [InlineData("treetrunk-oak-xs-no-north", "oak", "xs", "no", "north")]
    [InlineData("treetrunk-baldcypress-xxl-yes-west", "baldcypress", "xxl", "yes", "west")]
    [InlineData("treetrunk-oak-md-debarked-south", "oak", "md", "debarked", "south")]
    // A wood with a hyphen (none of Logging Expanded's has one) is the rest after the last three.
    [InlineData("treetrunk-dark-oak-sm-no-east", "dark-oak", "sm", "no", "east")]
    public void A_trunk_path_reads_from_its_end(string path, string wood, string size, string branches, string side)
    {
        var code = TrunkCode.Parse(path);
        Assert.Equal(new TrunkCode(wood, size, branches, side), code);
        Assert.Equal(path, code!.Value.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("log-placed-oak-ud")]
    [InlineData("treetrunk-oak-xs-north")]
    [InlineData("treetrunk-oak--no-north")]
    [InlineData("trunkstorage-oak-empty-north")]
    public void Other_paths_are_not_trunks(string? path) => Assert.Null(TrunkCode.Parse(path));

    [Theory]
    [InlineData("treetrunk-oak-xs-yes-north", "treetrunk-oak-xs-debarked-north")]
    [InlineData("treetrunk-pine-xxl-no-west", "treetrunk-pine-xxl-debarked-west")]
    [InlineData("treetrunk-pine-xxl-debarked-west", "treetrunk-pine-xxl-debarked-west")]
    [InlineData("log-placed-oak-ud", null)]
    public void The_debarked_trunk_keeps_wood_size_and_side(string path, string? debarked) =>
        Assert.Equal(debarked, TrunkVariants.DebarkedPath(path));

    [Theory]
    [InlineData("yes", 0, true)]   // Logging Expanded's own test also counts the variant alone
    [InlineData("no", 3, true)]
    [InlineData("no", 0, false)]
    [InlineData("debarked", 0, false)]
    [InlineData(null, 0, false)]
    public void Branched_is_the_variant_or_a_count(string? branches, int count, bool branched) =>
        Assert.Equal(branched, TrunkVariants.IsBranched(branches, count));

    [Theory]
    [InlineData(1, "xs")]
    [InlineData(3, "xs")]
    [InlineData(4, "sm")]
    [InlineData(8, "sm")]
    [InlineData(9, "md")]
    [InlineData(15, "md")]
    [InlineData(16, "lg")]
    [InlineData(24, "lg")]
    [InlineData(25, "xl")]
    [InlineData(35, "xl")]
    [InlineData(36, "xxl")]
    [InlineData(48, "xxl")]
    public void The_size_follows_the_logs_as_logging_expanded_counts_them(int logs, string size) =>
        Assert.Equal(size, TrunkCode.SizeFor(logs));

    [Fact]
    public void A_new_size_keeps_wood_branches_and_side() =>
        Assert.Equal("treetrunk-dark-oak-lg-debarked-west",
            TrunkCode.Parse("treetrunk-dark-oak-xl-debarked-west")!.Value.WithSize("lg").Path);

    [Fact]
    public void Only_the_new_state_is_debarked()
    {
        Assert.True(TrunkVariants.IsDebarked("debarked"));
        Assert.False(TrunkVariants.IsDebarked("no"));
        Assert.False(TrunkVariants.IsDebarked("yes"));
        Assert.False(TrunkVariants.IsDebarked(null));
    }
}
