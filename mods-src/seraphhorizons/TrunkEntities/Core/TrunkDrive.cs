namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// Driving a trunk on foot, game-independent: a player standing beyond one end of a trunk, facing
/// along it, pushes it like Cartwright's Caravan's sled. W pushes the trunk along its axis away
/// from the player, the far end leading, S draws it back into them (they walk backwards), A and D
/// turn it about its middle, so both ends swing. Speeds are blocks per second here; the game's
/// motion is blocks per 1/60 s (<see cref="PerTick"/>). Ends and axes are <see cref="TrunkPull"/>'s.
/// </summary>
public static class TrunkDrive
{
    /// <summary>A player's walk, blocks per second: the game's walk speed for a player with no
    /// modifiers, about 4.3 blocks a second on flat ground.</summary>
    public const double WalkBlocksPerSecond = 4.3;

    /// <summary>The logs from which a trunk drives at its slowest.</summary>
    public const int HeavyLogs = 48;

    /// <summary>The share of <see cref="WalkBlocksPerSecond"/> a trunk of <see cref="HeavyLogs"/>
    /// or more drives at on land; a 1-log trunk drives at the full walk, linear in logs between.</summary>
    public const double HeavySpeedShare = 0.5;

    /// <summary>The game's raft at <c>speedMultiplier</c> 1, blocks per second:
    /// <c>EntityBoat.SeatsToMotion</c> gives one paddling seat a forward speed of 2 ×
    /// <c>PhysicsFrameTime</c> (1/30) = 1/15 blocks per 1/60 s, so 4 blocks a second before the
    /// water's drag.</summary>
    public const double RaftBlocksPerSecond = 4.0;

    /// <summary>Afloat a trunk drives as on land, but never under this share of
    /// <see cref="RaftBlocksPerSecond"/>.</summary>
    public const double WaterFloorShare = 0.75;

    /// <summary>How fast a light trunk turns, radians per second.</summary>
    public const double TurnRate = 1.0;

    /// <summary>The share of <see cref="TurnRate"/> a trunk of <see cref="HeavyLogs"/> or more
    /// turns at; afloat it never turns slower than <see cref="WaterFloorShare"/> of the rate.</summary>
    public const double HeavyTurnShare = 0.5;

    /// <summary>The ease's time constant, seconds: the drive closes about two thirds of the gap
    /// to the speed asked for in this time, so it is up to speed in a few tenths of a second.</summary>
    public const double EaseSeconds = 0.1;

    /// <summary>How far, blocks, beyond the taken end the driver stands, along the axis.</summary>
    public const double StandOff = 0.6;

    /// <summary>How far, blocks, below the waterline a driver of a floating trunk has their feet
    /// (a swimming player's head at the surface).</summary>
    public const double SwimFeetBelow = 1.0;

    /// <summary>Where a trunk of <paramref name="logs"/> stands between light (1) and
    /// <see cref="HeavyLogs"/> (the heavy share), linearly.</summary>
    public static double Share(int logs, double heavyShare)
    {
        double t = Math.Clamp((logs - 1) / (double)(HeavyLogs - 1), 0, 1);
        return 1 - (1 - heavyShare) * t;
    }

    /// <summary>The drive speed, blocks per second, of a trunk of <paramref name="logs"/>.</summary>
    public static double Speed(int logs, bool afloat)
    {
        double land = WalkBlocksPerSecond * Share(logs, HeavySpeedShare);
        return afloat ? Math.Max(land, WaterFloorShare * RaftBlocksPerSecond) : land;
    }

    /// <summary>The turn rate, radians per second, of a trunk of <paramref name="logs"/>.</summary>
    public static double Turn(int logs, bool afloat)
    {
        double rate = TurnRate * Share(logs, HeavyTurnShare);
        return afloat ? Math.Max(rate, WaterFloorShare * TurnRate) : rate;
    }

    /// <summary>The speed asked for along the axis, blocks per second, positive away from the
    /// driver (the far end leading, <see cref="DriveMotion"/>): W (<paramref name="forward"/>)
    /// +<paramref name="speed"/>, S −, both or neither none.</summary>
    public static double Along(bool forward, bool backward, double speed) =>
        forward == backward ? 0 : forward ? speed : -speed;

    /// <summary>The turn asked for, radians per second: A (<paramref name="left"/>) steers the far
    /// end, the one ahead of the driver, to their left, as a pushed sled turns (so the driver's own
    /// end swings right), which is the yaw rising; D the other way; both or neither none.</summary>
    public static double Turning(bool left, bool right, double rate) =>
        left == right ? 0 : left ? rate : -rate;

    /// <summary><paramref name="current"/> eased towards <paramref name="target"/> over
    /// <paramref name="dt"/> seconds (<see cref="EaseSeconds"/>); it never overshoots.</summary>
    public static double Ease(double current, double target, double dt)
    {
        if (dt <= 0)
            return current;
        double eased = current + (target - current) * (1 - Math.Exp(-dt / EaseSeconds));
        return Math.Abs(eased - target) < 1e-4 ? target : eased;
    }

    /// <summary>Blocks per second as the game's motion, blocks per 1/60 s.</summary>
    public static double PerTick(double blocksPerSecond) => blocksPerSecond / 60;

    /// <summary>The horizontal motion, blocks per 1/60 s, of a trunk at <paramref name="yaw"/>
    /// driven at <paramref name="along"/> blocks per second (positive: end <paramref name="end"/>
    /// leading).</summary>
    public static (double X, double Z) Motion(double yaw, int end, double along)
    {
        var (ax, az) = TrunkPull.Axis(yaw, end);
        double m = PerTick(along);
        return (ax * m, az * m);
    }

    /// <summary>The horizontal motion, blocks per 1/60 s, of a trunk at <paramref name="yaw"/>
    /// whose driver holds end <paramref name="driveEnd"/>, driven at <paramref name="along"/>
    /// blocks per second (<see cref="Along"/>: positive pushes it away from the driver, the other
    /// end leading; negative draws it back into them).</summary>
    public static (double X, double Z) DriveMotion(double yaw, int driveEnd, double along) =>
        Motion(yaw, -driveEnd, along);

    /// <summary>What the driver's legs do: <see cref="EnumDriveGait.Walk"/> pushing (W, or only
    /// turning), <see cref="EnumDriveGait.WalkBack"/> drawing it back (S),
    /// <see cref="EnumDriveGait.Idle"/> otherwise. There is no faster gait: the speed is the
    /// trunk's (<see cref="Speed"/>), not the player's.</summary>
    public static EnumDriveGait Gait(bool forward, bool backward, bool left, bool right) =>
        forward != backward ? (forward ? EnumDriveGait.Walk : EnumDriveGait.WalkBack)
        : left != right ? EnumDriveGait.Walk : EnumDriveGait.Idle;

    /// <summary>Where the driver of end <paramref name="end"/> stands, horizontally, for a trunk
    /// centred at (<paramref name="cx"/>, <paramref name="cz"/>) of <paramref name="length"/>
    /// blocks: <see cref="StandOff"/> beyond that end along the axis.</summary>
    public static (double X, double Z) Stand(double cx, double cz, double yaw, double length, int end)
    {
        var (ax, az) = TrunkPull.Axis(yaw, end);
        double reach = length / 2 + StandOff;
        return (cx + ax * reach, cz + az * reach);
    }

    /// <summary>The yaw the driver of end <paramref name="end"/> faces: along the axis, over the
    /// trunk towards its far end, the way <see cref="DriveMotion"/> pushes it. (The game's view at
    /// yaw y looks along (sin y, cos y).)</summary>
    public static double FacingYaw(double yaw, int end)
    {
        var (ax, az) = TrunkPull.Axis(yaw, end);
        return TrunkPull.Wrap(Math.Atan2(-ax, -az));
    }
}

/// <summary>The driver's gait (<see cref="TrunkDrive.Gait"/>).</summary>
public enum EnumDriveGait
{
    Idle,
    Walk,
    WalkBack,
}
