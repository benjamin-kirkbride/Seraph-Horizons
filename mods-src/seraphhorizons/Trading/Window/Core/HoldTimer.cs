namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>Where a hold is.</summary>
public enum HoldState
{
    Idle,
    /// <summary>The button is down and the ring filling.</summary>
    Holding,
    /// <summary>The ring is full and the trade sent; waiting for the server's answer.</summary>
    Waiting,
    /// <summary>The server refused (or did not answer): nothing more until the button is let go.</summary>
    Blocked,
}

/// <summary>
/// Hold to trade (the trade window, #436): no cart and no deal button, every trade happens at once
/// after holding the mouse on a good (or the sell button) for <see cref="Seconds"/>, as Carry On's
/// hold to pick up. One completed hold is one trade unit; holding on after the server confirmed it
/// starts the next one, so a stack sells by holding. A refusal stops it until the button is let go.
/// Game-independent: the window feeds it presses, releases, frame times and the server's answers,
/// and draws <see cref="Progress"/>.
/// </summary>
public sealed class HoldTimer(double seconds = HoldTimer.DefaultSeconds, double waitSeconds = HoldTimer.DefaultWaitSeconds)
{
    /// <summary>Carry On's default interact delay.</summary>
    public const double DefaultSeconds = 0.8;

    /// <summary>How long an answer may take before the hold gives up.</summary>
    public const double DefaultWaitSeconds = 3;

    private double _held, _waited;
    private bool _down;

    public double Seconds { get; } = Math.Max(0.05, seconds);

    public HoldState State { get; private set; } = HoldState.Idle;

    /// <summary>What is being held (the window's own key: a shelf slot, the sell button, a map).</summary>
    public string? Target { get; private set; }

    /// <summary>0 to 1 while holding, 1 while waiting, else 0.</summary>
    public double Progress => State switch
    {
        HoldState.Holding => Math.Clamp(_held / Seconds, 0, 1),
        HoldState.Waiting => 1,
        _ => 0,
    };

    /// <summary>How many units completed and confirmed in this hold.</summary>
    public int Completed { get; private set; }

    /// <summary>The button went down on <paramref name="target"/>: a new hold.</summary>
    public void Press(string target)
    {
        Target = target;
        _down = true;
        _held = 0;
        _waited = 0;
        Completed = 0;
        State = HoldState.Holding;
    }

    /// <summary>The button came up (or the mouse left the target): the hold ends; a trade already sent
    /// still gets its answer.</summary>
    public void Release()
    {
        _down = false;
        if (State != HoldState.Waiting) Reset();
    }

    private void Reset()
    {
        State = HoldState.Idle;
        Target = null;
        _held = 0;
        _waited = 0;
    }

    /// <summary>Advances the hold by <paramref name="dt"/> seconds; true once, when the ring fills
    /// and the trade should be sent (the timer then waits for <see cref="Confirmed"/> or
    /// <see cref="Refused"/>).</summary>
    public bool Update(double dt)
    {
        switch (State)
        {
            case HoldState.Holding:
                _held += Math.Max(0, dt);
                if (_held < Seconds) return false;
                State = HoldState.Waiting;
                _waited = 0;
                return true;
            case HoldState.Waiting:
                _waited += Math.Max(0, dt);
                if (_waited >= waitSeconds) Refused();
                return false;
            default:
                return false;
        }
    }

    /// <summary>The server made the trade: still held, the next unit starts.</summary>
    public void Confirmed()
    {
        if (State != HoldState.Waiting) return;
        Completed++;
        if (_down)
        {
            State = HoldState.Holding;
            _held = 0;
        }
        else Reset();
    }

    /// <summary>The server refused: no more until the button is let go and pressed again.</summary>
    public void Refused()
    {
        if (_down)
        {
            State = HoldState.Blocked;
            _held = 0;
        }
        else Reset();
    }
}
