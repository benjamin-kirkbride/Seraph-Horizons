using SeraphHorizons.Mod.AcidParting.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests.Machines;

/// <summary>A rig's named points (<see cref="RigPoints"/>): every top-level object with a <c>pos</c>, read by the
/// generic rigs, and every shipped rig's parsing.</summary>
public class RigPointsTests
{
    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    [Fact]
    public void Every_top_level_object_with_a_pos_is_a_point_and_nothing_else_is()
    {
        var points = RigPoints.Read("""
            {
              "spray": { "pos": [1.5, 0.25, 2] },
              "fireDoor": { "pos": [0.5, 1.0625, 0], "note": "the stoking hole" },
              "infeedCell": [0, 0, 0], "infeedFace": "up",
              "cut": { "turnsPerTooth": 2 },
              "parts": [ { "pos": [9, 9, 9] } ],
              "nested": { "inner": { "pos": [9, 9, 9] } }
            }
            """);
        Assert.Equal(["spray", "fireDoor"], points.Keys);
        Assert.Equal(new Float3(1.5f, 0.25f, 2f), points["spray"]);
        Assert.Equal(new Float3(0.5f, 1.0625f, 0f), points["fireDoor"]);
    }

    [Fact]
    public void Points_match_in_any_case()
    {
        var points = RigPoints.Read("""{ "firedoor": { "pos": [1, 2, 3] } }""");
        Assert.Equal(new Float3(1, 2, 3), points["fireDoor"]);
    }

    [Theory]
    [InlineData("""{ "spray": { "pos": [1, 2] } }""")]
    [InlineData("""{ "spray": { "pos": [1, "two", 3] } }""")]
    [InlineData("""{ "spray": { "pos": 1 } }""")]
    public void A_malformed_pos_says_so(string json)
    {
        var e = Assert.Throws<FormatException>(() => RigPoints.Read(json));
        Assert.Contains("spray.pos", e.Message);
    }

    [Fact]
    public void The_ore_machine_rig_carries_its_points()
    {
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("rigs/amalgamationplates-rig.json")), ["dressing", "amalgam1", "amalgam2", "amalgam3"]);
        Assert.NotNull(rig.Point("spray"));
        Assert.NotNull(rig.Point("feedSpout"));
        Assert.Null(rig.Point("nowhere"));
        // the acid parting vessel's firedoor, as the others spell it
        Assert.NotNull(OreMachineRig.Parse(File.ReadAllText(Out("rigs/acidparting-rig.json")), VesselRequires.Extra).Point("fireDoor"));
    }

    public static TheoryData<string> ShippedRigs() =>
        [.. Directory.GetFiles(Out("rigs"), "*-rig.json").Select(Path.GetFileName).Order()!];

    [Theory]
    [MemberData(nameof(ShippedRigs))]
    public void Every_shipped_rigs_points_parse(string file)
    {
        var points = RigPoints.Read(File.ReadAllText(Out("rigs/" + file)));
        Assert.All(points.Values, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
    }
}
