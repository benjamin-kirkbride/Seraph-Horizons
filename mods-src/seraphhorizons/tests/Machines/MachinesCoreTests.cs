using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The machines' shared Core beyond the drivers (those are <see cref="DriverFixtureTests"/>):
/// the shaft's sign on every facing, the trunk path, hollow cells and the trunk's box on a path.</summary>
public class MachinesCoreTests
{
    private const float Eps = 1e-4f;

    private static void Near(Float3 expected, Float3 actual, float eps = Eps) =>
        Assert.True(Math.Abs(expected.X - actual.X) < eps && Math.Abs(expected.Y - actual.Y) < eps && Math.Abs(expected.Z - actual.Z) < eps,
            $"expected {expected}, got {actual}");

    // ---- the shaft's sign ----

    [Theory]
    [InlineData(Side.South, -1)]
    [InlineData(Side.West, -1)]
    [InlineData(Side.North, 1)]
    [InlineData(Side.East, 1)]
    public void A_shaft_along_x_keeps_the_mills_rule(Side facing, int sign)
    {
        Assert.Equal(sign * 0.7, MillMotion.NativeShaftAngle(facing, 0.7));
        Assert.Equal(sign * 0.7, MillMotion.NativeShaftAngle(facing, 0.7, Axis.X));
    }

    [Theory]
    [InlineData(Side.South, -1)]
    [InlineData(Side.East, -1)]
    [InlineData(Side.North, 1)]
    [InlineData(Side.West, 1)]
    public void A_shaft_along_z_follows_the_vanilla_axle_on_every_facing(Side facing, int sign)
    {
        // As the mill's test along x: turn a native point about native +z by θ, carry it to the
        // world, and compare with the vanilla axle's turn of −AngleRad about the world axis native
        // +z maps onto.
        const double angle = 0.7;
        double theta = MillMotion.NativeShaftAngle(facing, angle, Axis.Z);
        Assert.Equal(sign * angle, theta);
        var m = Mat4.Facing(facing);
        var pivot = new Float3(-8.5f, 3.5f, 0);
        var p = new Float3(-8.5f, 3.9f, 0.3f);
        var viaNative = Mat4.Apply(m, Mat4.Apply(Mat4.Rotation(Axis.Z, theta, pivot), p));
        var axis = Footprint.ToWorld(new Int3(0, 0, 1), facing);
        var worldAxis = axis.X != 0 ? Axis.X : Axis.Z;
        var viaAxle = Mat4.Apply(Mat4.Rotation(worldAxis, -angle, Mat4.Apply(m, pivot)), Mat4.Apply(m, p));
        Near(viaAxle, viaNative);
    }

    [Fact]
    public void A_vertical_shaft_has_no_sign()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MillMotion.NativeShaftAngle(Side.South, 1, Axis.Y));
    }

    // ---- the trunk path ----

    private static TrunkPath Path(string json = """
        { "origin": [-15, 1.6875, 0.5], "axis": "x", "length": 16, "nose0": -10, "lengths": { "thin": 4, "thick": 5 },
          "tailStop": 0, "radius": { "thin": [0.47, 0.56], "thick": [0.94, 1.16] }, "stations": { "ring": -6.5, "treadle": -12 } }
        """)
    {
        using var doc = JsonDocument.Parse(json);
        return TrunkPath.Parse(doc.RootElement);
    }

    [Fact]
    public void The_path_gives_nose_tail_and_the_end_of_the_trip()
    {
        var path = Path();
        Assert.Equal(Axis.X, path.Axis);
        Assert.Equal(-7.5, path.Nose(2.5));
        Assert.Equal(-11.5, path.Tail(2.5, 1));
        Assert.Equal(-12.5, path.Tail(2.5, 2));
        Assert.Equal(-7.5, path.Tail(2.5, 0));
        // T_end(k) = tailStop + L_k − nose0: the tail has reached tailStop.
        Assert.Equal(14, path.End(1));
        Assert.Equal(15, path.End(2));
        Assert.Equal(0, path.Tail(path.End(2), 2));
        Assert.Equal(-6.5f, path.Stations["ring"]);
        Assert.Equal((0.94f, 1.16f), path.Radii[2]);
        Assert.Equal(16, path.Length);
    }

    [Theory]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "y", "nose0": 0, "lengths": { "thin": 4, "thick": 5 }, "tailStop": 3 }""", "axis")]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "x", "nose0": 0, "lengths": { "thin": 4 }, "tailStop": 3 }""", "thick")]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "x", "nose0": 0, "lengths": { "thin": 0, "thick": 5 }, "tailStop": 3 }""", "lengths")]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "x", "lengths": { "thin": 4, "thick": 5 }, "tailStop": 3 }""", "nose0")]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "x", "nose0": 0, "lengths": { "thin": 4, "thick": 5 } }""", "tailStop")]
    [InlineData("""{ "axis": "x", "nose0": 0, "lengths": { "thin": 4, "thick": 5 }, "tailStop": 3 }""", "origin")]
    [InlineData("""{ "origin": [0, 1, 0], "axis": "x", "nose0": 0, "lengths": { "thin": 4, "thick": 5 }, "tailStop": 3, "stations": { "ring": "x" } }""", "ring")]
    public void A_broken_path_is_reported(string json, string expected)
    {
        var e = Assert.Throws<FormatException>(() => Path(json));
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Rolls_and_occupy_gauges_need_a_path_and_present_gauges_do_not()
    {
        static RigParts Parse(string parts, TrunkPath? path)
        {
            using var doc = JsonDocument.Parse(parts);
            return RigParts.Parse(doc.RootElement, new HashSet<string>(), path);
        }
        const string roll = """[ { "id": "r", "match": ["*"], "drivers": [ { "type": "roll", "axis": "z", "pivot": [0, 1, 0], "at": 1, "ratio": 2 } ] } ]""";
        const string occupy = """[ { "id": "g", "match": ["*"], "drivers": [ { "type": "gauge", "motion": "slide", "axis": "y", "amount": { "thin": 1, "thick": 2 }, "windows": [ { "from": 0, "to": 1, "ease": 0.5 } ] } ] } ]""";
        const string present = """[ { "id": "g", "match": ["*"], "drivers": [ { "type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": { "thin": 1, "thick": 2 } } ] } ]""";
        Assert.Contains("trunkPath", Assert.Throws<FormatException>(() => Parse(roll, null)).Message);
        Assert.Contains("trunkPath", Assert.Throws<FormatException>(() => Parse(occupy, null)).Message);
        var parts = Parse(present, null);
        Near(new Float3(0, 0.5f, 0), Mat4.Apply(parts.Matrices(new RigInput(0, Class: 2, Presence: 0.25))[0], new Float3(0, 0, 0)));
    }

    [Theory]
    [InlineData("feed")]
    [InlineData("step")]
    [InlineData("gauge")]
    [InlineData("roll")]
    public void Input_is_only_for_rotate_slide_and_swing(string type)
    {
        string driver = type switch
        {
            "feed" => """{ "type": "feed", "axis": "y", "travel": 1, "input": "trunk" }""",
            "step" => """{ "type": "step", "motion": "slide", "axis": "y", "amount": 1, "rectified": true }""",
            "gauge" => """{ "type": "gauge", "motion": "slide", "axis": "y", "mode": "present", "amount": { "thin": 1, "thick": 2 }, "input": "feed" }""",
            _ => """{ "type": "roll", "axis": "z", "pivot": [0, 1, 0], "at": 1, "ratio": 2, "input": "trunk" }""",
        };
        using var doc = JsonDocument.Parse($$"""[ { "id": "p", "match": ["*"], "drivers": [ {{driver}} ] } ]""");
        var e = Assert.Throws<FormatException>(() => RigParts.Parse(doc.RootElement, new HashSet<string>(), Path()));
        Assert.Contains("input", e.Message);
    }

    // ---- cells ----

    [Fact]
    public void A_hollow_cell_is_read_and_cannot_have_boxes()
    {
        using var ok = JsonDocument.Parse("""{ "cells": [ { "pos": [0, 0, 0] }, { "pos": [-1, 1, 0], "hollow": true }, { "pos": [-1, 0, 0], "boxes": [[0, 0, 0, 1, 0.5, 1]] } ] }""");
        var cells = RigJson.Cells(ok.RootElement);
        Assert.Equal([false, true, false], cells.Select(c => c.Hollow));
        Assert.Empty(cells[1].Boxes);
        Assert.Single(cells[2].Boxes);
        using var bad = JsonDocument.Parse("""{ "cells": [ { "pos": [0, 0, 0], "hollow": true, "boxes": [[0, 0, 0, 1, 1, 1]] } ] }""");
        Assert.Contains("hollow", Assert.Throws<FormatException>(() => RigJson.Cells(bad.RootElement)).Message);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1.5", false)]
    [InlineData("\"high\"", false)]
    [InlineData("0.0625", true)]
    [InlineData("1", true)]
    public void A_lid_is_a_height_from_its_thickness_to_the_cells_top(string lid, bool valid)
    {
        using var doc = JsonDocument.Parse($$"""{ "cells": [ { "pos": [0, 0, 0], "hollow": true, "lid": {{lid}} } ] }""");
        if (!valid)
        {
            Assert.Contains("lid", Assert.Throws<FormatException>(() => RigJson.Cells(doc.RootElement)).Message);
            return;
        }
        var cell = Assert.Single(RigJson.Cells(doc.RootElement));
        Assert.True(cell.Hollow);
        Assert.Equal(new Box(0, cell.Lid!.Value - 1 / 16f, 0, 1, cell.Lid.Value, 1), cell.LidBox);
    }

    [Fact]
    public void A_cell_without_a_lid_has_no_lid_box()
    {
        using var doc = JsonDocument.Parse("""{ "cells": [ { "pos": [0, 0, 0], "boxes": [[0, 0, 0, 1, 0.5, 1]] } ] }""");
        Assert.Null(Assert.Single(RigJson.Cells(doc.RootElement)).LidBox);
    }

    [Fact]
    public void A_cell_under_a_hollow_cell_reaches_into_it_and_the_hollow_cell_holds_its_own_part()
    {
        // A thick trunk 2 tall from y 0.5 over a column of a solid cell under a hollow one, and the
        // same column with both solid.
        var trunk = (new Float3(0, 0.5f, 0), new Float3(1, 2.5f, 1));
        var hollow = TrunkBox.CellBoxes([new RigCell(new Int3(0, 0, 0), []), new RigCell(new Int3(0, 1, 0), [], Hollow: true)], trunk);
        Assert.Equal(new Box(0, 0.5f, 0, 1, 2, 1), hollow[new Int3(0, 0, 0)]);
        Assert.Equal(new Box(0, 0, 0, 1, 1.5f, 1), hollow[new Int3(0, 1, 0)]);
        var solid = TrunkBox.CellBoxes([new RigCell(new Int3(0, 0, 0), []), new RigCell(new Int3(0, 1, 0), [])], trunk);
        Assert.Equal(new Box(0, 0.5f, 0, 1, 1, 1), solid[new Int3(0, 0, 0)]);
        // With nothing hollow the rig-cell overload is the mill's.
        Assert.Equal(TrunkBox.CellBoxes([new Int3(0, 0, 0), new Int3(0, 1, 0)], trunk), solid);
    }

    // ---- the trunk on its path ----

    [Fact]
    public void The_trunks_box_runs_from_tail_to_nose_centred_on_the_axis()
    {
        var path = Path();
        var (min, max) = TrunkBox.BoundsOnPath(path, TrunkClass.Thick, 2.5f);
        Near(new Float3(-12.5f, 0.6875f, -0.5f), min);
        Near(new Float3(-7.5f, 2.6875f, 1.5f), max);
        (min, max) = TrunkBox.BoundsOnPath(path, TrunkClass.Thin, 0);
        Near(new Float3(-14, 1.1875f, 0), min);
        Near(new Float3(-10, 2.1875f, 1), max);
        (min, max) = TrunkBox.BoundsOnPath(path, TrunkClass.None, 3);
        Assert.Equal(min, max);
    }

    [Fact]
    public void A_trunk_mesh_is_laid_on_the_path_centred_between_tail_and_nose()
    {
        var path = Path();
        // An xxl trunk as Logging Expanded tessellates it facing north: 2 wide, 2 tall, 5 long in z.
        var m = MillMotion.TrunkOnAxis(new Float3(-1, 0, -2), new Float3(1, 2, 3), path, 2, 2.5);
        var a = Mat4.Apply(m, new Float3(-1, 0, -2));
        var b = Mat4.Apply(m, new Float3(1, 2, 3));
        var (min, max) = TrunkBox.BoundsOnPath(path, TrunkClass.Thick, 2.5f);
        Near(min, new Float3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
        Near(max, new Float3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
    }
}
