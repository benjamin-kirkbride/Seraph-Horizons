using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The rosser's test rig (tests/Rosser/fixtures/rosser-rig-test.json) and, once it exists,
/// the shipped one.</summary>
public static class RosserFixture
{
    public static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rosser-rig-test.json"));

    public static RosserRig Rig() => RosserRig.Parse(Json);

    public static RosserPace Pace(RosserConfig? config = null) => new(Rig(), config ?? new RosserConfig());

    /// <summary>The shipped rig, or null before Rosser/tools/make_shape.py has written it.</summary>
    public static RosserRig? Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rosser-rig.json");
        return File.Exists(path) ? RosserRig.Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>The fixture with <paramref name="edit"/> applied to its JSON.</summary>
    public static string Edited(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(Json)!.AsObject();
        edit(root);
        return root.ToJsonString();
    }
}

public class RosserRigTests
{
    [Fact]
    public void Parses_the_test_rig()
    {
        var rig = RosserFixture.Rig();
        Assert.Equal(new Int3(-9, 1, -2), rig.PowerCell);
        Assert.Equal(Side.North, rig.PowerFace);
        Assert.Equal(new Int3(-8, 1, 2), rig.WaterCell);
        Assert.Equal(Side.South, rig.WaterFace);
        Assert.Equal(Side.West, rig.InfeedSide);
        Assert.Equal(Side.East, rig.OutputSide);
        Assert.Equal(new RosserChute(new Float3(-7, 0.1f, 2.9f), Side.South), rig.Chute);
        Assert.Equal(-8.875f, rig.Breaker);
        Assert.Equal(-7.5f, rig.Ring);
        // no trunkPath.tips in the test rig: the heads stand in at the ring for both classes
        Assert.Equal(new[] { 0f, -7.5f, -7.5f }, rig.Tips);
        Assert.Equal(-7.5f, rig.TipAt(0));
        Assert.Equal(-9.875f, rig.Path.Nose0);
        Assert.Equal(9.625, rig.Path.End(1), 6);
        Assert.Equal(10.625, rig.Path.End(2), 6);
        Assert.Equal(0.0714286f, rig.Feed.BlocksPerRadian);
        Assert.Equal(0.31538f, rig.Feed.Gear[1]);
        Assert.Equal(0.14008f, rig.Feed.Gear[2]);
        Assert.Equal(4, rig.MovingParts.Parts.Count);
        Assert.Same(rig.Path, rig.MovingParts.Path);
    }

    [Fact]
    public void Reads_where_the_heads_touch_each_class()
    {
        string json = RosserFixture.Edited(r => r["trunkPath"]!["tips"] = new JsonObject { ["thin"] = -6.5, ["thick"] = -6.25 });
        var rig = RosserRig.Parse(json);
        Assert.Equal(-6.5f, rig.TipAt(1));
        Assert.Equal(-6.25f, rig.TipAt(2));
        Assert.Equal(rig.Ring, rig.TipAt(0));
    }

    [Fact]
    public void Reads_hollow_cells_and_boxes()
    {
        var rig = RosserFixture.Rig();
        Assert.True(rig.CellAt(new Int3(-13, 1, 0))!.Hollow);
        Assert.True(rig.CellAt(new Int3(0, 1, 1))!.Hollow);
        Assert.False(rig.CellAt(new Int3(-8, 1, 0))!.Hollow);
        Assert.False(rig.CellAt(Int3.Zero)!.Hollow);
        var partial = rig.CellAt(new Int3(-15, 1, -1))!;
        Assert.False(partial.Hollow);
        Assert.Equal(new Box(0, 0, 0, 1, 0.5f, 1), Assert.Single(partial.Boxes));
        Assert.Equal(rig.Cells.Count - 1, rig.GhostCells.Count());
    }

    [Fact]
    public void A_trunk_in_the_hollow_cells_gets_boxes_there()
    {
        // A thick trunk waiting on the infeed bed: its boxes are in the bed's cells, the hollow ones too.
        var rig = RosserFixture.Rig();
        var boxes = TrunkBox.CellBoxes(rig.Cells, TrunkBox.BoundsOnPath(rig.Path, TrunkClass.Thick, 0));
        Assert.Contains(new Int3(-13, 1, 0), boxes.Keys);
        Assert.Contains(new Int3(-13, 0, 0), boxes.Keys);
        Assert.DoesNotContain(boxes.Keys, c => c.X > -9);
    }

    [Fact]
    public void Neighbours_are_the_ground_cells_beyond_each_end()
    {
        var rig = RosserFixture.Rig();
        Assert.Equal(new[] { new Int3(-16, 0, -1), new Int3(-16, 0, 0), new Int3(-16, 0, 1) }, rig.InfeedNeighbours().OrderBy(p => p.Z));
        Assert.Equal(new[] { new Int3(1, 0, -1), new Int3(1, 0, 0), new Int3(1, 0, 1) }, rig.OutfeedNeighbours().OrderBy(p => p.Z));
        Assert.True(rig.IsOutfeedNeighbour(new Int3(1, 0, 0)));
        Assert.False(rig.IsOutfeedNeighbour(new Int3(-16, 0, 0)));
        Assert.False(rig.IsOutfeedNeighbour(new Int3(1, 1, 0)));
    }

    [Fact]
    public void The_parts_use_the_rossers_vocabulary()
    {
        var bad = RosserFixture.Edited(r => r["parts"]![0]!["requires"] = "crankshaft");
        Assert.Contains("crankshaft", Assert.Throws<FormatException>(() => RosserRig.Parse(bad)).Message);
        // and drivers on the trunk path work: the feed driver turns with φ
        var rig = RosserFixture.Rig();
        var at0 = rig.MovingParts.Matrices(new RigInput(0, Class: 1, Presence: 1, Feed: 0));
        var at1 = rig.MovingParts.Matrices(new RigInput(0, Class: 1, Presence: 1, Feed: 1));
        Assert.NotEqual(at0[1], at1[1]);
        Assert.Equal(at0[0], at1[0]);
    }

    [Fact]
    public void Parts_are_optional()
    {
        var rig = RosserRig.Parse(RosserFixture.Edited(r => r.Remove("parts")));
        Assert.Empty(rig.MovingParts.Parts);
    }

    public static TheoryData<string, string> Broken => new()
    {
        { "missing:waterCell", "waterCell" },
        { "missing:chute", "chute" },
        { "missing:feed", "feed" },
        { "missing:trunkPath", "trunkPath" },
        { "powerCell=[0,0,0]", "controller" },
        { "powerCell=[-9,5,-2]", "not one of the cells" },
        { "powerFace=south", "inside the footprint" },
        { "waterCell=[-9,1,-2]", "power cell" },
        { "waterCell=[-13,1,0]", "hollow" },
        { "waterFace=north", "inside the footprint" },
        { "infeedSide=east", "goes in at west" },
        { "outputSide=north", "goes in at west" },
        { "chute.side=north", "opposite powerFace" },
        { "stations-no-ring", "\"breaker\" and \"ring\"" },
        { "breaker-after-ring", "nose0 < breaker < ring <= tailStop" },
        { "breaker-before-nose", "nose0 < breaker < ring <= tailStop" },
        { "ring-after-tailstop", "nose0 < breaker < ring <= tailStop" },
        { "tips-before-ring", "ring <= tips <= tailStop" },
        { "tips-after-tailstop", "ring <= tips <= tailStop" },
        { "tips-no-thick", "thick" },
        { "feed.blocksPerRadian=0", "blocksPerRadian" },
        { "feed.gear.thick=0", "above 0" },
        { "feed.gear.thick=0.5", "thick trunks feed slower" },
        { "missing:feed.gear", "gear" },
        { "controller-hollow", "controller" },
        { "duplicate-cell", "twice" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void Rejects_a_broken_rig(string change, string expected)
    {
        string json = RosserFixture.Edited(r =>
        {
            var path = r["trunkPath"]!.AsObject();
            var stations = path["stations"]!.AsObject();
            switch (change)
            {
                case "missing:waterCell": r.Remove("waterCell"); break;
                case "missing:chute": r.Remove("chute"); break;
                case "missing:feed": r.Remove("feed"); break;
                case "missing:trunkPath": r.Remove("trunkPath"); break;
                case "missing:feed.gear": r["feed"]!.AsObject().Remove("gear"); break;
                case "powerCell=[0,0,0]": r["powerCell"] = new JsonArray(0, 0, 0); break;
                case "powerCell=[-9,5,-2]": r["powerCell"] = new JsonArray(-9, 5, -2); break;
                case "powerFace=south": r["powerFace"] = "south"; r["chute"]!["side"] = "north"; break;
                case "waterCell=[-9,1,-2]": r["waterCell"] = new JsonArray(-9, 1, -2); r["waterFace"] = "north"; break;
                case "waterCell=[-13,1,0]": r["waterCell"] = new JsonArray(-13, 1, 0); break;
                case "waterFace=north": r["waterFace"] = "north"; break;
                case "infeedSide=east": r["infeedSide"] = "east"; r["outputSide"] = "west"; break;
                case "outputSide=north": r["outputSide"] = "north"; break;
                case "chute.side=north": r["chute"]!["side"] = "north"; break;
                case "stations-no-ring": stations.Remove("ring"); break;
                case "breaker-after-ring": stations["breaker"] = -7.0; break;
                case "breaker-before-nose": stations["breaker"] = -10.0; break;
                case "ring-after-tailstop": stations["ring"] = -4.0; break;
                case "tips-before-ring": path["tips"] = new JsonObject { ["thin"] = -7.6, ["thick"] = -6.5 }; break;
                case "tips-after-tailstop": path["tips"] = new JsonObject { ["thin"] = -6.5, ["thick"] = -4.0 }; break;
                case "tips-no-thick": path["tips"] = new JsonObject { ["thin"] = -6.5 }; break;
                case "feed.blocksPerRadian=0": r["feed"]!["blocksPerRadian"] = 0; break;
                case "feed.gear.thick=0": r["feed"]!["gear"]!["thick"] = 0; break;
                case "feed.gear.thick=0.5": r["feed"]!["gear"]!["thick"] = 0.5; break;
                case "controller-hollow":
                    foreach (var c in r["cells"]!.AsArray())
                        if (c!["pos"]!.ToJsonString() == "[0,0,0]")
                            c["hollow"] = true;
                    break;
                case "duplicate-cell": r["cells"]!.AsArray().Add(new JsonObject { ["pos"] = new JsonArray(-3, 0, 0) }); break;
                default: throw new ArgumentException(change);
            }
        });
        Assert.Contains(expected, Assert.Throws<FormatException>(() => RosserRig.Parse(json)).Message);
    }

    [Fact]
    public void A_path_along_z_goes_out_at_the_south()
    {
        // The same rig turned onto z would need south/north ends: west/east is refused there.
        string json = RosserFixture.Edited(r => r["trunkPath"]!["axis"] = "z");
        Assert.Contains("goes in at north", Assert.Throws<FormatException>(() => RosserRig.Parse(json)).Message);
    }

    [Fact]
    public void The_shipped_rig_parses_and_is_drawn_at_the_gameplays_pace()
    {
        // Runs once Rosser/tools/make_shape.py has written assets/seraphhorizons/config/rosser-rig.json.
        var rig = RosserFixture.Shipped();
        if (rig == null)
            return;
        Assert.Equal(RosserRequires.KnownRequires.Order(), rig.MovingParts.Parts.Select(p => p.Requires).OfType<string>().Distinct().Order());
        RosserPaceTests.AssertGearMatchesPace(rig);
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        // Rosser/tools/make_shape.py writes each part's matrix at a grid of poses (θ, ψ, φ, T, k, p)
        // from its own reference maths; RigParts must give the same, so the two cannot drift. As the
        // mill's RigAnimationTests, against tests/Rosser/rig-reference.json.
        var rig = RosserFixture.Shipped();
        var reference = Path.Combine(AppContext.BaseDirectory, "rosser-rig-reference.json");
        Assert.NotNull(rig);
        Assert.True(File.Exists(reference), "no tests/Rosser/rig-reference.json");
        var parts = rig!.MovingParts;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reference));
        int poses = 0;
        var classes = new HashSet<int>();
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double D(string key) => pose.GetProperty(key).GetDouble();
            var input = new RigInput(D("theta"), Travel: D("travel"), Work: D("trunk"), Class: pose.GetProperty("size").GetInt32(),
                                     Presence: D("presence"), Feed: D("feed"));
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
        Assert.True(poses >= 100, $"only {poses} poses");
        // no trunk, thin and thick all posed
        Assert.Equal([0, 1, 2], classes.Order());
    }
}
