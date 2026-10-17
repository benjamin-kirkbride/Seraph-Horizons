using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Jig;

/// <summary>
/// The jig's shipped rig (written by Jig/tools/make_shape.py) read by the shared rig maths, which its
/// renderer uses: every part parses with the machines' parser and the jig's <c>requires</c>
/// vocabulary, there is no work, every service sits on a face of its own, and every reference pose (θ, the
/// axle's angle) gives the matrices the Python reference maths wrote. The gameplay (#919) is
/// JigGameplayTests'.
/// </summary>
public class JigRigTests
{
    private static readonly HashSet<string> Requires =
        ["timber", "sieve", "drive", "plunger", "infeed", "outlets", "pipecopper", "pipelead", "water", "bed"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("jig-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work_and_its_services_on_faces_of_their_own()
    {
        var parts = Shipped();
        Assert.Equal(["shaft", "sheave", "plunger", "watersieve", "waterplunger", "bed", "stand", "sieve", "infeed", "outlets",
                      "pipecopper", "pipelead", "frame", "hutch"], parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("jig-rig.json"));
        var root = doc.RootElement;
        var faces = new HashSet<string>();
        foreach (var (key, cell, face) in new[] {
                     ("power", "0,1,1", "west"), ("infeed", "0,1,0", "up"), ("tailings", "0,1,0", "west"),
                     ("concentrate", "0,0,0", "north"), ("water", "0,0,1", "south") })
        {
            var got = string.Join(",", root.GetProperty(key + "Cell").EnumerateArray().Select(v => v.GetInt32()));
            Assert.Equal(cell, got);
            Assert.Equal(face, root.GetProperty(key + "Face").GetString());
            Assert.True(faces.Add(got + " " + face), $"{key} shares a face");
        }
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("jig-rig-reference.json"));
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

    [Fact]
    public void The_plunger_strokes_the_same_either_way_the_axle_turns()
    {
        var parts = Shipped();
        int plunger = parts.Parts.Select(p => p.Id).ToList().IndexOf("plunger");
        foreach (var theta in new[] { 0.4, 1.3, 2.9, 5.0 })
        {
            var up = parts.Matrices(new RigInput(theta))[plunger][13];
            var back = parts.Matrices(new RigInput(-theta))[plunger][13];
            Assert.True(Math.Abs(up - 0.5 / 16 * Math.Sin(theta)) < 1e-9, $"at {theta}: {up}");
            Assert.True(Math.Abs(up + back) < 1e-12, $"at {theta}: {up} and {back}");
        }
    }
}
