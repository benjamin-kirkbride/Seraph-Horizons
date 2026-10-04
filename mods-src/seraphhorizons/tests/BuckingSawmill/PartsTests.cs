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
    public void Complete_takes_two_sashes_a_crankshaft_levers_and_one_kit()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers");
        Assert.False(parts.Complete);
        Assert.Equal(["sawmillblade-*"], parts.Missing());
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-iron"));
        Assert.True(parts.Complete);
        Assert.Empty(parts.Missing());
        Assert.Equal("iron", parts.BladeMetal);
        Assert.True(parts.BladeKit);
    }

    [Fact]
    public void Any_order_works_for_the_mechanism()
    {
        Assert.True(Fitted("sawmilllevers", "sawmillcrankshaft", "sawmillsash", "sawmillsash", "sawmillblade-copper").Complete);
        Assert.True(Fitted("sawmillsash", "sawmillsash", "sawmillblade-copper", "sawmillcrankshaft", "sawmilllevers").Complete);
    }

    [Fact]
    public void The_levers_are_needed()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmillblade-copper");
        Assert.False(parts.Complete);
        Assert.Equal(["sawmilllevers"], parts.Missing());
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmilllevers"));
        Assert.True(parts.Levers);
        Assert.True(parts.Complete);
    }

    [Fact]
    public void The_blade_kit_needs_both_sashes()
    {
        var parts = new Parts();
        Assert.Equal(FitVerdict.NeedsSashes, parts.Fit("sawmillblade-iron"));
        parts.Fit("sawmillsash");
        Assert.Equal(FitVerdict.NeedsSashes, parts.Fit("sawmillblade-iron"));
        parts.Fit("sawmillsash");
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-iron"));
        Assert.Equal("iron", parts.BladeMetal);
    }

    [Fact]
    public void One_kit_only_of_any_metal()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillblade-iron");
        Assert.Equal(FitVerdict.BladeFitted, parts.Fit("sawmillblade-iron"));
        Assert.Equal(FitVerdict.BladeFitted, parts.Fit("sawmillblade-steel"));
        Assert.Equal("iron", parts.BladeMetal);
    }

    [Fact]
    public void Removing_the_kit_frees_its_place()
    {
        var parts = Fitted("sawmillsash", "sawmillsash", "sawmillblade-iron");
        Assert.True(parts.RemoveBladeKit());
        Assert.False(parts.RemoveBladeKit());
        Assert.Null(parts.BladeMetal);
        Assert.Equal(FitVerdict.Fits, parts.Fit("sawmillblade-steel"));
        Assert.Equal("steel", parts.BladeMetal);
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
        Assert.Equal(["sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-*"],
            new Parts().Missing());
    }

    // The creative shortcut fits the missing parts one per click, in Missing's order, the kit last.
    [Fact]
    public void The_creative_shortcut_steps_through_the_parts_in_order()
    {
        var parts = new Parts();
        var fitted = new List<string>();
        while (parts.NextPart("steel") is { } next)
        {
            Assert.Equal(FitVerdict.Fits, parts.Fit(next));
            fitted.Add(next);
        }
        Assert.Equal(["sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-steel"], fitted);
        Assert.True(parts.Complete);
        // From wherever a hand-built mill is.
        var some = Fitted("sawmilllevers", "sawmillsash");
        Assert.Equal("sawmillsash", some.NextPart("copper"));
        some.Fit("sawmillsash");
        Assert.Equal("sawmillcrankshaft", some.NextPart("copper"));
        some.Fit("sawmillcrankshaft");
        Assert.Equal("sawmillblade-copper", some.NextPart("copper"));
    }

    [Fact]
    public void Restored_state_drops_a_kit_without_both_sashes()
    {
        var parts = new Parts(sashes: 1, crankshaft: true, levers: true, bladeMetal: "iron");
        Assert.Null(parts.BladeMetal);
        Assert.True(parts.Levers);
        Assert.Equal("iron", new Parts(sashes: 2, bladeMetal: "iron").BladeMetal);
        Assert.Null(new Parts(sashes: 2, bladeMetal: "").BladeMetal);
        Assert.Equal(2, new Parts(sashes: 5).Sashes);
    }
}
