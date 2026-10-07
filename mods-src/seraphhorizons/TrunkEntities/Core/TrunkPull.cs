namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// A trunk's geometry, game-independent, for the drive (<see cref="TrunkDrive"/>) and the rope's
/// end (<c>TrunkRope</c>). A trunk lies along its local z and is turned
/// by its yaw as <see cref="TrunkBoxes.Turned"/> turns its boxes: local (0, z) lands at
/// (z·sin(yaw+π), z·cos(yaw+π)). End +1 is the end at local +z, end -1 the one at local -z.
/// Positions here are horizontal (x, z) only.
/// </summary>
public static class TrunkPull
{
    /// <summary>The horizontal unit vector from the centre to end <paramref name="end"/> (±1).</summary>
    public static (double X, double Z) Axis(double yaw, int end)
    {
        double a = yaw + Math.PI, s = end >= 0 ? 1 : -1;
        return (s * Math.Sin(a), s * Math.Cos(a));
    }

    /// <summary>End <paramref name="end"/>'s position for a trunk centred at (<paramref name="cx"/>,
    /// <paramref name="cz"/>) of <paramref name="length"/> blocks.</summary>
    public static (double X, double Z) EndPos(double cx, double cz, double yaw, double length, int end)
    {
        var (ax, az) = Axis(yaw, end);
        return (cx + ax * length / 2, cz + az * length / 2);
    }

    /// <summary>The end (±1) nearer to the point (<paramref name="px"/>, <paramref name="pz"/>).</summary>
    public static int NearerEnd(double cx, double cz, double yaw, double px, double pz)
    {
        var (ax, az) = Axis(yaw, 1);
        return (px - cx) * ax + (pz - cz) * az >= 0 ? 1 : -1;
    }

    /// <summary>The yaw that points end <paramref name="end"/> along (<paramref name="dx"/>,
    /// <paramref name="dz"/>), in [-π, π).</summary>
    public static double YawFacing(double dx, double dz, int end) =>
        Wrap(Math.Atan2(dx, dz) - (end >= 0 ? Math.PI : 0));

    /// <summary><paramref name="a"/> wrapped into [-π, π).</summary>
    public static double Wrap(double a)
    {
        a = (a + Math.PI) % (2 * Math.PI);
        if (a < 0)
            a += 2 * Math.PI;
        return a - Math.PI;
    }

    /// <summary>Turns <paramref name="yaw"/> towards <paramref name="target"/> the short way round,
    /// by at most <paramref name="maxStep"/> radians; the result is wrapped.</summary>
    public static double StepYaw(double yaw, double target, double maxStep)
    {
        double diff = Wrap(target - yaw);
        return Wrap(yaw + Math.Clamp(diff, -maxStep, maxStep));
    }

    /// <summary>How much lighter a trunk is afloat than on land, for a rope's pull: the weight
    /// the game's rope sees in water is the land weight over this.</summary>
    public const double WaterLightening = 6.0;

    /// <summary>The weight a rope's pull sees for a trunk of land <paramref name="weight"/>: the
    /// same on land, <see cref="WaterLightening"/> times lighter afloat.</summary>
    public static double EffectiveWeight(double weight, bool afloat) => afloat ? weight / WaterLightening : weight;
}
