namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Where it stands for a trunk and which way it faces (a yaw as the game's: forward is
/// (sin yaw, cos yaw)).</summary>
public readonly record struct HaulStand(double X, double Z, float Yaw);

/// <summary>Where a delivered trunk is laid: its underside's middle (<see cref="X"/>, <see cref="Y"/>,
/// <see cref="Z"/>, the middle of one infeed cell), lying along <see cref="TrunkYaw"/> (across the
/// machine's line, as the machines lay one beyond their infeed), and where the eidolon stands to lay it.</summary>
public readonly record struct HaulDrop(double X, double Y, double Z, float TrunkYaw, HaulStand Stand);

/// <summary>One of the shape's trunk animations: its code, when in it the trunk is taken or let go
/// (the event frame of <c>Eidolon/tools/make_shape.py</c>'s <c>EVENTS</c>) and when it ends, seconds.</summary>
public readonly record struct HaulMove(string Animation, double EventAt, double Ends);

/// <summary>
/// The haul order's geometry and timing, game-independent (README "Eidolon", hauling). A trunk lies
/// along its yaw (its length along (sin yaw, cos yaw)), its position the middle of its underside, as
/// TrunkEntities has it. The shape's trunk animations take a trunk lying across in front of the feet:
/// a thin one about 1.2 blocks in front of the eidolon's middle, a thick one about 2.1
/// (<c>Eidolon/README.md</c>, the animation table). It stands a little further off a thin one,
/// <see cref="Reach"/>, so that TrunkEntities' solidity (which moves an agent out of a trunk's footprint)
/// leaves it where it is: its 1.7-wide box clears a thin trunk's half block.
/// </summary>
public static class HaulPlan
{
    /// <summary>The eidolon's half width (its box is 1.7 blocks).</summary>
    public const double HalfWidth = 0.85;

    /// <summary>The shape's animations run at 30 frames a second.</summary>
    public const double FramesPerSecond = 30;

    /// <summary>How far in front of its middle a trunk's middle lies when it takes or lays one:
    /// clear of the trunk's half width (0.5 thin, 1 thick) by a little.</summary>
    public static double Reach(bool thick) => thick ? 2.0 : 1.4;

    /// <summary>The two places to stand to take a trunk whose underside middle is at
    /// (<paramref name="x"/>, <paramref name="z"/>), lying along <paramref name="trunkYaw"/>: either side
    /// of it, square to its length at <see cref="Reach"/>, facing it. The one nearer
    /// (<paramref name="fromX"/>, <paramref name="fromZ"/>) first.</summary>
    public static HaulStand[] PickupStands(double x, double z, float trunkYaw, bool thick, double fromX, double fromZ)
    {
        double reach = Reach(thick);
        // square to the length: the length is (sin, cos), so (cos, -sin) is across it
        double ax = Math.Cos(trunkYaw), az = -Math.Sin(trunkYaw);
        var a = Facing(x + ax * reach, z + az * reach, x, z);
        var b = Facing(x - ax * reach, z - az * reach, x, z);
        double da = Sq(a.X - fromX) + Sq(a.Z - fromZ), db = Sq(b.X - fromX) + Sq(b.Z - fromZ);
        return da <= db ? [a, b] : [b, a];
    }

    /// <summary>
    /// Where a trunk is laid at a machine's infeed: in the middle of the infeed cell nearest the cells'
    /// middle (<paramref name="cells"/>, the blocks just beyond the infeed end; the machines take a
    /// trunk whose middle lies in one), on its floor, lying across the machine's line (square to
    /// <paramref name="outwardX"/>, <paramref name="outwardZ"/>, the way out of the machine), and the
    /// eidolon <see cref="Reach"/> further out, facing the machine. Null for no cells.
    /// </summary>
    public static HaulDrop? Drop(IReadOnlyList<MarkPos> cells, int outwardX, int outwardZ, bool thick)
    {
        if (cells.Count == 0)
            return null;
        double mx = cells.Average(c => c.X + 0.5), mz = cells.Average(c => c.Z + 0.5);
        var cell = cells.OrderBy(c => Sq(c.X + 0.5 - mx) + Sq(c.Z + 0.5 - mz)).ThenBy(c => c.X).ThenBy(c => c.Z).First();
        double x = cell.X + 0.5, z = cell.Z + 0.5;
        float trunkYaw = (float)(Math.Atan2(outwardX, outwardZ) + Math.PI / 2);
        double reach = Reach(thick);
        var stand = Facing(x + outwardX * reach, z + outwardZ * reach, x, z);
        return new HaulDrop(x, cell.Y, z, trunkYaw, stand);
    }

    /// <summary>Whether a trunk whose underside middle is at (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) lies in <paramref name="area"/>: its middle's column inside the area, and
    /// its underside from the area's lowest block to <see cref="AreaHeadroom"/> above its highest (the
    /// marks are usually the ground it lies on).</summary>
    public static bool InArea(MarkArea area, double x, double y, double z)
    {
        int bx = (int)Math.Floor(x), by = (int)Math.Floor(y + 0.5), bz = (int)Math.Floor(z);
        return bx >= area.Min.X && bx <= area.Max.X && bz >= area.Min.Z && bz <= area.Max.Z
               && by >= area.Min.Y && by <= area.Max.Y + AreaHeadroom;
    }

    /// <summary>The longest a hauler goes for a trunk before it counts it lost: a path that seemed to
    /// lead there but never arrives (the trunk lies in another tree's crown, or the way changed).</summary>
    public const double FetchSeconds = 45;

    /// <summary>How many times the walk to a trunk may stick (the traverser's stuck detection) before
    /// the hauler counts it lost.</summary>
    public const int StuckTries = 3;

    /// <summary>How many blocks above an area's highest mark a trunk still counts as in it.</summary>
    public const int AreaHeadroom = 4;

    /// <summary>Taking a trunk up: <c>trunk-pickup</c> (60 frames, grabbed on 24) or
    /// <c>trunk-thick-pickup</c> (60, grabbed on 26). Both hold their last frame, the carry's first.</summary>
    public static HaulMove PickUp(bool thick) =>
        thick ? new("trunk-thick-pickup", 26 / FramesPerSecond, 60 / FramesPerSecond)
              : new("trunk-pickup", 24 / FramesPerSecond, 60 / FramesPerSecond);

    /// <summary>Laying a trunk down: <c>trunk-setdown</c> (50 frames, let go on 36) or
    /// <c>trunk-thick-setdown</c> (50, let go on 32).</summary>
    public static HaulMove SetDown(bool thick) =>
        thick ? new("trunk-thick-setdown", 32 / FramesPerSecond, 50 / FramesPerSecond)
              : new("trunk-setdown", 36 / FramesPerSecond, 50 / FramesPerSecond);

    /// <summary>Standing with a trunk.</summary>
    public static string CarryIdle(bool thick) => thick ? "trunk-thick-carry-idle" : "trunk-carry-idle";

    /// <summary>Walking with a trunk (it never runs with one).</summary>
    public static string CarryWalk(bool thick) => thick ? "trunk-thick-carry-walk" : "trunk-carry-walk";

    /// <summary>The attachment point a trunk is drawn at: <c>ThickTrunk</c> in both arms, <c>Trunk</c>
    /// on the left shoulder.</summary>
    public static string AttachmentPoint(bool thick) => thick ? "ThickTrunk" : "Trunk";

    private static HaulStand Facing(double x, double z, double atX, double atZ) =>
        new(x, z, (float)Math.Atan2(atX - x, atZ - z));

    private static double Sq(double v) => v * v;
}
