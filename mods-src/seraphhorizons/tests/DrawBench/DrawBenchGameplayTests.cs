using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.DrawBench;

/// <summary>DrawBench: its build (config/builds/drawbench.json on the shared staged build, the die its durable
/// wearing part): the order, what each stage takes, the die's metal, take-back and wear; the draw's arithmetic
/// (W with the axle, a section at each hand-out); the renderer's clock; the settings; and the rig's reader on
/// a hand-trimmed test rig.</summary>
public class DrawBenchGameplayTests
{
    private const string Gearbox = "game:jonasframes-gearbox01";
    private const string Spring = "seraphhorizons:spring-steel";

    private static readonly string[] Order =
    [
        Gearbox, "game:metalchain-iron", "game:bracket-heavy-steel", "game:rod-meteoriciron",
    ];

    /// <summary>The build as a game with springs (or without) has it, the die's uses the settings' durability.</summary>
    private static MachineBuild Build(bool springs = true) =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "drawbench.json")))
            .Resolve(code => springs || code != Spring).WithUses(Drawing.DiePart, Config.DieDurability);

    /// <summary>Fits <paramref name="code"/> from a stack of <paramref name="held"/> (two for the chain and the rods).</summary>
    private static AssemblyVerdict Fit(MachineAssembly parts, string? code, int held = 2, double? uses = null) =>
        parts.Fit(code, held, 64, uses).Verdict;

    /// <summary>Every stage up to the mandrel fitted, the mandrel's spring with its rods.</summary>
    private static MachineAssembly UpToMandrel()
    {
        var parts = new MachineAssembly(Build());
        foreach (var code in Order)
            Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, code));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Spring, 1));
        return parts;
    }

    private static MachineAssembly Complete(string die = Drawing.DieSteelCode, int left = 100)
    {
        var parts = UpToMandrel();
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, die, 1, left));
        return parts;
    }

    private static string? DieMetal(MachineAssembly parts) => Drawing.DieMetalOf(parts.Wearing.Fitted(Drawing.DiePart)?.Code);

    private static readonly DrawBenchConfig Config = new();

    // ---- The build ----

    [Fact]
    public void The_stages_go_in_in_the_contracts_order()
    {
        var build = Build();
        Assert.Equal(DrawBenchRequires.Stages, build.Stages.Select(s => s.Id));
        Assert.Equal(DrawBenchRequires.Stages, build.Stages.Select(s => Assert.Single(s.Adds)));
        Assert.Null(build.Block);   // its construction record is its own in the export
        var parts = new MachineAssembly(build);
        foreach (var (code, stage) in Order.Zip(Enumerable.Range(0, 4)))
        {
            Assert.Equal(stage, parts.Build.Next);
            Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, code));
        }
        // the mandrel stage is in once its spring is too
        Assert.Equal(3, parts.Build.Next);
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Spring, 1));
        Assert.Equal(4, parts.Build.Next);
        Assert.False(parts.Ready);
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Drawing.DieIronCode, 1, 100));
        Assert.True(parts.Ready);
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.OutOfOrder, Fit(parts, "game:metalchain-steel"));
        Assert.Equal(AssemblyVerdict.OutOfOrder, Fit(parts, "game:rod-iron"));
        Assert.Equal(AssemblyVerdict.OutOfOrder, Fit(parts, Drawing.DieSteelCode, 1, 100));
        Assert.Equal(AssemblyVerdict.NotAPart, Fit(parts, "game:jonasframes-gearbox02"));
        Assert.Equal(AssemblyVerdict.NotAPart, Fit(parts, "game:metalchain-copper"));
        Assert.Equal(AssemblyVerdict.NotAPart, Fit(parts, "game:rod-copper"));
        Assert.Equal(AssemblyVerdict.NotAPart, Fit(parts, null));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "jonasframes-gearbox01", 1));   // a code with no domain is the game's
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Fit(parts, Gearbox, 1));
        Assert.Equal(1, parts.Build.Next);
    }

    [Fact]
    public void The_chain_and_the_mandrel_take_two_from_one_stack()
    {
        Assert.Equal([1, 2, 1, 2], Build().Stages.Take(4).Select(s => s.Materials[0].Quantity));
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Gearbox, 1));
        Assert.Equal(AssemblyVerdict.TooFew, Fit(parts, "game:metalchain-iron", 1));
        Assert.Equal(1, parts.Build.Next);
        var chain = parts.Fit("game:metalchain-iron", 5, 64);
        Assert.Equal((AssemblyVerdict.Fitted, 2), (chain.Verdict, chain.Take));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:bracket-heavy-iron", 1));
        Assert.Equal(AssemblyVerdict.TooFew, Fit(parts, "game:rod-iron", 1));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:rod-iron", 5));
        // a part already in, or out of order, says so before counting
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Fit(parts, "game:metalchain-iron", 1));
        Assert.Equal([1, 2, 1, 2], parts.Returns().Select(d => d.Count));
    }

    [Theory]
    [InlineData("iron")]
    [InlineData("meteoriciron")]
    [InlineData("steel")]
    public void Each_ferrous_chain_bracket_and_rod_fits(string metal)
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Gearbox, 1));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:metalchain-" + metal));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:bracket-heavy-" + metal, 1));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:rod-" + metal));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Spring, 1));
        Assert.Equal(4, parts.Build.Next);
    }

    [Fact]
    public void The_die_carries_its_metal_and_durability()
    {
        var iron = Complete(Drawing.DieIronCode, 37);
        Assert.Equal("iron", DieMetal(iron));
        Assert.Equal((Drawing.DieIronCode, 37.0), iron.Wearing.Fitted(Drawing.DiePart));
        Assert.Equal(0.37, iron.Wearing.ShareLeft(Drawing.DiePart), 9);
        Assert.Equal("steel", DieMetal(Complete()));
        Assert.Null(DieMetal(UpToMandrel()));
        Assert.Equal(AssemblyVerdict.Spent, Fit(UpToMandrel(), Drawing.DieSteelCode, 1, 0));
        Assert.Equal(("iron", "steel"), (Drawing.DieMetalOf(Drawing.DieIronCode), Drawing.DieMetalOf(Drawing.DieSteelCode)));
        Assert.Equal(Drawing.DieSteelCode, Drawing.DieCodeFor("steel"));
        Assert.Null(Drawing.DieMetalOf("game:rod-steel"));
    }

    [Fact]
    public void Each_stage_is_drawn_once_it_is_in_the_die_while_one_is_fitted_and_never_the_billets()
    {
        var parts = UpToMandrel();
        Assert.True(parts.Shows(null));
        Assert.True(parts.Shows("mandrel"));
        Assert.False(parts.Shows("die"));
        Assert.False(new MachineAssembly(Build()).Shows("gearbox"));
        var complete = Complete();
        Assert.True(complete.Shows("die"));
        complete.Take(Drawing.DiePart);
        Assert.False(complete.Shows("die"));
        // the billets are the work's: no stage adds them (BEDrawBench.ShowsPart draws them by its clock)
        Assert.DoesNotContain(Build().Stages, s => s.Adds.Contains("billetlead") || s.Adds.Contains("billetcopper"));
        Assert.True(DrawBenchRequires.KnownRequires.SetEquals(["gearbox", "chain", "dog", "mandrel", "die", "billetlead", "billetcopper"]));
    }

    [Fact]
    public void The_die_comes_back_out_with_its_durability_and_a_die_goes_back_in()
    {
        var parts = Complete(Drawing.DieIronCode, 64);
        Assert.Equal((Drawing.DieIronCode, 64.0), parts.Take(Drawing.DiePart));
        Assert.False(parts.Ready);
        Assert.True(parts.Built);
        Assert.Null(parts.Take(Drawing.DiePart));
        // the other stages stay, and a die goes back in
        Assert.Equal(AssemblyVerdict.Renewed, Fit(parts, Drawing.DieIronCode, 1, 64));
        Assert.True(parts.Ready);
        Assert.Equal(AssemblyVerdict.NotWorn, Fit(parts, Drawing.DieSteelCode, 1, 100));
    }

    [Fact]
    public void A_spent_die_is_gone_and_the_bench_needs_another()
    {
        var parts = Complete(left: 2);
        Assert.False(parts.Wearing.Wear(Drawing.DiePart, 1));
        Assert.Equal(1, parts.Wearing.Fitted(Drawing.DiePart)!.Value.Left);
        Assert.True(parts.Wearing.Wear(Drawing.DiePart, 1));
        Assert.False(parts.Ready);
        Assert.Null(DieMetal(parts));
        Assert.DoesNotContain(parts.Returns(), d => d.Durability != null);
        // the creative shortcut fits a new steel die
        Assert.True(parts.FillNext());
        Assert.Equal((Drawing.DieSteelCode, 100.0), parts.Wearing.Fitted(Drawing.DiePart));
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_the_die_with_its_durability()
    {
        var parts = Complete(left: 58);
        var drops = parts.Returns();
        Assert.Equal(6, drops.Count);
        Assert.Equal([.. Order, Spring, Drawing.DieSteelCode], drops.Select(d => d.Code));
        Assert.Equal(new StageDrop(Drawing.DieSteelCode, 1, 58), drops[^1]);
        Assert.All(drops.Take(5), d => Assert.Null(d.Durability));
        Assert.Equal([1, 2, 1, 2, 1, 1], drops.Select(d => d.Count));
        Assert.Empty(new MachineAssembly(Build()).Returns());
    }

    [Fact]
    public void The_creative_shortcut_fits_each_stage_in_turn_with_a_steel_die()
    {
        var parts = new MachineAssembly(Build());
        while (!parts.Ready)
            Assert.True(parts.FillNext());
        Assert.Equal([Gearbox, "game:metalchain-iron", "game:bracket-heavy-iron", "game:rod-iron", Spring, Drawing.DieSteelCode],
            parts.Returns().Select(d => d.Code));
        Assert.Equal(100, parts.Wearing.Fitted(Drawing.DiePart)!.Value.Left);
    }

    [Fact]
    public void The_mandrel_stage_takes_its_rods_and_a_spring_either_first()
    {
        var parts = new MachineAssembly(Build());
        foreach (var code in Order.Take(3))
            Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, code));
        // the spring first: one is enough, and a second is refused
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Spring, 1));
        Assert.False(parts.Build.StageComplete(3));
        Assert.False(parts.Shows("mandrel"));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Fit(parts, Spring, 1));
        Assert.Equal(["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"], Assert.Single(parts.Build.NextNeeds()).Material.Codes);
        Assert.Equal(AssemblyVerdict.TooFew, Fit(parts, "game:rod-steel", 1));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, "game:rod-steel", 2));
        Assert.True(parts.Shows("mandrel"));
        Assert.Equal(4, parts.Build.Next);

        // the rods first: the stage then waits for the spring, and more rods are refused
        var rods = new MachineAssembly(Build());
        foreach (var code in Order)
            Assert.Equal(AssemblyVerdict.Fitted, Fit(rods, code));
        Assert.Equal(3, rods.Build.Next);
        Assert.Equal([Spring], Assert.Single(rods.Build.NextNeeds()).Material.Codes);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Fit(rods, "game:rod-iron"));
        Assert.Equal(AssemblyVerdict.OutOfOrder, Fit(rods, Drawing.DieSteelCode, 1, 100));
        // a spring before its stage is out of order
        Assert.Equal(AssemblyVerdict.OutOfOrder, Fit(new MachineAssembly(Build()), Spring, 1));
    }

    [Fact]
    public void Without_springs_the_rods_alone_fit_the_mandrel_stage()
    {
        var parts = new MachineAssembly(Build(springs: false));
        foreach (var code in Order)
            Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, code));
        Assert.Equal(4, parts.Build.Next);
        Assert.Equal(AssemblyVerdict.NotAPart, Fit(parts, Spring, 1));
        Assert.Equal(AssemblyVerdict.Fitted, Fit(parts, Drawing.DieSteelCode, 1, 100));
        Assert.True(parts.Ready);
        Assert.DoesNotContain(parts.Returns(), d => d.Code == Spring);
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_the_die_and_drops_what_could_not_have_been_fitted()
    {
        var parts = Complete(Drawing.DieIronCode, 12);
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal("iron", DieMetal(restored));

        var gap = MachineAssembly.Restore(Build(), new Dictionary<string, int> { [StagedBuild.Key(0, 0, Gearbox)] = 1, [StagedBuild.Key(2, 0, "game:bracket-heavy-iron")] = 1 },
            new Dictionary<string, (string, double)>(), out dropped);
        Assert.Equal([new StageDrop(Gearbox, 1)], gap.Returns());
        Assert.Equal([new StageDrop("game:bracket-heavy-iron", 1)], dropped);
        var wrong = build.ToDictionary(kv => kv.Key, kv => kv.Value);
        wrong.Remove(StagedBuild.Key(1, 0, "game:metalchain-iron"));
        wrong[StagedBuild.Key(1, 0, "game:metalchain-gold")] = 2;
        Assert.Equal(1, MachineAssembly.Restore(Build(), wrong, wearing, out _).Build.Next);
    }

    // ---- The draw ----

    [Fact]
    public void Hollows_and_pipe_sections_by_metal()
    {
        Assert.Equal(1, Drawing.ClassOfHollow("game:chutesection-lead"));
        Assert.Equal(2, Drawing.ClassOfHollow("game:chutesection-copper"));
        Assert.Equal(0, Drawing.ClassOfHollow("game:chutesection-iron"));
        // ingots, angles, plates and what comes off are never taken as input
        Assert.Equal(0, Drawing.ClassOfHollow("game:ingot-lead"));
        Assert.Equal(0, Drawing.ClassOfHollow("game:ingot-copper"));
        Assert.Equal(0, Drawing.ClassOfHollow("seraphhorizons:angle-copper"));
        Assert.Equal(0, Drawing.ClassOfHollow("seraphhorizons:pipesection-lead"));
        Assert.Equal(0, Drawing.ClassOfHollow("seraphhorizons:pipesection-copper"));
        Assert.Equal(0, Drawing.ClassOfHollow("game:metalplate-copper"));
        Assert.Equal(("game:chutesection-lead", "game:chutesection-copper"), (Drawing.HollowFor(1), Drawing.HollowFor(2)));
        Assert.Null(Drawing.HollowFor(0));
        Assert.Equal("seraphhorizons:pipesection-lead", Drawing.SectionFor(1));
        Assert.Equal("seraphhorizons:pipesection-copper", Drawing.SectionFor(2));
        Assert.Null(Drawing.SectionFor(0));
        // the chain's figure: four pipe sections a hollow on the bench
        Assert.Equal(SeraphHorizons.Mod.Pipes.Core.PipeSections.PipeSectionsPerHollowDrawn, Drawing.SectionsPerHollow);
        Assert.Equal(SeraphHorizons.Mod.Pipes.Core.PipeSections.PipeSection("lead"), Drawing.SectionFor(1));
        Assert.Equal(1, Drawing.SectionsFor(2 * Math.PI * 10.3, 10.3), 9);
        Assert.Equal(0, Drawing.SectionsFor(-1, 10.3));
    }

    [Fact]
    public void The_die_decides_the_metal()
    {
        var iron = Config.MetalsFor("iron");
        var steel = Config.MetalsFor("steel");
        Assert.Equal(["lead"], iron);
        Assert.Equal(["lead", "copper"], steel);
        Assert.Empty(Config.MetalsFor(null));
        Assert.Equal(DrawLoadVerdict.Loads, Drawing.CanLoad(Drawing.LeadHollow, true, false, iron.ToList()));
        Assert.Equal(DrawLoadVerdict.DieRefuses, Drawing.CanLoad(Drawing.CopperHollow, true, false, iron.ToList()));
        Assert.Equal(DrawLoadVerdict.Loads, Drawing.CanLoad(Drawing.CopperHollow, true, false, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.Loads, Drawing.CanLoad(Drawing.LeadHollow, true, false, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.Incomplete, Drawing.CanLoad(Drawing.LeadHollow, false, false, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.Occupied, Drawing.CanLoad(Drawing.LeadHollow, true, true, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.NotAHollow, Drawing.CanLoad("game:ingot-lead", true, false, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.NotAHollow, Drawing.CanLoad("seraphhorizons:angle-copper", true, false, steel.ToList()));
        Assert.Equal(DrawLoadVerdict.NotAHollow, Drawing.CanLoad("seraphhorizons:pipesection-lead", true, false, steel.ToList()));
    }

    [Theory]
    [InlineData(1, 1.373)]
    [InlineData(2, 2.746)]
    public void W_advances_with_the_axle_and_a_section_comes_off_at_each_hand_out(int k, double turns)
    {
        Assert.Equal(turns, Config.TurnsPerSection(k), 5);
        var job = Drawing.Start(k);
        var crossings = new List<double>();
        int finished = 0;
        // a tenth of a turn at a time, past the end to show it holds
        double turned = 0;
        for (int i = 0; i < (int)(turns * 4 * 10) + 20; i++)
        {
            (job, int sections, bool done) = Drawing.Advance(job, 2 * Math.PI * 0.1, turns);
            turned += 0.1;
            for (int p = 0; p < sections; p++)
                crossings.Add(turned);
            if (done)
                finished++;
        }
        Assert.Equal(4, crossings.Count);
        // section m + 1 comes off as its draw finishes, at W = m + 0.43125, not a return and a pause later
        for (int m = 0; m < 4; m++)
            Assert.InRange(crossings[m], (m + Drawing.HandOut) * turns - 1e-6, (m + Drawing.HandOut) * turns + 0.1 + 1e-6);
        Assert.Equal(1, finished);
        Assert.Equal(4, job.Work, 9);
        Assert.True(job.Done);
        Assert.Equal(4, Drawing.SectionsDone(job));
    }

    [Fact]
    public void One_big_step_gives_every_section_it_crosses()
    {
        var (job, sections, done) = Drawing.Advance(Drawing.Restore(1, 0.5), 2 * Math.PI * 10.3 * 3, 10.3);
        Assert.Equal(3, sections);
        Assert.False(done);
        Assert.Equal(3.5, job.Work, 9);
        // all four are out by 3.5, but the hollow is done only at 4, the dog back at the die
        Assert.Equal(4, Drawing.SectionsDone(job));
        (job, sections, done) = Drawing.Advance(job, 2 * Math.PI * 10.3 * 100, 10.3);
        Assert.Equal(0, sections);
        Assert.True(done);
        Assert.Equal(4, Drawing.SectionsDone(job));
        Assert.Equal((MachineJob.None, 0, false), Drawing.Advance(MachineJob.None, 100, 10.3));
        // what has come off, by W: none before the first hand-out, one from it to the second, all four from the fourth
        foreach (var (w, n) in new[] { (0.0, 0), (0.43, 0), (Drawing.HandOut, 1), (1.0, 1), (1.43125, 2), (3.4, 3), (3.43125, 4), (4.0, 4) })
            Assert.Equal(n, Drawing.SectionsDone(Drawing.Restore(2, w)));
        Assert.Equal(new MachineJob(2, 4, 4), Drawing.Restore(2, 99));
        Assert.Equal(MachineJob.None, Drawing.Restore(3, 1));
        Assert.Equal(new MachineJob(1, 0, 4), Drawing.Restore(1, double.NaN));
    }

    [Fact]
    public void It_runs_only_complete_with_a_hollow_at_speed()
    {
        Assert.True(Drawing.Running(true, true, 0.1f, 0.05f));
        Assert.True(Drawing.Running(true, true, -0.1f, 0.05f));
        Assert.False(Drawing.Running(true, true, 0.01f, 0.05f));
        Assert.False(Drawing.Running(false, true, 1, 0.05f));
        Assert.False(Drawing.Running(true, false, 1, 0.05f));
    }

    [Fact]
    public void A_die_draws_a_hundred_hollows_one_point_each()
    {
        var parts = Complete(left: Config.DieDurability);
        int hollows = 0;
        while (parts.Ready)
        {
            parts.Wearing.Wear(Drawing.DiePart, Config.DieWearPerHollow);
            hollows++;
        }
        Assert.Equal(100, hollows);
    }

    // ---- The renderer's clock ----

    [Fact]
    public void The_clock_follows_the_shaft_but_never_strays_from_the_server()
    {
        var clock = new DrawBenchClock();
        Assert.Equal((4.0, 0f, 0), (clock.Work, clock.Presence, clock.Class));
        // a lead hollow goes on: W from the server's 0, p eases in
        clock.Advance(0.2f, 0, 1, 0, false, 10.3);
        Assert.Equal((0.0, 0.5f, 1), (clock.Work, clock.Presence, clock.Class));
        Assert.True(clock.ShowsBillet("billetlead"));
        Assert.False(clock.ShowsBillet("billetcopper"));
        // running, it moves with the shaft between syncs
        clock.Advance(0.2f, 2 * Math.PI * 10.3 * 0.05, 1, 0, true, 10.3);
        Assert.Equal(0.05, clock.Work, 9);
        Assert.Equal(1f, clock.Presence);
        // never more than Snap ahead of the server: back to it
        clock.Advance(0.1f, 2 * Math.PI * 10.3 * 0.2, 1, 0.05, true, 10.3);
        Assert.Equal(0.05, clock.Work, 9);
        // never behind it
        clock.Advance(0.1f, 0, 1, 1.4, true, 10.3);
        Assert.Equal(1.4, clock.Work, 9);
        // stopped, it holds
        clock.Advance(0.1f, 100, 1, 1.4, false, 10.3);
        Assert.Equal(1.4, clock.Work, 9);
    }

    [Fact]
    public void The_clock_holds_W_at_the_end_and_k_while_p_eases_out_then_takes_a_new_hollow_from_0()
    {
        var clock = new DrawBenchClock();
        clock.Advance(1f, 0, 2, 3.9, false, 20.6);
        Assert.Equal(1f, clock.Presence);
        clock.Advance(0.2f, 0, 0, 0, false, 20.6);   // done and cleared
        Assert.Equal(4.0, clock.Work);
        Assert.Equal(2, clock.Class);
        Assert.Equal(0.5f, clock.Presence, 5);
        Assert.True(clock.ShowsBillet("billetcopper"));
        clock.Advance(0.3f, 0, 0, 0, false, 20.6);
        Assert.Equal(0f, clock.Presence);
        Assert.Equal(0, clock.Class);
        Assert.False(clock.ShowsBillet("billetcopper"));
        clock.Advance(0.1f, 0, 1, 0, false, 10.3);
        Assert.Equal((0.0, 1), (clock.Work, clock.Class));
        var input = clock.Input(1.5, 2.5, 0.3);
        Assert.Equal((1.5, 2.5, 0.0, 1, 0.25, 0.3), (input.Theta, input.Psi, input.Work, input.Class, input.Presence, input.Oil));
    }

    // ---- Settings ----

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new DrawBenchConfig();
        Assert.Equal((100, 1, 1.373f, 2.746f, 0.2f, 0.35f, 0.05f),
            (c.DieDurability, c.DieWearPerHollow, c.TurnsPerSectionLead, c.TurnsPerSectionCopper, c.ResistanceLead, c.ResistanceCopper, c.MinSpeed));
        Assert.Equal(c.TurnsPerSectionLead * 2.0, c.TurnsPerSectionCopper, 0.02);   // copper in the slow gear, twice the turns
        Assert.Empty(c.Sanitise());
        var bad = new DrawBenchConfig
        {
            DieDurability = 0, DieWearPerHollow = -1, TurnsPerSectionLead = float.NaN, TurnsPerSectionCopper = -3,
            ResistanceLead = 99, ResistanceCopper = float.PositiveInfinity, MinSpeed = -1,
        };
        Assert.Equal(7, bad.Sanitise().Count);
        Assert.Equal((100, 1, 1.373f, 2.746f, 0.2f, 0.35f, 0.05f),
            (bad.DieDurability, bad.DieWearPerHollow, bad.TurnsPerSectionLead, bad.TurnsPerSectionCopper, bad.ResistanceLead, bad.ResistanceCopper, bad.MinSpeed));
        Assert.Equal(0.35f, c.Resistance(2));
        Assert.Equal(0.2f, c.Resistance(1));
        Assert.Equal(0.2f, c.Resistance(0));
    }

    [Fact]
    public void Die_metals_keep_only_lead_and_copper_and_fill_a_missing_die()
    {
        var c = new DrawBenchConfig { DieMetals = new() { ["iron"] = ["lead", "copper", "tin"], ["bronze"] = ["lead"] } };
        var fixes = c.Sanitise();
        Assert.Equal(3, fixes.Count);
        Assert.Equal(["lead", "copper"], c.DieMetals["iron"]);
        Assert.Equal(["lead", "copper"], c.DieMetals["steel"]);
        Assert.False(c.DieMetals.ContainsKey("bronze"));
        // a server can let the iron die draw copper too
        Assert.Equal(["lead", "copper"], c.MetalsFor("iron"));
    }

    // ---- The rig's reader ----

    private static DrawBenchRig TestRig() =>
        DrawBenchRig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "drawbench-rig-test.json")));

    [Fact]
    public void The_test_rig_parses_with_the_contracts_anchors()
    {
        var rig = TestRig();
        Assert.Equal(4, rig.Cells.Count);
        Assert.All(rig.Cells, c => Assert.NotNull(c.Lid));
        Assert.Equal(new Int3(0, 0, 3), rig.PowerCell);
        Assert.Equal(Side.West, rig.PowerFace);
        Assert.Equal(Side.East, rig.OutputSide);
        Assert.Equal(3, rig.GhostCells.Count());
        // the sections come off by the die end's east face
        Assert.Equal(new Int3(1, 0, 0), rig.OutputNeighbour());
        Assert.Equal(1.15f, rig.OutputDrop().X, 4);
        Assert.Equal(rig.Output.Z, rig.OutputDrop().Z);
        Assert.Equal(4f, rig.Work.Ends[1]);
        Assert.Equal(rig.TurnsPerSection[1] * 2, rig.TurnsPerSection[2], 4);
        Assert.Equal(8, rig.MovingParts.Parts.Count);
    }

    [Fact]
    public void The_rig_is_refused_when_it_is_not_the_contracts()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "drawbench-rig-test.json"));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"billetlead\"", "\"ingot\"")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"sectionsPerHollow\": 4", "\"sectionsPerHollow\": 3")));
        // the hand-out is the gameplay's: a rig that hides its sections elsewhere in the cycle is refused
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"handOut\": 0.43125", "\"handOut\": 1.0")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"handOut\": 0.43125,", "")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("game:chutesection-copper", "game:ingot-copper")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("game:chutesection-lead", "game:chutesection-copper")));
        // only the new keys: the old ones in their place do not do
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"sectionsPerHollow\"", "\"sectionsPerIngot\"")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"hollows\"", "\"ingots\"")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"thin\": 4.0", "\"thin\": 3.0")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"turnsPerSection\"", "\"turnsPerTooth\"")));
    }

    // The player looks along the bench: it runs away from them, the die end nearest.
    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_bench_runs_away_from_the_player(Side look)
    {
        var side = DrawBenchRig.PlacedSide(look);
        var far = Footprint.ToWorld(new Int3(0, 0, 3), side);
        Assert.Equal(look.Normal() with { X = look.Normal().X * 3, Z = look.Normal().Z * 3 }, far);
    }
}
