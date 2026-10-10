using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Riddle;

/// <summary>
/// The riddle's shipped rig (written by Riddle/tools/make_shape.py) read by the shared rig maths, which its
/// renderer will use: every part parses with the machines' parser and the riddle's <c>requires</c>
/// vocabulary, its work is one charge's riddling, and every reference pose (θ, W, k, p; ψ is |θ|) gives the
/// matrices the Python reference maths wrote. There is no riddle gameplay yet (#714); this holds the third
/// implementation of the rig maths to the model before there is.
/// </summary>
public class RiddleRigTests
{
    private static readonly HashSet<string> Requires = ["riddle", "chargesmall", "chargefull"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static (RigParts Parts, IWorkProgress Work) Shipped()
    {
        using var doc = JsonDocument.Parse(File("riddle-rig.json"));
        var work = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the riddle's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, work), work);
    }

    [Fact]
    public void The_shipped_rig_parses_and_counts_charges()
    {
        var (parts, path) = Shipped();
        Assert.Equal("riddle", parts.Parts[0].Id);
        Assert.Equal("frame", parts.Parts[^1].Id);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("riddling", work.Name);
        Assert.Equal("charges", work.Unit);
        Assert.Equal(1.0, path.End(1), 6);
        Assert.Equal(1.0, path.End(2), 6);
        Assert.Equal(Requires, parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet());
        using var doc = JsonDocument.Parse(File("riddle-rig.json"));
        Assert.False(doc.RootElement.TryGetProperty("powerCell", out _));
        Assert.Equal("north", doc.RootElement.GetProperty("operatorSide").GetString());
        var pace = doc.RootElement.GetProperty("riddling").GetProperty("shakesPerCharge");
        Assert.True(pace.GetProperty("thick").GetDouble() > pace.GetProperty("thin").GetDouble());
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = Shipped();
        using var doc = JsonDocument.Parse(File("riddle-rig-reference.json"));
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
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 40, $"only {poses} poses");
        Assert.Equal([0, 1, 2], classes.Order());
    }
}
