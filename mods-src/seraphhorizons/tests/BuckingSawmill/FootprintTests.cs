using SeraphHorizons.Mod.BuckingSawmill.Core;

namespace SeraphHorizons.Mod.Tests;

public class FootprintTests
{
    public static TheoryData<Side> Facings() => [Side.North, Side.East, Side.South, Side.West];

    [Fact]
    public void South_is_the_native_frame()
    {
        Assert.Equal(new Int3(3, 1, 2), Footprint.ToWorld(new Int3(3, 1, 2), Side.South));
        Assert.Equal(Side.West, Footprint.ToWorld(Side.West, Side.South));
        Assert.Equal(new Float3(3f, 0.6f, 3.25f), Footprint.ToWorld(new Float3(3f, 0.6f, 3.25f), Side.South));
    }

    // Immersive Woodworking's RotateToFacing: (x·nz + z·nx, y, −x·nx + z·nz).
    [Theory]
    [InlineData(Side.North, -3, 1, -2)]
    [InlineData(Side.East, 2, 1, -3)]
    [InlineData(Side.West, -2, 1, 3)]
    public void Cells_turn_as_Immersive_Woodworking_turns_them(Side facing, int x, int y, int z) =>
        Assert.Equal(new Int3(x, y, z), Footprint.ToWorld(new Int3(3, 1, 2), facing));

    [Theory, MemberData(nameof(Facings))]
    public void ToLocal_undoes_ToWorld(Side facing)
    {
        for (int x = -3; x <= 3; x++)
        for (int z = -3; z <= 3; z++)
        {
            var p = new Int3(x, 2, z);
            Assert.Equal(p, Footprint.ToLocal(Footprint.ToWorld(p, facing), facing));
        }
    }

    // The shape's rotateY and the cell rotation agree: turning by the facing is a rotation, not a
    // mirror, and a quarter turn apart for neighbouring facings.
    [Theory, MemberData(nameof(Facings))]
    public void Rotation_keeps_handedness(Side facing)
    {
        var x = Footprint.ToWorld(new Int3(1, 0, 0), facing);
        var z = Footprint.ToWorld(new Int3(0, 0, 1), facing);
        // The y component of x × z is −1 in the native frame and must stay so.
        Assert.Equal(-1, x.Z * z.X - x.X * z.Z);
    }

    [Fact]
    public void Shape_rotation_matches_Immersive_Woodworking()
    {
        Assert.Equal(180, Footprint.RotateY(Side.North));
        Assert.Equal(90, Footprint.RotateY(Side.East));
        Assert.Equal(0, Footprint.RotateY(Side.South));
        Assert.Equal(270, Footprint.RotateY(Side.West));
    }

    [Theory, MemberData(nameof(Facings))]
    public void Faces_turn_with_the_cells(Side facing)
    {
        foreach (var side in Sides.All)
        {
            var turned = Footprint.ToWorld(side, facing);
            Assert.Equal(Footprint.ToWorld(side.Normal(), facing), turned.Normal());
        }
    }

    [Theory, MemberData(nameof(Facings))]
    public void A_point_lands_in_the_cell_its_cell_lands_in(Side facing)
    {
        var cell = new Int3(4, 2, 1);
        var point = new Float3(4.25f, 2.5f, 1.75f);
        var w = Footprint.ToWorld(point, facing);
        Assert.Equal(Footprint.ToWorld(cell, facing), new Int3((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y), (int)MathF.Floor(w.Z)));
    }

    [Fact]
    public void Boxes_turn_within_their_cell()
    {
        var box = new Box(0, 0, 0, 0.5f, 1, 0.25f);
        Assert.Equal(box, Footprint.ToWorld(box, Side.South));
        Assert.Equal(new Box(0.5f, 0, 0.75f, 1, 1, 1), Footprint.ToWorld(box, Side.North));
        Assert.Equal(new Box(0, 0, 0.5f, 0.25f, 1, 1), Footprint.ToWorld(box, Side.East));
        Assert.Equal(new Box(0.75f, 0, 0, 1, 1, 0.5f), Footprint.ToWorld(box, Side.West));
    }

    [Fact]
    public void Side_codes_round_trip()
    {
        foreach (var side in Sides.All)
        {
            Assert.True(Sides.TryParse(side.Code(), out var parsed));
            Assert.Equal(side, parsed);
        }
        Assert.False(Sides.TryParse("up", out _));
        Assert.False(Sides.TryParse(null, out _));
    }

    // Placing: the mill extends away from the player. The block they clicked is the controller, the
    // middle of the near (east, output) end; the axle and the infeed are at the far (west) end; the
    // logs come out towards them.
    [Theory]
    [InlineData(Side.North, Side.West)]
    [InlineData(Side.East, Side.North)]
    [InlineData(Side.South, Side.East)]
    [InlineData(Side.West, Side.South)]
    public void Placing_turns_the_mill_to_extend_away_from_the_player(Side look, Side placed)
    {
        Assert.Equal(placed, Footprint.PlacedFacing(look));
        var rig = Rig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "buckingmill-rig.json")));
        var d = look.Normal();
        var toward = Sides.FromNormal(-d.X, -d.Z);
        int Along(Int3 local) { var w = Footprint.ToWorld(local, placed); return w.X * d.X + w.Z * d.Z; }
        int Across(Int3 local) { var w = Footprint.ToWorld(local, placed); return w.X * -d.Z + w.Z * d.X; }

        // the controller is the clicked cell, at the near end, centred across the machine
        Assert.Equal(Int3.Zero, Footprint.ToWorld(Int3.Zero, placed));
        Assert.Equal(0, rig.Cells.Min(c => Along(c.Pos)));
        Assert.Equal(-rig.Cells.Min(c => Across(c.Pos)), rig.Cells.Max(c => Across(c.Pos)));
        Assert.True(rig.Cells.Max(c => Along(c.Pos)) > rig.Cells.Max(c => Math.Abs(Across(c.Pos))), "the long axis runs away from the player");

        // the power cell is at the far end, and the axle and the rack come from beyond it
        Assert.Equal(rig.Cells.Max(c => Along(c.Pos)), Along(rig.PowerCell));
        Assert.Equal(look, Footprint.ToWorld(rig.PowerFace, placed));
        Assert.Equal(look, Footprint.ToWorld(rig.InfeedSide, placed));

        // the logs leave the near end, on the player's side of the controller
        Assert.Equal(toward, Footprint.ToWorld(rig.OutputSide, placed));
        var o = Footprint.ToWorld(rig.OutputPos, placed);
        float alongOut = (o.X - 0.5f) * d.X + (o.Z - 0.5f) * d.Z;
        Assert.True(alongOut < -0.5f, $"output {o} is not in front of the near end");
    }
}
