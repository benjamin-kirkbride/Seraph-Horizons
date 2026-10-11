using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>
/// Holds <see cref="RigParts"/> and <see cref="Driver"/> to the reference driver maths
/// (Machines/tools/machinegen/rigmath.py) through tests/Machines/driver-fixture.json, written by
/// Machines/tools/make_fixture.py: every driver alone at a grid of inputs, a small rig that uses
/// all of them with ride chains, and drivers every parser must reject; then the same with the
/// generic progress, a rig's <c>work</c>, in place of the trunkPath. The site's rig.ts replays the
/// same file.
/// </summary>
public class DriverFixtureTests
{

    private static JsonDocument Fixture() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "driver-fixture.json")));

    private static TrunkPath PathOf(JsonElement root) => TrunkPath.Parse(root.GetProperty("trunkPath"));

    private static RigInput InputOf(JsonElement inputs)
    {
        double D(string key) => inputs.GetProperty(key).GetDouble();
        var travel = inputs.GetProperty("travel");
        return new RigInput(D("theta"), D("depth"), D("lifting"),
                            travel.ValueKind == JsonValueKind.Null ? null : travel.GetDouble(),
                            D("work"), inputs.GetProperty("size").GetInt32(), D("presence"), D("feed"), D("oil"));
    }

    /// <summary>One driver as the only part of a rig, parsed as a rig's parts are.</summary>
    private static RigParts Alone(JsonElement driver, IWorkProgress path)
    {
        string json = $$"""[ { "id": "d", "match": ["*"], "drivers": [ {{driver.GetRawText()}} ] } ]""";
        using var doc = JsonDocument.Parse(json);
        return RigParts.Parse(doc.RootElement, new HashSet<string>(), path);
    }

    /// <summary>The fixture's own tolerance (1e-6): float32 matrices meet it.</summary>
    private static double ToleranceOf(JsonElement root) => root.GetProperty("tolerance").GetDouble();

    private static void AssertMatrix(JsonElement expected, float[] got, double tolerance, string what)
    {
        var rows = expected.EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Assert.Equal(4, rows.Length);
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                double g = got[col * 4 + row];
                Assert.True(Math.Abs(g - rows[row][col]) <= tolerance,
                    $"{what}: [{row},{col}] is {g}, the reference says {rows[row][col]}");
            }
    }

    [Fact]
    public void The_fixture_is_the_format_this_test_reads()
    {
        using var doc = Fixture();
        Assert.Equal(2, doc.RootElement.GetProperty("format").GetInt32());
        Assert.InRange(ToleranceOf(doc.RootElement), 0, 1e-6);
    }

    [Fact]
    public void Every_driver_alone_matches_the_reference()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        var path = PathOf(root);
        int cases = 0;
        var types = new HashSet<DriverType>();
        foreach (var entry in root.GetProperty("drivers").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            var parts = Alone(entry.GetProperty("driver"), path);
            types.Add(parts.Parts[0].Drivers[0].Type);
            foreach (var c in entry.GetProperty("cases").EnumerateArray())
            {
                var input = InputOf(c.GetProperty("inputs"));
                AssertMatrix(c.GetProperty("matrix"), parts.Matrices(input)[0], ToleranceOf(root), $"{id} at {input}");
                cases++;
            }
        }
        Assert.Equal(Enum.GetValues<DriverType>().ToHashSet(), types);
        Assert.True(cases >= 100, $"only {cases} cases");
    }

    [Fact]
    public void The_rig_matches_the_reference_with_its_ride_chains()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        var rig = root.GetProperty("rig");
        var partsJson = rig.GetProperty("parts");
        var requires = partsJson.EnumerateArray()
            .Select(p => p.TryGetProperty("requires", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : null)
            .OfType<string>().ToHashSet();
        var parts = RigParts.Parse(partsJson, requires, PathOf(root));
        int poses = 0;
        foreach (var pose in rig.GetProperty("poses").EnumerateArray())
        {
            var input = InputOf(pose.GetProperty("inputs"));
            var mats = parts.Matrices(input);
            var expected = pose.GetProperty("matrices");
            Assert.Equal(parts.Parts.Count, expected.EnumerateObject().Count());
            for (int i = 0; i < parts.Parts.Count; i++)
                AssertMatrix(expected.GetProperty(parts.Parts[i].Id), mats[i], ToleranceOf(root), $"{parts.Parts[i].Id} at {input}");
            poses++;
        }
        Assert.True(poses >= 30, $"only {poses} poses");
    }

    [Fact]
    public void Every_invalid_driver_is_rejected()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        var path = PathOf(root);
        int n = 0;
        foreach (var entry in root.GetProperty("invalid").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            var e = Record.Exception(() => Alone(entry.GetProperty("driver"), path));
            Assert.True(e is FormatException, $"{id} ({entry.GetProperty("error").GetString()}) was {(e == null ? "accepted" : "rejected with " + e.GetType().Name)}");
            n++;
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void Every_driver_matches_the_reference_on_a_work_quantity()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(root.GetProperty("work"));
        Assert.Equal(6, work.End(1), 6);
        Assert.Equal(9, work.End(2), 6);
        Assert.Equal(4, work.End(3), 6);
        Assert.Equal(0, work.End(0));
        Assert.Equal(0, work.End(4));
        Assert.Equal(0, work.End(RigClasses.Max + 1));
        int cases = 0;
        foreach (var entry in root.GetProperty("workDrivers").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            var parts = Alone(entry.GetProperty("driver"), work);
            foreach (var c in entry.GetProperty("cases").EnumerateArray())
            {
                var input = InputOf(c.GetProperty("inputs"));
                AssertMatrix(c.GetProperty("matrix"), parts.Matrices(input)[0], ToleranceOf(root), $"{id} at {input}");
                cases++;
            }
        }
        Assert.True(cases >= 100, $"only {cases} cases");
        foreach (var entry in root.GetProperty("workInvalid").EnumerateArray())
        {
            var e = Record.Exception(() => Alone(entry.GetProperty("driver"), work));
            Assert.True(e is FormatException, $"{entry.GetProperty("id").GetString()} was not rejected with a work quantity");
        }
    }

    /// <summary>A stretched box (a coil's side grown from its corner, a rod shrunk to nothing, a box riding a
    /// stretched part) lands where the reference's posed box does: the part's matrix applied to the element's
    /// authored corners, as the renderer draws it.</summary>
    [Fact]
    public void Stretched_boxes_land_on_the_reference_corners()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(root.GetProperty("work"));
        int n = 0, vanished = 0;
        foreach (var b in root.GetProperty("stretchBoxes").EnumerateArray())
        {
            string id = b.GetProperty("id").GetString()!;
            var parts = RigParts.Parse(b.GetProperty("parts"), new HashSet<string>(), work);
            var m = parts.Matrices(InputOf(b.GetProperty("inputs")))[parts.IndexOf(b.GetProperty("part").GetString()!)];
            static Float3 P(JsonElement c) => new(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
            var authored = b.GetProperty("authored").EnumerateArray().Select(P).ToArray();
            var want = b.GetProperty("corners").EnumerateArray().Select(P).ToArray();
            Assert.Equal(8, authored.Length);
            for (int i = 0; i < 8; i++)
            {
                var got = Mat4.Apply(m, new Float3(authored[i].X / 16, authored[i].Y / 16, authored[i].Z / 16));
                Assert.True(Math.Abs(got.X * 16 - want[i].X) < 1e-4 && Math.Abs(got.Y * 16 - want[i].Y) < 1e-4 && Math.Abs(got.Z * 16 - want[i].Z) < 1e-4,
                    $"{id}: corner {i} lands at {got.X * 16}, {got.Y * 16}, {got.Z * 16}, the reference says {want[i]}");
            }
            bool point = want.All(c => c == want[0]);
            Assert.Equal(point, Mat4.Collapsed(m));
            vanished += point ? 1 : 0;
            n++;
        }
        Assert.True(n >= 6 && vanished >= 2, $"{n} boxes, {vanished} vanished");
    }

    [Fact]
    public void A_rider_does_not_inherit_its_parents_stretch()
    {
        using var doc = JsonDocument.Parse("""
            [ { "id": "a", "match": ["a"], "drivers": [
                  { "type": "slide", "axis": "x", "amplitude": 0.5, "ratio": 1, "input": "work" },
                  { "type": "gauge", "motion": "stretch", "axis": "z", "origin": [0, 0, 0.5], "rotation": [0, 30, 0], "mode": "present", "amount": { "3": -0.5 } } ] },
              { "id": "b", "match": ["b"], "ride": "a", "drivers": [] } ]
            """);
        var parts = RigParts.Parse(doc.RootElement, new HashSet<string>(), null);
        var input = new RigInput(0, Work: 1.25, Class: 3, Presence: 1);
        var mats = parts.Matrices(input);
        var slide = parts.Parts[0].Drivers[0].Matrix(input, null);
        Assert.Equal(slide, mats[1]);
        Assert.NotEqual(slide, mats[0]);
        Assert.True(parts.Parts[0].Drivers[1].IsScale);
        Assert.Equal(0.5, parts.Parts[0].Drivers[1].StretchFactor(input, null), 6);
    }

    [Fact]
    public void Every_bad_progress_is_rejected()
    {
        using var doc = Fixture();
        int n = 0;
        foreach (var entry in doc.RootElement.GetProperty("badProgress").EnumerateArray())
        {
            var e = Record.Exception(() => RigProgress.Of(entry.GetProperty("rig")));
            Assert.True(e is FormatException, $"{entry.GetProperty("id").GetString()} ({entry.GetProperty("error").GetString()}) was accepted");
            n++;
        }
        Assert.True(n > 0);
        using var ok = JsonDocument.Parse("""{ "work": { "unit": "teeth", "end": { "thin": 12, "thick": 20 } } }""");
        Assert.IsType<WorkQuantity>(RigProgress.Of(ok.RootElement));
        Assert.Null(RigProgress.Of(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void The_mill_spells_travel_as_rectified()

    {
        using var doc = Fixture();
        var path = PathOf(doc.RootElement);
        using var a = JsonDocument.Parse("""{ "type": "rotate", "axis": "x", "pivot": [0, 1, 1], "ratio": 2, "rectified": true }""");
        using var b = JsonDocument.Parse("""{ "type": "rotate", "axis": "x", "pivot": [0, 1, 1], "ratio": 2, "input": "travel" }""");
        var da = Alone(a.RootElement, path).Parts[0].Drivers[0];
        var db = Alone(b.RootElement, path).Parts[0].Drivers[0];
        Assert.Equal(DriverInput.Travel, da.Input);
        Assert.True(da.Rectified);
        var input = new RigInput(-1.2, Travel: 7.5);
        Assert.Equal(db.Matrix(input, path), da.Matrix(input, path));
        Assert.Equal(da.Matrix(-1.2, 0, 0, 7.5), da.Matrix(input, null));
    }
}
