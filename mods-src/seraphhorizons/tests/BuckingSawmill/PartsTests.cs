using SeraphHorizons.Mod.BuckingSawmill.Core;

namespace SeraphHorizons.Mod.Tests;

public class PartsTests
{
    private static Parts Fitted(params string[] paths)
    {
        var parts = new Parts();
        foreach (var path in paths)
            Assert.Equal(FitVerdict.Fits, parts.Fit(path));
        return parts;
    }

    [Fact]
    public void Kinds_are_recognised_by_path()
    {
        Assert.Equal(PartKind.Sash, Parts.KindOf("sawmillsash", out _));
        Assert.Equal(PartKind.Crankshaft, Parts.KindOf("sawmillcrankshaft", out _));
        Assert.Equal(PartKind.Levers, Parts.KindOf("sawmilllevers", out _));
        Assert.Equal(PartKind.BladeKit, Parts.KindOf("sawmillblade-tinbronze", out var metal));
        Assert.Equal("tinbronze", metal);
        Assert.Equal(PartKind.None, Parts.KindOf("sawmillcarriage", out _));
        Assert.Equal(PartKind.None, Parts.KindOf("sawmillblade-", out _));
        Assert.Equal(PartKind.None, Parts.KindOf(null, out _));
    }

    [Fact]
    public void Complete_takes_two_sashes_a_crankshaft_levers_and_two_kits()
    {
        var parts = Fitted("sawmillsash", "sawmillblade-iron", "sawmillsash", "sawmillcrankshaft", "sawmilllevers");
        Assert.False(parts.Complete);
        Assert.Equal(["sawmillblade-*"], parts.Missing());
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-iron"));
        Assert.True(parts.Complete);
        Assert.Empty(parts.Missing());
        Assert.Equal("iron", parts.BladeMetal);
    }

    [Fact]
    public void Any_order_works_for_the_mechanism()
    {
        Assert.True(Fitted("sawmilllevers", "sawmillcrankshaft", "sawmillsash", "sawmillsash", "sawmillblade-copper", "sawmillblade-copper").Complete);
        Assert.True(Fitted("sawmillsash", "sawmillblade-copper", "sawmillcrankshaft", "sawmillsash", "sawmillblade-copper", "sawmilllevers").Complete);
    }

    [Fact]
    public void The_levers_are_needed()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmillblade-copper", "sawmillblade-copper");
        Assert.False(parts.Complete);
        Assert.Equal(["sawmilllevers"], parts.Missing());
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmilllevers"));
        Assert.True(parts.Levers);
        Assert.True(parts.Complete);
    }

    [Fact]
    public void A_blade_kit_needs_a_free_sash()
    {
        var parts = new Parts();
        Assert.Equal(FitVerdict.NeedsFreeSash, parts.Fit("sawmillblade-iron"));
        parts.Fit("sawmillsash");
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-iron"));
        Assert.Equal(FitVerdict.NeedsFreeSash, parts.Fit("sawmillblade-iron"));
        Assert.Single(parts.BladeMetals);
    }

    [Fact]
    public void The_second_kit_must_match_the_first_metal()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillblade-iron");
        Assert.Equal(FitVerdict.WrongMetal, parts.Fit("sawmillblade-steel"));
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-iron"));
        Assert.Equal(FitVerdict.BladesFitted, parts.Fit("sawmillblade-iron"));
    }

    [Fact]
    public void Removing_the_kits_frees_the_metal()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillblade-iron", "sawmillblade-iron");
        Assert.True(parts.RemoveBladeKit(1));
        Assert.True(parts.RemoveBladeKit(0));
        Assert.False(parts.RemoveBladeKit(0));
        Assert.Null(parts.BladeMetal);
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-steel"));
    }

    [Fact]
    public void Fitted_parts_are_refused_a_second_time()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers");
        Assert.Equal(FitVerdict.AlreadyFitted, parts.Fit("sawmillsash"));
        Assert.Equal(FitVerdict.AlreadyFitted, parts.Fit("sawmillcrankshaft"));
        Assert.Equal(FitVerdict.AlreadyFitted, parts.Fit("sawmilllevers"));
        Assert.Equal(FitVerdict.NotAPart, parts.Fit("sawmillcarriage"));
        Assert.Equal(2, parts.Sashes);
    }

    [Fact]
    public void Missing_lists_one_entry_per_item()
    {
        Assert.Equal(["sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-*", "sawmillblade-*"],
            new Parts().Missing());
    }

    [Fact]
    public void Restored_state_drops_kits_without_a_sash()
    {
        var parts = new Parts(sashes: 1, crankshaft: true, levers: true, bladeMetals: ["iron", "iron"]);
        Assert.Single(parts.BladeMetals);
        Assert.True(parts.Levers);
        Assert.Equal(2, new Parts(sashes: 5).Sashes);
    }
}
