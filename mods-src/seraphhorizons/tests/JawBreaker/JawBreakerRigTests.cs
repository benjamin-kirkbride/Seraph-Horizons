using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.JawBreaker;

/// <summary>
/// The jaw breaker's shipped rig (written by JawBreaker/tools/make_shape.py) read by the shared rig maths,
/// which its renderer will use: every part parses with the machines' parser and the breaker's stages as its
/// <c>requires</c> vocabulary, there is no work, the anchors are on their cells' faces, and every reference
/// pose (θ, the axle's angle) gives the matrices the Python reference maths wrote. There is no jaw breaker
/// gameplay yet (#912); this holds the third implementation of the rig maths to the model before there is.
/// </summary>
public class JawBreakerRigTests
{
    private static readonly HashSet<string> Requires = ["jaws", "eccentric", "toggles", "springs", "grizzly", "chutes"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("jawbreaker-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work_and_its_anchors()
    {
        var parts = Shipped();
        Assert.Equal(["eshaft", "pitman", "backtoggle", "fronttoggle", "swingjaw", "rods", "spring1", "spring2", "spring3", "spring4", "fixedjaw", "grizzly", "chutes", "frame"],
            parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("jawbreaker-rig.json"));
        var root = doc.RootElement;
        int[] Cell(string key) => root.GetProperty(key).EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal([1, 1, 1], Cell("powerCell"));
        Assert.Equal("east", root.GetProperty("powerFace").GetString());
        Assert.Equal([0, 2, 0], Cell("infeedCell"));
        Assert.Equal("up", root.GetProperty("infeedFace").GetString());
        Assert.Equal([0, 0, 0], Cell("outputCell"));
        Assert.Equal("north", root.GetProperty("outputFace").GetString());
        Assert.Equal(15, root.GetProperty("cells").GetArrayLength());   // the 2 x 3 x 2 box and the flywheel's three cells
        // the build: jaws; eccentric; toggles, then springs; grizzly; the services (the chute sections) last
        Assert.Equal(["jaws", "eccentric", "toggles", "grizzly", "services"],
            root.GetProperty("buildStages").EnumerateArray().Select(s => s.GetProperty("id").GetString()!));
        Assert.Equal(["jaws", "eccentric", "toggles", "springs", "grizzly", "chutes"],
            root.GetProperty("buildStages").EnumerateArray().SelectMany(s => s.GetProperty("adds").EnumerateArray().Select(v => v.GetString()!)));
    }

    [Fact]
    public void The_bite_and_the_particle_points_are_the_rigs()
    {
        using var doc = JsonDocument.Parse(File("jawbreaker-rig.json"));
        var breaker = doc.RootElement.GetProperty("breaker");
        Assert.Equal(1.0, breaker.GetProperty("turnsPerBite").GetDouble());
        Assert.Equal(Mod.JawBreaker.Core.JawBreaking.ClosesAt, breaker.GetProperty("closesAt").GetDouble());
        var rig = OreMachineRig.Parse(File("jawbreaker-rig.json"));
        Assert.NotNull(rig.Point("jaws"));
        Assert.NotNull(rig.Point("discharge"));
        // once a turn, either way round, and not again until the next
        Assert.True(Mod.JawBreaker.Core.JawBreaking.Bit(-0.1, 0.1));
        Assert.True(Mod.JawBreaker.Core.JawBreaking.Bit(0.1, -0.1));
        Assert.False(Mod.JawBreaker.Core.JawBreaking.Bit(0.1, 6.2));
        Assert.True(Mod.JawBreaker.Core.JawBreaking.Bit(6.2, 6.4));
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("jawbreaker-rig-reference.json"));
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
        Assert.True(poses >= 20, $"only {poses} poses");
    }
}
