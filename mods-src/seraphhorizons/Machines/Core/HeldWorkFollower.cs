namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The W a hand machine's renderer shows while a player holds right-click on it (the press brake's
/// lever, the squaring shear's treadle): predicted at frame rate and steered smoothly toward the
/// server's, never snapped to it.
/// <para>The server advances W on its 50 ms tick, which fires only on the server's own frames (so
/// its steps are uneven, and can be well over 50 ms apart), and syncs it in steps: what reaches the
/// client is a staircase, a step of a twentieth of a second's work or more at a time, late by the
/// trip from the server.
/// Following it directly shows the steps; clamping a prediction to a band around it snaps the shown
/// W at nearly every packet once the band is not much wider than a step (the cause of both
/// machines' stutter). Instead W moves at the work's own pace while held, and the gap to the server
/// is closed by an exponential ease (<see cref="CorrectRate"/>) toward the server's W carried
/// forward at that pace for the time held since it last changed (at most
/// <see cref="MaxExtrapolateSeconds"/>), so a packet that lands on time moves the target by almost
/// nothing. While held, the shown W never runs backward: ahead of the server it slows to
/// <see cref="MinPace"/>, and stops only a whole <see cref="Resync"/> ahead (the server stalled). A
/// gap behind the server over <see cref="Resync"/> is taken at once, and so is a drop in the server's
/// W, which only grows within a job: the next job has begun.</para>
/// </summary>
public sealed class HeldWorkFollower
{
    /// <summary>The share of the gap to the server's W closed a second, as an exponential ease.</summary>
    public const double CorrectRate = 5;

    /// <summary>How long the server's W is carried forward at the work's pace after it last changed:
    /// a few of the server's ticks, so a late packet is waited for and a stopped server is not run
    /// away from.</summary>
    public const double MaxExtrapolateSeconds = 0.25;

    /// <summary>While held and ahead of the server, the shown W moves at no less than this share of
    /// the work's pace, to let the server catch up without stopping (unless a whole
    /// <see cref="Resync"/> ahead).</summary>
    public const double MinPace = 0.5;

    /// <summary>A gap to the server this large (in W) is taken at once rather than eased, except ahead
    /// of it while held, where W waits for the server instead.</summary>
    public const double Resync = 0.25;

    private double _lastServer = double.NaN;
    private double _sinceServer;

    /// <summary>The W to show.</summary>
    public double Work { get; private set; }

    /// <summary>Starts again at the server's <paramref name="serverWork"/> (a new job).</summary>
    public void Reset(double serverWork)
    {
        Work = Math.Clamp(serverWork, 0, 1);
        _lastServer = serverWork;
        _sinceServer = 0;
    }

    /// <summary>Steps by <paramref name="dt"/> seconds toward the server's <paramref name="serverWork"/>,
    /// with the work running at <paramref name="rate"/> W a second (0 while no one holds).</summary>
    public void Advance(double dt, double serverWork, double rate)
    {
        dt = Math.Max(0, dt);
        rate = Math.Max(0, rate);
        serverWork = Math.Clamp(serverWork, 0, 1);
        if (serverWork < _lastServer)
        {
            // the server's W only grows within a job: a drop is the next one, from its start
            Reset(serverWork);
            return;
        }
        if (serverWork != _lastServer || rate == 0)
        {
            // carried forward only for the time held since the server's W last changed
            _lastServer = serverWork;
            _sinceServer = 0;
        }
        else
            _sinceServer += dt;
        double target = Math.Min(1, serverWork + rate * Math.Min(_sinceServer, MaxExtrapolateSeconds));
        double predicted = Work + rate * dt;
        double gap = target - predicted;
        if (gap > Resync || rate == 0 && gap < -Resync)
        {
            Work = target;
            return;
        }
        double correction = gap * (1 - Math.Exp(-CorrectRate * dt));
        if (rate > 0)
            // held: slow down to let the server catch up, but never turn back; stop only when a
            // whole Resync ahead (the server has stalled)
            correction = Math.Max(correction, gap < -Resync ? -rate * dt : -(1 - MinPace) * rate * dt);
        Work = Math.Clamp(predicted + correction, 0, 1);
    }
}
