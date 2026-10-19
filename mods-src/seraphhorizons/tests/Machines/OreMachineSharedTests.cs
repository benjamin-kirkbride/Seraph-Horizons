using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests.Machines;

/// <summary>The ore machines' shared code (#711, #694): the staged build and its recipes, wearing parts,
/// the running rules, the reservoir, tailings, anchors and the settings; and every shipped build file
/// against its rig.</summary>
public class OreMachineSharedTests
{
    // A fixture machine: a frame, then timber (bulk beams and nails), the works (a rod, then a plate,
    // drawn as two requires), and services (chutes and a water line in copper or lead); its shoes a
    // wearing part fitted with the works.
    private const string FixtureJson = """
        {
          "machine": "fixture",
          "stages": [
            { "id": "timber", "adds": ["timber"], "materials": [
              { "codes": ["game:supportbeam-*"], "quantity": 41 },
              { "codes": ["game:metalnailsandstrips-iron"], "quantity": 3 } ] },
            { "id": "works", "adds": ["rods", "plates"], "materials": [
              { "codes": ["game:rod-iron", "game:rod-steel"], "quantity": 2, "draws": ["rods"] },
              { "codes": ["game:metalplate-iron"], "quantity": 1 } ] },
            { "id": "services", "adds": ["chutes", "pipecopper", "pipelead"], "materials": [
              { "codes": ["game:chute-straight-*"], "quantity": 2 },
              { "codes": ["seraphhorizons:pipesection-copper", "seraphhorizons:pipesection-lead"], "quantity": 3,
                "picks": { "seraphhorizons:pipesection-copper": "pipecopper", "seraphhorizons:pipesection-lead": "pipelead" } } ] }
          ],
          "wearing": [ { "id": "shoes", "codes": ["game:poundercap-steel"], "count": 5, "uses": 10, "stage": "works" } ]
        }
        """;

    private static MachineBuild Fixture() => MachineBuildJson.Parse(FixtureJson);

    private static MachineAssembly Built()
    {
        var a = new MachineAssembly(Fixture());
        while (a.FillNext() && !a.Built) { }
        return a;
    }

    // ---- Codes ----

    [Theory]
    [InlineData("game:supportbeam-*", "game:supportbeam-oak", true)]
    [InlineData("game:supportbeam-*", "game:plank-oak", false)]
    [InlineData("game:leather-normal-*", "game:leather-normal-plain", true)]
    [InlineData("game:rod-iron", "game:rod-iron", true)]
    [InlineData("rod-iron", "game:rod-iron", true)]
    [InlineData("game:*-iron", "game:rod-iron", true)]
    [InlineData("game:*-iron", "game:rod-steel", false)]
    public void Code_patterns_match_as_the_game_reads_them(string pattern, string code, bool matches) =>
        Assert.Equal(matches, CodePattern.Matches(pattern, code));

    // ---- The staged build ----

    [Fact]
    public void Stages_go_in_order_a_material_a_click()
    {
        var b = Fixture().NewBuild();
        Assert.Equal(0, b.Next);
        Assert.Equal(StageFitVerdict.OutOfOrder, b.CanFit("game:rod-iron", 2).Verdict);
        Assert.Equal(StageFitVerdict.NotAMaterial, b.CanFit("game:stick", 5).Verdict);
        var fit = b.Fit("game:metalnailsandstrips-iron", 10);
        Assert.Equal((StageFitVerdict.Fits, 3), (fit.Verdict, fit.Count));
        Assert.Equal(StageFitVerdict.AlreadyFitted, b.CanFit("game:metalnailsandstrips-iron", 10).Verdict);
        Assert.False(b.Fitted("timber"));
        Assert.True(b.Fitted(null));
    }

    [Fact]
    public void Too_few_is_refused_but_a_full_stack_of_a_bulk_material_goes_in_a_stack_a_click()
    {
        var b = Fixture().NewBuild();
        Assert.Equal(StageFitVerdict.TooFew, b.CanFit("game:supportbeam-oak", 10, maxStack: 32).Verdict);
        var full = b.Fit("game:supportbeam-oak", 32, maxStack: 32);
        Assert.Equal((StageFitVerdict.Fits, 32), (full.Verdict, full.Count));
        Assert.Equal(9, b.Missing(0, 0));
        // the rest of a bulk material takes no more than it needs, of any wood
        Assert.Equal(9, b.Fit("game:supportbeam-pine", 20, maxStack: 32).Count);
        b.Fit("game:metalnailsandstrips-iron", 3);
        Assert.True(b.Fitted("timber"));
        Assert.Equal(1, b.Next);
        Assert.Contains(new StageDrop("game:supportbeam-oak", 32), b.Returns());
        Assert.Contains(new StageDrop("game:supportbeam-pine", 9), b.Returns());
    }

    [Fact]
    public void A_materials_draws_show_before_the_rest_of_its_stage()
    {
        var b = Fixture().NewBuild();
        b.FillNext();
        b.Fit("game:rod-steel", 2);
        Assert.True(b.Fitted("rods"));
        Assert.False(b.Fitted("plates"));
        b.Fit("game:metalplate-iron", 1);
        // the stage's last material is the shoes' first set
        Assert.False(b.Fitted("plates"));
        b.Fit("game:poundercap-steel", 5);
        Assert.True(b.Fitted("plates"));
    }

    [Fact]
    public void The_water_line_is_drawn_in_the_metal_fitted_and_is_all_one_metal()
    {
        var b = Fixture().NewBuild();
        b.FillNext();
        b.FillNext();
        b.Fit("game:chute-straight-ns", 2);
        Assert.Equal(StageFitVerdict.TooFew, b.CanFit("seraphhorizons:pipesection-lead", 2, maxStack: 64).Verdict);
        b.Fit("seraphhorizons:pipesection-lead", 3);
        Assert.True(b.Complete);
        Assert.True(b.Fitted("pipelead"));
        Assert.False(b.Fitted("pipecopper"));
        Assert.True(b.Fitted("chutes"));
        // a requires no stage adds is the machine's own, not the build's
        Assert.True(b.Fitted("concentrate"));
    }

    [Fact]
    public void A_one_metal_material_refuses_the_other_metal_part_way()
    {
        var b = Fixture().NewBuild();
        b.FillNext();
        b.FillNext();
        b.Fit("game:chute-straight-ns", 2);
        Assert.Equal(2, b.Fit("seraphhorizons:pipesection-copper", 2, maxStack: 2).Count);
        Assert.Equal(StageFitVerdict.OtherKind, b.CanFit("seraphhorizons:pipesection-lead", 64).Verdict);
        Assert.Equal(StageFitVerdict.Fits, b.CanFit("seraphhorizons:pipesection-copper", 1).Verdict);
    }

    [Fact]
    public void A_snapshot_restores_and_drops_what_no_longer_fits()
    {
        var b = Fixture().NewBuild();
        b.Fit("game:supportbeam-oak", 41);
        b.Fit("game:metalnailsandstrips-iron", 3);
        b.Fit("game:rod-iron", 2);
        var snap = new Dictionary<string, int>(b.Snapshot())
        {
            ["0/0/game:plank-oak"] = 4,     // not its material
            ["2/0/game:chute-straight-ns"] = 2, // after a stage not complete
        };
        var r = StagedBuild.Restore(Fixture().Stages, snap, out var dropped);
        Assert.Equal(b.Snapshot(), r.Snapshot());
        Assert.Equal(1, r.Next);
        Assert.Contains(new StageDrop("game:plank-oak", 4), dropped);
        Assert.Contains(new StageDrop("game:chute-straight-ns", 2), dropped);
    }

    // ---- Wearing parts and the assembly ----

    [Fact]
    public void A_wearing_part_fitted_with_a_stage_gets_its_first_set_with_it()
    {
        var a = new MachineAssembly(Fixture());
        a.FillNext();
        a.Fit("game:rod-iron", 2);
        a.Fit("game:metalplate-iron", 1);
        Assert.False(a.Build.StageComplete(1));
        Assert.Equal(AssemblyVerdict.TooFew, a.Fit("game:poundercap-steel", 4).Verdict);
        var click = a.Fit("game:poundercap-steel", 8);
        Assert.Equal((AssemblyVerdict.Fitted, 5), (click.Verdict, click.Take));
        Assert.True(a.Build.StageComplete(1));
        Assert.Equal(1.0, a.Wearing.ShareLeft("shoes"));
    }

    [Fact]
    public void Worn_parts_stop_the_machine_and_are_renewed_by_right_click()
    {
        var a = Built();
        Assert.True(a.Ready);
        Assert.Equal(AssemblyVerdict.NotWorn, a.Fit("game:poundercap-steel", 5).Verdict);
        Assert.False(a.Wearing.Wear("shoes", 4));
        Assert.Equal(0.6, a.Wearing.ShareLeft("shoes"), 6);
        Assert.True(a.Wearing.Wear("shoes", 6));
        Assert.False(a.Ready);
        Assert.Equal(AssemblyVerdict.TooFew, a.Fit("game:poundercap-steel", 3).Verdict);
        var renew = a.Fit("game:poundercap-steel", 5);
        Assert.Equal((AssemblyVerdict.Renewed, 5, "shoes"), (renew.Verdict, renew.Take, renew.Wearing));
        Assert.True(a.Ready);
    }

    [Fact]
    public void Breaking_returns_everything_once_and_a_worn_out_set_not_at_all()
    {
        var a = Built();
        var drops = a.Returns().ToDictionary(d => d.Code, d => d.Count);
        Assert.Equal(5, drops["game:poundercap-steel"]);
        Assert.Equal(41, drops["game:supportbeam-oak"]);
        a.Wearing.Wear("shoes", 100);
        Assert.DoesNotContain(a.Returns(), d => d.Code == "game:poundercap-steel");
        a.Fit("game:poundercap-steel", 5);
        Assert.Equal(5, a.Returns().Single(d => d.Code == "game:poundercap-steel").Count);
    }

    [Fact]
    public void A_wearing_part_before_its_stage_is_out_of_order()
    {
        var a = new MachineAssembly(Fixture());
        Assert.Equal(AssemblyVerdict.OutOfOrder, a.Fit("game:poundercap-steel", 5).Verdict);
        Assert.Equal("seraphhorizons:machine-error-outoforder", a.Fit("game:poundercap-steel", 5).ErrorLangCode);
    }

    [Fact]
    public void The_assembly_restores_from_its_snapshot()
    {
        var a = Built();
        a.Wearing.Wear("shoes", 3);
        var (build, wearing) = a.Snapshot();
        var r = MachineAssembly.Restore(Fixture(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.True(r.Ready);
        Assert.Equal(0.7, r.Wearing.ShareLeft("shoes"), 6);
        Assert.True(r.Shows("pipecopper") ^ r.Shows("pipelead"));
    }

    // ---- Build files ----

    [Theory]
    [InlineData("""{ "stages": [] }""", "machine")]
    [InlineData("""{ "machine": "m", "stages": [] }""", "stages")]
    [InlineData("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [] } ] }""", "no materials")]
    [InlineData("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 0 } ] } ] }""", "quantity")]
    [InlineData("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1, "picks": { "game:x": "b" } } ] } ] }""", "pick")]
    [InlineData("""{ "machine": "m", "feed": "sideways", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1 } ] } ] }""", "feed")]
    [InlineData("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1 } ] } ], "wearing": [ { "id": "w", "codes": ["game:y"], "count": 1, "uses": 1, "stage": "b" } ] }""", "not a stage")]
    public void A_broken_build_file_says_what_is_wrong(string json, string says)
    {
        var e = Assert.Throws<FormatException>(() => MachineBuildJson.Parse(json));
        Assert.Contains(says, e.Message);
    }

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    public static TheoryData<string> BuildFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Out("builds"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
            data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(BuildFiles))]
    public void Every_shipped_build_file_is_its_rigs_stages(string machine)
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out($"builds/{machine}.json")));
        Assert.Equal(machine, build.Machine);
        string rig = Out($"rigs/{machine}-rig.json");
        Assert.True(File.Exists(rig), $"{machine} has no rig");
        using var doc = RigJson.Parse(File.ReadAllText(rig));
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, doc.RootElement));
        // the whole build can be fitted and broken back to what went in
        var a = new MachineAssembly(build);
        while (a.FillNext() && !a.Built) { }
        Assert.True(a.Ready || build.Wearing.Any(w => !build.Stages.Any(s => s.Materials.Any(m => m.Wearing == w.Id))));
        foreach (var stage in build.Stages)
            foreach (var add in stage.Adds)
                Assert.True(stage.Materials.Any(m => m.Picks?.Values.Contains(add) == true) || a.Shows(add), $"{machine}: {add} not drawn when built");
    }

    [Fact]
    public void A_build_is_checked_against_its_rigs_stages_and_parts()
    {
        var build = Fixture();
        using var good = JsonDocument.Parse("""
            { "buildStages": [ { "adds": ["timber"] }, { "id": "works", "adds": ["plates", "rods"] }, { "adds": ["chutes", "pipecopper", "pipelead"] } ],
              "parts": [ { "requires": "timber" }, { "requires": "rods" }, { "requires": "plates" }, { "requires": "chutes" },
                         { "requires": "pipecopper" }, { "requires": "pipelead" } ] }
            """);
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, good.RootElement));
        using var bad = JsonDocument.Parse("""
            { "buildStages": [ { "adds": ["timber"] }, { "id": "drive", "adds": ["rods"] } ], "parts": [ { "requires": "timber" } ] }
            """);
        var problems = MachineBuildJson.CheckAgainstRig(build, bad.RootElement);
        Assert.Contains(problems, p => p.Contains("2 stages"));
        Assert.Contains(problems, p => p.Contains("drive"));
        Assert.Contains(problems, p => p.Contains("which no part"));
        // a hand station's rig has no buildStages: held to its parts only
        using var hand = JsonDocument.Parse("""{ "parts": [ { "requires": "timber" } ] }""");
        Assert.All(MachineBuildJson.CheckAgainstRig(build, hand.RootElement), p => Assert.Contains("which no part", p));
    }

    [Fact]
    public void The_stamp_battery_is_the_first_build_file_its_shoes_fitted_with_its_stamps()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/stampbattery.json")));
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["timber", "mortar", "stamps", "camshaft", "services"], build.Stages.Select(s => s.Id));
        var stamps = build.Stages[2];
        Assert.Contains(stamps.Materials, m => m.Wearing == "shoes" && m.Quantity == 5);
        Assert.Equal(41, build.Stages[0].Materials[0].Quantity);
    }

    // ---- Running, load, rate ----

    [Theory]
    [InlineData(false, true, true, 1f, true, true, true, MachineState.NotBuilt)]
    [InlineData(true, false, true, 1f, true, true, true, MachineState.Worn)]
    [InlineData(true, true, true, 0.049f, true, true, true, MachineState.NoPower)]
    [InlineData(true, true, true, -0.6f, true, true, true, MachineState.Working)]
    [InlineData(true, true, true, 0.6f, false, true, true, MachineState.Dry)]
    [InlineData(true, true, true, 0.6f, true, true, false, MachineState.Blocked)]
    [InlineData(true, true, true, 0.6f, true, false, true, MachineState.Idle)]
    [InlineData(true, true, false, 0f, true, true, true, MachineState.Working)]
    public void A_machine_runs_only_built_fitted_turning_wet_and_with_room(bool built, bool parts, bool powered, float speed,
        bool wet, bool work, bool room, MachineState expected) =>
        Assert.Equal(expected, MachineRun.StateOf(new MachineCheck(built, parts, powered, speed, wet, work, room), 0.05f));

    [Fact]
    public void Work_is_counted_in_turns_so_an_item_takes_the_same_turns_at_any_speed()
    {
        // 57.3 turns an hour at speed 0.6: an item of 3 turns, worked at two speeds over the same turns
        double perItem = 3;
        foreach (float speed in new[] { 0.1f, 0.6f, 2.5f })
        {
            var turns = WorkTurns.None;
            int items = 0;
            double dt = 0.2;
            for (int i = 0; i < 10000 && items < 4; i++)
            {
                (turns, int n) = turns.Add(speed * MachineRun.RadiansPerSpeedSecond * dt, perItem);
                items += n;
            }
            Assert.Equal(4, items);
        }
        var (rest, done) = new WorkTurns(2.5).Add(2 * Math.PI * 4, 3);
        Assert.Equal(2, done);
        Assert.Equal(0.5, rest.Turns, 9);
        Assert.Equal(0, WorkTurns.None.Add(-1, 3).Items);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.4199, 41)]
    [InlineData(0.42, 42)]
    [InlineData(0.999, 99)]
    [InlineData(1.0, 100)]
    [InlineData(1.7, 100)]
    [InlineData(-0.2, 0)]
    public void Working_shows_the_whole_percent_done_floored(double progress, int percent) =>
        Assert.Equal(percent, MachineRun.Percent(progress));

    [Fact]
    public void A_machine_with_no_progress_shows_no_percent()
    {
        Assert.Null(MachineRun.Percent(null));
        Assert.Null(MachineRun.Percent(double.NaN));
        Assert.Null(MachineRun.Percent(double.PositiveInfinity));
    }

    [Fact]
    public void Progress_syncs_when_its_percent_changes_at_most_once_a_second()
    {
        var sync = ProgressSync.None;
        Assert.True(sync.Due(0, 10));               // starting work sends at once
        sync = sync.Sent(0, 10);
        Assert.False(sync.Due(0, 20));              // the same percent never resends
        Assert.False(sync.Due(3, 10.5));            // a change within the second waits
        Assert.True(sync.Due(3, 11));
        sync = sync.Sent(3, 11);
        Assert.True(sync.Due(null, 11.1));          // stopping sends at once
        sync = sync.Sent(null, 11.1);
        Assert.False(sync.Due(null, 30));
        Assert.True(sync.Due(4, 11.2));             // and so does starting again
    }

    [Fact]
    public void Load_is_the_jobs_once_built_a_token_before_and_three_times_dry()
    {
        Assert.Equal(MachineLoad.Unassembled, MachineLoad.Resistance(false, 0.4));
        Assert.Equal(0.4f, MachineLoad.Resistance(true, 0.4));
        Assert.Equal(1.2f, MachineLoad.Resistance(true, 0.4, oilDry: true), 5);
        Assert.Equal(0.24, MachineLoad.Power(0.4, -0.6), 9);
    }

    // ---- The reservoir ----

    [Fact]
    public void A_dry_reservoir_stops_the_machine_and_water_returning_restarts_it()
    {
        var config = new OreMachinesConfig();
        double cap = MachineReservoir.Capacity(config, 10);
        Assert.Equal(20, cap);
        var r = MachineReservoir.Empty;
        Assert.False(r.Holds(10));
        Assert.Equal(10, r.Wanted(cap, config.WaterIntakeLitresPerSecond, 1));
        r = r.Fill(10, cap);
        var (after, spent) = r.Spend(10);
        Assert.True(spent);
        Assert.Equal(0, after.Litres);
        Assert.False(after.Spend(10).Spent);
        Assert.Equal(20, after.Fill(100, cap).Litres);
        Assert.True(MachineReservoir.Empty.Holds(0));
    }

    [Fact]
    public void A_reservoir_holds_two_items_where_an_item_takes_more_than_half_of_it()
    {
        // the stamp battery's 40 L a rock
        Assert.Equal(80, MachineReservoir.Capacity(new OreMachinesConfig(), 40));
        Assert.Equal(15, MachineReservoir.Restore(15, 20).Litres);
        Assert.Equal(20, MachineReservoir.Restore(99, 20).Litres);
        Assert.Equal(0, MachineReservoir.Restore(double.NaN, 20).Litres);
    }

    // ---- Tailings ----

    [Theory]
    [InlineData(TailingsForm.Gravel, "granite", 3, "game:gravel-granite-land-3")]
    [InlineData(TailingsForm.Gravel, "granite", 8, "game:gravel-granite")]
    [InlineData(TailingsForm.Sand, "basalt", 7, "game:sand-basalt-7")]
    [InlineData(TailingsForm.Sand, "basalt", 8, "game:sand-basalt")]
    public void Tailings_are_vanillas_layered_gravel_and_sand(TailingsForm form, string rock, int layers, string code)
    {
        Assert.Equal(code, TailingsBlocks.Code(form, rock, layers));
        Assert.Equal((form, rock, layers), TailingsBlocks.Parse(code));
    }

    [Theory]
    [InlineData("game:gravel-granite-water-2", TailingsForm.Gravel, 2)]
    [InlineData("game:sand-chalk", TailingsForm.Sand, 8)]
    public void Any_habitat_of_layered_gravel_is_read(string code, TailingsForm form, int layers) =>
        Assert.Equal((form, layers), (TailingsBlocks.Parse(code)!.Value.Form, TailingsBlocks.Parse(code)!.Value.Layers));

    [Theory]
    [InlineData("game:soil-low-none")]
    [InlineData("game:gravel-granite-land-9")]
    [InlineData("seraphhorizons:sand-x")]
    [InlineData(null)]
    public void Other_blocks_are_not_tailings(string? code) => Assert.Null(TailingsBlocks.Parse(code));

    [Fact]
    public void Fractions_of_a_layer_carry_from_rock_to_rock()
    {
        var rule = new TailingsRule(TailingsForm.Sand, 0.75);
        var owed = TailingsOwed.None;
        int total = 0;
        for (int i = 0; i < 4; i++)
        {
            (int whole, owed) = owed.Add(1, rule);
            total += whole;
        }
        Assert.Equal(3, total);
        Assert.Equal(0, owed.Layers, 9);
        Assert.Equal(0, TailingsOwed.None.Add(1, new TailingsRule(TailingsForm.Gravel, 0)).Whole);
    }

    [Fact]
    public void The_heap_fills_its_cell_then_round_it_then_up()
    {
        var cells = Enumerable.Repeat(new TailingsCell(TailingsCellKind.Open), TailingsHeap.Offsets.Count).ToArray();
        var (writes, unplaced) = TailingsHeap.Place(i => cells[i], 11);
        Assert.Equal([(0, 8), (1, 3)], writes);
        Assert.Equal(0, unplaced);
        // layers on its own layers, and the cell over a full one is supported by it
        cells[0] = new TailingsCell(TailingsCellKind.Blocked, 8);
        for (int i = 1; i < 5; i++)
            cells[i] = new TailingsCell(TailingsCellKind.Blocked, 8);
        cells[5] = new TailingsCell(TailingsCellKind.Open, 0, Supported: false);
        cells[6] = new TailingsCell(TailingsCellKind.Open, 0, Supported: false);
        (writes, unplaced) = TailingsHeap.Place(i => cells[i], 20);
        Assert.Equal([(5, 8), (6, 8)], writes);
        Assert.Equal(4, unplaced);
    }

    [Fact]
    public void The_heap_skips_cells_it_cannot_use()
    {
        var cells = new[]
        {
            new TailingsCell(TailingsCellKind.Layered, 6), new TailingsCell(TailingsCellKind.Blocked),
            new TailingsCell(TailingsCellKind.Open, 0, Supported: false), new TailingsCell(TailingsCellKind.Open),
            new TailingsCell(TailingsCellKind.Open), new TailingsCell(TailingsCellKind.Open), new TailingsCell(TailingsCellKind.Open),
        };
        var (writes, _) = TailingsHeap.Place(i => cells[i], 5);
        Assert.Equal([(0, 8), (3, 3)], writes);
    }

    [Fact]
    public void Tailings_settings_are_the_owners_starting_values()
    {
        var c = new OreMachinesConfig();
        Assert.Equal(new TailingsRule(TailingsForm.Gravel, 1), c.TailingsFor("sluice"));
        Assert.Equal(new TailingsRule(TailingsForm.Gravel, 1), c.TailingsFor("jig"));
        Assert.Equal(new TailingsRule(TailingsForm.Sand, 0.75), c.TailingsFor("shakingtable"));
        Assert.Equal(new TailingsRule(TailingsForm.Sand, 0.5), c.TailingsFor("vanner"));
        Assert.Equal(new TailingsRule(TailingsForm.Sand, 1), c.TailingsFor("amalgamationplates"));
        Assert.Equal(new TailingsRule(TailingsForm.Gravel, 1), c.TailingsFor("rocker"));
        foreach (var none in new[] { "jawbreaker", "stampbattery", "arrastra", "ballmill", "stallroaster" })
            Assert.Null(c.TailingsFor(none));
        Assert.Equal(0.05f, c.MinSpeed);
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new OreMachinesConfig { MinSpeed = -1, ReservoirLitres = double.NaN };
        c.Tailings["sluice"].Form = "mud";
        c.Tailings["jig"].LayersPerRock = -2;
        Assert.Equal(4, c.Sanitise().Count);
        Assert.Equal(0.05f, c.MinSpeed);
        Assert.Equal(20, c.ReservoirLitres);
        Assert.Equal(TailingsForm.Gravel, c.TailingsFor("sluice")!.Form);
        Assert.Equal(1, c.TailingsFor("jig")!.LayersPerRock);
    }

    // ---- Anchors ----

    [Fact]
    public void Anchors_are_read_from_the_rigs_cell_and_face_pairs_and_turned_with_the_machine()
    {
        var anchors = MachineAnchors.Read(File.ReadAllText(Out("rigs/sluice-rig.json")));
        Assert.Equal(new MachineAnchor("infeed", new Int3(0, 0, 0), CellFace.Up), anchors["infeed"]);
        Assert.Equal(new MachineAnchor("tailings", new Int3(0, 0, 3), CellFace.South), anchors["tailings"]);
        Assert.Equal(CellFace.Down, anchors["concentrate"].Face);
        Assert.Contains("water", anchors.Keys);
        // native south is the identity; north turns half round, up stays up
        Assert.Equal((new Int3(0, 0, 3), CellFace.South), anchors["tailings"].ToWorld(Side.South));
        Assert.Equal((new Int3(0, 0, -3), CellFace.North), anchors["tailings"].ToWorld(Side.North));
        Assert.Equal(new Int3(0, 0, -4), anchors["tailings"].Beyond(Side.North));
        Assert.Equal(CellFace.Up, anchors["infeed"].ToWorld(Side.East).Face);
    }

    public static TheoryData<string> OreRigs() =>
    [
        "amalgamationplates", "ballmill", "jawbreaker", "jig", "shakingtable", "sluice", "stampbattery", "vanner",
    ];

    [Theory]
    [MemberData(nameof(OreRigs))]
    public void Every_continuous_ore_machines_anchors_parse(string machine)
    {
        var anchors = MachineAnchors.Read(File.ReadAllText(Out($"rigs/{machine}-rig.json")));
        Assert.True(anchors.ContainsKey("infeed") || anchors.ContainsKey("feed"), string.Join(", ", anchors.Keys));
    }

    [Fact]
    public void A_malformed_anchor_says_so()
    {
        using var doc = JsonDocument.Parse("""{ "infeedCell": [0, 0], "infeedFace": "up" }""");
        Assert.Throws<FormatException>(() => MachineAnchors.Read(doc.RootElement));
        using var doc2 = JsonDocument.Parse("""{ "infeedCell": [0, 0, 0], "infeedFace": "sideways" }""");
        Assert.Throws<FormatException>(() => MachineAnchors.Read(doc2.RootElement));
    }
}
