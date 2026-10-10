namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Where the eidolon stands to lift or set down a block: its feet's centre and the yaw that
/// faces the block (the game's <c>atan2(dx, dz)</c>, as its other tasks turn).</summary>
public readonly record struct CarryStand(double X, double Y, double Z, float Yaw);

/// <summary>
/// The rules of carrying a block (Eidolon/README.md, "Carrying", #676) that need no game: the timing
/// of the shape's <c>lift</c> and <c>setdown</c> one-shots and their event frames, where it stands
/// beside a block, and how it is turned to face it.
/// </summary>
public static class EidolonCarrying
{
    /// <summary>The shape's frame rate.</summary>
    public const double FramesPerSecond = 30;

    /// <summary><c>lift</c>: 45 frames, the block taken by the sides on frame 22 (make_shape.py's EVENTS).</summary>
    public const double LiftSeconds = 45 / FramesPerSecond;
    public const double GrabSeconds = 22 / FramesPerSecond;

    /// <summary><c>setdown</c>: 45 frames, the block let go on frame 28.</summary>
    public const double SetDownSeconds = 45 / FramesPerSecond;
    public const double ReleaseSeconds = 28 / FramesPerSecond;

    /// <summary>How far from the block's centre its feet's centre stands: its box (1.7 wide) clear of
    /// the block's cell by 0.05. The animations reach to 1.09 blocks (the block's underside centre at
    /// (−9.5, 0, 8) in the shape), so the block is taken and set down 0.31 blocks further out than
    /// the hands are drawn; nearer, the box would stand in the cell it sets the block into.</summary>
    public const double Reach = 1.4;

    /// <summary>Its collision box's half width.</summary>
    public const double HalfWidth = 0.85;

    /// <summary>How far it may stop from a stand point and still be put on it (the game's waypoint
    /// traverser stops within half a block of the last waypoint).</summary>
    public const double SnapDistance = 0.75;

    private static readonly (int Dx, int Dz)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// The places it may stand to work the block at (<paramref name="bx"/>, <paramref name="by"/>,
    /// <paramref name="bz"/>): on each of its four sides, <see cref="Reach"/> from its centre, feet
    /// level with the block's underside or a block above or below it, where its box is free and has
    /// ground under it. Nearest to (<paramref name="fromX"/>, <paramref name="fromZ"/>) first.
    /// </summary>
    public static List<CarryStand> Stands(int bx, int by, int bz, double fromX, double fromZ, IWideSpace space)
    {
        double cx = bx + 0.5, cz = bz + 0.5;
        var stands = new List<CarryStand>();
        foreach (var (dx, dz) in Sides)
        {
            double x = cx + dx * Reach, z = cz + dz * Reach;
            foreach (int dy in new[] { 0, 1, -1 })
            {
                int y = by + dy;
                if (!space.Free(x, y, z) || space.Free(x, y - 1, z))
                    continue;
                stands.Add(new CarryStand(x, y, z, Facing(x, z, cx, cz)));
                break;
            }
        }
        return stands.OrderBy(s => (s.X - fromX) * (s.X - fromX) + (s.Z - fromZ) * (s.Z - fromZ)).ToList();
    }

    /// <summary>The yaw that faces (<paramref name="toX"/>, <paramref name="toZ"/>) from (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static float Facing(double x, double z, double toX, double toZ) => (float)Math.Atan2(toX - x, toZ - z);

    /// <summary>Whether a box <see cref="HalfWidth"/> each way about (<paramref name="x"/>, <paramref name="z"/>)
    /// reaches into the block column (<paramref name="bx"/>, <paramref name="bz"/>).</summary>
    public static bool Overlaps(double x, double z, int bx, int bz) =>
        x + HalfWidth > bx && x - HalfWidth < bx + 1 && z + HalfWidth > bz && z - HalfWidth < bz + 1;

    /// <summary>Whether it stands near enough <paramref name="stand"/> to be put on it.</summary>
    public static bool Near(CarryStand stand, double x, double y, double z) =>
        (stand.X - x) * (stand.X - x) + (stand.Z - z) * (stand.Z - z) <= SnapDistance * SnapDistance && Math.Abs(stand.Y - y) < 1.2;
}

/// <summary>Where a carry or set-down order has got to (saved with it, so a restart picks it up).</summary>
public enum CarryPhase
{
    /// <summary>Walking to the block's side.</summary>
    Approach,
    /// <summary>Playing <c>lift</c> or <c>setdown</c>, before its event frame.</summary>
    Reaching,
    /// <summary>After the event frame, to the one-shot's end.</summary>
    Finishing,
    /// <summary>Holding the block (carry: following its commander).</summary>
    Holding,
}
