using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.AmalgamPan;

/// <summary>
/// The amalgam pan's shipped rig (written by AmalgamPan/tools/make_shape.py) read by the shared rig
/// maths, which its renderer will use: every part parses with the machines' parser and the pan's
/// <c>requires</c> vocabulary, there is no work, and every reference pose (θ, the crank's angle) gives the
/// matrices the Python reference maths wrote. There is no amalgam pan gameplay yet (#718); this holds
/// the third implementation of the rig maths to the model before there is.
/// </summary>
public class AmalgamPanRigTests
{
    private static readonly HashSet<string> Requires = ["muller", "shoes", "mercury", "amalgam"];

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
        Assert.Equal(["muller", "shoes", "mercury", "amalgam", "frame"], parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("amalgampan-rig.json"));
        Assert.False(doc.RootElement.TryGetProperty("powerCell", out _));
        Assert.Equal("north", doc.RootElement.GetProperty("operatorSide").GetString());
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
