using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Rocker;

/// <summary>
/// The rocker's shipped rig (written by Rocker/tools/make_shape.py) read by the shared rig maths, which its
/// renderer will use: every part parses with the machines' parser and the rocker's <c>requires</c>
/// vocabulary (its three states: a hand station has no build stages), there is no work, every reference
/// pose (θ, the rocking clock) gives the matrices the Python reference maths wrote, and the water, which
/// rides the cradle and swings back against it, stays level. There is no rocker gameplay yet (#716); this
/// holds the third implementation of the rig maths to the model before there is.
/// </summary>
public class RockerRigTests
{
    private static readonly HashSet<string> Requires = ["water", "charge", "concentrate"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("rocker-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work_and_no_power()
    {
        var parts = Shipped();
        Assert.Equal(["cradle", "water", "charge", "concentrate", "frame"], parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("rocker-rig.json"));
        Assert.False(doc.RootElement.TryGetProperty("powerCell", out _));
        Assert.Equal("north", doc.RootElement.GetProperty("operatorSide").GetString());
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("rocker-rig-reference.json"));
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

    [Theory]
    [InlineData(0.4)]
    [InlineData(Math.PI / 2)]
    [InlineData(3 * Math.PI / 2)]
    public void The_water_stays_level_while_the_cradle_leans(double theta)
    {
        var parts = Shipped();
        var mats = parts.Matrices(new RigInput(theta));
        int cradle = parts.Parts.ToList().FindIndex(p => p.Id == "cradle");
        int water = parts.Parts.ToList().FindIndex(p => p.Id == "water");
        Assert.True(Math.Abs(mats[cradle][6]) > 0.05, "the cradle leans");
        foreach (int j in new[] { 0, 1, 2, 4, 5, 6, 8, 9, 10 })
            Assert.True(Math.Abs(mats[water][j] - (j % 5 == 0 ? 1 : 0)) < 1e-5, $"the water's [{j}] is {mats[water][j]}");
    }
}
