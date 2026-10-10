using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CylinderBorer;

/// <summary>
/// The cylinder borer's shipped rig (written by CylinderBorer/tools/make_shape.py) read by the shared
/// rig maths its renderer uses and by the gameplay's reader: every part and driver parses with the
/// borer's <c>requires</c> vocabulary, the anchors are the contract's, every reference pose (θ, ψ, W,
/// k, p, oil) gives the matrices the Python reference maths wrote, as the draw bench's and the gear
/// cutter's tests hold theirs, and the settings' pace is the rig's <c>bore.turnsPerBlock</c>.
/// </summary>
public class CylinderBorerRigTests
{
    private const string RigFile = "cylinderborer-rig.json";
    private const string ReferenceFile = "cylinderborer-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped());
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the cylinder borer's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), CylinderBorerRequires.KnownRequires, path), path);
    }

    [Fact]
    public void The_shipped_rig_parses_and_measures_the_heads_travel()
    {
        var (parts, path) = ShippedParts();
        Assert.True(parts.Parts.Count > 40);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("blocks", work.Unit);
        Assert.Equal("cutter travel", work.Name);
        Assert.Equal(Boring.Travel, path.End(1), 6);
        Assert.Equal(Boring.Travel, path.End(2), 6);
        // every stage and both castings are drawn by some part
        var requires = parts.Parts.Select(p => p.Requires).ToHashSet();
        foreach (var r in CylinderBorerRequires.KnownRequires)
            Assert.Contains(r, requires);
    }

    [Fact]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = CylinderBorerRig.Parse(Shipped());
        Assert.Equal(8, rig.Cells.Count);
        Assert.Equal(new Int3(0, 0, 3), rig.PowerCell);
        Assert.Equal(Side.South, rig.PowerFace);
        Assert.Equal(Side.East, rig.InfeedSide);
        Assert.Equal(Side.West, rig.OutputSide);
        Assert.Equal(7, rig.GhostCells.Count());
        // a container beside the cradle, beyond the output face, takes the cylinder
        Assert.Equal(new Int3(-1, 0, 2), rig.OutputNeighbour());
        Assert.True(rig.OutputDrop().X < 0);
        Assert.Equal(8, rig.InfeedNeighbours().Count());
        Assert.All(rig.InfeedNeighbours(), p => Assert.Equal(1, p.X));
        // the casting lies in the cradle's cells, z 2..3, across both levels
        var casting = rig.CastingByCell();
        Assert.Equal(new[] { new Int3(0, 0, 2), new Int3(0, 1, 2) }, casting.Keys.OrderBy(k => k.Y));
        Assert.All(casting.Values, b => Assert.True(b.X1 >= 0 && b.X2 <= 1 && b.Y1 >= 0 && b.Y2 <= 1 && b.Z1 >= 0 && b.Z2 <= 1));
    }

    [Fact]
    public void The_settings_pace_is_the_rigs()
    {
        var rig = CylinderBorerRig.Parse(Shipped());
        Assert.Equal(CylinderBorerConfig.Defaults.TurnsPerBlockIron, rig.TurnsPerBlock[1], 3);
        Assert.Equal(CylinderBorerConfig.Defaults.TurnsPerBlockSteel, rig.TurnsPerBlock[2], 3);
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = ShippedParts();
        var reference = Path.Combine(AppContext.BaseDirectory, ReferenceFile);
        Assert.True(File.Exists(reference), "no tests/CylinderBorer/rig-reference.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(reference));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double D(string key) => pose.GetProperty(key).GetDouble();
            var input = new RigInput(D("theta"), Travel: D("travel"), Work: D("work"), Class: pose.GetProperty("size").GetInt32(),
                                     Presence: D("presence"), Oil: D("oil"));
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
                        Assert.True(Math.Abs(got - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
        Assert.Contains(1, classes);
        Assert.Contains(2, classes);
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
        Assert.Contains("work.end", Assert.Throws<FormatException>(() => CylinderBorerRig.Parse(Edited(r => r["work"]!["end"]!["thin"] = 2.0))).Message);
        Assert.Contains("bore.blanks.thin", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["bore"]!["blanks"]!["thin"] = "game:ingot-iron"))).Message);
        Assert.Contains("bore.cylinders.thick", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["bore"]!["cylinders"]!["thick"] = "seraphhorizons:cylinder-iron"))).Message);
        Assert.Contains("turnsPerBlock", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["bore"]!["turnsPerBlock"]!["thick"] = 0))).Message);
        Assert.Contains("powerCell", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["powerCell"] = new JsonArray(0, 0, 0)))).Message);
        Assert.Contains("inside the footprint", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["powerFace"] = "north"))).Message);
        Assert.Contains("same face", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["outputSide"] = "east"))).Message);
        Assert.Contains("box", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["bore"]!.AsObject().Remove("box")))).Message);
        Assert.Contains("requires", Assert.Throws<FormatException>(() =>
            CylinderBorerRig.Parse(Edited(r => r["parts"]![0]!["requires"] = "bearings"))).Message);
    }
}
