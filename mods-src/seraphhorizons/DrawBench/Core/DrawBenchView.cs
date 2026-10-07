using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>
/// What the draw bench's renderer reads of the bench, and nothing else: its facing, the parts
/// fitted, the die's metal, the job on the bench (class k and the server's W), whether it runs, the
/// shaft and the oil. The block entity implements it; the renderer turns it into the rig's inputs
/// through <see cref="DrawBenchClock"/>.
/// </summary>
public interface IDrawBenchView
{
    Side Side { get; }

    /// <summary>Whether a part needing a stage's <c>requires</c> (or null) is drawn. The billets
    /// are the clock's (<see cref="DrawBenchClock.ShowsBillet"/>).</summary>
    bool PartFitted(string? requires);

    /// <summary>The fitted die's metal (<c>iron</c>, <c>steel</c>), or null.</summary>
    string? DieMetal { get; }

    /// <summary>The hollow section on the bench: 1 lead, 2 copper, 0 none.</summary>
    int JobClass { get; }

    /// <summary>The server's W: pipe sections drawn of this hollow, synced in steps.</summary>
    double JobWork { get; }

    /// <summary>Complete, a hollow on and the shaft fast enough.</summary>
    bool Running { get; }

    float ShaftAngle { get; }
    float ShaftSpeed { get; }

    /// <summary>The oil tank's fill, 0..1 (1 without MachineOil).</summary>
    double OilFill { get; }

    /// <summary>Axle turns per section of class k, as the server runs.</summary>
    double TurnsPerSection(int k);
}

/// <summary>
/// The renderer's own W, presence and class, from the view each frame (the model's contract,
/// "The work"): W advances with the shaft while the bench runs, never behind the server's and at
/// most <see cref="Snap"/> ahead of it; with no hollow on it is held at the end, 4, while p eases
/// out and k is held, so the sections lie in the trough and the hollow, follower and spring come back;
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
