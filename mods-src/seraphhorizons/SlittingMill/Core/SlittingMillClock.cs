using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.SlittingMill.Core;

/// <summary>
/// The renderer's own W, presence and class, from the mill each frame (<c>BESlittingMill.FrameInput</c>; the
/// model's contract, README "Rig schema"): W advances with the shaft while the mill runs, never behind the
/// server's and at most <see cref="Snap"/> ahead of it. The work (the plate, its rods, nails and strips) is drawn
/// only while a plate is on: once its nails and strips are handed out (or it is taken back) W is held where it was
/// while p eases out, so the clutch is thrown out with the knife down and nothing of the work grows back on the
/// table; a new plate starts from the server's W (0) with p easing in. Every pattern of the stepping train repeats
/// over a plate, so W going from 5 back to 0 moves nothing.
/// </summary>
public sealed class SlittingMillClock
{
    /// <summary>How far the shown W may stray ahead of the server's, strokes.</summary>
    public const double Snap = 0.25;

    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    private bool _on;

    public double Work { get; private set; }
    public float Presence { get; private set; }
    public int Class { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds, the shaft having turned
    /// <paramref name="radians"/> (unsigned) since the last step; <paramref name="plateOn"/> whether a plate is
    /// on the table, <paramref name="serverWork"/> its W as the server last said.</summary>
    public void Advance(float dt, double radians, bool plateOn, double serverWork, bool running, double turnsPerStroke)
    {
        _on = plateOn;
        if (_on)
        {
            if (Class != 1)
            {
                Class = 1;
                Work = serverWork;
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            if (running)
                Work += Slitting.StrokesFor(radians, turnsPerStroke);
            if (Work < serverWork || Work > serverWork + Snap)
                Work = serverWork;
            Work = Math.Clamp(Work, 0, Slitting.HandOut);
            return;
        }
        Presence = Math.Max(0, Presence - dt * EaseRate);
        if (Presence <= 0)
        {
            Class = 0;
            Work = 0;
        }
    }

    /// <summary>Whether the work's parts (<c>plate</c>) are drawn: only while a plate is on.</summary>
    public bool ShowsWork => _on && Class == 1;

    /// <summary>The rig's inputs at this moment: θ and ψ the shaft's, the clock's W, k and p, and the oil's fill.</summary>
    public RigInput Input(double theta, double psi, double oil) =>
        new(theta, Travel: psi, Work: Work, Class: Class, Presence: Presence, Oil: oil);
}
