using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.ShakingTable.Core;
using Xunit;

namespace SeraphHorizons.Tests.ShakingTable;

/// <summary>
/// The shaking table's gameplay (#921) on the shipped config: its figures (r = 0.70, 7 rock an hour, a steady
/// load of 0.1, 20 L a rock), its pass sorted onto its three outlets (the locked units out as middlings, the
/// rest on as tailings, nothing lost), its place in the chain on the jig's tailings, and its build file (the
/// README's stage table) against its rig.
/// </summary>
public class ShakingTableGameplayTests
{
    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Out("ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static double Caught(TableOutlets o) => o.Concentrate.Sum(p => p.Units);

    [Fact]
    public void Its_figures_are_ore_cores()
    {
        var spec = R.Machine(OreMachine.Table);
        Assert.Equal(0.70, spec.Recovery, 6);
        Assert.Equal(0.1, spec.Load, 6);
        Assert.Equal(0.02, spec.EmptyLoad, 6);
        Assert.False(spec.LoadFollowsJob);
        Assert.Equal(20, spec.WaterPerItem, 6);
        Assert.Equal(7, spec.ItemsPerHour, 6);
        Assert.False(spec.Batch);
        // steady: the same load working or idle once built, 0.005 unbuilt
        Assert.Equal(0.1, R.LoadAt(OreMachine.Table, 0), 6);
        Assert.Equal(0.1, R.LoadAt(OreMachine.Table, 1), 6);
        Assert.Equal(MachineLoad.Unassembled, MachineLoad.Resistance(false, 0.1));
    }

    [Fact]
    public void An_hour_at_the_reference_speed_washes_seven()
    {
        double perRock = R.Machine(OreMachine.Table).TurnsPerItem;
        double turnsAnHour = OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed;
        var (_, rocks) = WorkTurns.None.Add(turnsAnHour * 2 * Math.PI, perRock);
        Assert.Equal(7, rocks);
        // at twice the speed, twice as many in the hour: the work is in turns
        (_, rocks) = WorkTurns.None.Add(2 * turnsAnHour * 2 * Math.PI, perRock);
        Assert.Equal(14, rocks);
    }

    [Fact]
    public void Fresh_unscreened_ore_sends_its_locked_share_out_as_middlings()
    {
        var feed = OreStream.FromRock("hematite", "rich", OreForm.Crushed, rock: "granite");
        var o = ShakingTabling.Wash(R, feed);
        double locked = feed.Units * (1 - 0.85);
        var middlings = Assert.IsType<OreStream>(o.Middlings);
        Assert.Equal(locked, middlings.Units, 9);
        Assert.Equal(locked, middlings.Locked!.Value, 9);
        Assert.Equal(WorkedBy.None, middlings.Worked);
        Assert.Equal("granite", middlings.Rock);
        Assert.Equal((feed.Units - locked) * R.PassShare(R.Ore("hematite"), OreMachine.Table), Caught(o), 9);
        Assert.Equal(OreProducts.ConcentrateCode("hematite"), Assert.Single(o.Concentrate).Code);
        // the tailings carry the rest, nothing locked left in them, worked by the table
        Assert.Equal(0, o.Tailings.Locked!.Value, 9);
        Assert.True(o.Tailings.Worked.HasFlag(WorkedBy.Table));
        Assert.Equal(feed.Units, Caught(o) + middlings.Units + o.Tailings.Units, 9);
        Assert.False(o.Whole);
    }

    [Fact]
    public void Screened_and_ground_ore_has_no_middlings()
    {
        var feed = OreStream.FromRock("hematite", "poor", OreForm.Ground, OreState.Classified);
        var o = ShakingTabling.Wash(R, feed);
        Assert.Null(o.Middlings);
        Assert.Equal(feed.Units * R.PassShare(R.Ore("hematite"), OreMachine.Table), Caught(o), 9);
    }

    [Fact]
    public void Middlings_ground_in_the_ball_mill_come_back_free_to_the_head_of_the_line()
    {
        var feed = OreStream.FromRock("hematite", "poor", OreForm.Crushed);
        var middlings = ShakingTabling.Wash(R, feed).Middlings!;
        var freed = R.Lift(middlings, OreState.Ground | OreState.Classified);
        Assert.Equal(0, freed.Locked!.Value, 9);
        Assert.Equal(WorkedBy.None, freed.Worked);
        // through the table again they are all catchable, and no middlings
        var again = ShakingTabling.Wash(R, freed);
        Assert.Null(again.Middlings);
        Assert.Equal(freed.Units * R.PassShare(R.Ore("hematite"), OreMachine.Table), Caught(again), 9);
    }

    [Fact]
    public void On_the_jigs_tailings_it_catches_its_share_of_what_is_left()
    {
        var ore = R.Ore("hematite");
        var feed = OreStream.FromRock("hematite", "medium", OreForm.Crushed);
        var sluice = R.Concentrate(OreMachine.Sluice, feed);
        var jig = R.Concentrate(OreMachine.Jig, sluice.Tailings);
        var table = ShakingTabling.Wash(R, jig.Tailings);
        double catchable = feed.Units * 0.85;
        double expected = catchable * (1 - R.PassShare(ore, OreMachine.Sluice)) * (1 - R.PassShare(ore, OreMachine.Jig))
                          * R.PassShare(ore, OreMachine.Table);
        Assert.Equal(expected, Caught(table), 9);
        // the locked share split at the sluice leaves here, as middlings
        Assert.Equal(feed.Units * 0.15, table.Middlings!.Units, 9);
        // and on its own tailings it catches nothing
        var twice = ShakingTabling.Wash(R, table.Tailings);
        Assert.Equal(0, Caught(twice), 9);
        Assert.Null(twice.Middlings);
    }

    [Fact]
    public void Its_build_is_the_readme_table_and_its_rig_has_the_three_outlets()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/shakingtable.json")));
        Assert.Equal("seraphhorizons:shakingtable-north", build.Block);
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["deck", "rectifier", "head", "services"], build.Stages.Select(s => s.Id));
        Assert.Empty(build.Wearing);

        var rig = OreMachineRig.Parse(File.ReadAllText(Out("shakingtable-rig.json")));
        Assert.Equal(rig.StageRequires, build.Stages.SelectMany(s => s.Adds));
        Assert.Equal(new MachineAnchor("power", Int3.Zero, CellFace.West), rig.Anchor("power"));
        Assert.Equal(new MachineAnchor("infeed", new Int3(0, 0, 1), CellFace.Up), rig.Anchor("infeed"));
        Assert.Equal(new MachineAnchor("water", new Int3(0, 0, 2), CellFace.Up), rig.Anchor("water"));
        Assert.Equal(new MachineAnchor(ShakingTabling.Concentrate, new Int3(0, 0, 2), CellFace.South), rig.Anchor(ShakingTabling.Concentrate));
        Assert.Equal(new MachineAnchor(ShakingTabling.Middlings, new Int3(1, 0, 2), CellFace.South), rig.Anchor(ShakingTabling.Middlings));
        Assert.Equal(new MachineAnchor(ShakingTabling.Tailings, new Int3(1, 0, 0), CellFace.East), rig.Anchor(ShakingTabling.Tailings));
        Assert.Equal(Axis.X, rig.ShaftAxis);

        // fitted in order; the services take either metal for the water line, drawing the one fitted
        var assembly = new MachineAssembly(build);
        Assert.Equal(AssemblyVerdict.OutOfOrder, assembly.Fit("seraphhorizons:spring-steel", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:plank-oak", 10).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:metalnailsandstrips-iron", 4).Verdict);
        Assert.True(assembly.Shows("deck"));
        Assert.False(assembly.Shows("rectifier"));
        while (assembly.Build.Next is < 3)
            assembly.FillNext();
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:chute-straight-ns", 4).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("seraphhorizons:pipesection-lead", 1).Verdict);
        Assert.True(assembly.Built);
        Assert.True(assembly.Ready);
        Assert.True(assembly.Shows("pipelead"));
        Assert.False(assembly.Shows("pipecopper"));
    }
}
