using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Tests;

public class RuinFoundationColumnTests
{
    /// <summary>A column of ground up to <paramref name="ground"/>, open above, with liquid at
    /// <paramref name="liquid"/>.</summary>
    private static Func<int, FoundationCell> Column(int ground, int? liquid = null) =>
        y => y == liquid ? FoundationCell.Liquid : y <= ground ? FoundationCell.Ground : FoundationCell.Open;

    [Fact]
    public void The_lowest_solid_layer_is_the_first_from_the_bottom()
    {
        Assert.Equal(0, RuinFoundationColumn.LowestSolid(_ => true, 5));
        Assert.Equal(2, RuinFoundationColumn.LowestSolid(y => y >= 2, 5));
        Assert.Null(RuinFoundationColumn.LowestSolid(_ => false, 5));
    }

    [Fact]
    public void The_top_is_under_the_lowest_solid_block_and_never_above_the_seat()
    {
        Assert.Equal(139, RuinFoundationColumn.Top(140, 141));
        // Under an arch: the ground comes up to the seat, no higher.
        Assert.Equal(141, RuinFoundationColumn.Top(145, 141));
        Assert.Null(RuinFoundationColumn.Top(null, 141));
    }

    [Fact]
    public void The_waystone_on_a_slope_stands_on_a_foundation()
    {
        // BetterRuins' ogdred-waystones: OffsetY -1, seated on 141, its floor at 140 (layer 0); the
        // ground falls from 141 on the north edge to 137 on the south.
        const int seat = 141, floor = 140;
        int top = RuinFoundationColumn.Top(floor, seat)!.Value;
        Assert.Null(RuinFoundationColumn.Span(top, Column(141)));
        Assert.Null(RuinFoundationColumn.Span(top, Column(139)));
        Assert.Equal((139, 139), RuinFoundationColumn.Span(top, Column(138)));
        Assert.Equal((138, 139), RuinFoundationColumn.Span(top, Column(137)));
    }

    [Fact]
    public void A_liquid_in_the_gap_leaves_the_column()
    {
        Assert.Null(RuinFoundationColumn.Span(139, Column(135, liquid: 136)));
        Assert.Equal((136, 139), RuinFoundationColumn.Span(139, Column(135, liquid: 134)));
    }

    [Fact]
    public void No_ground_within_the_depth_leaves_the_column()
    {
        int top = 139;
        Assert.Equal((top - RuinFoundationColumn.MaxDepth + 1, top),
            RuinFoundationColumn.Span(top, Column(top - RuinFoundationColumn.MaxDepth)));
        Assert.Null(RuinFoundationColumn.Span(top, Column(top - RuinFoundationColumn.MaxDepth - 1)));
    }

    [Fact]
    public void The_scan_stops_at_the_world_floor() => Assert.Null(RuinFoundationColumn.Span(3, _ => FoundationCell.Open));
}
