using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.BallMill;

/// <summary>
/// The ball mill's shipped rig (written by BallMill/tools/make_shape.py) read by the shared rig maths, which
/// its renderer will use: every part parses with the machines' parser and the mill's <c>requires</c>
/// vocabulary, there is no work, and every reference pose (θ, the axle's angle) gives the matrices the Python
/// reference maths wrote. There is no ball mill gameplay yet (#915); this holds the third implementation of
/// the rig maths to the model before there is.
/// </summary>
public class BallMillRigTests
{
    private static readonly HashSet<string> Requires =
        ["pinion", "drum", "girth", "screens", "feedbox", "hopper", "chutes", "pipecopper", "pipelead"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("ballmill-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work()
    {
        var parts = Shipped();
        Assert.Equal(["pinion", "drum", "girth", "screens", "feedbox", "hopper", "chutes", "pipecopper", "pipelead", "frame"],
            parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("ballmill-rig.json"));
        Assert.Equal("east", doc.RootElement.GetProperty("powerFace").GetString());
        Assert.Equal([1, 0, 0], doc.RootElement.GetProperty("powerCell").EnumerateArray().Select(v => v.GetInt32()));
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("ballmill-rig-reference.json"));
        int poses = 0;
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            var mats = parts.Matrices(new RigInput(pose.GetProperty("theta").GetDouble()));
            var expected = pose.GetProperty("matrices");
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        Assert.True(Math.Abs(mats[i][col * 4 + row] - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at theta {pose.GetProperty("theta")}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 10, $"only {poses} poses");
    }
}
