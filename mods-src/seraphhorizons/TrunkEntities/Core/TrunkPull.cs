namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// The maths of the grab's pull, game-independent. A trunk lies along its local z and is turned
/// by its yaw as <see cref="TrunkBoxes.Turned"/> turns its boxes: local (0, z) lands at
/// (z·sin(yaw+π), z·cos(yaw+π)). End +1 is the end at local +z, end -1 the one at local -z.
/// Positions here are horizontal (x, z) only.
/// </summary>
public static class TrunkPull
{
    /// <summary>How far, blocks, the hand may be from the grabbed end before it pulls.</summary>
    public const double Slack = 1.0;

    /// <summary>The pull's speed per block of stretch past <see cref="Slack"/>, blocks per second,
    /// before the weight's factor.</summary>
    public const double Gain = 3.0;

    /// <summary>The fastest a pulled trunk moves, blocks per second: under a player's walk.</summary>
    public const double MaxSpeed = 3.5;

    /// <summary>The fastest a trunk of factor 1 turns, radians per second.</summary>
    public const double TurnRate = 1.5;

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

    /// <summary>How readily a trunk of <paramref name="weight"/> follows the pull: the old rope
    /// pull's 50 / weight, between 0.1 and 2.</summary>
    public static double Factor(double weight) => weight <= 0 ? 2 : Math.Clamp(50 / weight, 0.1, 2);

    /// <summary>How much lighter a trunk pulls afloat than on land: the weight the pull sees in
    /// water is the land weight over this.</summary>
    public const double WaterLightening = 6.0;

    /// <summary>The least factor of a trunk afloat, so even the heaviest follows nearly as fast as
    /// a player swims.</summary>
    public const double WaterFloor = 1.0;

    /// <summary>The weight a pull sees for a trunk of land <paramref name="weight"/>: the same on
    /// land, <see cref="WaterLightening"/> times lighter afloat.</summary>
    public static double EffectiveWeight(double weight, bool afloat) => afloat ? weight / WaterLightening : weight;

    /// <summary><see cref="Factor(double)"/> afloat or not: afloat it is that of the
    /// <see cref="EffectiveWeight"/>, never under <see cref="WaterFloor"/>.</summary>
    public static double Factor(double weight, bool afloat) =>
        afloat ? Math.Max(WaterFloor, Factor(EffectiveWeight(weight, true))) : Factor(weight);

    /// <summary>The pull's speed, blocks per second, with the hand <paramref name="distance"/>
    /// blocks from the grabbed end: none within <see cref="Slack"/>, then growing with the stretch
    /// and the weight's factor, never past <see cref="MaxSpeed"/>.</summary>
    public static double Speed(double distance, double weight, bool afloat = false)
    {
        double stretch = distance - Slack;
        return stretch <= 0 ? 0 : Math.Min(MaxSpeed, stretch * Gain * Factor(weight, afloat));
    }

    /// <summary>The largest turn, radians, in <paramref name="dt"/> seconds for a trunk of
    /// <paramref name="weight"/> pulled with the hand <paramref name="distance"/> from the grabbed
    /// end: none within <see cref="Slack"/>, full rate once a block past it.</summary>
    public static double TurnStep(double distance, double weight, double dt, bool afloat = false)
    {
        double stretch = Math.Clamp(distance - Slack, 0, 1);
        return TurnRate * Math.Min(1, Factor(weight, afloat)) * stretch * dt;
    }
}
