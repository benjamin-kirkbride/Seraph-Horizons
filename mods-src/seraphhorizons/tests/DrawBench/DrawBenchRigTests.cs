using System.Text.Json;
using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.DrawBench;

/// <summary>A test that needs a file the model's generator writes (DrawBench/tools/make_shape.py):
/// skipped, with the reason, until the file is there.</summary>
public sealed class NeedsDrawBenchFileFactAttribute : FactAttribute
{
    public NeedsDrawBenchFileFactAttribute(params string[] files)
    {
        var missing = files.Where(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))).ToList();
        if (missing.Count > 0)
            Skip = $"not generated yet: {string.Join(", ", missing)} (DrawBench/tools/make_shape.py writes it)";
    }
}

/// <summary>
/// The draw bench's shipped rig (written by DrawBench/tools/make_shape.py) read by the shared rig
/// maths its renderer uses: every part and driver parses with the machines' parser and the bench's
/// <c>requires</c> vocabulary, the gameplay's reader takes it, and every reference pose (θ, ψ, W,
/// k, p, oil) gives the matrices the Python reference maths wrote, as GearCutterRigTests holds the
/// gear cutter's. The settings' pace is held to the rig's <c>draw.turnsPerSection</c>.
/// </summary>
public class DrawBenchRigTests
{
    private const string RigFile = "drawbench-rig.json";
    private const string ReferenceFile = "drawbench-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped());
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the draw bench's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), DrawBenchRequires.KnownRequires, path), path);
    }

    [NeedsDrawBenchFileFact(RigFile)]
    public void The_shipped_rig_parses_and_counts_sections()
    {
        var (parts, path) = ShippedParts();
        Assert.True(parts.Parts.Count > 20);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("sections", work.Unit);
        Assert.Equal("sections drawn", work.Name);
        Assert.Equal(3.0, path.End(1), 6);
        Assert.Equal(3.0, path.End(2), 6);
    }

    [NeedsDrawBenchFileFact(RigFile)]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = DrawBenchRig.Parse(Shipped());
        Assert.Equal(4, rig.Cells.Count);
        Assert.All(rig.Cells, c => Assert.NotNull(c.Lid));
        Assert.Equal(new Int3(0, 0, 3), rig.PowerCell);
        Assert.Equal(Side.West, rig.PowerFace);
        Assert.Equal(Side.North, rig.InfeedSide);
        Assert.Equal(Side.East, rig.OutputSide);
        Assert.Equal([new Int3(0, 0, -1)], rig.InfeedNeighbours());
        Assert.Equal(1, rig.OutputNeighbour().X);
        // the anchors lie in the bench
        foreach (var p in new[] { rig.Output, rig.Die, rig.Drip })
        {
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
            Assert.InRange(p.Z, 0, 4);
        }
    }

    [NeedsDrawBenchFileFact(RigFile)]
    public void Every_requires_of_the_rig_is_known_and_every_stage_and_billet_draws_something()
    {
        var (parts, _) = ShippedParts();
        var used = parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Subset(DrawBenchRequires.KnownRequires.ToHashSet(), used);
        Assert.Superset(DrawBenchRequires.KnownRequires.ToHashSet(), used);
    }

    // The gameplay's pace is the model's: the dog's stroke and the change gear agree only at the
    // drawn turns a section.
    [NeedsDrawBenchFileFact(RigFile)]
    public void The_default_pace_is_the_rigs()
    {
        var rig = DrawBenchRig.Parse(Shipped());
        var config = new DrawBenchConfig();
        Assert.Equal(rig.TurnsPerSection[1], config.TurnsPerSectionLead, 0.01);
        Assert.Equal(rig.TurnsPerSection[2], config.TurnsPerSectionCopper, 0.01);
    }

    [NeedsDrawBenchFileFact(RigFile, ReferenceFile)]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = ShippedParts();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ReferenceFile)));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double D(string key) => pose.GetProperty(key).GetDouble();
            var input = new RigInput(D("theta"), Travel: D("travel"), Work: D("work"), Class: pose.GetProperty("size").GetInt32(),
                                     Presence: D("presence"), Oil: D("oil"));
            classes.Add(input.Class);
            var mats = parts.Matrices(input);
            var expected = pose.GetProperty("matrices");
            Assert.Equal(parts.Parts.Count, expected.EnumerateObject().Count());
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                    {
                        double got = mats[i][col * 4 + row];
                        // single-precision matrices, as the other machines' tests allow
                        Assert.True(Math.Abs(got - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
        Assert.Equal([0, 1, 2], classes.Order());
    }
}
