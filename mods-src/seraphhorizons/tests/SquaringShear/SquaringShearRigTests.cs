using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear.Core;
using Xunit;

namespace SeraphHorizons.Tests.SquaringShear;

/// <summary>A test that needs a file the model's generator writes (SquaringShear/tools/make_shape.py):
/// skipped, with the reason, until the file is there.</summary>
public sealed class NeedsSquaringShearFileFactAttribute : FactAttribute
{
    public NeedsSquaringShearFileFactAttribute(params string[] files)
    {
        var missing = files.Where(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))).ToList();
        if (missing.Count > 0)
            Skip = $"not generated yet: {string.Join(", ", missing)} (SquaringShear/tools/make_shape.py writes it)";
    }
}

/// <summary>
/// The squaring shear's shipped rig (written by SquaringShear/tools/make_shape.py) read by the shared
/// rig maths its renderer uses: every part and driver parses with the machines' parser and the shear's
/// <c>requires</c> vocabulary, the gameplay's reader takes it with the contract's anchors, and every
/// reference pose (θ, W, k, p) gives the matrices the Python reference maths wrote, as
/// DrawBenchRigTests holds the draw bench's. The settings' pace is held to the rig's
/// <c>cut.strokesPerPlate</c>. The reader's refusals are checked on edits of the shipped rig.
/// </summary>
public class SquaringShearRigTests
{
    private const string RigFile = "squaringshear-rig.json";
    private const string ReferenceFile = "squaringshear-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped());
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the squaring shear's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), SquaringShearRequires.KnownRequires, path), path);
    }

    [NeedsSquaringShearFileFact(RigFile)]
    public void The_shipped_rig_parses_and_counts_plates()
    {
        var (parts, path) = ShippedParts();
        Assert.True(parts.Parts.Count >= 10);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("plates", work.Unit);
        Assert.Equal("cut", work.Name);
        Assert.Equal(1.0, path.End(1), 6);
        Assert.Equal(1.0, path.End(2), 6);
        // the contract's stable part ids
        foreach (var id in new[] { "crosshead", "upperblade", "treadle", "linkw", "linke", "holddown", "gauge", "lowerblade", "lf", "lb", "cf", "cb", "frame" })
            Assert.True(parts.IndexOf(id) >= 0, $"no part {id}");
    }

    [NeedsSquaringShearFileFact(RigFile)]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = SquaringShearRig.Parse(Shipped());
        Assert.Equal([Int3.Zero, new Int3(0, 0, 1)], rig.Cells.Select(c => c.Pos));
        Assert.All(rig.Cells, c => Assert.NotNull(c.Lid));
        Assert.Single(rig.GhostCells);
        Assert.Equal(Side.North, rig.OutputSide);
        // the half plates come off over the table
        Assert.Equal(new Int3(0, 0, -1), rig.OutputNeighbour());
        Assert.True(rig.OutputDrop().Z < 0);
        // the anchors lie in the shear
        foreach (var p in new[] { rig.Output, rig.Plate, rig.Edge })
        {
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
            Assert.InRange(p.Z, 0, 2);
        }
        // one stroke a plate, the cut heard once at its bottom (down and back up, the moment 0.4)
        Assert.Single(rig.Strokes);
        Assert.InRange(Assert.Single(rig.CutMoments), 0.3, 0.55);
        Assert.True(rig.IsCutting(rig.CutMoments[0]));
        Assert.False(rig.IsCutting(0.01));
        Assert.False(rig.IsCutting(0.9));
    }

    [NeedsSquaringShearFileFact(RigFile)]
    public void Every_requires_of_the_rig_is_known_and_every_stage_and_plate_draws_something()
    {
        var (parts, _) = ShippedParts();
        var used = parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Subset(SquaringShearRequires.KnownRequires.ToHashSet(), used);
        Assert.Superset(SquaringShearRequires.KnownRequires.ToHashSet(), used);
    }

    // The gameplay's pace is the model's: the settings default to the rig's treadle strokes a plate.
    [NeedsSquaringShearFileFact(RigFile)]
    public void The_default_pace_is_the_rigs()
    {
        var rig = SquaringShearRig.Parse(Shipped());
        var config = new SquaringShearConfig();
        Assert.Equal(rig.StrokesPerPlate[1], config.StrokesPerPlateLead, 0.001);
        Assert.Equal(rig.StrokesPerPlate[2], config.StrokesPerPlateCopper, 0.001);
    }

    [NeedsSquaringShearFileFact(RigFile)]
    public void The_rig_is_refused_when_it_is_not_the_contracts()
    {
        var json = Shipped();
        SquaringShearRig.Parse(json);
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"platelead\"", "\"plate\"")));
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"halfPlatesPerPlate\": 2", "\"halfPlatesPerPlate\": 1")));
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"game:metalplate-copper\"", "\"game:metalplate-tin\"")));
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"seraphhorizons:halfplate-lead\"", "\"seraphhorizons:angle-lead\"")));
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"strokesPerPlate\"", "\"turnsPerPlate\"")));
        Assert.Throws<FormatException>(() => SquaringShearRig.Parse(json.Replace("\"outputSide\"", "\"powerFace\": \"west\", \"outputSide\"")));
    }

    [NeedsSquaringShearFileFact(RigFile, ReferenceFile)]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var (parts, _) = ShippedParts();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ReferenceFile)));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double D(string key, double fallback = 0) => pose.TryGetProperty(key, out var v) ? v.GetDouble() : fallback;
            double theta = D("theta");
            // travel is |θ| when the reference does not give it (the shear's has no ψ of its own)
            var input = new RigInput(theta, Travel: D("travel", Math.Abs(theta)), Work: D("work"), Class: pose.GetProperty("size").GetInt32(),
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
                        // single-precision matrices, as the other machines' tests allow
                        Assert.True(Math.Abs(got - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at {input}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
        Assert.Equal([0, 1, 2], classes.Order());
    }
}
