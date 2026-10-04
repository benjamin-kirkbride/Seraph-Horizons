namespace SeraphHorizons.Mod.BuckingSawmill.Core;

public readonly record struct Int3(int X, int Y, int Z)
{
    public static readonly Int3 Zero = new(0, 0, 0);
    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public override string ToString() => $"[{X},{Y},{Z}]";
}

public readonly record struct Float3(float X, float Y, float Z)
{
    public override string ToString() => $"[{X},{Y},{Z}]";
}

/// <summary>A horizontal side, as the block's <c>side</c> variant names it.</summary>
public enum Side { North, East, South, West }

public static class Sides
{
    public static readonly Side[] All = [Side.North, Side.East, Side.South, Side.West];

    public static string Code(this Side side) => side.ToString().ToLowerInvariant();

    public static bool TryParse(string? code, out Side side)
    {
        foreach (var s in All)
            if (s.Code() == code)
            {
                side = s;
                return true;
            }
        side = default;
        return false;
    }

    /// <summary>The unit step towards this side (north is -z, east is +x).</summary>
    public static Int3 Normal(this Side side) => side switch
    {
        Side.North => new Int3(0, 0, -1),
        Side.East => new Int3(1, 0, 0),
        Side.South => new Int3(0, 0, 1),
        _ => new Int3(-1, 0, 0),
    };

    public static Side FromNormal(int x, int z) =>
        All.First(s => s.Normal().X == x && s.Normal().Z == z);
}

/// <summary>
/// Native (south-facing) frame to world frame for a mill facing <c>side</c>, Immersive
/// Woodworking's convention: with n the normal of the side, local (x, y, z) goes to
/// (x·nz + z·nx, y, −x·nx + z·nz). South is the identity, north turns 180°. The block's shape
/// is turned to match: rotateY north 180, east 90, south 0, west 270.
/// </summary>
public static class Footprint
{
    public static Int3 ToWorld(Int3 local, Side facing)
    {
        var n = facing.Normal();
        return new Int3(local.X * n.Z + local.Z * n.X, local.Y, -local.X * n.X + local.Z * n.Z);
    }

    /// <summary>The inverse of <see cref="ToWorld(Int3, Side)"/>: a world offset from the
    /// controller back to the native frame.</summary>
    public static Int3 ToLocal(Int3 world, Side facing)
    {
        var n = facing.Normal();
        return new Int3(world.X * n.Z - world.Z * n.X, world.Y, world.X * n.X + world.Z * n.Z);
    }

    public static Side ToWorld(Side nativeSide, Side facing)
    {
        var w = ToWorld(nativeSide.Normal(), facing);
        return Sides.FromNormal(w.X, w.Z);
    }

    /// <summary>A point in native-frame blocks (the controller cell spans 0..1) to a world offset
    /// from the controller's corner: turned about the controller cell's centre, as the cells are.</summary>
    public static Float3 ToWorld(Float3 local, Side facing)
    {
        var n = facing.Normal();
        float x = local.X - 0.5f, z = local.Z - 0.5f;
        return new Float3(x * n.Z + z * n.X + 0.5f, local.Y, -x * n.X + z * n.Z + 0.5f);
    }

    /// <summary>The inverse of <see cref="ToWorld(Float3, Side)"/>: a world offset from the
    /// controller's corner back to native-frame blocks.</summary>
    public static Float3 ToLocal(Float3 world, Side facing)
    {
        var n = facing.Normal();
        float x = world.X - 0.5f, z = world.Z - 0.5f;
        return new Float3(x * n.Z - z * n.X + 0.5f, world.Y, x * n.X + z * n.Z + 0.5f);
    }

    /// <summary>A cell-local box turned within its cell (about 0.5, 0.5).</summary>
    public static Box ToWorld(Box box, Side facing)
    {
        var a = ToWorld(new Float3(box.X1, box.Y1, box.Z1), facing);
        var b = ToWorld(new Float3(box.X2, box.Y2, box.Z2), facing);
        return new Box(Math.Min(a.X, b.X), box.Y1, Math.Min(a.Z, b.Z), Math.Max(a.X, b.X), box.Y2, Math.Max(a.Z, b.Z));
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>:
    /// the mill extends away from them, its native west end (the axle and the infeed) farthest and
    /// its east end (the output, with the controller, the block they clicked) nearest. The native
    /// west normal goes to <paramref name="look"/> under <see cref="ToWorld(Int3, Side)"/>, so
    /// looking north places <c>west</c>, east places <c>north</c>, south <c>east</c>, west <c>south</c>.</summary>
    public static Side PlacedFacing(Side look)
    {
        var d = look.Normal();
        return Sides.FromNormal(d.Z, -d.X);
    }

    /// <summary>The shape's rotateY for a facing.</summary>
    public static int RotateY(Side facing) => facing switch
    {
        Side.North => 180,
        Side.East => 90,
        Side.South => 0,
        _ => 270,
    };
}
