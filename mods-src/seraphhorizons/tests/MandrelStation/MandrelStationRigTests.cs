using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Xunit;

namespace SeraphHorizons.Tests.MandrelStation;

/// <summary>A test that needs a file the model's generator writes (MandrelStation/tools/make_shape.py):
/// skipped, with the reason, until the file is there.</summary>
public sealed class NeedsMandrelStationFileFactAttribute : FactAttribute
{
    public NeedsMandrelStationFileFactAttribute(params string[] files)
    {
        var missing = files.Where(f => !File.Exists(Path.Combine(AppContext.BaseDirectory, f))).ToList();
        if (missing.Count > 0)
            Skip = $"not generated yet: {string.Join(", ", missing)} (MandrelStation/tools/make_shape.py writes it)";
    }
}

/// <summary>
/// The mandrel station's shipped rig (written by MandrelStation/tools/make_shape.py) read by the
/// shared rig maths its renderer uses: every part and driver parses with the machines' parser and the
/// station's <c>requires</c> vocabulary, the gameplay's reader takes it with the contract's anchors,
/// and every reference pose (θ, W, k, p) gives the matrices the Python reference maths wrote, as
/// PressBrakeRigTests holds the press brake's. The settings' pace is held to the rig's
/// <c>forge.blowsPerHollow</c>.
/// </summary>
public class MandrelStationRigTests
{
    private const string RigFile = "mandrelstation-rig.json";
    private const string ReferenceFile = "mandrelstation-rig-reference.json";

    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RigFile));

    private static (RigParts Parts, IWorkProgress Path) ShippedParts()
    {
        using var doc = JsonDocument.Parse(Shipped(), RigJson.Options);
        var path = RigProgress.Of(doc.RootElement) ?? throw new FormatException("the mandrel station's rig has no work");
        return (RigParts.Parse(doc.RootElement.GetProperty("parts"), MandrelRequires.KnownRequires, path), path);
    }

    [NeedsMandrelStationFileFact(RigFile)]
    public void The_shipped_rig_parses_and_counts_hollows()
    {
        var (parts, path) = ShippedParts();
        var work = Assert.IsType<WorkQuantity>(path);
        Assert.Equal("hollows", work.Unit);
        Assert.Equal("blows", work.Name);
        Assert.Equal(1.0, path.End(1), 6);
        Assert.Equal(1.0, path.End(2), 6);
        // the contract's stable part ids
        foreach (var id in new[] { "mandrel", "frame" })
            Assert.True(parts.IndexOf(id) >= 0, $"no part {id}");
        // the work is eight rings a metal, closing and spreading along the mandrel together
        foreach (var metal in new[] { "l", "c" })
            foreach (var ring in new[] { "1", "2", "3", "4", "5", "6", "7", "8" })
                foreach (var wall in new[] { "u", "d", "e", "w", "ue", "uw", "de", "dw" })
                    Assert.True(parts.IndexOf(metal + ring + wall) >= 0, $"no part {metal + ring + wall}");
    }

    [NeedsMandrelStationFileFact(RigFile)]
    public void The_gameplay_reads_the_shipped_rig_with_the_contracts_anchors()
    {
        var rig = MandrelStationRig.Parse(Shipped());
        Assert.Equal([Int3.Zero, new Int3(0, 0, 1)], rig.Cells.Select(c => c.Pos));
        Assert.All(rig.Cells, c => Assert.NotNull(c.Lid));
        Assert.Single(rig.GhostCells);
        Assert.Equal(Side.West, rig.InfeedSide);
        Assert.Equal(Side.South, rig.OutputSide);
        // a chest beside the stump feeds it; the sections come off beyond the tip
        Assert.Contains(new Int3(-1, 0, 0), rig.InfeedNeighbours());
        Assert.Equal(new Int3(0, 0, 2), rig.OutputNeighbour());
        Assert.True(rig.OutputDrop().Z > 2);
        foreach (var p in new[] { rig.Output, rig.Strike })
        {
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
            Assert.InRange(p.Z, 0, 2);
        }
    }

    [NeedsMandrelStationFileFact(RigFile)]
    public void Every_requires_of_the_rig_is_known_and_each_draws_something()
    {
        var (parts, _) = ShippedParts();
        var used = parts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Subset(MandrelRequires.KnownRequires.ToHashSet(), used);
        Assert.Superset(MandrelRequires.KnownRequires.ToHashSet(), used);
    }

    // The gameplay's pace is the model's: the settings default to the rig's blows a hollow.
    [NeedsMandrelStationFileFact(RigFile)]
    public void The_default_pace_is_the_rigs()
    {
        var rig = MandrelStationRig.Parse(Shipped());
        var config = new MandrelStationConfig();
        Assert.Equal(rig.BlowsPerHollow[1], config.BlowsPerHollowLead);
        Assert.Equal(rig.BlowsPerHollow[2], config.BlowsPerHollowCopper);
    }

    [NeedsMandrelStationFileFact(RigFile)]
    public void The_shipped_rig_is_refused_when_it_is_not_the_contracts()
    {
        var json = Shipped();
        MandrelStationRig.Parse(json);
        Assert.Throws<FormatException>(() => MandrelStationRig.Parse(json.Replace("\"hollowlead\"", "\"hollow\"")));
        Assert.Throws<FormatException>(() => MandrelStationRig.Parse(json.Replace("\"game:chutesection-copper\"", "\"game:chutesection-tin\"")));
        Assert.Throws<FormatException>(() => MandrelStationRig.Parse(json.Replace("\"seraphhorizons:pipesection-lead\"", "\"game:chutesection-lead\"")));
        Assert.Throws<FormatException>(() => MandrelStationRig.Parse(json.Replace("\"blowsPerHollow\"", "\"blowsPerSection\"")));
    }

    [NeedsMandrelStationFileFact(RigFile, ReferenceFile)]
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
            // travel is |θ| when the reference does not give it (the station has no ψ of its own)
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
