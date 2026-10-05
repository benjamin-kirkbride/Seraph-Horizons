using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>
/// Holds <see cref="RigParts"/> and <see cref="Driver"/> to the reference driver maths
/// (Machines/tools/machinegen/rigmath.py) through tests/Machines/driver-fixture.json, written by
/// Machines/tools/make_fixture.py: every driver alone at a grid of inputs, a small rig that uses
/// all of them with ride chains, and drivers every parser must reject. The site's rig.ts replays
/// the same file.
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
                            D("trunk"), inputs.GetProperty("size").GetInt32(), D("presence"), D("feed"));
    }

    /// <summary>One driver as the only part of a rig, parsed as a rig's parts are.</summary>
    private static RigParts Alone(JsonElement driver, TrunkPath path)
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
        Assert.Equal(1, doc.RootElement.GetProperty("format").GetInt32());
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
