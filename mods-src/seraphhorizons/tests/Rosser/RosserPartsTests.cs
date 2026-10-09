using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

public class RosserPartsTests
{
    private const string Shaft = "immersivewoodworking:sawmillcrankshaft";
    private const string Ring = "game:largegearsection-wood";
    private const string Levers = "immersivewoodworking:sawmilllevers";
    private const string CopperPipe = "ppex:pipe-straight-ns-copper";
    private const string LeadPipe = "ppex:pipe-straight-ns-lead";

    /// <summary>Fits every stage with iron, <paramref name="pipes"/> pipes and <paramref name="heads"/> heads.</summary>
    private static RosserParts Complete(string heads = "steel", int capacity = 9000, string pipes = "copper")
    {
        var parts = new RosserParts();
        foreach (var (code, count) in new[] { (Shaft, 1), (Ring, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (Levers, 1), ($"ppex:pipe-straight-ns-{pipes}", 4),
                                              ($"immersivewoodworking:barkspudhead-{heads}", 4) })
            Assert.Equal(new RosserFit(RosserFitVerdict.Fits, count), parts.Fit(code, count, capacity));
        Assert.True(parts.Complete);
        return parts;
    }

    [Theory]
    [InlineData(Shaft, RosserStage.Shaft, null)]
    [InlineData(Ring, RosserStage.Ring, null)]
    [InlineData(Levers, RosserStage.Levers, null)]
    [InlineData("game:hoop-iron", RosserStage.Tyres, "iron")]
    [InlineData("hoop-steel", RosserStage.Tyres, "steel")]
    [InlineData("game:rod-meteoriciron", RosserStage.RollsIn, "meteoriciron")]
    [InlineData("game:metalplate-copper", RosserStage.Breaker, "copper")]
    [InlineData("immersivewoodworking:barkspudhead-tinbronze", RosserStage.Heads, "tinbronze")]
    [InlineData(CopperPipe, RosserStage.Pipes, "copper")]
    [InlineData("ppex:pipe-straight-we-lead", RosserStage.Pipes, "lead")]
    [InlineData("ppex:pipe-straight-ud-iron", RosserStage.Pipes, "iron")]
    public void Recognises_the_parts(string code, RosserStage stage, string? metal)
    {
        Assert.Equal(stage, RosserParts.StageOf(code, out var m));
        Assert.Equal(metal, m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("immersivewoodworking:sawmillsash")]
    [InlineData("immersivewoodworking:sawmillblade-iron")]
    [InlineData("game:sawmillcrankshaft")]
    [InlineData("immersivewoodworking:largegearsection-wood")]
    [InlineData("game:hoop-")]
    [InlineData("game:rod-iron-extra")]
    [InlineData("game:metalplate")]
    [InlineData("game:barkspudhead-iron")]
    [InlineData("immersivewoodworking:barkspud-iron")]
    [InlineData("ppex:pipe-straight-ns")]
    [InlineData("ppex:pipe-straight-xy-copper")]
    [InlineData("ppex:pipe-straight-ns-copper-extra")]
    [InlineData("ppex:pipe-bend-nw-copper")]
    [InlineData("ppex:valve-sn-tinbronze")]
    [InlineData("pipe-straight-ns-copper")]
    [InlineData("seraphhorizons:pipesection-copper")]
    public void Other_items_are_not_parts(string? code)
    {
        Assert.Null(RosserParts.StageOf(code, out _));
        Assert.Equal(RosserFitVerdict.NotAPart, new RosserParts().CanFit(code, 4).Verdict);
        Assert.False(new RosserParts().TakesClick(code));
    }

    [Fact]
    public void Quantities_take_what_the_stage_still_needs()
    {
        var parts = new RosserParts();
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 4), parts.Fit(Ring, 64));
        Assert.True(parts.Has(RosserStage.Ring));
        Assert.Equal(RosserFitVerdict.AlreadyFitted, parts.CanFit(Ring, 1).Verdict);

        // rods: the infeed's two, then the outfeed's, from one stack or several
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 1), parts.Fit("game:rod-iron", 1));
        Assert.False(parts.Has(RosserStage.RollsIn));
        Assert.False(parts.Fitted("rollsin"));
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 2), parts.Fit("game:rod-steel", 2));
        Assert.True(parts.Has(RosserStage.RollsIn));
        Assert.Equal(new[] { "game:rod-iron", "game:rod-steel" }, parts.FittedIn(RosserStage.RollsIn));
        Assert.Equal(new[] { "game:rod-steel" }, parts.FittedIn(RosserStage.RollsOut));
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 1), parts.Fit("game:rod-iron", 32));
        Assert.True(parts.Has(RosserStage.RollsOut));
        Assert.Equal(RosserFitVerdict.AlreadyFitted, parts.CanFit("game:rod-iron", 1).Verdict);

        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 1), parts.Fit(Shaft, 1));
        Assert.Equal(RosserFitVerdict.AlreadyFitted, parts.Fit(Shaft, 1).Verdict);
        Assert.Equal(RosserFitVerdict.NotAPart, parts.CanFit(Shaft, 0).Verdict);
    }

    [Fact]
    public void Tyres_and_heads_need_the_whole_ring()
    {
        var parts = new RosserParts();
        Assert.Equal(RosserFitVerdict.NeedsRing, parts.CanFit("game:hoop-iron", 2).Verdict);
        Assert.Equal(RosserFitVerdict.NeedsRing, parts.CanFit("immersivewoodworking:barkspudhead-iron", 4).Verdict);
        parts.Fit(Ring, 3);
        Assert.Equal(RosserFitVerdict.NeedsRing, parts.CanFit("game:hoop-iron", 2).Verdict);
        parts.Fit(Ring, 1);
        Assert.Equal(RosserFitVerdict.Fits, parts.CanFit("game:hoop-iron", 2).Verdict);
        Assert.Equal(RosserFitVerdict.Fits, parts.CanFit("immersivewoodworking:barkspudhead-iron", 4).Verdict);
    }

    [Fact]
    public void Heads_go_on_as_a_set_of_four()
    {
        var parts = new RosserParts();
        parts.Fit(Ring, 4);
        Assert.Equal(RosserFitVerdict.NeedsFullSet, parts.Fit("immersivewoodworking:barkspudhead-iron", 3, 3600).Verdict);
        Assert.Null(parts.HeadMetal);
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 4), parts.Fit("immersivewoodworking:barkspudhead-iron", 4, 3600));
        Assert.Equal("iron", parts.HeadMetal);
        Assert.Equal(3600, parts.HeadsLeft);
        Assert.Equal(3600, parts.HeadsCapacity);
        Assert.True(parts.HeadsUnworn);
        Assert.Equal(RosserFitVerdict.AlreadyFitted, parts.CanFit("immersivewoodworking:barkspudhead-steel", 4).Verdict);
    }

    [Fact]
    public void Pipes_go_on_as_four_of_copper_or_lead()
    {
        var parts = new RosserParts();
        Assert.True(parts.TakesClick(CopperPipe));
        // not iron or steel, whatever the rule for the iron work; and four from one stack
        Assert.Equal(RosserFitVerdict.WrongMetal, parts.CanFit("ppex:pipe-straight-ns-iron", 4).Verdict);
        Assert.Equal(RosserFitVerdict.WrongMetal, new RosserParts(anyMetal: true).CanFit("ppex:pipe-straight-ns-steel", 4).Verdict);
        Assert.Equal(RosserFitVerdict.NeedsFullSet, parts.Fit(LeadPipe, 3).Verdict);
        Assert.Null(parts.PipeMetal);
        Assert.False(parts.Fitted("pipelead"));
        // no ring needed, and a click takes four of a bigger stack
        Assert.Equal(new RosserFit(RosserFitVerdict.Fits, 4), parts.Fit(LeadPipe, 16));
        Assert.Equal("lead", parts.PipeMetal);
        Assert.True(parts.Has(RosserStage.Pipes));
        Assert.True(parts.Fitted("pipelead"));
        Assert.False(parts.Fitted("pipecopper"));
        Assert.Equal(RosserFitVerdict.AlreadyFitted, parts.CanFit(CopperPipe, 4).Verdict);
        // once in, a pipe in hand is not the rosser's: it is placed against it, as on the water face
        Assert.False(parts.TakesClick(CopperPipe));
        Assert.False(parts.TakesClick("ppex:pipe-straight-ns-iron"));
        Assert.True(parts.TakesClick(Ring));
        // any orientation of the straight pipe is the pipe, and goes back as it came
        var we = new RosserParts();
        Assert.True(we.Fit("ppex:pipe-straight-we-copper", 4).Fitted);
        Assert.Equal("copper", we.PipeMetal);
        Assert.Equal(new[] { ("ppex:pipe-straight-we-copper", 4) }, we.Returns());
    }

    [Fact]
    public void Without_the_copper_and_lead_pipes_the_rosser_is_built_without_them()
    {
        var parts = new RosserParts(pipesNeeded: false);
        Assert.False(parts.Required(RosserStage.Pipes));
        Assert.DoesNotContain(parts.Missing(), m => m.Stage == RosserStage.Pipes);
        Assert.Equal(RosserFitVerdict.NotAPart, parts.CanFit(CopperPipe, 4).Verdict);
        Assert.False(parts.TakesClick(CopperPipe));
        foreach (var (code, count) in new[] { (Shaft, 1), (Ring, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (Levers, 1), ("immersivewoodworking:barkspudhead-iron", 4) })
            Assert.True(parts.Fit(code, count, 3600).Fitted);
        Assert.True(parts.Complete);
        Assert.Null(parts.PipeMetal);
        Assert.Null(parts.NextPart("steel"));
        // the rule survives a change of metal rule, and a restore
        Assert.False(parts.WithMetals(anyMetal: true).PipesNeeded);
        Assert.True(parts.WithMetals(anyMetal: true, pipesNeeded: true).PipesNeeded);
        Assert.False(parts.WithMetals(anyMetal: true, pipesNeeded: true).Complete);
        Assert.True(RosserParts.Restore(parts.Snapshot(), parts.HeadsLeft, parts.HeadsCapacity, pipesNeeded: false).Complete);
    }

    [Fact]
    public void A_rosser_saved_before_the_pipes_needs_them()
    {
        var saved = Complete().Snapshot().Where(kv => kv.Key != "pipes").ToDictionary(kv => kv.Key, kv => kv.Value);
        var parts = RosserParts.Restore(saved, 9000, 9000);
        Assert.False(parts.Complete);
        Assert.Equal(new[] { (RosserStage.Pipes, "ppex:pipe-straight-ns-*", 4) }, parts.Missing());
        Assert.Equal((CopperPipe, 4), parts.NextPart("steel"));
        Assert.Equal((LeadPipe, 4), parts.NextPart("lead"));
        Assert.True(parts.Fit(LeadPipe, 4).Fitted);
        Assert.True(parts.Complete);
    }

    [Theory]
    [InlineData("game:hoop-tinbronze")]
    [InlineData("game:rod-copper")]
    [InlineData("game:metalplate-gold")]
    public void Metal_parts_are_iron_work(string code)
    {
        var parts = new RosserParts();
        parts.Fit(Ring, 4);
        Assert.Equal(RosserFitVerdict.WrongMetal, parts.CanFit(code, 4).Verdict);
        // unless the caller allows any metal (IronWoodworkingMachines off)
        var any = new RosserParts(anyMetal: true);
        any.Fit(Ring, 4);
        Assert.Equal(RosserFitVerdict.Fits, any.CanFit(code, 4).Verdict);
        // the heads may be any metal
        Assert.Equal(RosserFitVerdict.Fits, parts.CanFit("immersivewoodworking:barkspudhead-copper", 4).Verdict);
    }

    [Fact]
    public void With_metals_keeps_what_is_fitted_and_applies_the_new_rule_to_what_comes_next()
    {
        var any = new RosserParts(anyMetal: true);
        Assert.True(any.Fit(Ring, 4).Fitted);
        Assert.True(any.Fit("game:hoop-tinbronze", 2).Fitted);
        Assert.True(any.Fit("immersivewoodworking:barkspudhead-iron", 4, 3600).Fitted);
        any.WearHeads(100);
        var iron = any.WithMetals(RosserParts.IronMetals);
        Assert.Equal(any.Snapshot(), iron.Snapshot());
        Assert.Equal((3500, 3600), (iron.HeadsLeft, iron.HeadsCapacity));
        Assert.True(iron.Has(RosserStage.Tyres));
        Assert.Equal(RosserFitVerdict.WrongMetal, iron.CanFit("game:rod-copper", 2).Verdict);
        Assert.Equal(RosserFitVerdict.Fits, any.CanFit("game:rod-copper", 2).Verdict);
        // the copy is its own: fitting one leaves the other
        Assert.True(iron.Fit("game:rod-iron", 2).Fitted);
        Assert.False(any.Has(RosserStage.RollsIn));
    }

    [Fact]
    public void A_custom_metal_list()
    {
        var parts = new RosserParts(new HashSet<string> { "copper" });
        Assert.Equal(RosserFitVerdict.Fits, parts.CanFit("game:rod-copper", 2).Verdict);
        Assert.Equal(RosserFitVerdict.WrongMetal, parts.CanFit("game:rod-iron", 2).Verdict);
    }

    [Fact]
    public void Requires_vocabulary()
    {
        Assert.Equal(new[] { "breaker", "heads", "levers", "pipecopper", "pipelead", "ring", "rollsin", "rollsout", "shaft", "tyres" },
                     RosserRequires.KnownRequires.Order());
        var parts = new RosserParts();
        Assert.True(parts.Fitted(null));
        Assert.False(parts.Fitted("shaft"));
        Assert.False(parts.Fitted("crankshaft"));
        parts.Fit(Shaft, 1);
        Assert.True(parts.Fitted("shaft"));
        // complete, every requires value is drawn but the other metal's pipes
        foreach (var (metal, other) in new[] { ("copper", "lead"), ("lead", "copper") })
        {
            var complete = Complete(pipes: metal);
            Assert.All(RosserRequires.KnownRequires.Where(r => r != RosserRequires.Pipe(other)), r => Assert.True(complete.Fitted(r), r));
            Assert.False(complete.Fitted(RosserRequires.Pipe(other)));
            Assert.False(complete.Fitted("blade"));
        }
    }

    [Fact]
    public void The_creative_order_and_next_part()
    {
        var parts = new RosserParts();
        Assert.Equal(new[]
        {
            (RosserStage.Shaft, Shaft, 1), (RosserStage.Ring, Ring, 4), (RosserStage.Tyres, "game:hoop-*", 2),
            (RosserStage.RollsIn, "game:rod-*", 2), (RosserStage.RollsOut, "game:rod-*", 2), (RosserStage.Breaker, "game:metalplate-*", 2),
            (RosserStage.Levers, Levers, 1), (RosserStage.Pipes, "ppex:pipe-straight-ns-*", 4), (RosserStage.Heads, "immersivewoodworking:barkspudhead-*", 4),
        }, parts.Missing());
        // the shortcut fits stage by stage, always successfully: the pipes copper, steel being no pipe metal
        var fitted = new List<string>();
        while (parts.NextPart("steel") is var (code, count))
        {
            Assert.Equal(new RosserFit(RosserFitVerdict.Fits, count), parts.Fit(code, count, 9000));
            fitted.Add(code);
        }
        Assert.Equal(new[]
        {
            Shaft, Ring, "game:hoop-steel", "game:rod-steel", "game:rod-steel", "game:metalplate-steel", Levers, CopperPipe,
            "immersivewoodworking:barkspudhead-steel",
        }, fitted);
        Assert.True(parts.Complete);
        Assert.Empty(parts.Missing());
        Assert.Null(parts.NextPart("steel"));
    }

    [Fact]
    public void Next_part_after_a_partial_stage_is_what_remains()
    {
        var parts = new RosserParts();
        parts.Fit(Shaft, 1);
        parts.Fit(Ring, 2);
        Assert.Equal((Ring, 2), parts.NextPart("iron"));
    }

    [Fact]
    public void Wear_and_capacity()
    {
        Assert.Equal(3600, RosserParts.HeadCapacity(900, 4));
        Assert.Equal(9000, RosserParts.HeadCapacity(2250, 4));
        Assert.Equal(1, RosserParts.HeadCapacity(0, 4));
        Assert.Equal(20, RosserParts.WearFor(20, 1));
        Assert.Equal(40, RosserParts.WearFor(20, 2));
        Assert.Equal(1, RosserParts.WearFor(3, 0.1f));
        Assert.Equal(3, RosserParts.WearFor(10, 0.3f));
        Assert.Equal(0, RosserParts.WearFor(10, 0));

        var parts = Complete("iron", 50);
        Assert.False(parts.WearHeads(0));
        Assert.True(parts.HeadsUnworn);
        Assert.False(parts.WearHeads(20));
        Assert.Equal(30, parts.HeadsLeft);
        Assert.False(parts.HeadsUnworn);
        Assert.True(parts.Complete);
        Assert.False(parts.WearHeads(29));
        Assert.True(parts.WearHeads(1));     // at zero they are spent
        Assert.Null(parts.HeadMetal);
        Assert.False(parts.Complete);
        Assert.False(parts.Fitted("heads"));
        Assert.Equal(0, parts.HeadsLeft);
        Assert.False(parts.WearHeads(5));    // nothing left to wear
        // a fresh set goes on
        Assert.Equal(RosserFitVerdict.Fits, parts.Fit("immersivewoodworking:barkspudhead-steel", 4, 9000).Verdict);
        Assert.True(parts.Complete);
    }

    [Fact]
    public void Overwear_spends_the_heads()
    {
        var parts = Complete("copper", 10);
        Assert.True(parts.WearHeads(48));
        Assert.Null(parts.HeadMetal);
    }

    [Fact]
    public void Unworn_heads_come_back_out_worn_ones_do_not()
    {
        var parts = Complete("iron", 3600);
        Assert.Equal(("immersivewoodworking:barkspudhead-iron", 4), parts.RemoveHeads());
        Assert.Null(parts.HeadMetal);
        Assert.Null(parts.RemoveHeads());
        parts.Fit("immersivewoodworking:barkspudhead-iron", 4, 3600);
        parts.WearHeads(1);
        Assert.Null(parts.RemoveHeads());
        Assert.Equal("iron", parts.HeadMetal);
    }

    [Fact]
    public void Breaking_returns_what_went_in()
    {
        var parts = new RosserParts();
        parts.Fit(Ring, 4);
        parts.Fit("game:rod-iron", 1);
        parts.Fit("game:rod-steel", 2);
        parts.Fit("game:hoop-steel", 2);
        parts.Fit("immersivewoodworking:barkspudhead-gold", 4, 280);
        Assert.Equal(new[] { (Ring, 4), ("game:hoop-steel", 2), ("game:rod-iron", 1), ("game:rod-steel", 2), ("immersivewoodworking:barkspudhead-gold", 4) },
                     parts.Returns());
        parts.WearHeads(1);
        Assert.Equal(new[] { (Ring, 4), ("game:hoop-steel", 2), ("game:rod-iron", 1), ("game:rod-steel", 2) }, parts.Returns());
        Assert.Equal(new[] { (Shaft, 1), (Ring, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4), ("game:metalplate-iron", 2), (Levers, 1), (CopperPipe, 4),
                             ("immersivewoodworking:barkspudhead-steel", 4) },
                     Complete().Returns());
        Assert.Empty(new RosserParts().Returns());
    }

    [Fact]
    public void Save_and_restore_round_trip()
    {
        var parts = Complete("iron", 3600, "lead");
        parts.WearHeads(100);
        var snapshot = parts.Snapshot();
        Assert.Equal(RosserRequires.Stages.Select(RosserRequires.Name).Order(), snapshot.Keys.Order());
        var restored = RosserParts.Restore(snapshot, parts.HeadsLeft, parts.HeadsCapacity);
        Assert.True(restored.Complete);
        Assert.Equal("iron", restored.HeadMetal);
        Assert.Equal("lead", restored.PipeMetal);
        Assert.Equal(3500, restored.HeadsLeft);
        Assert.Equal(3600, restored.HeadsCapacity);
        Assert.Equal(parts.Returns(), restored.Returns());
        foreach (var stage in RosserRequires.Stages)
            Assert.Equal(parts.FittedIn(stage), restored.FittedIn(stage));
    }

    [Fact]
    public void Restore_drops_what_could_not_have_been_fitted()
    {
        var saved = new Dictionary<string, IReadOnlyList<string>>
        {
            ["shaft"] = [Shaft, Shaft],                                   // one too many
            ["ring"] = [Ring, Ring, Ring],                                 // incomplete ring
            ["tyres"] = ["game:hoop-iron", "game:hoop-iron"],              // needs the ring
            ["heads"] = Enumerable.Repeat("immersivewoodworking:barkspudhead-iron", 4).ToArray(),
            ["rollsin"] = ["game:rod-copper", "game:rod-iron", "game:hoop-iron"],  // copper is not iron work; a hoop is not a rod
            ["rollsout"] = ["game:rod-iron", "game:rod-iron", "game:rod-iron"],
            ["breaker"] = ["game:metalplate-iron"],
            ["levers"] = ["nonsense"],
            ["pipes"] = [CopperPipe, CopperPipe, LeadPipe, LeadPipe],             // two metals
            ["unknown"] = ["x"],
        };
        var parts = RosserParts.Restore(saved, 100, 3600);
        Assert.Equal(new[] { Shaft }, parts.FittedIn(RosserStage.Shaft));
        Assert.Equal(3, parts.FittedIn(RosserStage.Ring).Count);
        Assert.Empty(parts.FittedIn(RosserStage.Tyres));
        Assert.Empty(parts.FittedIn(RosserStage.Heads));
        Assert.Equal(0, parts.HeadsLeft);
        Assert.Equal(new[] { "game:rod-iron" }, parts.FittedIn(RosserStage.RollsIn));
        Assert.Equal(2, parts.FittedIn(RosserStage.RollsOut).Count);
        Assert.Single(parts.FittedIn(RosserStage.Breaker));
        Assert.Empty(parts.FittedIn(RosserStage.Levers));
        Assert.Empty(parts.FittedIn(RosserStage.Pipes));
        // and pipes of another metal or a part set
        var iron = new Dictionary<string, IReadOnlyList<string>> { ["pipes"] = Enumerable.Repeat("ppex:pipe-straight-ns-iron", 4).ToArray() };
        Assert.Empty(RosserParts.Restore(iron, 0, 0).FittedIn(RosserStage.Pipes));
        var three = new Dictionary<string, IReadOnlyList<string>> { ["pipes"] = Enumerable.Repeat(CopperPipe, 3).ToArray() };
        Assert.Empty(RosserParts.Restore(three, 0, 0).FittedIn(RosserStage.Pipes));
    }

    [Fact]
    public void Restore_drops_mixed_partial_or_spent_heads()
    {
        var ring = new Dictionary<string, IReadOnlyList<string>> { ["ring"] = Enumerable.Repeat(Ring, 4).ToArray() };
        IReadOnlyDictionary<string, IReadOnlyList<string>> With(params string[] heads) =>
            new Dictionary<string, IReadOnlyList<string>>(ring) { ["heads"] = heads };
        string iron = "immersivewoodworking:barkspudhead-iron", steel = "immersivewoodworking:barkspudhead-steel";
        Assert.Null(RosserParts.Restore(With(iron, iron, iron, steel), 10, 10).HeadMetal);
        Assert.Null(RosserParts.Restore(With(iron, iron, iron), 10, 10).HeadMetal);
        Assert.Null(RosserParts.Restore(With(iron, iron, iron, iron), 0, 10).HeadMetal);
        var ok = RosserParts.Restore(With(iron, iron, iron, iron), 50, 10);
        Assert.Equal("iron", ok.HeadMetal);
        Assert.Equal(10, ok.HeadsLeft);   // capped at the capacity
        Assert.True(ok.HeadsUnworn);
        Assert.Equal(1, RosserParts.Restore(With(iron, iron, iron, iron), 1, 0).HeadsCapacity);
    }
}
