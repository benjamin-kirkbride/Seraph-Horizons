using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using Xunit;

namespace SeraphHorizons.Tests.PressBrake;

/// <summary>A test that needs a file the model's generator writes (PressBrake/tools/make_shape.py):
/// skipped, with the reason, until the file is there.</summary>
public sealed class NeedsPressBrakeFileFactAttribute : FactAttribute
{
    public NeedsPressBrakeFileFactAttribute(params string[] files)
    {
        var missing = files.Where(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))).ToList();
        if (missing.Count > 0)
            Skip = $"not generated yet: {string.Join(", ", missing)} (PressBrake/tools/make_shape.py writes it)";
    }
}

/// <summary>
/// The press brake's shipped rig (written by PressBrake/tools/make_shape.py) read by the shared rig
/// maths its renderer uses: every part and driver parses with the machines' parser and the brake's
/// <c>requires</c> vocabulary, the gameplay's reader takes it with the contract's anchors, and every
/// reference pose (θ, W, k, p) gives the matrices the Python reference maths wrote, as
/// DrawBenchRigTests holds the draw bench's. The settings' pace is held to the rig's
/// <c>fold.leverTurnsPerPlate</c>. The reader's refusals are checked on edits of the shipped rig.
/// </summary>
public class PressBrakeRigTests
{
    private const string RigFile = "pressbrake-rig.json";
    private const string ReferenceFile = "pressbrake-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped());
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the press brake's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), PressBrakeRequires.KnownRequires, path), path);
    }

    [NeedsPressBrakeFileFact(RigFile)]
    public void The_shipped_rig_parses_and_counts_plates()
    {
        var (parts, path) = ShippedParts();
        Assert.True(parts.Parts.Count >= 10);
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("plates", work.Unit);
        Assert.Equal("fold", work.Name);
        Assert.Equal(1.0, path.End(1), 6);
        Assert.Equal(1.0, path.End(2), 6);
        // the contract's stable part ids
        foreach (var id in new[] { "leaf", "leafedge", "lever", "bar", "baredge", "screww", "screwe", "bededge", "la", "lm", "ca", "cm", "frame" })
            Assert.True(parts.IndexOf(id) >= 0, $"no part {id}");
        // one fold, two legs: the third panel of the old U is gone
        foreach (var id in new[] { "lb", "cb" })
            Assert.True(parts.IndexOf(id) < 0, $"part {id} is still in the rig");
    }

    [NeedsPressBrakeFileFact(RigFile)]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = PressBrakeRig.Parse(Shipped());
        Assert.Equal([Int3.Zero, new Int3(0, 0, 1)], rig.Cells.Select(c => c.Pos));
        Assert.All(rig.Cells, c => Assert.NotNull(c.Lid));
        Assert.Single(rig.GhostCells);
        Assert.Equal(Side.South, rig.InfeedSide);
        Assert.Equal(Side.North, rig.OutputSide);
        // a chest beyond the far end of the bed feeds it; the angle comes off over the leaf
        Assert.Equal([new Int3(0, 0, 2)], rig.InfeedNeighbours());
        Assert.Equal(new Int3(0, 0, -1), rig.OutputNeighbour());
        Assert.True(rig.OutputDrop().Z < 0);
        // the anchors lie in the brake
        foreach (var p in new[] { rig.Output, rig.Plate, rig.Edge })
        {
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
            Assert.InRange(p.Z, 0, 2);
        }
        // one fold a plate, heard once, in the leaf's window (up and back, the moment about 0.42)
        Assert.Single(rig.Folds);
        Assert.InRange(Assert.Single(rig.FoldMoments), 0.3, 0.55);
        Assert.True(rig.IsFolding(rig.FoldMoments[0]));
        Assert.False(rig.IsFolding(0.01));
        Assert.False(rig.IsFolding(0.9));
    }

    [NeedsPressBrakeFileFact(RigFile)]
    public void Every_requires_of_the_rig_is_known_and_every_stage_and_plate_draws_something()
    {
        var (parts, _) = ShippedParts();
        var used = parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Subset(PressBrakeRequires.KnownRequires.ToHashSet(), used);
        Assert.Superset(PressBrakeRequires.KnownRequires.ToHashSet(), used);
    }

    // The gameplay's pace is the model's: the settings default to the rig's lever turns a plate.
    [NeedsPressBrakeFileFact(RigFile)]
    public void The_default_pace_is_the_rigs()
    {
        var rig = PressBrakeRig.Parse(Shipped());
        var config = new PressBrakeConfig();
        Assert.Equal(rig.LeverTurnsPerPlate[1], config.LeverTurnsPerPlateLead, 0.001);
        Assert.Equal(rig.LeverTurnsPerPlate[2], config.LeverTurnsPerPlateCopper, 0.001);
    }

    [NeedsPressBrakeFileFact(RigFile)]
    public void The_rig_is_refused_when_it_is_not_the_contracts()
    {
        var json = Shipped();
        PressBrakeRig.Parse(json);
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"platelead\"", "\"plate\"")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"anglesPerPlate\": 1", "\"anglesPerPlate\": 2")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"seraphhorizons:halfplate-copper\"", "\"game:metalplate-copper\"")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"seraphhorizons:angle-lead\"", "\"game:chutesection-lead\"")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"infeedSide\": \"south\"", "\"infeedSide\": \"north\"")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"leverTurnsPerPlate\"", "\"turnsPerPlate\"")));
        Assert.Throws<FormatException>(() => PressBrakeRig.Parse(json.Replace("\"infeedSide\"", "\"powerFace\": \"west\", \"infeedSide\"")));
    }

    [NeedsPressBrakeFileFact(RigFile, ReferenceFile)]
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
            // travel is |θ| when the reference does not give it (the brake's has no ψ of its own)
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
