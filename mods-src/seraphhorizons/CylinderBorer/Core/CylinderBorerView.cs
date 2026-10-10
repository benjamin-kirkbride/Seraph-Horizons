using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>
/// What the cylinder borer's renderer reads of the borer, and nothing else: its facing, the parts
/// fitted, the job in the cradle (class k and the server's W), whether it runs, the shaft and the
/// oil. The block entity implements it; the renderer turns it into the rig's inputs through
/// <see cref="CylinderBorerClock"/>.
/// </summary>
public interface ICylinderBorerView
{
    Side Side { get; }

    /// <summary>Whether a part needing a stage's <c>requires</c> (or null) is drawn. The castings
    /// are the clock's (<see cref="CylinderBorerClock.ShowsBlank"/>).</summary>
    bool PartFitted(string? requires);

    /// <summary>The casting in the cradle: 1 iron, 2 steel, 0 none.</summary>
    int JobClass { get; }

    /// <summary>The server's W: the head's travel through this casting, blocks, synced in steps.</summary>
    double JobWork { get; }

    /// <summary>Complete, a casting in and the shaft fast enough.</summary>
    bool Running { get; }

    float ShaftAngle { get; }
    float ShaftSpeed { get; }

    /// <summary>The oil tank's fill, 0..1 (1 without MachineOil).</summary>
    double OilFill { get; }

    /// <summary>Axle turns per block of class k, as the server runs.</summary>
    double TurnsPerBlock(int k);
}

/// <summary>
/// The renderer's own W, presence and class, from the view each frame (the model's contract, README
/// "Rig"): W advances with the shaft while the borer runs, never behind the server's and at most
/// <see cref="Snap"/> ahead of it. A casting is drawn only while it is in the cradle; once it is gone
/// (bored and handed out, or taken back) W is held where it was and k with it while p eases out, so
/// the cutter head, the crosshead and the weight come back to their rest with the cradle empty; a new
/// casting starts from the server's W (0) with p easing in.
/// </summary>
public sealed class CylinderBorerClock
{
    /// <summary>How far the shown W may stray ahead of the server's, blocks (a sixteenth of the travel).</summary>
    public const double Snap = 1.0 / 16;

    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    private bool _on;

    public double Work { get; private set; }
    public float Presence { get; private set; }
    public int Class { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds, the shaft having turned
    /// <paramref name="radians"/> (unsigned) since the last step.</summary>
    public void Advance(float dt, double radians, int jobClass, double serverWork, bool running, double turnsPerBlock)
    {
        _on = jobClass is 1 or 2;
        if (_on)
        {
            if (Class != jobClass)
            {
                Class = jobClass;
                Work = serverWork;
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            if (running)
                Work += Boring.BlocksFor(radians, turnsPerBlock);
            if (Work < serverWork || Work > serverWork + Snap)
                Work = serverWork;
            Work = Math.Clamp(Work, 0, Boring.Travel);
            return;
        }
        Presence = Math.Max(0, Presence - dt * EaseRate);
        if (Presence <= 0)
        {
            Class = 0;
            Work = 0;
        }
    }

    /// <summary>Whether a casting's parts (<c>blankiron</c>, <c>blanksteel</c>: the casting, its
    /// skin and its chains) are drawn: only while that metal's casting is in the cradle.</summary>
    public bool ShowsBlank(string requires) =>
        _on && CylinderBorerRequires.Blank(Class) == requires;

    /// <summary>The rig's inputs at this moment: θ and ψ the shaft's, the clock's W, k and p, and
    /// the oil's fill.</summary>
    public RigInput Input(double theta, double psi, double oil) =>
        new(theta, Travel: psi, Work: Work, Class: Class, Presence: Presence, Oil: oil);
}
