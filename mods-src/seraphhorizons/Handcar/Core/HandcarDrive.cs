namespace SeraphHorizons.Mod.Handcar.Core;

/// <summary>One seat's state for a tick: whether a rider stands there, the movement keys they hold
/// (the game hands a mounted player's keys to the seat), and which way they face: +1 towards the
/// car's front (Yang's EndA, the way the car goes when it goes forward), -1 towards its back.</summary>
public readonly record struct SeatInput(bool Occupied, bool Forward, bool Backward, bool Left, bool Right, int Facing);

/// <summary>What the car's drive is given for a tick, in the terms of Yang's standard-gauge drive
/// (<c>EntityStandardGaugeLocomotive.TryComputeConvoyDrive</c>): a throttle of -1, 0 or +1 towards the
/// front, the acceleration while it pulls and the top speed it pulls to (blocks a second). Yang does
/// the rest: with no throttle the car coasts down by its drag; with a throttle against the way the
/// car rolls it brakes to a stop and only then turns its drive around.</summary>
public readonly record struct DriveCommand(int Throttle, double Acceleration, double TopSpeed, int Pumpers, bool Locked);

/// <summary>
/// The handcar's drive: who pumps, which way, how hard. A rider pumps while holding forward or
/// back (not both), which pushes the car the way they face or the other way. Riders pumping the
/// same way add up: the thrust and the top speed are those of the number pumping. Two pumping
/// against each other lock the beam: the car brakes to a stop and stays there.
/// </summary>
public static class HandcarDrive
{
    /// <summary>Yang's speed under which a car counts as stopped (<c>StopEpsilon</c>).</summary>
    public const double StopEpsilon = 0.02;

    /// <summary>-1, 0 or +1: the way a seat's rider pushes the car, towards its front.</summary>
    public static int PumpDirection(in SeatInput seat) =>
        !seat.Occupied || seat.Forward == seat.Backward ? 0 : (seat.Forward ? 1 : -1) * Math.Sign(seat.Facing);

    public static bool Pumping(in SeatInput seat) => PumpDirection(seat) != 0;

    /// <summary>The tick's drive. <paramref name="speed"/> is the car's speed (unsigned) and
    /// <paramref name="motion"/> the way it rolls (+1 towards the front, -1 back); weights are Yang's
    /// (the convoy's, and the bare car's own).</summary>
    public static DriveCommand Command(IReadOnlyList<SeatInput> seats, double speed, int motion, int weight, int selfWeight, HandcarConfig config)
    {
        int sum = 0, pumping = 0;
        foreach (var seat in seats)
        {
            int d = PumpDirection(seat);
            if (d == 0)
                continue;
            pumping++;
            sum += d;
        }
        if (sum == 0)
        {
            if (pumping == 0)
                return new DriveCommand(0, 0, 0, 0, false);
            // pumping against each other: brake while rolling, then hold still
            return new DriveCommand(speed > StopEpsilon ? -Math.Sign(motion == 0 ? 1 : motion) : 0, 0, 0, pumping, true);
        }
        int n = Math.Abs(sum);
        return new DriveCommand(Math.Sign(sum), n >= 2 ? config.AccelerationTwo : config.AccelerationOne,
                                TopSpeed(n, weight, selfWeight, config), n, false);
    }

    /// <summary>The top speed <paramref name="pumpers"/> riders push the car to, less
    /// <see cref="HandcarConfig.SpeedLossPerWeight"/> per unit of weight above the bare car's.</summary>
    public static double TopSpeed(int pumpers, int weight, int selfWeight, HandcarConfig config)
    {
        if (pumpers <= 0)
            return 0;
        double top = pumpers >= 2 ? config.TopSpeedTwo : config.TopSpeedOne;
        return Math.Max(config.MinTopSpeed, top - config.SpeedLossPerWeight * Math.Max(0, weight - selfWeight));
    }

    /// <summary>Satiety a rider spends pumping for <paramref name="seconds"/>, as the hunger
    /// behaviour charges sprinting: <paramref name="perSecond"/> times the calendar's speed (its
    /// speed of time times its speed multiplier, over 30). The game multiplies it by the player's
    /// hunger rate when it is spent.</summary>
    public static double Satiety(double seconds, double perSecond, double speedOfTime, double calendarSpeedMul) =>
        Math.Max(0, seconds) * perSecond * speedOfTime * calendarSpeedMul / 30.0;
}

/// <summary>
/// The branch at the next switch: Yang's turn lever, three places (its <c>turnLeverPhase</c>: 0
/// left, 1 straight, 2 right, as seen facing the car's front). A rider's press of left or right
/// moves it one place that way, as they face it.
/// </summary>
public static class HandcarTurn
{
    public const int Left = 0, Straight = 1, Right = 2;

    /// <summary>The lever's step for a rider's press: -1 (towards left), 0 or +1, in the car's frame.</summary>
    public static int Step(bool left, bool right, int facing) =>
        left == right ? 0 : (left ? -1 : 1) * Math.Sign(facing);

    /// <summary>The lever moved by <paramref name="step"/>, kept in its three places.</summary>
    public static int Move(int phase, int step) => Math.Clamp(Math.Clamp(phase, Left, Right) + step, Left, Right);
}
