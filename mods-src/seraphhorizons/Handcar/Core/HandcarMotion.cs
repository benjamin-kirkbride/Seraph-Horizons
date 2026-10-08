namespace SeraphHorizons.Mod.Handcar.Core;

/// <summary>
/// The distance a car has rolled, measured on a client from where it is drawn frame by frame: each
/// step's length, signed by whether it went towards the car's front (its yaw). Everything the pump
/// moves (wheels, gears, beam, pitman, and both riders' arms) is set from this one number, so they
/// stay in step at any speed. A step longer than <see cref="MaxStep"/> (a teleport, a reload) adds
/// nothing.
/// </summary>
public sealed class TravelTracker
{
    public const double MaxStep = 4.0;

    private bool _started;
    private double _x, _y, _z;

    /// <summary>The signed distance rolled so far, blocks: + towards the front.</summary>
    public double Distance { get; private set; }

    /// <summary>Takes the car's position and yaw (VS's: the front is (sin yaw, cos yaw) in x and z);
    /// returns the step added.</summary>
    public double Update(double x, double y, double z, double yaw)
    {
        if (!_started)
        {
            (_x, _y, _z, _started) = (x, y, z, true);
            return 0;
        }
        double dx = x - _x, dy = y - _y, dz = z - _z;
        (_x, _y, _z) = (x, y, z);
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-9 || length > MaxStep)
            return 0;
        double along = dx * Math.Sin(yaw) + dz * Math.Cos(yaw);
        if (Math.Abs(along) < 1e-12)
            return 0;
        double step = along > 0 ? length : -length;
        Distance += step;
        return step;
    }
}

/// <summary>The pump's cycle: one stroke of the beam is <c>distancePerCycle</c> blocks of travel
/// (the rig's <c>cycle</c>), and the animations' frame is where in it the car is.</summary>
public static class HandcarPhase
{
    /// <summary>0 up to 1: how far through its stroke the pump is.</summary>
    public static double Of(double distance, double distancePerCycle)
    {
        if (distancePerCycle <= 0)
            return 0;
        double c = distance / distancePerCycle;
        double phase = c - Math.Floor(c);
        return phase >= 1 ? 0 : phase;
    }

    /// <summary>The animation frame for a phase, in 0 up to (not reaching) <paramref name="frames"/>,
    /// kept clear of the end so the game's animator never counts a turn round.</summary>
    public static float Frame(double phase, int frames) =>
        (float)Math.Clamp(phase * frames, 0, frames - 1e-3);
}

/// <summary>
/// A rider's pumping effort coming in and going out: linear over <c>seconds</c>, eased
/// (smoothstep). The client sets the grip animation's and the pump animation's weights from it so
/// they always add up to the rider's mount fade: the hands stay on the handle all the while.
/// </summary>
public sealed class EffortFade
{
    public double Value { get; private set; }

    public double Advance(bool on, double dt, double seconds)
    {
        double step = seconds <= 0 ? 1 : Math.Max(0, dt) / seconds;
        Value = on ? Math.Min(1, Value + step) : Math.Max(0, Value - step);
        return Value;
    }

    /// <summary>The value smoothed: no jolt where it starts or ends.</summary>
    public double Eased => Value * Value * (3 - 2 * Value);

    /// <summary>The grip's and the pump's easing for a rider mounted <paramref name="mount"/> (0..1)
    /// with this effort.</summary>
    public (float Grip, float Pump) Weights(double mount)
    {
        double m = Math.Clamp(mount, 0, 1);
        return ((float)(m * (1 - Eased)), (float)(m * Eased));
    }
}

/// <summary>
/// Easing set ahead of the game's own step. Each frame the game's animator moves an active
/// animation's easing a fraction k of the way to 1 and a stopped one's a fraction k of the way to 0,
/// k being the frame's time times the animation's speed times its ease-in (or ease-out) speed
/// (<c>RunningAnimation.Progress</c>). Setting it to <see cref="Before"/> beforehand leaves it at the
/// wanted value once the game has stepped.
/// </summary>
public static class AnimationEasing
{
    public static float Before(double target, bool active, double step)
    {
        double k = Math.Clamp(step, 0, 0.999);
        double e = active ? (target - k) / (1 - k) : target / (1 - k);
        return (float)Math.Clamp(e, 0, 1);
    }

    /// <summary>The game's own step, for the tests: easing <paramref name="e"/> after a frame.</summary>
    public static double GameStep(double e, bool active, double step) =>
        active ? Math.Min(1, e + (1 - e) * step) : Math.Max(0, e - e * step);
}
