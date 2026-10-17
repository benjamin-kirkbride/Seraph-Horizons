using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.AmalgamPan;

/// <summary>
/// The amalgam pan's shipped rig (written by AmalgamPan/tools/make_shape.py) read by the shared rig
/// maths, which its renderer will use: every part parses with the machines' parser and the pan's
/// <c>requires</c> vocabulary (#718's fitted stages, the muller and then its shoes, which ride it, the
/// contents, and the water film over the charge, which rides it too), there is no work, the axle comes in
/// from below at the pan's own cell (as into the quern), and every reference pose (θ, the spindle's angle) gives the matrices the Python reference maths wrote. There
/// is no amalgam pan gameplay yet (#718); this holds the third implementation of the rig maths to the model
/// before there is.
/// </summary>
public class AmalgamPanRigTests
{
    private static readonly HashSet<string> Requires = ["muller", "shoes", "mercury", "slurry", "water", "amalgam"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("amalgampan-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work()
    {
        var parts = Shipped();
        Assert.Equal(["muller", "shoes", "mercury", "slurry", "water", "amalgam", "frame"], parts.Parts.Select(p => p.Id));
        // the fitted stages in #718's order: the muller (a rod), then its shoes (a plate), riding it
        Assert.Equal(["muller", "shoes"], parts.Parts.Where(p => p.Requires is "muller" or "shoes").Select(p => p.Requires));
        var shoes = parts.Parts.Single(p => p.Id == "shoes");
        Assert.Equal("muller", shoes.Ride);
        Assert.Empty(shoes.Drivers);
        // the water film over the charge turns with them, cut away round the shoes
        var water = parts.Parts.Single(p => p.Id == "water");
        Assert.Equal(("water", "muller"), (water.Requires, water.Ride));
        Assert.Empty(water.Drivers);
        using var doc = JsonDocument.Parse(File("amalgampan-rig.json"));
        Assert.Equal([0, 0, 0], doc.RootElement.GetProperty("powerCell").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal("down", doc.RootElement.GetProperty("powerFace").GetString());
        Assert.Equal("north", doc.RootElement.GetProperty("operatorSide").GetString());
        // no automated input or output: the player loads and empties it by hand
        Assert.Equal(["operatorSide"], doc.RootElement.EnumerateObject().Select(p => p.Name)
            .Where(n => n.EndsWith("Side") || n.StartsWith("infeed") || n.StartsWith("output") || n.StartsWith("outfeed")));
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("amalgampan-rig-reference.json"));
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
