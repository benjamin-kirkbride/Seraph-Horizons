using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.DrawBench;

/// <summary>DrawBench/Core: the build order, what each stage takes, the die's metal and take-back;
/// the draw's arithmetic (W with the axle, a section at each whole number, the die's wear); the
/// renderer's clock; the settings; and the rig's reader on a hand-trimmed test rig.</summary>
public class DrawBenchGameplayTests
{
    private static readonly string[] Order =
    [
        DrawBenchParts.GearboxCode, "game:metalchain-iron", "game:bracket-heavy-steel", "game:rod-meteoriciron",
    ];

    /// <summary>Every stage up to the mandrel fitted.</summary>
    private static DrawBenchParts UpToMandrel()
    {
        var parts = new DrawBenchParts();
        foreach (var code in Order)
            Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(code));
        return parts;
    }

    private static DrawBenchParts Complete(string die = DrawBenchParts.DieSteelCode, int left = 100)
    {
        var parts = UpToMandrel();
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(die, left, 100));
        return parts;
    }

    private static readonly DrawBenchConfig Config = new();

    // ---- Parts ----

    [Fact]
    public void The_stages_go_in_in_the_contracts_order()
    {
        Assert.Equal(["gearbox", "chain", "dog", "mandrel", "die"], DrawBenchRequires.Stages.Select(DrawBenchRequires.Name));
        var parts = new DrawBenchParts();
        foreach (var (code, stage) in Order.Zip(DrawBenchRequires.Stages))
        {
            Assert.Equal(stage, parts.Next);
            Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(code));
        }
        Assert.Equal(DrawBenchStage.Die, parts.Next);
        Assert.False(parts.Complete);
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(DrawBenchParts.DieIronCode, 100, 100));
        Assert.True(parts.Complete);
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new DrawBenchParts();
        Assert.Equal(DrawBenchFitVerdict.OutOfOrder, parts.Fit("game:metalchain-steel"));
        Assert.Equal(DrawBenchFitVerdict.OutOfOrder, parts.Fit("game:rod-iron"));
        Assert.Equal(DrawBenchFitVerdict.OutOfOrder, parts.Fit(DrawBenchParts.DieSteelCode, 100, 100));
        Assert.Equal(DrawBenchFitVerdict.NotAPart, parts.Fit("game:jonasframes-gearbox02"));
        Assert.Equal(DrawBenchFitVerdict.NotAPart, parts.Fit("game:metalchain-copper"));
        Assert.Equal(DrawBenchFitVerdict.NotAPart, parts.Fit("game:rod-copper"));
        Assert.Equal(DrawBenchFitVerdict.NotAPart, parts.Fit(null));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("jonasframes-gearbox01"));   // a code with no domain is the game's
        Assert.Equal(DrawBenchFitVerdict.AlreadyFitted, parts.Fit(DrawBenchParts.GearboxCode));
        Assert.Equal(DrawBenchStage.Chain, parts.Next);
    }

    [Fact]
    public void The_chain_and_the_mandrel_take_two_from_one_stack()
    {
        Assert.Equal([1, 2, 1, 2, 1], DrawBenchRequires.Stages.Select(DrawBenchParts.Needed));
        var parts = new DrawBenchParts();
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(DrawBenchParts.GearboxCode, held: 1));
        Assert.Equal(DrawBenchFitVerdict.TooFew, parts.Fit("game:metalchain-iron", held: 1));
        Assert.False(parts.Has(DrawBenchStage.Chain));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:metalchain-iron", held: 2));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:bracket-heavy-iron", held: 1));
        Assert.Equal(DrawBenchFitVerdict.TooFew, parts.Fit("game:rod-iron", held: 1));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:rod-iron", held: 5));
        // a part already in, or out of order, says so before counting
        Assert.Equal(DrawBenchFitVerdict.AlreadyFitted, parts.Fit("game:metalchain-iron", held: 1));
        Assert.Equal([1, 2, 1, 2], parts.Returns().Select(d => d.Count));
    }

    [Theory]
    [InlineData("iron")]
    [InlineData("meteoriciron")]
    [InlineData("steel")]
    public void Each_ferrous_chain_bracket_and_rod_fits(string metal)
    {
        var parts = new DrawBenchParts();
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(DrawBenchParts.GearboxCode));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:metalchain-" + metal));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:bracket-heavy-" + metal));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit("game:rod-" + metal));
        Assert.Equal(DrawBenchStage.Die, parts.Next);
    }

    [Fact]
    public void The_die_carries_its_metal_and_durability()
    {
        var iron = Complete(DrawBenchParts.DieIronCode, 37);
        Assert.Equal("iron", iron.DieMetal);
        Assert.Equal((37, 100), (iron.DieLeft, iron.DieCapacity));
        Assert.Equal("steel", Complete().DieMetal);
        Assert.Null(UpToMandrel().DieMetal);
        Assert.Equal(DrawBenchFitVerdict.DieSpent, UpToMandrel().Fit(DrawBenchParts.DieSteelCode, 0, 100));
    }

    [Fact]
    public void Fitted_draws_each_stage_once_it_is_in_and_never_the_billets()
    {
        var parts = UpToMandrel();
        Assert.True(parts.Fitted(null));
        Assert.True(parts.Fitted("mandrel"));
        Assert.False(parts.Fitted("die"));
        Assert.False(parts.Fitted("billetlead"));
        Assert.False(new DrawBenchParts().Fitted("gearbox"));
        Assert.True(Complete().Fitted("die"));
        Assert.False(Complete().Fitted("billetcopper"));
        Assert.True(DrawBenchRequires.KnownRequires.SetEquals(["gearbox", "chain", "dog", "mandrel", "die", "billetlead", "billetcopper"]));
    }

    [Fact]
    public void The_die_comes_back_only_with_no_hollow_on_the_bench_with_its_durability()
    {
        var parts = Complete(DrawBenchParts.DieIronCode, 64);
        Assert.False(parts.CanTakeDie(jobOn: true));
        Assert.True(parts.CanTakeDie(jobOn: false));
        Assert.Equal((DrawBenchParts.DieIronCode, 64, 100), parts.RemoveDie());
        Assert.False(parts.Complete);
        Assert.Equal(DrawBenchStage.Die, parts.Next);
        Assert.False(parts.CanTakeDie(false));
        Assert.Null(parts.RemoveDie());
        // the other stages stay, and a die goes back in
        Assert.True(parts.Has(DrawBenchStage.Mandrel));
        Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(DrawBenchParts.DieIronCode, 64, 100));
    }

    [Fact]
    public void A_spent_die_is_gone_and_the_bench_needs_another()
    {
        var parts = Complete(left: 2);
        Assert.False(parts.WearDie(1));
        Assert.Equal(1, parts.DieLeft);
        Assert.True(parts.WearDie(1));
        Assert.False(parts.Complete);
        Assert.Null(parts.DieMetal);
        Assert.Equal(0, parts.DieLeft);
        Assert.Equal(DrawBenchParts.DieSteelCode, parts.NextPart);
        Assert.DoesNotContain(parts.Returns(), d => d.Durability != null);
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_the_die_with_its_durability()
    {
        var parts = Complete(left: 58);
        var drops = parts.Returns();
        Assert.Equal(5, drops.Count);
        Assert.Equal([.. Order, DrawBenchParts.DieSteelCode], drops.Select(d => d.Code));
        Assert.Equal(new DrawBenchDrop(DrawBenchParts.DieSteelCode, 58), drops[^1]);
        Assert.All(drops.Take(4), d => Assert.Null(d.Durability));
        Assert.Equal([1, 2, 1, 2, 1], drops.Select(d => d.Count));
        Assert.Empty(new DrawBenchParts().Returns());
    }

    [Fact]
    public void The_creative_shortcut_fits_each_stage_in_turn_with_a_steel_die()
    {
        var parts = new DrawBenchParts();
        var codes = new List<string>();
        while (parts.NextPart is { } code)
        {
            codes.Add(code);
            Assert.Equal(DrawBenchFitVerdict.Fits, parts.Fit(code, 100, 100));
        }
        Assert.Equal([DrawBenchParts.GearboxCode, "game:metalchain-iron", "game:bracket-heavy-iron", "game:rod-iron", DrawBenchParts.DieSteelCode], codes);
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_drops_what_could_not_have_been_fitted()
    {
        var parts = Complete(DrawBenchParts.DieIronCode, 12);
        var restored = DrawBenchParts.Restore(parts.Snapshot(), parts.DieLeft, parts.DieCapacity);
        Assert.Equal(parts.Returns(), restored.Returns());
        Assert.Equal("iron", restored.DieMetal);

        var gap = new Dictionary<string, string> { ["gearbox"] = DrawBenchParts.GearboxCode, ["dog"] = "game:bracket-heavy-iron" };
        Assert.Equal([new DrawBenchDrop(DrawBenchParts.GearboxCode)], DrawBenchParts.Restore(gap, 0, 0).Returns());
        var wrong = parts.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);
        wrong["chain"] = "game:metalchain-gold";
        Assert.Equal(DrawBenchStage.Chain, DrawBenchParts.Restore(wrong, 12, 100).Next);
        Assert.Equal(DrawBenchStage.Die, DrawBenchParts.Restore(parts.Snapshot(), 0, 100).Next);
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
    [InlineData(1, 7.72)]
    [InlineData(2, 15.45)]
    public void W_advances_with_the_axle_and_a_section_comes_off_at_each_whole_number(int k, double turns)
    {
        Assert.Equal(turns, Config.TurnsPerSection(k), 5);
        var job = new DrawJob(k, 0);
        var crossings = new List<double>();
        int finished = 0;
        // a tenth of a turn at a time, past the end to show it holds
        double turned = 0;
        for (int i = 0; i < (int)(turns * 4 * 10) + 20; i++)
        {
            (job, int sections, bool done) = job.Advance(2 * Math.PI * 0.1, turns);
            turned += 0.1;
            for (int p = 0; p < sections; p++)
                crossings.Add(turned);
            if (done)
                finished++;
        }
        Assert.Equal(4, crossings.Count);
        for (int m = 1; m <= 4; m++)
            Assert.InRange(crossings[m - 1], m * turns - 1e-6, m * turns + 0.1 + 1e-6);
        Assert.Equal(1, finished);
        Assert.Equal(4, job.Work, 9);
        Assert.True(job.Done);
        Assert.Equal(4, job.SectionsDone);
    }

    [Fact]
    public void One_big_step_gives_every_section_it_crosses()
    {
        var (job, sections, done) = new DrawJob(1, 0.5).Advance(2 * Math.PI * 10.3 * 3, 10.3);
        Assert.Equal(3, sections);
        Assert.False(done);
        Assert.Equal(3.5, job.Work, 9);
        (job, sections, done) = job.Advance(2 * Math.PI * 10.3 * 100, 10.3);
        Assert.Equal(1, sections);
        Assert.True(done);
        Assert.Equal((DrawJob.None, 0, false), DrawJob.None.Advance(100, 10.3));
        Assert.Equal(new DrawJob(2, 4), DrawJob.Restore(2, 99));
        Assert.Equal(DrawJob.None, DrawJob.Restore(3, 1));
        Assert.Equal(new DrawJob(1, 0), DrawJob.Restore(1, double.NaN));
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
        while (parts.Complete)
        {
            parts.WearDie(Config.DieWearPerHollow);
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
        Assert.Equal((100, 1, 7.72f, 15.45f, 0.2f, 0.35f, 0.05f),
            (c.DieDurability, c.DieWearPerHollow, c.TurnsPerSectionLead, c.TurnsPerSectionCopper, c.ResistanceLead, c.ResistanceCopper, c.MinSpeed));
        Assert.Equal(c.TurnsPerSectionLead * 2.0, c.TurnsPerSectionCopper, 0.02);   // copper in the slow gear, twice the turns
        Assert.Empty(c.Sanitise());
        var bad = new DrawBenchConfig
        {
            DieDurability = 0, DieWearPerHollow = -1, TurnsPerSectionLead = float.NaN, TurnsPerSectionCopper = -3,
            ResistanceLead = 99, ResistanceCopper = float.PositiveInfinity, MinSpeed = -1,
        };
        Assert.Equal(7, bad.Sanitise().Count);
        Assert.Equal((100, 1, 7.72f, 15.45f, 0.2f, 0.35f, 0.05f),
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
        Assert.Equal(Side.North, rig.InfeedSide);
        Assert.Equal(Side.East, rig.OutputSide);
        Assert.Equal(3, rig.GhostCells.Count());
        // a chest in front of the die end feeds it
        Assert.Equal([new Int3(0, 0, -1)], rig.InfeedNeighbours());
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
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("game:chutesection-copper", "game:ingot-copper")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("game:chutesection-lead", "game:chutesection-copper")));
        // only the new keys: the old ones in their place do not do
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"sectionsPerHollow\"", "\"sectionsPerIngot\"")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"hollows\"", "\"ingots\"")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"thin\": 4.0", "\"thin\": 3.0")));
        Assert.Throws<FormatException>(() => DrawBenchRig.Parse(json.Replace("\"infeedSide\": \"north\"", "\"infeedSide\": \"east\"")));
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
