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
        Assert.Equal("up", root.GetProperty("infeedFace").GetString());
        Assert.Equal("north", root.GetProperty("concentrateFace").GetString());
        Assert.Equal("south", root.GetProperty("tailingsFace").GetString());
    }

    [Fact]
    public void The_shake_turns_back_where_the_rig_draws_the_frame_at_its_ends()
    {
        var rig = OreMachineRig.Parse(File("vanner-rig.json"));
        Assert.NotNull(rig.Point("headBox"));
        Assert.NotNull(rig.Point("foot"));
        using var doc = JsonDocument.Parse(File("vanner-rig.json"));
        Assert.Equal(1.0, doc.RootElement.GetProperty("drive").GetProperty("strokesPerTurn").GetDouble());
        var parts = Shipped();
        int shaker = parts.IndexOf("shaker");
        double X(double theta) => parts.Matrices(new RigInput(theta))[shaker][12];
        double most = Enumerable.Range(0, 3600).Max(k => Math.Abs(X(k * 2 * Math.PI / 3600)));
        // at each end of the shake the frame is furthest across, and there are two a turn, either way round
        foreach (double end in new[] { Math.PI / 2, 3 * Math.PI / 2 })
        {
            Assert.True(most - Math.Abs(X(end)) < 1e-5);
            Assert.True(Mod.Vanner.Core.Vanning.ShakeTurned(end - 0.01, end + 0.01));
            Assert.True(Mod.Vanner.Core.Vanning.ShakeTurned(end + 0.01, end - 0.01));
        }
        Assert.Equal(2, Enumerable.Range(0, 3600).Count(k => Mod.Vanner.Core.Vanning.ShakeTurned(k * 2 * Math.PI / 3600, (k + 1) * 2 * Math.PI / 3600)));
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
