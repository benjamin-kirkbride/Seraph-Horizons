using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Xunit;

namespace SeraphHorizons.Tests.Riddle;

/// <summary>
/// The riddle's shipped rig (written by Riddle/tools/make_shape.py), read by the shared rig maths, which its renderer
/// will use: every part parses with the machines' parser and its <c>requires</c> vocabulary (the riddle, the charge
/// and the fines in the box), the work is one charge's riddling (three lumps in twelve shakes, #714), and every
/// reference pose (θ, W, k, p; ψ is |θ|) gives the matrices the Python reference maths wrote, the stretching gauges
/// of the charge and the fines included. The gameplay's reader takes it with the contract's anchors, refuses any
/// automated input or output, and its default settings are the rig's charge (#714).
/// </summary>
public class RiddleRigTests
{
    private static readonly HashSet<string> Requires = ["riddle", "charge", "fines"];

    [Fact]
    public void The_gameplay_reads_the_shipped_rig_and_its_vocabulary_is_the_rigs()
    {
        var json = File("riddle-rig.json");
        var rig = RiddleRig.Parse(json);
        Assert.Equal([Int3.Zero], rig.Cells.Select(c => c.Pos));
        Assert.Equal(Side.North, rig.OperatorSide);
        Assert.Equal(Requires, RiddleRequires.KnownRequires.ToHashSet());
        foreach (var p in new[] { rig.Fines, rig.Charge })
        {
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
            Assert.InRange(p.Z, 0, 1);
        }
        Assert.True(rig.Charge.Y > rig.Fines.Y);
        // the settings' defaults are the model's charge
        var config = new RiddleConfig();
        Assert.Equal(rig.LumpsPerCharge, config.LumpsPerCharge);
        Assert.Equal(rig.ShakesPerCharge, config.ShakesPerCharge, 3);
        // a hand station: no power, no automated input or output
        foreach (var bad in new[] { "\"powerCell\": [0,0,0], ", "\"infeedSide\": \"south\", ", "\"outputSide\": \"south\", ", "\"output\": {\"pos\": [0,0,0]}, " })
            Assert.Throws<FormatException>(() => RiddleRig.Parse(json.Replace("\"operatorSide\"", bad + "\"operatorSide\"")));
        Assert.Throws<FormatException>(() => RiddleRig.Parse(json.Replace("\"lumpsPerCharge\"", "\"lumps\"")));
        Assert.Throws<FormatException>(() => RiddleRig.Parse(json.Replace("\"charge\": \"charge\"", "\"charge\": \"load\"")));
    }

    public static TheoryData<string, string> Forms => new()
    {
        { "riddle-rig.json", "riddle-rig-reference.json" },
    };

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static HashSet<string> RequiresOf(string rig) => Requires;

    private static (RigParts Parts, IWorkProgress Work) Shipped(string rig)
    {
        using var doc = JsonDocument.Parse(File(rig));
        var work = RigProgress.Of(doc.RootElement) ?? throw new FormatException($"{rig} has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), RequiresOf(rig), work), work);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void The_shipped_rig_parses_and_counts_charges(string rig, string reference)
    {
        Assert.EndsWith("-reference.json", reference);
        var (parts, path) = Shipped(rig);
        Assert.Equal("frame", parts.Parts[^1].Id);
        Assert.Contains(parts.Parts, p => p.Id == "riddle");
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("riddling", work.Name);
        Assert.Equal("charges", work.Unit);
        Assert.Equal(1.0, path.End(1), 6);
        Assert.Equal(RequiresOf(rig), parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet());
        using var doc = JsonDocument.Parse(File(rig));
        Assert.False(doc.RootElement.TryGetProperty("powerCell", out _));
        // a hand station takes no automated input or output: only the side the operator stands on
        Assert.Equal(["operatorSide"], doc.RootElement.EnumerateObject().Select(p => p.Name)
                                              .Where(n => n.EndsWith("Side") || n.EndsWith("Face") || n.EndsWith("Cell")));
        Assert.Equal("north", doc.RootElement.GetProperty("operatorSide").GetString());
        Assert.False(doc.RootElement.TryGetProperty("output", out _));
        var riddling = doc.RootElement.GetProperty("riddling");
        Assert.Equal(12.0, riddling.GetProperty("shakesPerCharge").GetProperty("thin").GetDouble());
        Assert.Equal(3, riddling.GetProperty("lumpsPerCharge").GetInt32());
        var contents = doc.RootElement.GetProperty("contents");
        Assert.Equal("charge", contents.GetProperty("charge").GetString());
        Assert.Equal("fines", contents.GetProperty("fines").GetString());
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void The_shipped_rig_matches_the_python_reference_poses(string rig, string reference)
    {
        var (parts, _) = Shipped(rig);
        using var doc = JsonDocument.Parse(File(reference));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double theta = pose.GetProperty("theta").GetDouble();
            var input = new RigInput(theta, Travel: Math.Abs(theta), Work: pose.GetProperty("work").GetDouble(),
                                     Class: pose.GetProperty("size").GetInt32(), Presence: pose.GetProperty("presence").GetDouble());
            classes.Add(input.Class);
            var mats = parts.Matrices(input);
            var expected = pose.GetProperty("matrices");
            Assert.Equal(parts.Parts.Count, expected.EnumerateObject().Count());
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        Assert.True(Math.Abs(mats[i][col * 4 + row] - rows[row][col]) < 2e-4,
                            $"{rig}: {parts.Parts[i].Id} at {input}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 40, $"only {poses} poses");
        Assert.Equal([0, 1], classes.Order());
    }
}
