using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>
/// The renderer's own clock θ, W, presence and class, from the riddle each frame (the model's contract,
/// "Presence"; <c>BERiddle.FrameInput</c>). A charge goes on at p 1 at once and is no longer drawn (k 0) once taken off, since the
/// rig's gauges are right only at p 1. While the riddling runs, θ turns at
/// <see cref="Riddling.ShakeRadiansPerSecond"/>, and W is predicted at the charge's pace and eased toward
/// the server's (<see cref="HeldWorkFollower"/>), so the model moves at frame rate; once riddled, W is 1
/// and the clock stops where it is.
/// </summary>
public sealed class RiddleClock
{
    private readonly HeldWorkFollower _work = new();

    public double Theta { get; private set; }
    public double Work { get; private set; }
    public int Class { get; private set; }
    public float Presence => Class == 1 ? 1 : 0;

    /// <summary>Steps the clock by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt, bool chargeOn, double serverWork, bool held, double shakesPerCharge)
    {
        if (!chargeOn)
        {
            Class = 0;
            Work = 0;
            _work.Reset(0);
            return;
        }
        if (Class != 1)
        {
            Class = 1;
            _work.Reset(serverWork);
        }
        double rate = 0;
        bool running = held && serverWork < 1 - 1e-9;
        if (running)
        {
            Theta += Riddling.ShakeRadiansPerSecond * dt;
            rate = Riddling.ChargesFor(Riddling.ShakeRadiansPerSecond, shakesPerCharge);
        }
        _work.Advance(dt, serverWork, rate);
        Work = serverWork >= 1 - 1e-9 ? 1 : _work.Work;
        if (Math.Abs(Theta) > 1e6)
            Theta = 0;
    }

    /// <summary>Whether a part needing <paramref name="requires"/> is drawn: the frame always, the riddle
    /// when fitted, the charge while one is on (<paramref name="chargeOn"/>: being riddled, or riddled with
    /// its oversize still on the mesh), the fines while the box holds them.</summary>
    public static bool Shows(string? requires, bool riddleFitted, bool chargeOn, bool boxHoldsFines) => requires switch
    {
        null => true,
        RiddleRequires.Riddle => riddleFitted,
        RiddleRequires.Charge => chargeOn && riddleFitted,
        RiddleRequires.Fines => boxHoldsFines,
        _ => false,
    };

    /// <summary>The rig's inputs at this moment: θ the clock's (ψ its size), W, k and p.</summary>
    public RigInput Input() =>
        new(Theta, Travel: Math.Abs(Theta), Work: Work, Class: Class, Presence: Presence);
}
