using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.GearCutter;

/// <summary>
/// The gear cutter's shipped rig (written by GearCutter/tools/make_shape.py) read by the shared rig
/// maths, which its renderer will use: every part and driver parses with the machines' parser and
/// the rig's own work, and every reference pose (θ, ψ, W, k, p, oil) gives the matrices the Python
/// reference maths wrote. There is no gear cutter gameplay yet; this holds the third implementation
/// of the rig maths to the model before there is.
/// </summary>
public class GearCutterRigTests
{
    private static readonly HashSet<string> Requires =
        ["spindle", "feedscrew", "camfeed", "camindex", "liftcam", "index", "oiler", "head", "cutter", "master", "masterlarge", "blanksmall", "blanklarge", "cover"];

    private static (RigParts Parts, IWorkProgress Path) Shipped()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "gearcutter-rig.json");
        Assert.True(File.Exists(file), "no assets/seraphhorizons/config/gearcutter-rig.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the gear cutter's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, path), path);
    }

    [Fact]
    public void The_shipped_rig_parses_and_counts_teeth()
    {
        var (parts, path) = Shipped();
        Assert.True(parts.Parts.Count > 60);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("teeth", work.Unit);
        Assert.Equal("teeth cut", work.Name);
        Assert.Equal(12.0, path.End(1), 6);
        Assert.Equal(20.0, path.End(2), 6);
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = Shipped();
        var reference = Path.Combine(AppContext.BaseDirectory, "gearcutter-rig-reference.json");
        Assert.True(File.Exists(reference), "no tests/GearCutter/rig-reference.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(reference));
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
                        // single-precision matrices, as the mill's and the rosser's tests allow
                        Assert.True(Math.Abs(got - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 40, $"only {poses} poses");
        Assert.Equal([0, 1, 2], classes.Order());
    }
}
