using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Vanner;

/// <summary>
/// The vanner's shipped rig (written by Vanner/tools/make_shape.py) read by the shared rig maths, which its
/// renderer will use: every part parses with the machines' parser and the vanner's <c>requires</c>
/// vocabulary, there is no work, and every reference pose (θ the axle, ψ its travel either way) gives the
/// matrices the Python reference maths wrote. There is no vanner gameplay yet (#923); this holds the third
/// implementation of the rig maths to the model before there is.
/// </summary>
public class VannerRigTests
{
    private static readonly HashSet<string> Requires =
        ["countershaft", "shaker", "rollers", "belt", "creep", "feedbox", "chutes", "pipecopper", "pipelead"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("vanner-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work_and_its_anchors()
    {
        var parts = Shipped();
        Assert.Equal(["countershaft", "shaker", "uprightw", "uprighte", "seam1", "seam2", "belt", "head", "foot", "worm", "pawl", "chutes",
            "feedbox", "pipecopper", "pipelead", "frame"], parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("vanner-rig.json"));
        var root = doc.RootElement;
        Assert.Equal([1, 0, 2], root.GetProperty("powerCell").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal("south", root.GetProperty("powerFace").GetString());
        Assert.Equal("up", root.GetProperty("waterFace").GetString());
        Assert.Equal("up", root.GetProperty("feedFace").GetString());
        Assert.Equal("north", root.GetProperty("concentrateFace").GetString());
        Assert.Equal("south", root.GetProperty("tailingsFace").GetString());
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("vanner-rig-reference.json"));
        int poses = 0;
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            var mats = parts.Matrices(new RigInput(pose.GetProperty("theta").GetDouble(), Travel: pose.GetProperty("travel").GetDouble()));
            var expected = pose.GetProperty("matrices");
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        Assert.True(Math.Abs(mats[i][col * 4 + row] - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at theta {pose.GetProperty("theta")}, travel {pose.GetProperty("travel")}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
    }
}
