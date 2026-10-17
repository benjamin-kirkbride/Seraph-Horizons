using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.ShakingTable;

/// <summary>
/// The shaking table's shipped rig (written by ShakingTable/tools/make_shape.py) read by the shared rig
/// maths, which its renderer will use: every part parses with the machines' parser and the table's
/// <c>requires</c> vocabulary, there is no work, every reference pose (θ the axle's angle, ψ its travel)
/// gives the matrices the Python reference maths wrote, and past the rectifier nothing depends on which way
/// the axle turns. There is no shaking table gameplay yet (#921); this holds the third implementation of
/// the rig maths to the model before there is.
/// </summary>
public class ShakingTableRigTests
{
    private static readonly HashSet<string> Requires = ["deck", "head", "rectifier", "chutes", "pipecopper", "pipelead"];

    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static RigParts Shipped()
    {
        using var doc = JsonDocument.Parse(File("shakingtable-rig.json"));
        Assert.Null(RigProgress.Of(doc.RootElement));
        return RigParts.Parse(doc.RootElement.GetProperty("parts"), Requires, null);
    }

    [Fact]
    public void The_shipped_rig_parses_with_no_work()
    {
        var parts = Shipped();
        Assert.Equal(["entry", "headshaft", "deck", "pullrod", "rectifier", "head", "chutes", "pipecopper", "pipelead", "frame"],
            parts.Parts.Select(p => p.Id));
        using var doc = JsonDocument.Parse(File("shakingtable-rig.json"));
        Assert.Equal("west", doc.RootElement.GetProperty("powerFace").GetString());
        Assert.Equal(3.0, doc.RootElement.GetProperty("stroke").GetProperty("strokesPerTurn").GetDouble());
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = Shipped();
        using var doc = JsonDocument.Parse(File("shakingtable-rig-reference.json"));
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
                            $"{parts.Parts[i].Id} at theta {pose.GetProperty("theta")}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 10, $"only {poses} poses");
    }

    [Fact]
    public void Past_the_rectifier_the_head_and_the_deck_move_the_same_whichever_way_the_axle_turns()
    {
        var parts = Shipped();
        foreach (var theta in new[] { 0.4, 1.3, 2.9, 7.7 })
        {
            var forward = parts.Matrices(new RigInput(theta));
            var backward = parts.Matrices(new RigInput(-theta));
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                if (parts.Parts[i].Id == "entry")
                    continue;
                for (int k = 0; k < 16; k++)
                    Assert.True(Math.Abs(forward[i][k] - backward[i][k]) < 1e-5, $"{parts.Parts[i].Id} at axle ±{theta}");
            }
        }
    }
}
