using SeraphHorizons.Mod.JawBreaker.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using Xunit;

namespace SeraphHorizons.Tests.JawBreaker;

/// <summary>
/// The jaw breaker's gameplay (#912) and the shared ore machine rig reader its game half uses: what it
/// takes, its load following the charge, its rate and draws from ore processing, its build file (the
/// README's stage table, the jaw faces fitted with the jaws), and every ore machine's rig read by
/// <see cref="OreMachineRig"/>.
/// </summary>
public class JawBreakerGameplayTests
{
    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static OreRecovery Recovery() => new(System.Text.Json.JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Out("ore-processing.json")),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    [Theory]
    [InlineData("ore", "rich", true)]
    [InlineData("ore", "poor", true)]
    [InlineData("crystalizedore", "medium", true)]
    [InlineData("ore", null, false)]
    [InlineData("crushed", "rich", false)]
    [InlineData("nugget", "rich", false)]
    public void It_takes_raw_ore_of_a_grade_as_spalling_does(string first, string? grade, bool takes) =>
        Assert.Equal(takes, JawBreaking.Takes(first, grade));

    [Fact]
    public void Its_load_follows_the_charge_in_its_mouth()
    {
        var recovery = Recovery();
        var spec = recovery.Machine(OreMachine.JawBreaker);
        Assert.True(spec.LoadFollowsJob);
        Assert.Equal(0.8, spec.Load, 6);
        Assert.Equal(0.16, spec.EmptyLoad, 6);
        Assert.Equal(0, spec.WaterPerItem);
        Assert.Equal(24, spec.ItemsPerHour, 6);
        Assert.Equal(0.16, recovery.LoadAt(OreMachine.JawBreaker, JawBreaking.Fill(0, 8)), 6);
        Assert.Equal(0.48, recovery.LoadAt(OreMachine.JawBreaker, JawBreaking.Fill(4, 8)), 6);
        Assert.Equal(0.8, recovery.LoadAt(OreMachine.JawBreaker, JawBreaking.Fill(8, 8)), 6);
        Assert.Equal(0.8, recovery.LoadAt(OreMachine.JawBreaker, JawBreaking.Fill(20, 8)), 6);
        // the shared rule: 0.005 unbuilt, the job's load once built
        Assert.Equal(MachineLoad.Unassembled, MachineLoad.Resistance(false, 0.8));
        Assert.Equal(0.48f, MachineLoad.Resistance(true, recovery.LoadAt(OreMachine.JawBreaker, 0.5)), 4);
    }

    [Fact]
    public void A_rock_takes_the_same_turns_at_any_speed()
    {
        var recovery = Recovery();
        double perRock = recovery.Machine(OreMachine.JawBreaker).TurnsPerItem;
        Assert.True(perRock > 0);
        // an in-game hour at speed 0.6 breaks 24
        double turnsAnHour = OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed;
        var (_, rocks) = WorkTurns.None.Add(turnsAnHour * 2 * Math.PI, perRock);
        Assert.Equal(24, rocks);
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(2, 8, 0.25)]
    [InlineData(8, 8, 1)]
    [InlineData(9, 8, 1)]
    [InlineData(-1, 8, 0)]
    [InlineData(1, 0, 1)]
    public void Its_fill_is_the_charge_share(int rocks, int charge, double fill) =>
        Assert.Equal(fill, JawBreaking.Fill(rocks, charge), 6);

    [Fact]
    public void Breaking_keeps_the_stream_as_it_came()
    {
        var raw = new OreStream("galena", "rich", 3, null, OreState.None, WorkedBy.None, "granite");
        Assert.Equal(raw, JawBreaking.Crush(raw));
    }

    [Fact]
    public void The_settings_fall_back_out_of_range()
    {
        var config = new JawBreakerConfig { ChargeRocks = 0 };
        Assert.Single(config.Sanitise());
        Assert.Equal(8, config.ChargeRocks);
        Assert.Empty(new JawBreakerConfig().Sanitise());
    }

    [Fact]
    public void Its_build_is_the_readme_table_with_the_jaw_faces_fitted_with_the_jaws()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/jawbreaker.json")));
        Assert.Equal("seraphhorizons:jawbreaker-north", build.Block);
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["jaws", "eccentric", "toggles", "grizzly", "services"], build.Stages.Select(s => s.Id));
        var faces = Assert.Single(build.Wearing);
        Assert.Equal(("jawfaces", 2, 1000.0), (faces.Id, faces.Count, faces.Uses));
        Assert.Contains(build.Stages[0].Materials, m => m.Wearing == "jawfaces" && m.Quantity == 2);

        // the jaws: the stage's own steel plate, then the faces' two
        var assembly = new MachineAssembly(build);
        Assert.Equal(AssemblyVerdict.OutOfOrder, assembly.Fit("game:rod-iron", 5).Verdict);
        foreach (var (code, n) in new[] { ("game:metalplate-iron", 2), ("game:metalnailsandstrips-iron", 1), ("game:rod-steel", 1),
                     ("game:metalplate-steel", 1), ("game:metalnailsandstrips-steel", 2), ("game:metalplate-steel", 2) })
            Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit(code, n).Verdict);
        Assert.Equal(1, assembly.Build.StagesDone);
        Assert.NotNull(assembly.Wearing.Fitted("jawfaces"));
        Assert.True(assembly.Shows("jaws"));
        Assert.False(assembly.Shows("eccentric"));

        // the toggles: the rods draw the toggles before the springs go in
        while (assembly.Build.Next is < 2)
            assembly.FillNext();
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:rod-steel", 2).Verdict);
        Assert.True(assembly.Shows("toggles"));
        Assert.False(assembly.Shows("springs"));
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("seraphhorizons:spring-steel", 2).Verdict);
        Assert.True(assembly.Shows("springs"));
        while (!assembly.Built)
            assembly.FillNext();
        Assert.True(assembly.Ready);

        // a thousand rocks wear the faces out; two steel plates renew them whole
        Assert.Equal(AssemblyVerdict.NotWorn, assembly.Fit("game:metalplate-steel", 2).Verdict);
        for (int i = 0; i < 1000; i++)
            assembly.Wearing.WearAll(1);
        Assert.False(assembly.Ready);
        Assert.Equal(AssemblyVerdict.TooFew, assembly.Fit("game:metalplate-steel", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Renewed, assembly.Fit("game:metalplate-steel", 2).Verdict);
        Assert.True(assembly.Ready);
    }

    [Fact]
    public void Its_rig_reads_as_an_ore_machine_with_its_anchors()
    {
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("jawbreaker-rig.json")));
        Assert.Equal(15, rig.Cells.Count);
        Assert.Equal(new Int3(1, 1, 1), rig.Anchor("power")!.Value.Cell);
        Assert.Equal(CellFace.East, rig.Anchor("power")!.Value.Face);
        Assert.Equal(new Int3(0, 2, 0), rig.Anchor("infeed")!.Value.Cell);
        Assert.Equal(CellFace.Up, rig.Anchor("infeed")!.Value.Face);
        Assert.Equal(new Int3(0, 0, 0), rig.Anchor("output")!.Value.Cell);
        Assert.Equal(CellFace.North, rig.Anchor("output")!.Value.Face);
        Assert.Null(rig.Anchor("water"));
        Assert.Equal(Axis.X, rig.ShaftAxis);
        Assert.Equal(["jaws", "eccentric", "toggles", "springs", "grizzly", "chutes"], rig.StageRequires);
        Assert.Equal(14, rig.Parts.Parts.Count);
        // the ore machines' front faces the player: the facing is the way they look
        Assert.Equal(Side.South, OreMachineRig.PlacedSide(Side.South));
    }

    public static TheoryData<string> OreRigs()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Out("rigs"), "*-rig.json").OrderBy(f => f, StringComparer.Ordinal))
            if (File.ReadAllText(f).Contains("\"buildStages\"", StringComparison.Ordinal))
                data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(OreRigs))]
    public void Every_staged_rig_reads_as_an_ore_machine(string file)
    {
        // a machine's own content requires (drawn by its state, not its build) are its blocktype's
        // extraRequires; here every requires a part uses is allowed, to hold the rest of the rig to the reader
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Out("rigs/" + file)));
        var requires = doc.RootElement.TryGetProperty("parts", out var parts)
            ? parts.EnumerateArray().Select(p => p.TryGetProperty("requires", out var r) ? r.GetString() : null).OfType<string>().Distinct().ToList()
            : [];
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("rigs/" + file)), requires);
        Assert.Contains(rig.Cells, c => c.Pos == Int3.Zero);
    }

    [Fact]
    public void A_rig_with_an_anchor_off_its_cells_or_facing_inward_is_refused()
    {
        const string cells = "\"cells\": [ { \"pos\": [0,0,0] }, { \"pos\": [1,0,0] } ]";
        Assert.Throws<FormatException>(() => OreMachineRig.Parse("{" + cells + ", \"outputCell\": [2,0,0], \"outputFace\": \"north\" }"));
        Assert.Throws<FormatException>(() => OreMachineRig.Parse("{" + cells + ", \"outputCell\": [0,0,0], \"outputFace\": \"east\" }"));
        // the power cell may be the controller's (the shaking table's)
        Assert.Equal(Int3.Zero, OreMachineRig.Parse("{" + cells + ", \"powerCell\": [0,0,0], \"powerFace\": \"west\" }").Anchor("power")!.Value.Cell);
        var rig = OreMachineRig.Parse("{" + cells + ", \"powerCell\": [1,0,0], \"powerFace\": \"east\" }");
        Assert.Single(rig.OtherCells);
    }
}
