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
    public void Feed_follows_the_depth_and_ignores_the_shaft()
    {
        var parts = Parts("""[ { "id": "f", "match": ["*"], "drivers": [ { "type": "feed", "axis": "z", "travel": 2 } ] } ]""");
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(1.3, 0)[0], new Float3(0, 0, 0)));
        Near(new Float3(0, 0, 1.5f), Mat4.Apply(parts.Matrices(4.0, 0.75)[0], new Float3(0, 0, 0)));
    }

    [Fact]
    public void Step_slides_through_its_depth_window()
    {
        var parts = Parts("""[ { "id": "s", "match": ["*"], "drivers": [ { "type": "step", "motion": "slide", "axis": "x", "amount": 2, "from": 0.5, "to": 0.75 } ] } ]""");
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(3.0, 0.4)[0], new Float3(0, 0, 0)));
        Near(new Float3(1, 0, 0), Mat4.Apply(parts.Matrices(3.0, 0.625)[0], new Float3(0, 0, 0)));
        Near(new Float3(2, 0, 0), Mat4.Apply(parts.Matrices(3.0, 0.9)[0], new Float3(0, 0, 0)));
        // ungated: lifting changes nothing
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(3.0, 0.4, 1)[0], new Float3(0, 0, 0)));
    }

    [Fact]
    public void Step_defaults_to_the_whole_depth_and_rotates_about_its_pivot()
    {
        var parts = Parts("""[ { "id": "s", "match": ["*"], "drivers": [ { "type": "step", "motion": "rotate", "axis": "z", "pivot": [1, 0, 0], "amount": 3.1415927 } ] } ]""");
        Near(new Float3(2, 0, 0), Mat4.Apply(parts.Matrices(0, 0)[0], new Float3(2, 0, 0)));
        Near(new Float3(1, 1, 0), Mat4.Apply(parts.Matrices(0, 0.5)[0], new Float3(2, 0, 0)));
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(0, 1)[0], new Float3(2, 0, 0)));
    }

    [Fact]
    public void A_held_step_stays_thrown_while_lifting_and_a_blocked_one_stays_home()
    {
        var parts = Parts("""
            [ { "id": "hold", "match": ["h"], "drivers": [ { "type": "step", "motion": "slide", "axis": "y", "amount": 1, "from": 0.9, "to": 1, "lifting": "hold" } ] },
              { "id": "block", "match": ["b"], "drivers": [ { "type": "step", "motion": "slide", "axis": "y", "amount": 1, "from": 0, "to": 0.1, "lifting": "block" } ] } ]
            """);
        var o = new Float3(0, 0, 0);
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(0, 0.5)[0], o));
        Near(new Float3(0, 1, 0), Mat4.Apply(parts.Matrices(0, 0.5, 1)[0], o));
        Near(new Float3(0, 0.5f, 0), Mat4.Apply(parts.Matrices(0, 0.2, 0.5)[0], o));
        Near(new Float3(0, 0.5f, 0), Mat4.Apply(parts.Matrices(0, 0.95)[0], o));
        Near(new Float3(0, 1, 0), Mat4.Apply(parts.Matrices(0, 0.5)[1], o));
        Near(new Float3(0, 0, 0), Mat4.Apply(parts.Matrices(0, 0.5, 1)[1], o));
        Near(new Float3(0, 0.25f, 0), Mat4.Apply(parts.Matrices(0, 0.5, 0.75)[1], o));
    }

    [Fact]
    public void A_trip_step_is_thrown_at_the_bottom_going_down_and_back_at_the_top_going_up()
    {
        var parts = Parts("""
            [ { "id": "t", "match": ["t"], "drivers": [ { "type": "step", "motion": "slide", "axis": "x", "amount": 1, "from": 0.9, "to": 1, "lifting": "trip", "top": 0.1 } ] } ]
            """);
        float X(double depth, double lifting) => Mat4.Apply(parts.Matrices(0, depth, lifting)[0], new Float3(0, 0, 0)).X;
        // down: out until the bottom window, thrown in over it
        Assert.Equal(0f, X(0.5, 0), 4);
        Assert.Equal(0.5f, X(0.95, 0), 4);
        Assert.Equal(1f, X(1, 0), 4);
        // up: stays in until the top window, thrown out over it
        Assert.Equal(1f, X(1, 1), 4);
        Assert.Equal(1f, X(0.5, 1), 4);
        Assert.Equal(0.5f, X(0.05, 1), 4);
        Assert.Equal(0f, X(0, 1), 4);
        // where the direction changes, both halves agree, so the lever never jumps
        Assert.Equal(X(1, 0), X(1, 1), 4);
        Assert.Equal(X(0, 0), X(0, 1), 4);
    }

    [Fact]
    public void A_trip_step_needs_a_top()
    {
        var e = Assert.Throws<FormatException>(() => Parts("""
            [ { "id": "t", "match": ["t"], "drivers": [ { "type": "step", "motion": "slide", "axis": "x", "amount": 1, "from": 0.9, "to": 1, "lifting": "trip" } ] } ]
            """));
        Assert.Contains("top", e.Message);
    }

    [Fact]
    public void A_rectified_rotate_turns_with_the_shaft_travel_whichever_way_the_shaft_turns()
    {
        var parts = Parts("""
            [ { "id": "r", "match": ["r"], "drivers": [ { "type": "rotate", "axis": "z", "pivot": [0, 0, 0], "ratio": 0.5, "rectified": true } ] },
              { "id": "s", "match": ["s"], "drivers": [ { "type": "rotate", "axis": "z", "pivot": [0, 0, 0], "ratio": 0.5 } ] } ]
            """);
        var x = new Float3(1, 0, 0);
        // θ = −π with a travel of π: the rectified part has turned +π/2, the plain one −π/2
        Near(new Float3(0, 1, 0), Mat4.Apply(parts.Matrices(-Math.PI, 0, 0, Math.PI)[0], x));
        Near(new Float3(0, -1, 0), Mat4.Apply(parts.Matrices(-Math.PI, 0, 0, Math.PI)[1], x));
        // with no travel given it is |θ|
        Near(new Float3(0, 1, 0), Mat4.Apply(parts.Matrices(-Math.PI, 0, 0)[0], x));
    }

    [Theory]
    [InlineData(1.0, "pinion_w")]
    [InlineData(-1.0, "pinion_e")]
    public void The_shipped_rectifier_turns_the_disc_one_way_and_carries_the_pinion_that_turns_with_the_shaft(double direction, string carried)
    {
        // Each loose pinion has a one-way catch: whichever way the shaft turns, one pinion turns
        // with it and the crown disc turns the same way; in a raise the small crown gear's half of
        // the crown axle (geared to the drums) turns as the disc does, so the dog clutch can lock
        // them together (6 shaft turns per raise).
        var parts = Shipped().MovingParts;
        int pin = parts.IndexOf(carried), crown = parts.IndexOf("crown"), crownB = parts.IndexOf("crown_b");
        Assert.True(pin >= 0 && crown >= 0 && crownB >= 0);
        var axis = B(0, 3.5f, 1.5f);
        var probe = B(0, 3.5f, 1.5f + 0.15f);
        double t0 = 1.0, step = 0.05, psi0 = 0.0;   // the disc starts square, so its top moving east means it turns the raising way
        var a0 = Mat4.Apply(parts.Matrices(t0, 0.5, 1, psi0)[pin], probe);
        var a1 = Mat4.Apply(parts.Matrices(t0 + direction * step, 0.5, 1, psi0 + step)[pin], probe);
        Near(Mat4.Apply(Mat4.Rotation(Axis.X, direction * step, axis), a0), a1, 1e-4f);
        var disc = B(0.75f, 3.5f + 0.3f, 1.2f);
        var c0 = Mat4.Apply(parts.Matrices(t0, 0.5, 1, psi0)[crown], disc);
        var c1 = Mat4.Apply(parts.Matrices(t0 + direction * step, 0.5, 1, psi0 + step)[crown], disc);
        Assert.True(c1.X > c0.X, "the crown disc's top should move east whichever way the shaft turns");
        // in a raise of `step` shaft radians the depth falls step / (2π·6)
        static double Angle(Float3 v) => Math.Atan2(v.Y - 3.5, v.X - B(0.75f, 0, 0).X);
        var q = B(0.75f + 0.12f, 3.5f, 1.0f);
        double d0 = 0.5, dd = step / (2 * Math.PI * 6);
        double discTurn = Math.IEEERemainder(Angle(Mat4.Apply(parts.Matrices(t0, d0, 1, psi0 + step)[crown], q)) - Angle(Mat4.Apply(parts.Matrices(t0, d0, 1, psi0)[crown], q)), 2 * Math.PI);
        double halfTurn = Math.IEEERemainder(Angle(Mat4.Apply(parts.Matrices(t0, d0 - dd, 1, psi0)[crownB], q)) - Angle(Mat4.Apply(parts.Matrices(t0, d0, 1, psi0)[crownB], q)), 2 * Math.PI);
        Assert.True(Math.Abs(discTurn) > 1e-3, "the disc turns");
        Assert.Equal(discTurn, halfTurn, 4);
    }

    [Fact]
    public void The_shipped_rock_shaft_throws_the_dog_clutch_onto_the_dog_hub_while_the_saws_rise()
    {
        var parts = Shipped().MovingParts;
        int dog = parts.IndexOf("dog");
        var axisPoint = B(0.75f, 3.5f, 1.1f);           // on the crown axle, which the clutch turns about
        float rest = Mat4.Apply(parts.Matrices(0, 0.5)[dog], axisPoint).Z;
        float thrown = Mat4.Apply(parts.Matrices(0, 1)[dog], axisPoint).Z;
        float held = Mat4.Apply(parts.Matrices(0, 0.3, 1)[dog], axisPoint).Z;
        Assert.True(thrown < rest - 0.05f, "slid north, onto the dog hub");
        Assert.Equal(thrown, held, 4);
    }

    [Fact]
    public void Stretch_scales_along_its_axis_from_the_anchor()
    {
        // a rope hanging 0.5 below an anchor at y 3, whose free end falls 1.5 over the depth
        var parts = Parts("""[ { "id": "r", "match": ["*"], "drivers": [ { "type": "stretch", "axis": "y", "anchor": [9, 3, 9], "length": -0.5, "travel": -1.5 } ] } ]""");
        var m0 = parts.Matrices(1.0, 0)[0];
        var m1 = parts.Matrices(1.0, 1)[0];
        Near(new Float3(2, 2.5f, 1), Mat4.Apply(m0, new Float3(2, 2.5f, 1)));
        Near(new Float3(2, 3, 1), Mat4.Apply(m1, new Float3(2, 3, 1)));          // the anchor stays
        Near(new Float3(2, 1, 1), Mat4.Apply(m1, new Float3(2, 2.5f, 1)));       // the free end follows
        Near(new Float3(2, 1.75f, 1), Mat4.Apply(parts.Matrices(1.0, 0.5)[0], new Float3(2, 2.5f, 1)));
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
    [InlineData("""[ { "id": "a", "match": ["a"], "requires": "carriage" } ]""", "carriage")]
    [InlineData("""[ { "id": "a", "match": ["a"] }, { "id": "a", "match": ["b"] } ]""", "twice")]
    [InlineData("""[ { "id": "a", "match": [] } ]""", "match")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "spin", "axis": "x" } ] } ]""", "spin")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "rotate", "axis": "x" } ] } ]""", "pivot")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "feed", "axis": "w", "travel": 1 } ] } ]""", "axis")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "step", "axis": "x", "amount": 1 } ] } ]""", "motion")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "step", "motion": "rotate", "axis": "x", "amount": 1 } ] } ]""", "pivot")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "step", "motion": "slide", "axis": "x", "from": 1, "to": 0.5 } ] } ]""", "window")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "step", "motion": "slide", "axis": "x", "lifting": "always" } ] } ]""", "always")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "stretch", "axis": "y", "length": 1 } ] } ]""", "anchor")]
    [InlineData("""[ { "id": "a", "match": ["a"], "drivers": [ { "type": "stretch", "axis": "y", "anchor": [0, 0, 0] } ] } ]""", "length")]
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
        Assert.True(RigPart.Fitted("levers", 0, false, 0, levers: true));
        Assert.False(RigPart.Fitted("crankshaft", 2, false, 2, levers: true));
    }

    [Fact]
    public void Levers_are_a_known_requirement()
    {
        Assert.Contains("levers", RigPart.KnownRequires);
        var parts = Parts("""[ { "id": "a", "match": ["a"], "requires": "levers" } ]""");
        Assert.Equal("levers", parts.Parts[0].Requires);
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

    // A point given in the generator's build frame (the machine box from its north-west corner, in
    // which make_shape.py places everything) in the shipped native frame, whose controller is the
    // middle cell of the east end: build cell (5, 0, 1) is [0,0,0].
    private static Float3 B(float x, float y, float z) => new(x - 5, y, z - 1);

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
    public void The_shipped_rig_matches_the_python_reference()
    {
        // tools/make_shape.py writes each part's matrix at a grid of (θ, depth, lifting) poses from
        // its own reference maths; RigParts must give the same, so the two cannot drift.
        var parts = Shipped().MovingParts;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rig-reference.json")));
        int poses = 0;
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            double theta = pose.GetProperty("theta").GetDouble(), depth = pose.GetProperty("depth").GetDouble(), lifting = pose.GetProperty("lifting").GetDouble();
            double travel = pose.GetProperty("travel").GetDouble();
            var mats = parts.Matrices(theta, depth, lifting, travel);
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
                            $"{parts.Parts[i].Id} at θ {theta}, depth {depth}, lifting {lifting}, travel {travel}: [{row},{col}] is {got}, the reference says {rows[row][col]}");
                    }
            }
            poses++;
        }
        Assert.True(poses >= 80);
    }

    [Fact]
    public void The_shipped_carriages_fall_from_the_saws_top_to_its_bottom()
    {
        var rig = Shipped();
        var parts = rig.MovingParts;
        foreach (var id in new[] { "f1_carriage", "f2_carriage" })
        {
            int i = parts.IndexOf(id);
            Assert.True(i >= 0, id);
            float drop = Mat4.Apply(parts.Matrices(0, 1)[i], new Float3(0, 0, 0)).Y - Mat4.Apply(parts.Matrices(0, 0)[i], new Float3(0, 0, 0)).Y;
            Assert.Equal(rig.Saw.BottomY - rig.Saw.TopY, drop, 4);
        }
    }

    [Fact]
    public void The_shipped_guide_blocks_sink_with_the_saws_but_do_not_stroke()
    {
        // Each blade's tail is held in a guide block on a post at the south end: the block follows
        // the saw down and up, but stays put while the saw strokes along z through it.
        var parts = Shipped().MovingParts;
        foreach (int n in new[] { 1, 2 })
        {
            int slider = parts.IndexOf($"f{n}_slider"), saw = parts.IndexOf($"f{n}_saw"), carriage = parts.IndexOf($"f{n}_carriage");
            Assert.True(slider >= 0 && saw >= 0 && carriage >= 0, $"station {n}");
            double strokeSpan = 0;
            for (int i = 0; i < 16; i++)
                foreach (double depth in new[] { 0.0, 0.5, 1.0 })
                {
                    var mats = parts.Matrices(i * Math.PI / 8, depth);
                    var origin = new Float3(0, 0, 0);
                    Near(Mat4.Apply(mats[carriage], origin), Mat4.Apply(mats[slider], origin));
                    var s = Mat4.Apply(mats[saw], origin);
                    var b = Mat4.Apply(mats[slider], origin);
                    Assert.Equal(b.Y, s.Y, 4);
                    strokeSpan = Math.Max(strokeSpan, Math.Abs(s.Z - b.Z));
                }
            Assert.True(strokeSpan > 0.05, $"station {n}'s saw does not stroke past its guide block");
        }
    }
}
