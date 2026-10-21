using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SlittingMill.Core;
using Xunit;

namespace SeraphHorizons.Tests.SlittingMill;

/// <summary>
/// The slitting mill's shipped rig (written by SlittingMill/tools/make_shape.py) read by the shared rig maths
/// its renderer uses and by the gameplay's reader: every part and driver parses with the mill's <c>requires</c>
/// vocabulary, the anchors are the contract's, every reference pose (θ, ψ, W, k, p) gives the matrices the Python
/// reference maths wrote, as the cylinder borer's tests hold its, and the settings' pace is the rig's
/// <c>slit.turnsPerStroke</c>.
/// </summary>
public class SlittingMillRigTests
{
    private const string RigFile = "slittingmill-rig.json";
    private const string ReferenceFile = "slittingmill-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped());
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the slitting mill's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), SlittingMillRequires.KnownRequires, path), path);
    }

    [Fact]
    public void The_shipped_rig_parses_and_counts_knife_strokes()
    {
        var (parts, path) = ShippedParts();
        Assert.True(parts.Parts.Count > 20);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("strokes", work.Unit);
        Assert.Equal("knife strokes", work.Name);
        Assert.Equal(Slitting.HandOut, path.End(1), 6);
        Assert.Equal(0, path.End(2));
        // every stage, the knife and the work are drawn by some part, and the build's stages are the rig's
        var requires = parts.Parts.Select(p => p.Requires).ToHashSet();
        foreach (var r in SlittingMillRequires.KnownRequires)
            Assert.Contains(r, requires);
        Assert.Equal("knife", parts.Parts.Single(p => p.Id == "knife").Requires);
        Assert.Equal("shear", parts.Parts.Single(p => p.Id == "bedknife").Requires);
        Assert.Equal("pinions", parts.Parts.Single(p => p.Id == "wheel").Requires);
        Assert.Equal("pusher", parts.Parts.Single(p => p.Id == "pusher").Requires);
    }

    [Fact]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = SlittingMillRig.Parse(Shipped());
        Assert.Equal(8, rig.Cells.Count);
        Assert.Equal(new Int3(1, 1, 0), rig.PowerCell);
        Assert.Equal(Side.East, rig.PowerFace);
        Assert.Equal(Side.North, rig.InfeedSide);
        Assert.Equal(Side.South, rig.OutputSide);
        Assert.Equal(7, rig.OtherCells().Count());
        Assert.NotNull(rig.PowerAnchor());
        // a container beyond the nail box's end takes the nails and strips; one at the feed table's end feeds plates
        Assert.Equal(new Int3(1, 0, 2), rig.OutputNeighbour());
        Assert.True(rig.OutputDrop().Z > 2);
        Assert.Equal(4, rig.InfeedNeighbours().Count());
        Assert.All(rig.InfeedNeighbours(), p => Assert.Equal(-1, p.Z));
        // the cut is behind the nip, along the plate's way
        Assert.True(rig.Knife.Z > rig.Nip.Z);
        Assert.True(rig.Nip.Z > rig.Plate.Z);
    }

    [Fact]
    public void The_settings_pace_is_the_rigs()
    {
        var rig = SlittingMillRig.Parse(Shipped());
        Assert.Equal(SlittingMillConfig.Defaults.TurnsPerStroke, rig.TurnsPerStroke, 6);
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = ShippedParts();
        var reference = Path.Combine(AppContext.BaseDirectory, ReferenceFile);
        Assert.True(File.Exists(reference), "no tests/SlittingMill/rig-reference.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(reference));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double D(string key) => pose.GetProperty(key).GetDouble();
            var input = new RigInput(D("theta"), Travel: D("travel"), Work: D("work"), Class: pose.GetProperty("size").GetInt32(),
                                     Presence: D("presence"), Oil: pose.TryGetProperty("oil", out var oil) ? oil.GetDouble() : 1);
            classes.Add(input.Class);
            var mats = parts.Matrices(input);
            var expected = pose.GetProperty("matrices");
            Assert.Equal(parts.Parts.Count, expected.EnumerateObject().Count());
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                    {
                        double got = mats[i][col * 4 + row];
                        // relative for the work's stretched slivers, scaled up to 200 times (a float's precision)
                        Assert.True(Math.Abs(got - rows[row][col]) < Math.Max(2e-4, 4e-6 * Math.Abs(rows[row][col])),
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
        Assert.Contains(0, classes);
        Assert.Contains(1, classes);
    }

    private static string Edited(Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(Shipped(), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        edit(node);
        return node.ToJsonString();
    }

    [Fact]
    public void The_rig_is_refused_when_it_is_not_the_contracts()
    {
        Assert.Contains("work.end", Assert.Throws<FormatException>(() => SlittingMillRig.Parse(Edited(r => r["work"]!["end"]!["thin"] = 4.0))).Message);
        Assert.Contains("slit.handOut", Assert.Throws<FormatException>(() => SlittingMillRig.Parse(Edited(r => r["slit"]!["handOut"] = 4.5))).Message);
        Assert.Contains("slit.cuts", Assert.Throws<FormatException>(() => SlittingMillRig.Parse(Edited(r => r["slit"]!["cuts"] = new JsonArray(2.0, 3.0)))).Message);
        Assert.Contains("slit.plate", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["slit"]!["plate"] = "game:ingot-{metal}"))).Message);
        Assert.Contains("slit.output", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["slit"]!["output"] = "game:nails-{metal}"))).Message);
        Assert.Contains("turnsPerStroke", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["slit"]!["turnsPerStroke"] = 0))).Message);
        Assert.Contains("powerCell", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["powerCell"] = new JsonArray(0, 0, 0)))).Message);
        Assert.Contains("inside the footprint", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["powerFace"] = "west"))).Message);
        Assert.Contains("same face", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["outputSide"] = "north"))).Message);
        Assert.Contains("requires", Assert.Throws<FormatException>(() =>
            SlittingMillRig.Parse(Edited(r => r["parts"]![1]!["requires"] = "gearing"))).Message);
    }
}
