using System.Text;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>Machines/Core's half of the shared machine base (docs/modeling/machine-base.md): the hand holds,
/// the shared job, the rig helpers, a durable wearing part and a material left out of a game without its
/// items, the oil entries by name, the hand machine's state and the wrench's detail.</summary>
public class MachineBaseTests
{
    // ---- Hands ----

    [Fact]
    public void A_player_counts_as_working_until_the_timeout_or_letting_go()
    {
        var holds = new HandHolds();
        Assert.Equal(600, holds.TimeoutMs);
        Assert.False(holds.Any(0));
        holds.Hold("a", 1000);
        Assert.True(holds.Any(1600));
        Assert.False(holds.Any(1601));
        holds.Hold("a", 2000);
        holds.Hold("b", 2100);
        holds.Release("a");
        Assert.True(holds.Any(2100));
        holds.Clear();
        Assert.False(holds.Any(2100));
    }

    [Fact]
    public void The_hold_timeout_is_the_machines_own()
    {
        var holds = new HandHolds(1000);
        holds.Hold("a", 0);
        Assert.True(holds.Any(1000));
        Assert.False(holds.Any(1001));
    }

    // ---- The job ----

    [Fact]
    public void A_job_advances_to_its_end_and_finishes_once()
    {
        var job = MachineJob.Start(2, 4);
        Assert.True(job.On);
        Assert.True(job.Untouched);
        (job, bool finished) = job.Advance(1.5);
        Assert.False(finished);
        Assert.False(job.Untouched);
        Assert.Equal(0.375, job.Progress, 9);
        (job, finished) = job.Advance(10);
        Assert.True(finished);
        Assert.Equal(4, job.Work);
        Assert.True(job.Done);
        (job, finished) = job.Advance(1);
        Assert.False(finished);
        Assert.Equal(MachineJob.None, MachineJob.Start(0, 4));
        Assert.Equal(MachineJob.None, MachineJob.Start(1, 0));
    }

    [Fact]
    public void A_counted_step_is_a_blow_even_when_it_adds_little()
    {
        var job = MachineJob.Start(1, 1);
        (job, _) = job.Step(0.25);
        (job, bool finished) = job.Step(0.25);
        Assert.Equal(2, job.Steps);
        Assert.False(finished);
        Assert.False(job.Untouched);
        (job, finished) = job.Step(0.5);
        Assert.True(finished);
        Assert.Equal(3, job.Steps);
    }

    [Fact]
    public void A_saved_job_is_clamped_and_a_class_with_no_end_is_none()
    {
        Assert.Equal(new MachineJob(1, 1, 1), MachineJob.Restore(1, 5, 1));
        Assert.Equal(new MachineJob(2, 0, 4, 3), MachineJob.Restore(2, double.NaN, 4, 3));
        Assert.Equal(MachineJob.None, MachineJob.Restore(3, 0.5, 0));
        Assert.Equal(MachineJob.None, MachineJob.Restore(0, 0.5, 1));
        Assert.Equal(2, MachineJob.Crossed([0.43125, 1.43125, 2.43125], 0.4, 1.5));
    }

    // ---- Rigs ----

    private sealed class TestRig(IReadOnlyList<RigCell> cells, params MachineAnchor[] anchors) : IMachineRig
    {
        public IReadOnlyList<RigCell> Cells => cells;
        public RigParts Parts { get; } = new([], null);
        public IReadOnlyDictionary<string, MachineAnchor> Anchors => anchors.ToDictionary(a => a.Name);
        public MachineAnchor? Anchor(string name) => Anchors.TryGetValue(name, out var a) ? a : null;
    }

    private static RigCell Cell(int x, int y, int z) => new(new Int3(x, y, z), []);

    [Fact]
    public void A_rig_gives_its_other_cells_its_shaft_its_middle_and_its_light()
    {
        // the press brake's footprint: two cells along z
        var brake = new TestRig([Cell(0, 0, 0), Cell(0, 0, 1)]);
        Assert.Single(brake.OtherCells());
        Assert.Null(brake.PowerAnchor());
        Assert.Null(brake.ShaftAxis());
        Assert.Equal(new Float3(0.5f, 0.5f, 1f), brake.Middle());
        Assert.Equal(new Int3(0, 1, 1), brake.LightCell());
        // the draw bench's: four along z, the axle on the far cell's west face
        var bench = new TestRig([Cell(0, 0, 0), Cell(0, 0, 1), Cell(0, 0, 2), Cell(0, 0, 3)],
            new MachineAnchor(MachineRigs.Power, new Int3(0, 0, 3), Side.West.Face()));
        Assert.Equal(Axis.X, bench.ShaftAxis());
        Assert.Equal(new Int3(0, 1, 2), bench.LightCell());
        Assert.Equal(CellFace.North, Side.North.Face());
        Assert.Equal(CellFace.East, Side.East.Face());
    }

    // ---- Builds ----

    private const string BenchLike = """
        {
          "machine": "bench",
          "stages": [
            { "id": "frame", "adds": ["frame"], "materials": [ { "codes": ["game:plank-*"], "quantity": 2 } ] },
            { "id": "mandrel", "adds": ["mandrel"], "materials": [
                { "codes": ["game:rod-iron"], "quantity": 2 },
                { "codes": ["game:spring-steel"], "quantity": 1, "ifExists": true } ] },
            { "id": "die", "adds": ["die"], "materials": [] }
          ],
          "wearing": [ { "id": "die", "codes": ["game:die-steel", "game:die-iron"], "count": 1, "uses": 100, "requires": "die", "stage": "die", "durable": true } ]
        }
        """;

    [Fact]
    public void A_material_whose_items_are_not_in_the_game_is_left_out()
    {
        var build = MachineBuildJson.Parse(BenchLike);
        Assert.True(build.Stages[1].Materials[1].IfExists);
        var without = build.Resolve(code => code != "game:spring-steel");
        Assert.Single(without.Stages[1].Materials);
        var with = build.Resolve(_ => true);
        Assert.Equal(2, with.Stages[1].Materials.Count);
        // a stage is never left with nothing to fit
        var bare = MachineBuildJson.Parse("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1, "ifExists": true } ] } ] }""");
        Assert.Single(bare.Resolve(_ => false).Stages[0].Materials);
        // a build with no such material is itself
        var plain = MachineBuildJson.Parse("""{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1 } ] } ] }""");
        Assert.Same(plain, plain.Resolve(_ => false));
    }

    [Fact]
    public void A_durable_part_goes_in_with_its_durability_and_comes_back_with_what_is_left()
    {
        var build = MachineBuildJson.Parse(BenchLike).Resolve(_ => true).WithUses("die", 80);
        Assert.Equal(80, build.Wearing[0].Uses);
        var a = new MachineAssembly(build);
        Assert.True(a.Fit("game:plank-oak", 2).Took);
        Assert.True(a.Fit("game:rod-iron", 2).Took);
        Assert.True(a.Fit("game:spring-steel", 1).Took);
        Assert.False(a.Built);
        // a spent die does not go in
        Assert.Equal(AssemblyVerdict.Spent, a.Fit("game:die-iron", 1, 1, uses: 0).Verdict);
        var fit = a.Fit("game:die-iron", 1, 1, uses: 40);
        Assert.Equal(AssemblyVerdict.Fitted, fit.Verdict);
        Assert.True(a.Ready);
        Assert.Equal(("game:die-iron", 40.0), a.Wearing.Fitted("die"));
        Assert.Equal(0.5, a.Wearing.ShareLeft("die"), 9);
        Assert.True(a.Shows("die"));
        // broken now, the die comes back with what is left of it, by itself
        var drops = a.Returns();
        Assert.Contains(new StageDrop("game:die-iron", 1, 40), drops);
        Assert.Equal(new StageDrop("game:plank-oak", 2), drops[0]);
        // taken out, the bench lacks it; worn out, it is gone
        Assert.Equal(("game:die-iron", 40.0), a.Take("die"));
        Assert.False(a.Ready);
        Assert.False(a.Shows("die"));
        Assert.Null(a.Take("die"));
        Assert.Equal(AssemblyVerdict.Renewed, a.Fit("game:die-steel", 1, 1, uses: 100).Verdict);
        Assert.Equal(80, a.Wearing.Fitted("die")!.Value.Left);   // never past the part's uses
        Assert.True(a.Wearing.Wear("die", 80));
        Assert.DoesNotContain(a.Returns(), d => d.Code == "game:die-steel");
        Assert.Equal(AssemblyVerdict.Spent, a.Fit("game:die-steel", 1, 1, uses: 0).Verdict);
        // the creative shortcut fits a new one
        Assert.True(a.FillNext());
        Assert.Equal(("game:die-steel", 80.0), a.Wearing.Fitted("die"));
    }

    [Fact]
    public void A_durable_part_is_one_item()
    {
        var e = Assert.Throws<FormatException>(() => MachineBuildJson.Parse(
            """{ "machine": "m", "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1 } ] } ], "wearing": [ { "id": "w", "codes": ["game:y"], "count": 2, "uses": 1, "durable": true } ] }"""));
        Assert.Contains("durable", e.Message);
    }

    // ---- Oil ----

    [Fact]
    public void Every_oil_machine_has_its_entry_by_name()
    {
        var config = new MachineOilConfig();
        foreach (var machine in Enum.GetValues<OilMachine>())
            Assert.Same(typeof(MachineOilConfig).GetProperty(machine.ToString())!.GetValue(config), config.For(machine));
        config.DrawBench = null!;
        var fixes = config.Sanitise();
        Assert.Contains(fixes, f => f.StartsWith("DrawBench is missing", StringComparison.Ordinal));
        Assert.Equal(MachineOilConfig.Defaults.DrawBench.DrainPerJob, config.DrawBench.DrainPerJob);
    }

    // ---- State ----

    [Fact]
    public void A_hand_machine_with_work_and_no_one_at_it_waits()
    {
        var check = new MachineCheck(true, true, false, 0, true, true, true, Held: false);
        Assert.Equal(MachineState.Unworked, MachineRun.StateOf(check, 0.05f));
        Assert.Equal(MachineState.Working, MachineRun.StateOf(check with { Held = true }, 0.05f));
        Assert.Equal(MachineState.Idle, MachineRun.StateOf(check with { HasWork = false }, 0.05f));
        // a machine no hand works is not asked
        Assert.Equal(MachineState.Working, MachineRun.StateOf(check with { Held = null }, 0.05f));
        Assert.False(MachineRun.Works(MachineState.Unworked));
    }

    // ---- Info ----

    [Fact]
    public void The_detail_shows_only_with_a_wrench_and_a_hint_says_so()
    {
        var info = new MachineInfo();
        info.Add("Build: stage 1 of 2");
        var plain = new StringBuilder();
        info.WriteTo(plain, wrench: false, "Hold a wrench");
        Assert.Equal("Build: stage 1 of 2" + Environment.NewLine, plain.ToString());
        info.AddDetail("Shaft speed: 0.40");
        plain.Clear();
        info.WriteTo(plain, wrench: false, "Hold a wrench");
        Assert.Equal(string.Join(Environment.NewLine, "Build: stage 1 of 2", "Hold a wrench", ""), plain.ToString());
        var wrench = new StringBuilder();
        info.WriteTo(wrench, wrench: true, "Hold a wrench");
        Assert.Equal(string.Join(Environment.NewLine, "Build: stage 1 of 2", "Shaft speed: 0.40", ""), wrench.ToString());
    }
}
