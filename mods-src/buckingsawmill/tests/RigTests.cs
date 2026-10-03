using BuckingSawmill.Core;

namespace BuckingSawmill.Tests;

public class RigTests
{
    // A 2×1×2 rig in the shipped file's shape, with keys the parser must ignore.
    private const string Fixture = """
        {
          "_comment": "test fixture",
          "cells": [
            { "pos": [0, 0, 0], "boxes": [] },
            { "pos": [1, 0, 0], "boxes": [[0, 0, 0, 1, 0.5, 1], [0.25, 0.5, 0.25, 0.75, 1, 0.75]] },
            { "pos": [0, 0, 1] },
            { "pos": [1, 0, 1], "boxes": [], "extra": true }
          ],
          "powerCell": [1, 0, 1], "powerFace": "east",
          "infeedSide": "north", "outputSide": "south",
          "output": { "pos": [1.0, 0.5, 2.25], "note": "x" },
          "trunkBed": { "origin": [0.5, 0.5, 0.5], "axis": "x", "length": 2.0 },
          "parts": [ { "id": "frame", "match": ["*"] } ],
        }
        """;

    [Fact]
    public void Parses_the_fixture_and_ignores_unknown_keys()
    {
        var rig = Rig.Parse(Fixture);
        Assert.Equal(4, rig.Cells.Count);
        Assert.Equal(new Int3(1, 0, 1), rig.PowerCell);
        Assert.Equal(Side.East, rig.PowerFace);
        Assert.Equal(Side.North, rig.InfeedSide);
        Assert.Equal(Side.South, rig.OutputSide);
        Assert.Equal(new Float3(1, 0.5f, 2.25f), rig.OutputPos);
        Assert.Equal(3, rig.GhostCells.Count());
        Assert.Empty(rig.CellAt(Int3.Zero)!.Boxes);
        Assert.Empty(rig.CellAt(new Int3(0, 0, 1))!.Boxes);
        Assert.Equal(new Box(0.25f, 0.5f, 0.25f, 0.75f, 1, 0.75f), rig.CellAt(new Int3(1, 0, 0))!.Boxes[1]);
        Assert.Null(rig.CellAt(new Int3(5, 5, 5)));
    }

    [Fact]
    public void Infeed_neighbours_are_the_ground_cells_beyond_the_infeed_side()
    {
        var rig = Rig.Parse(Fixture);
        Assert.Equal(new[] { new Int3(0, 0, -1), new Int3(1, 0, -1) }, rig.InfeedNeighbours().OrderBy(p => p.X));
    }

    [Fact]
    public void Infeed_neighbours_skip_upper_cells_and_cells_of_the_mill()
    {
        var rig = Rig.Parse("""
            { "cells": [ { "pos": [0,0,0] }, { "pos": [0,0,-1] }, { "pos": [1,0,0] }, { "pos": [1,1,0] } ],
              "powerCell": [1,1,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south",
              "output": { "pos": [0,0,0] } }
            """);
        Assert.Equal(new[] { new Int3(0, 0, -2), new Int3(1, 0, -1) }, rig.InfeedNeighbours().OrderBy(p => p.X));
    }

    [Theory]
    [InlineData("""{ "cells": [ { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "controller")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0] }, { "pos": [1,0,0] } ], "powerCell": [2,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "powerCell")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0] } ], "powerCell": [0,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "ghost cell")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0] }, { "pos": [0,0,0] } ], "powerCell": [0,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "twice")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0] }, { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "up", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "powerFace")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0] }, { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "west", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "infeedSide")]
    [InlineData("""{ "cells": [ { "pos": [0,0.5,0] }, { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "whole")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0], "boxes": [[0,0,0,1,1]] }, { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "6 numbers")]
    [InlineData("""{ "cells": [ { "pos": [0,0,0], "boxes": [[1,0,0,0,1,1]] }, { "pos": [1,0,0] } ], "powerCell": [1,0,0], "powerFace": "west", "infeedSide": "north", "outputSide": "south", "output": { "pos": [0,0,0] } }""", "wrong way")]
    [InlineData("""{ "cells": [ """, "JSON")]
    public void Rejects_a_broken_rig_saying_why(string json, string expected)
    {
        var e = Assert.Throws<FormatException>(() => Rig.Parse(json));
        Assert.Contains(expected, e.Message);
    }

    // The rig the mod ships: it parses, the controller and power cell are where the mill can work,
    // and every cell is reachable (no floating cell above an empty column).
    [Fact]
    public void The_shipped_rig_parses_and_is_consistent()
    {
        var rig = Rig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rig.json")));
        Assert.Contains(rig.Cells, c => c.Pos == Int3.Zero);
        Assert.NotEmpty(rig.InfeedNeighbours());
        Assert.All(rig.Cells.SelectMany(c => c.Boxes), b =>
            Assert.True(b.X1 >= 0 && b.Y1 >= 0 && b.Z1 >= 0 && b.X2 <= 1 && b.Y2 <= 1 && b.Z2 <= 1, $"box {b} leaves its cell"));
        // The axle comes in from outside the mill.
        var beyond = rig.PowerCell + rig.PowerFace.Normal();
        Assert.Null(rig.CellAt(beyond));
    }
}
