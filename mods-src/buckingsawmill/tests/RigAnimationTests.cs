using BuckingSawmill.Core;

namespace BuckingSawmill.Tests;

public class RigAnimationTests
{
    private const float Eps = 1e-4f;

    private static void Near(Float3 expected, Float3 actual, float eps = Eps)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < eps && Math.Abs(expected.Y - actual.Y) < eps && Math.Abs(expected.Z - actual.Z) < eps,
            $"expected {expected}, got {actual}");
    }

    private static string RigWith(string parts) => $$"""
        {
          "cells": [ { "pos": [0, 0, 0] }, { "pos": [1, 0, 0] } ],
          "powerCell": [1, 0, 0], "powerFace": "east",
          "infeedSide": "north", "outputSide": "south",
          "output": { "pos": [0.5, 0.5, 1.25] },
          "trunkBed": { "origin": [1.0, 0.5, 0.5], "axis": "x", "length": 2.0 },
          "parts": {{parts}}
        }
        """;

    private static RigParts Parts(string parts) => Rig.Parse(RigWith(parts)).MovingParts;

    // ---- matching ----

    [Fact]
    public void The_first_matching_part_wins_and_globs_are_case_sensitive()
    {
        var parts = Parts("""
            [ { "id": "a", "match": ["f1_sash_*"] },
              { "id": "b", "match": ["f1_*", "gear"] },
              { "id": "frame", "match": ["*"] } ]
            """);
        Assert.Equal(0, parts.PartOf(["f1_sash_002"]));
        Assert.Equal(1, parts.PartOf(["f1_blade_saw_001"]));
        Assert.Equal(1, parts.PartOf(["gear"]));
        Assert.Equal(2, parts.PartOf(["gears"]));
        Assert.Equal(2, parts.PartOf(["F1_sash_002"]));
    }

    [Fact]
    public void An_ancestor_name_claims_its_children()
    {
        var parts = Parts("""[ { "id": "sash", "match": ["sash*"] }, { "id": "frame", "match": ["*"] } ]""");
        Assert.Equal(0, parts.PartOf(["sash_027", "anything"]));
        Assert.Equal(1, parts.PartOf(["Frame", "anything"]));
        Assert.Equal(-1, Parts("""[ { "id": "only", "match": ["x"] } ]""").PartOf(["y"]));
    }

    [Fact]
    public void Regex_characters_in_globs_are_literal()
    {
        var parts = Parts("""[ { "id": "a", "match": ["sash_019.001"] }, { "id": "frame", "match": ["*"] } ]""");
        Assert.Equal(0, parts.PartOf(["sash_019.001"]));
        Assert.Equal(1, parts.PartOf(["sash_019x001"]));
    }

    // ---- drivers ----

    [Fact]
    public void Rotate_turns_right_handed_about_its_pivot_by_ratio_times_theta()
    {
        var m = Parts("""[ { "id": "r", "match": ["*"], "drivers": [ { "type": "rotate", "axis": "x", "pivot": [0, 1, 1], "ratio": 2 } ] } ]""")
            .Matrices(Math.PI / 4, 0)[0];
        // 2·π/4 = 90° about +x through (y 1, z 1): a point 1 above the axis goes to +z.
        Near(new Float3(5, 1, 2), Mat4.Apply(m, new Float3(5, 2, 1)));
        // y and z rotations, right-handed
        Near(new Float3(0, 0, -1), Mat4.Apply(Mat4.Rotation(Axis.Y, Math.PI / 2, new Float3(0, 0, 0)), new Float3(1, 0, 0)));
        Near(new Float3(0, 1, 0), Mat4.Apply(Mat4.Rotation(Axis.Z, Math.PI / 2, new Float3(0, 0, 0)), new Float3(1, 0, 0)));
    }

    [Fact]
    public void Slide_moves_by_amplitude_times_sine()
    {
        var parts = Parts("""[ { "id": "s", "match": ["*"], "drivers": [ { "type": "slide", "axis": "y", "amplitude": 0.25, "ratio": 1, "phase": -1.5707963 } ] } ]""");
        Near(new Float3(0, -0.25f, 0), Mat4.Apply(parts.Matrices(0, 0)[0], new Float3(0, 0, 0)));
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(Math.PI / 2, 0)[0], new Float3(0, 0, 0)));
        Near(new Float3(0, 0.25f, 0), Mat4.Apply(parts.Matrices(Math.PI, 0)[0], new Float3(0, 0, 0)));
    }

    [Fact]
    public void Swing_oscillates_about_its_pivot()
    {
        var parts = Parts("""[ { "id": "w", "match": ["*"], "drivers": [ { "type": "swing", "axis": "z", "pivot": [0, 0, 0], "amplitude": 1.5707963, "ratio": 1, "phase": 0 } ] } ]""");
        Near(new Float3(1, 0, 0), Mat4.Apply(parts.Matrices(0, 0)[0], new Float3(1, 0, 0)));
        Near(new Float3(0, 1, 0), Mat4.Apply(parts.Matrices(Math.PI / 2, 0)[0], new Float3(1, 0, 0)));
        Near(new Float3(0, -1, 0), Mat4.Apply(parts.Matrices(-Math.PI / 2, 0)[0], new Float3(1, 0, 0)));
    }

    [Fact]
    public void Feed_follows_the_cut_and_ignores_the_shaft()
    {
        var parts = Parts("""[ { "id": "f", "match": ["*"], "drivers": [ { "type": "feed", "axis": "z", "travel": 2 } ] } ]""");
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(1.3, 0)[0], new Float3(0, 0, 0)));
        Near(new Float3(0, 0, 1.5f), Mat4.Apply(parts.Matrices(4.0, 0.75)[0], new Float3(0, 0, 0)));
    }

    [Fact]
    public void Drivers_apply_in_list_order()
    {
        // rotate 90° about the origin, then slide +x: (1,0,0) -> (0,0,-1) -> (1,0,-1)
        var parts = Parts("""
            [ { "id": "p", "match": ["*"], "drivers": [
                { "type": "rotate", "axis": "y", "pivot": [0, 0, 0], "ratio": 1 },
                { "type": "slide", "axis": "x", "amplitude": 1, "ratio": 1, "phase": 0 } ] } ]
            """);
        Near(new Float3(1, 0, -1), Mat4.Apply(parts.Matrices(Math.PI / 2, 0)[0], new Float3(1, 0, 0)));
    }

    [Fact]
    public void A_rider_gets_its_own_drivers_then_its_parents_transform()
    {
        // listed before its parent on purpose: order of evaluation must not depend on file order
        var parts = Parts("""
            [ { "id": "blade", "match": ["b*"], "ride": "sash", "drivers": [ { "type": "feed", "axis": "z", "travel": 1 } ] },
              { "id": "sash", "match": ["s*"], "drivers": [ { "type": "slide", "axis": "y", "amplitude": 0.5, "ratio": 1, "phase": 0 } ] },
              { "id": "frame", "match": ["*"] } ]
            """);
        var mats = parts.Matrices(Math.PI / 2, 0.5);
        Near(new Float3(0, 0.5f, 0.5f), Mat4.Apply(mats[0], new Float3(0, 0, 0)));
        Near(new Float3(0, 0.5f, 0), Mat4.Apply(mats[1], new Float3(0, 0, 0)));
        Near(new Float3(3, 4, 5), Mat4.Apply(mats[2], new Float3(3, 4, 5)));
    }

    [Fact]
    public void A_ride_cycle_is_an_error()
    {
        var e = Assert.Throws<FormatException>(() => Parts("""
            [ { "id": "a", "match": ["a"], "ride": "b" }, { "id": "b", "match": ["b"], "ride": "a" } ]
            """));
        Assert.Contains("cycle", e.Message);
        Assert.Throws<FormatException>(() => Parts("""[ { "id": "a", "match": ["a"], "ride": "a" } ]"""));
    }

    [Theory]
    [InlineData("""[ { "id": "a", "match": ["a"], "ride": "nope" } ]""", "not a part")]
    [InlineData("""[ { "id": "a", "match": ["a"], "requires": "levers" } ]""", "levers")]
    [InlineData("""[ { "id": "a", "match": ["a"] }, { "id": "a", "match": ["b"] } ]""", "twice")]
    [InlineData("""[ { "id": "a", "match": [] } ]""", "match")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "spin", "axis": "x" } ] } ]""", "spin")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "rotate", "axis": "x" } ] } ]""", "pivot")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "feed", "axis": "w", "travel": 1 } ] } ]""", "axis")]
    public void Broken_parts_are_reported(string parts, string expected)
    {
        var e = Assert.Throws<FormatException>(() => Parts(parts));
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Requires_is_fitted_by_the_parts_count()
    {
        Assert.True(RigPart.Fitted(null, 0, false, 0));
        Assert.False(RigPart.Fitted("crankshaft", 2, false, 2));
        Assert.True(RigPart.Fitted("crankshaft", 0, true, 0));
        Assert.True(RigPart.Fitted("sash1", 1, false, 0));
        Assert.False(RigPart.Fitted("sash2", 1, false, 0));
        Assert.True(RigPart.Fitted("blade2", 2, false, 2));
        Assert.False(RigPart.Fitted("blade1", 2, true, 0));
        Assert.False(RigPart.Fitted("levers", 2, true, 2));
    }

    // ---- facing, shaft angle, strokes, trunk ----

    [Theory]
    [InlineData(Side.South)]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.West)]
    public void The_facing_matrix_turns_points_as_the_footprint_does(Side side)
    {
        var m = Mat4.Facing(side);
        foreach (var p in new[] { new Float3(0, 0, 0), new Float3(5.5f, 3.5f, 1.5f), new Float3(3, 0.6f, 3.25f) })
            Near(Footprint.ToWorld(p, side), Mat4.Apply(m, p));
    }

    [Fact]
    public void The_native_shaft_follows_the_vanilla_axle_on_every_facing()
    {
        // A vanilla axle along world x or z draws as a right-handed turn of -AngleRad about that
        // axis. Turn a native point about native +x by θ, carry it to the world, and compare with
        // the axle's turn about the world axis native +x maps onto.
        const double angle = 0.7;
        foreach (var side in Sides.All)
        {
            double theta = MillMotion.NativeShaftAngle(side, angle);
            var facing = Mat4.Facing(side);
            var pivot = new Float3(0, 3.5f, 1.5f);
            var p = new Float3(0, 3.7f, 1.5f);
            var viaNative = Mat4.Apply(facing, Mat4.Apply(Mat4.Rotation(Axis.X, theta, pivot), p));
            var axis = Footprint.ToWorld(new Int3(1, 0, 0), side);
            var worldAxis = axis.X != 0 ? Axis.X : Axis.Z;
            var worldPivot = Mat4.Apply(facing, pivot);
            var viaAxle = Mat4.Apply(Mat4.Rotation(worldAxis, -angle, worldPivot), Mat4.Apply(facing, p));
            Near(viaAxle, viaNative);
        }
    }

    [Fact]
    public void Wrapped_delta_takes_the_short_way_round()
    {
        Assert.Equal(0.2, MillMotion.WrappedDelta(6.2, 6.4 - 2 * Math.PI + 2 * Math.PI), 6);
        Assert.Equal(0.2, MillMotion.WrappedDelta(2 * Math.PI - 0.1, 0.1), 6);
        Assert.Equal(-0.2, MillMotion.WrappedDelta(0.1, 2 * Math.PI - 0.1), 6);
    }

    [Fact]
    public void Strokes_are_counted_per_half_turn_either_way()
    {
        Assert.Equal(0, MillMotion.StrokesBetween(0.1, 3.0));
        Assert.Equal(1, MillMotion.StrokesBetween(3.0, 3.3));
        Assert.Equal(2, MillMotion.StrokesBetween(0.1, 6.5));
        Assert.Equal(1, MillMotion.StrokesBetween(0.1, -0.1));
        Assert.Equal('s', MillMotion.SpeedBand(0.2f));
        Assert.Equal('m', MillMotion.SpeedBand(0.5f));
        Assert.Equal('f', MillMotion.SpeedBand(0.9f));
    }

    [Fact]
    public void A_trunk_is_centred_on_the_bed_and_turned_onto_its_axis()
    {
        var bed = new TrunkBed(new Float3(3, 0.5f, 1.5f), Axis.X, 5);
        // An xxl trunk as Logging Expanded tessellates it facing north: 2 wide, 2 tall, 5 long in z,
        // offset from its controller block.
        var m = MillMotion.TrunkPlacement(new Float3(-1, 0, -2), new Float3(1, 2, 3), bed);
        var a = Mat4.Apply(m, new Float3(-1, 0, -2));
        var b = Mat4.Apply(m, new Float3(1, 2, 3));
        Near(new Float3(0.5f, 0.5f, 0.5f), new Float3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
        Near(new Float3(5.5f, 2.5f, 2.5f), new Float3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
        // Already along x: only moved.
        var n = MillMotion.TrunkPlacement(new Float3(0, 0, 0), new Float3(1, 1, 1), bed);
        Near(new Float3(2.5f, 0.5f, 1), Mat4.Apply(n, new Float3(0, 0, 0)));
    }

    // ---- the shipped rig ----

    private static Rig Shipped() => Rig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rig.json")));

    [Fact]
    public void The_shipped_rig_has_consistent_parts()
    {
        var rig = Shipped();
        var parts = rig.MovingParts.Parts;
        Assert.NotEmpty(parts);
        Assert.Equal(["*"], parts[^1].Match);
        Assert.Null(parts[^1].Requires);
        foreach (var p in parts)
        {
            if (p.Ride != null)
                Assert.Contains(parts, q => q.Id == p.Ride);
            Assert.True(p.Requires == null || RigPart.KnownRequires.Contains(p.Requires), $"{p.Id} requires {p.Requires}");
        }
        Assert.NotNull(rig.TrunkBed);
        Assert.Equal(Axis.X, rig.TrunkBed!.Axis);
        // Every part the gameplay can fit has something to show.
        foreach (var req in RigPart.KnownRequires)
            Assert.Contains(parts, p => p.Requires == req);
    }

    [Fact]
    public void The_shipped_rods_stay_on_their_crank_pins()
    {
        var parts = Shipped().MovingParts;
        int shaft = parts.IndexOf("shaft");
        foreach (var (rodId, frameX, pinSign) in new[] { ("f1_rod", 2f, -1f), ("f2_rod", 4f, 1f) })
        {
            int rod = parts.IndexOf(rodId);
            for (int i = 0; i < 64; i++)
            {
                var mats = parts.Matrices(i * Math.PI / 32, 0);
                var top = Mat4.Apply(mats[rod], new Float3(frameX, 3.5f, 1.5f));
                var pin = Mat4.Apply(mats[shaft], new Float3(frameX, 3.5f + pinSign * 2.5f / 16, 1.5f));
                float gap = MathF.Sqrt((top.X - pin.X) * (top.X - pin.X) + (top.Y - pin.Y) * (top.Y - pin.Y) + (top.Z - pin.Z) * (top.Z - pin.Z));
                Assert.True(gap < 0.75f / 16, $"{rodId} at step {i}: {gap * 16:0.00} voxels off its pin");
            }
        }
    }
}
