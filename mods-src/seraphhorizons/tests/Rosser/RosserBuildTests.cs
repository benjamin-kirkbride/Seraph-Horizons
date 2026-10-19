using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The rosser's build file (<c>config/builds/rosser.json</c>) on the shared machine base, with its rules
/// beyond the file (<see cref="RosserBuild"/>): the heads on the ring as a set of four, their capacity by metal and
/// their wear, and the iron work of any metal with <c>IronWoodworkingMachines</c> off.</summary>
public class RosserBuildTests
{
    private const string Shaft = RosserBuild.ShaftCode, Ring = RosserBuild.RingCode, Levers = RosserBuild.LeversCode;
    private const string CopperPipe = "ppex:pipe-straight-ns-copper", LeadPipe = "ppex:pipe-straight-ns-lead";
    private const string Heads = "immersivewoodworking:barkspudhead-";

    private static MachineBuild File(bool pipes = true, bool anyMetal = false, Func<string, double>? capacity = null)
    {
        var build = MachineBuildJson.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "rosser.json")))
            .WithUses(RosserBuild.HeadsPart, capacity ?? (code => RosserBuild.HeadMetalOf(code) == "gold" ? 280 : 3600));
        if (!pipes)
            build = build.Resolve(code => !code.StartsWith(RosserBuild.PipePrefix, StringComparison.Ordinal));
        return anyMetal ? RosserBuild.AnyMetal(build) : build;
    }

    private static MachineAssembly Complete(string heads = "iron", string pipes = "copper", Func<string, double>? capacity = null)
    {
        var a = new MachineAssembly(File(capacity: capacity));
        foreach (var (code, count) in new[] { (Shaft, 1), (Ring, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (Levers, 1), ($"ppex:pipe-straight-ns-{pipes}", 4) })
            Assert.Equal(AssemblyVerdict.Fitted, a.Fit(code, count, 64).Verdict);
        Assert.Equal(HeadsVerdict.Fits, RosserBuild.FitHeads(a, Heads + heads, 4));
        Assert.True(a.Ready);
        return a;
    }

    [Theory]
    [InlineData("immersivewoodworking:barkspudhead-iron", "iron")]
    [InlineData("immersivewoodworking:barkspudhead-", null)]
    [InlineData("immersivewoodworking:barkspudhead-iron-x", null)]
    [InlineData("game:hoop-iron", null)]
    public void Heads_are_recognised_by_code(string code, string? metal) => Assert.Equal(metal, RosserBuild.HeadMetalOf(code));

    [Theory]
    [InlineData("ppex:pipe-straight-ns-copper", "copper")]
    [InlineData("ppex:pipe-straight-we-lead", "lead")]
    [InlineData("ppex:pipe-straight-ud-iron", "iron")]
    [InlineData("ppex:pipe-straight-xy-copper", null)]
    [InlineData("pipe-straight-ns-copper", null)]
    [InlineData("seraphhorizons:pipesection-copper", null)]
    public void Pipes_are_recognised_by_code(string code, string? metal) => Assert.Equal(metal, RosserBuild.PipeMetalOf(code));

    [Fact]
    public void A_click_takes_what_a_part_still_needs()
    {
        var a = new MachineAssembly(File());
        // a ring section at a time, or the rest of a stack
        Assert.Equal(3, a.Fit(Ring, 3, 64).Take);
        Assert.False(a.Shows("ring"));
        Assert.Equal(1, a.Fit(Ring, 64, 64).Take);
        Assert.True(a.Shows("ring"));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, a.Check(Ring, 1, 64).Verdict);
        // rods: the infeed's two, then the outfeed's, of any of the iron metals, from one stack or several
        Assert.Equal(1, a.Fit("game:rod-iron", 1, 64).Take);
        Assert.False(RosserBuild.Shows(a, "rollsin"));
        Assert.Equal(2, a.Fit("game:rod-steel", 2, 64).Take);
        Assert.True(RosserBuild.Shows(a, "rollsin"));
        Assert.False(RosserBuild.Shows(a, "rollsout"));
        Assert.Equal(1, a.Fit("game:rod-iron", 32, 64).Take);
        Assert.True(RosserBuild.Shows(a, "rollsout"));
        Assert.Contains(new StageDrop("game:rod-iron", 2), a.Returns());
        Assert.Contains(new StageDrop("game:rod-steel", 2), a.Returns());
        Assert.Equal(AssemblyVerdict.AlreadyFitted, a.Check("game:rod-iron", 1, 64).Verdict);
        Assert.Equal(1, a.Fit(Shaft, 1, 1).Take);
        Assert.True(a.Shows("shaft"));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, a.Check(Shaft, 1, 1).Verdict);
    }

    [Fact]
    public void The_heads_need_the_whole_ring_and_go_on_as_a_set_of_four()
    {
        var a = new MachineAssembly(File());
        Assert.Equal(HeadsVerdict.NeedsRing, RosserBuild.CanFitHeads(a, Heads + "iron", 4));
        Assert.False(RosserBuild.RingIn(a));
        a.Fit(Ring, 3, 64);
        Assert.Equal(HeadsVerdict.NeedsRing, RosserBuild.CanFitHeads(a, Heads + "iron", 4));
        a.Fit(Ring, 1, 64);
        Assert.True(RosserBuild.RingIn(a));
        Assert.Equal(HeadsVerdict.NeedsFullSet, RosserBuild.FitHeads(a, Heads + "iron", 3));
        Assert.Null(a.Wearing.Fitted(RosserBuild.HeadsPart));
        Assert.Equal(HeadsVerdict.Fits, RosserBuild.FitHeads(a, Heads + "iron", 16));
        Assert.Equal((Heads + "iron", 3600.0), a.Wearing.Fitted(RosserBuild.HeadsPart));
        Assert.True(RosserBuild.HeadsUnworn(a));
        Assert.True(a.Shows("heads"));
        Assert.Equal(HeadsVerdict.AlreadyFitted, RosserBuild.CanFitHeads(a, Heads + "steel", 4));
        Assert.Equal(HeadsVerdict.NotHeads, RosserBuild.CanFitHeads(a, "game:hoop-iron", 4));
        // the rest of the build is not done: the heads are not one of its stage's materials
        Assert.False(a.Built);
    }

    [Fact]
    public void The_pipes_go_on_as_four_of_copper_or_lead_drawn_in_their_metal()
    {
        var a = new MachineAssembly(File());
        Assert.Equal(AssemblyVerdict.TooFew, a.Check(LeadPipe, 3, 64).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, a.Check("ppex:pipe-straight-ns-iron", 4, 64).Verdict);
        Assert.False(RosserBuild.Shows(a, "pipelead"));
        // no ring needed, and a click takes four of a bigger stack
        Assert.Equal(4, a.Fit(LeadPipe, 16, 64).Take);
        Assert.True(RosserBuild.Shows(a, "pipelead"));
        Assert.False(RosserBuild.Shows(a, "pipecopper"));
        Assert.Equal("lead", RosserBuild.PipeMetal(a));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, a.Check(CopperPipe, 4, 64).Verdict);
        // any orientation of the straight pipe is the pipe, and goes back as it came
        var we = new MachineAssembly(File());
        Assert.Equal(4, we.Fit("ppex:pipe-straight-we-copper", 4, 64).Take);
        Assert.True(we.Shows("pipecopper"));
        Assert.Equal(new[] { new StageDrop("ppex:pipe-straight-we-copper", 4) }, RosserBuild.Returns(we));
    }

    [Fact]
    public void Without_the_copper_and_lead_pipes_the_rosser_is_built_without_them()
    {
        var a = new MachineAssembly(File(pipes: false));
        Assert.Equal(AssemblyVerdict.NotAPart, a.Check(CopperPipe, 4, 64).Verdict);
        foreach (var (code, count) in new[] { (Shaft, 1), (Ring, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (Levers, 1) })
            a.Fit(code, count, 64);
        Assert.True(a.Built);
        RosserBuild.FitHeads(a, Heads + "iron", 4);
        Assert.True(a.Ready);
        // no pipes drawn in either metal
        Assert.Null(RosserBuild.PipeMetal(a));
        Assert.False(RosserBuild.Shows(a, "pipecopper"));
        Assert.False(RosserBuild.Shows(a, "pipelead"));
    }

    [Theory]
    [InlineData("game:hoop-tinbronze")]
    [InlineData("game:rod-copper")]
    [InlineData("game:metalplate-gold")]
    public void The_iron_work_is_iron_unless_any_metal_is_allowed(string code)
    {
        Assert.True(RosserBuild.IsIronWork(code));
        var iron = new MachineAssembly(File());
        Assert.Equal(AssemblyVerdict.NotAPart, iron.Check(code, 4, 64).Verdict);
        var any = new MachineAssembly(File(anyMetal: true));
        Assert.Equal(AssemblyVerdict.Fitted, any.Check(code, 4, 64).Verdict);
        // the heads may be any metal either way
        iron.Fit(Ring, 4, 64);
        Assert.Equal(HeadsVerdict.Fits, RosserBuild.CanFitHeads(iron, Heads + "copper", 4));
    }

    [Fact]
    public void The_creative_shortcut_fills_the_parts_in_steel_and_copper_then_steel_heads()
    {
        var a = new MachineAssembly(File());
        Assert.True(a.FillNext());
        Assert.True(a.Built);
        var drops = a.Returns();
        foreach (var code in new[] { Shaft, Ring, "game:hoop-steel", "game:rod-steel", "game:metalplate-steel", Levers, CopperPipe })
            Assert.Contains(drops, d => d.Code == code);
        Assert.Equal(4, drops.Single(d => d.Code == "game:rod-steel").Count);
        Assert.True(a.FillNext());
        Assert.Equal(Heads + "steel", a.Wearing.Fitted(RosserBuild.HeadsPart)!.Value.Code);
        Assert.True(a.Ready);
    }

    [Fact]
    public void Wear_and_capacity()
    {
        Assert.Equal(3600, RosserBuild.HeadCapacity(900, 4));
        Assert.Equal(9000, RosserBuild.HeadCapacity(2250, 4));
        Assert.Equal(1, RosserBuild.HeadCapacity(0, 4));
        Assert.Equal(20, RosserBuild.WearFor(20, 1));
        Assert.Equal(40, RosserBuild.WearFor(20, 2));
        Assert.Equal(1, RosserBuild.WearFor(3, 0.1f));
        Assert.Equal(3, RosserBuild.WearFor(10, 0.3f));
        Assert.Equal(0, RosserBuild.WearFor(10, 0));

        var a = Complete("iron", capacity: _ => 50);
        Assert.False(a.Wearing.Wear(RosserBuild.HeadsPart, 0));
        Assert.True(RosserBuild.HeadsUnworn(a));
        Assert.False(a.Wearing.Wear(RosserBuild.HeadsPart, 20));
        Assert.Equal(30, a.Wearing.Fitted(RosserBuild.HeadsPart)!.Value.Left);
        Assert.False(RosserBuild.HeadsUnworn(a));
        Assert.True(a.Ready);
        Assert.False(a.Wearing.Wear(RosserBuild.HeadsPart, 29));
        Assert.True(a.Wearing.Wear(RosserBuild.HeadsPart, 1));     // at zero they are spent
        Assert.Null(a.Wearing.Fitted(RosserBuild.HeadsPart));
        Assert.False(a.Ready);
        Assert.False(a.Shows("heads"));
        Assert.False(a.Wearing.Wear(RosserBuild.HeadsPart, 5));    // nothing left to wear
        // a fresh set goes on
        Assert.Equal(HeadsVerdict.Fits, RosserBuild.FitHeads(a, Heads + "steel", 4));
        Assert.True(a.Ready);
        // overwear spends them too
        Assert.True(a.Wearing.Wear(RosserBuild.HeadsPart, 480));
    }

    [Fact]
    public void Unworn_heads_come_back_out_worn_ones_do_not()
    {
        var a = Complete("gold");
        Assert.Contains(new StageDrop(Heads + "gold", 4), RosserBuild.Returns(a));
        Assert.Equal(280, a.Wearing.Fitted(RosserBuild.HeadsPart)!.Value.Left);
        a.Wearing.Wear(RosserBuild.HeadsPart, 1);
        Assert.False(RosserBuild.HeadsUnworn(a));
        Assert.DoesNotContain(RosserBuild.Returns(a), d => d.Code.StartsWith(Heads, StringComparison.Ordinal));
        Assert.Contains(new StageDrop(Ring, 4), RosserBuild.Returns(a));
        Assert.Contains(new StageDrop("game:rod-iron", 4), RosserBuild.Returns(a));
        Assert.Empty(RosserBuild.Returns(new MachineAssembly(File())));
    }

    [Fact]
    public void Save_and_restore_round_trip()
    {
        var a = Complete("iron", "lead");
        a.Wearing.Wear(RosserBuild.HeadsPart, 100);
        var (build, wearing) = a.Snapshot();
        var restored = MachineAssembly.Restore(File(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.True(restored.Ready);
        Assert.True(restored.Shows("pipelead"));
        Assert.False(restored.Shows("pipecopper"));
        Assert.Equal(3500, restored.Wearing.Fitted(RosserBuild.HeadsPart)!.Value.Left);
        Assert.Equal(a.Returns(), restored.Returns());
    }

    [Fact]
    public void Requires_vocabulary()
    {
        Assert.Equal(new[] { "breaker", "heads", "levers", "pipecopper", "pipelead", "ring", "rollsin", "rollsout", "shaft", "tyres" },
                     RosserRequires.KnownRequires.Order());
        // ready, every requires value is drawn but the other metal's pipes
        foreach (var (metal, other) in new[] { ("copper", "lead"), ("lead", "copper") })
        {
            var complete = Complete(pipes: metal);
            Assert.All(RosserRequires.KnownRequires.Where(r => r != RosserRequires.Pipe(other)), r => Assert.True(RosserBuild.Shows(complete, r), r));
            Assert.False(RosserBuild.Shows(complete, RosserRequires.Pipe(other)));
        }
    }
}
