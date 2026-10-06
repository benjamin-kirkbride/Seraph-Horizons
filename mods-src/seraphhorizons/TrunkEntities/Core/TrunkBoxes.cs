using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// A trunk entity's boxes, entity-local blocks with the entity's position at the middle of the
/// trunk's underside, lying along z at yaw 0. The size is its display class's
/// (<see cref="TrunkBox.Size"/>): thin 1×1×4, thick 2×2×5. Collision is a row of boxes along the
/// trunk, each as long as the trunk is wide (square across), overlapping where the length is not a
/// whole number of widths. The game's multi-box passive physics moves each box's middle round with
/// the yaw and keeps its size (as for the raft), so square boxes make the same trunk at a quarter
/// turn as along z; selection is the same row turned the same way.
/// </summary>
public static class TrunkBoxes
{
    /// <summary>The collision boxes at yaw 0: thin, four 1×1×1 along z from −2 to 2; thick, four
    /// 2×2×2 one block apart, middles at z −1.5, −0.5, 0.5, 1.5 (together −2.5 to 2.5). Empty for
    /// none.</summary>
    public static IReadOnlyList<Box> Collision(TrunkClass trunk)
    {
        var (length, width, height) = TrunkBox.Size(trunk);
        if (length <= 0 || width <= 0)
            return [];
        float across = width / 2f, start = -length / 2f;
        int count = Math.Max(1, length - width + 1);
        var boxes = new List<Box>(count);
        for (int i = 0; i < count; i++)
            boxes.Add(new Box(-across, 0, start + i, across, height, start + i + width));
        return boxes;
    }

    /// <summary>The whole trunk's box at yaw 0 (the collision boxes' bounds).</summary>
    public static Box Selection(TrunkClass trunk)
    {
        var (length, width, height) = TrunkBox.Size(trunk);
        return new Box(-width / 2f, 0, -length / 2f, width / 2f, height, length / 2f);
    }

    /// <summary>The entity's own square hitbox (<c>hitboxSize</c>): its width across and its
    /// height. What the game measures touching and shoving by.</summary>
    public static (float Width, float Height) Hitbox(TrunkClass trunk)
    {
        var (_, width, height) = TrunkBox.Size(trunk);
        return (width, height);
    }

    /// <summary>The radius the whole trunk fits in, from its middle (for culling).</summary>
    public static float Radius(TrunkClass trunk)
    {
        var (length, width, height) = TrunkBox.Size(trunk);
        return MathF.Sqrt(length * length + width * width + height * height) / 2f;
    }

    /// <summary>
    /// The collision boxes at <paramref name="yaw"/> (radians) for picking: each box's middle turned
    /// about the entity as the game's multi-box physics turns it (a turn of yaw + π about y, x' =
    /// x cos a + z sin a, z' = −x sin a + z cos a), its size kept. The boxes are square across, so
    /// a quarter turn lays the row along x with nothing swapped.
    /// </summary>
    public static IReadOnlyList<Box> Turned(TrunkClass trunk, float yaw)
    {
        double a = yaw + Math.PI;
        float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
        var turned = new List<Box>();
        foreach (var b in Collision(trunk))
        {
            float mx = (b.X1 + b.X2) / 2, mz = (b.Z1 + b.Z2) / 2;
            float x = mx * c + mz * s, z = -mx * s + mz * c;
            float hx = (b.X2 - b.X1) / 2, hz = (b.Z2 - b.Z1) / 2;
            turned.Add(new Box(x - hx, b.Y1, z - hz, x + hx, b.Y2, z + hz));
        }
        return turned;
    }
}
