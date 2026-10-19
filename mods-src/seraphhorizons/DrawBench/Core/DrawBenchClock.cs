using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>
/// The renderer's own W, presence and class, from the bench each frame (<c>BEDrawBench.FrameInput</c>) (the model's contract,
/// "The work"): W advances with the shaft while the bench runs, never behind the server's and at
/// most <see cref="Snap"/> ahead of it; with no hollow on it is held at the end, 4, while p eases
/// out and k is held, so the hollow, follower and spring come back (the sections, handed out, are hidden);
/// a new hollow starts from the server's W (0) with p easing in.
/// </summary>
public sealed class DrawBenchClock
{
    /// <summary>How far the shown W may stray ahead of the server's, sections.</summary>
    public const double Snap = 0.1;

    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    public double Work { get; private set; } = Drawing.SectionsPerHollow;
    public float Presence { get; private set; }
    public int Class { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds, the shaft having turned
    /// <paramref name="radians"/> (unsigned) since the last step.</summary>
    public void Advance(float dt, double radians, int jobClass, double serverWork, bool running, double turnsPerSection)
    {
        bool on = jobClass is 1 or 2;
        if (on)
        {
            if (Class != jobClass)
            {
                Class = jobClass;
                Work = serverWork;
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            if (running)
                Work += Drawing.SectionsFor(radians, turnsPerSection);
            if (Work < serverWork || Work > serverWork + Snap)
                Work = serverWork;
            Work = Math.Clamp(Work, 0, Drawing.SectionsPerHollow);
            return;
        }
        Work = Drawing.SectionsPerHollow;
        Presence = Math.Max(0, Presence - dt * EaseRate);
        if (Presence <= 0)
            Class = 0;
    }

    /// <summary>Whether a billet part (<c>billetlead</c>, <c>billetcopper</c>) is drawn: while
    /// its metal is the shown class and present at all.</summary>
    public bool ShowsBillet(string requires) =>
        Presence > 0 && DrawBenchRequires.Billet(Class) == requires;

    /// <summary>The rig's inputs at this moment: θ and ψ the shaft's, the clock's W, k and p, and
    /// the oil's fill.</summary>
    public RigInput Input(double theta, double psi, double oil) =>
        new(theta, Travel: psi, Work: Work, Class: Class, Presence: Presence, Oil: oil);
}
