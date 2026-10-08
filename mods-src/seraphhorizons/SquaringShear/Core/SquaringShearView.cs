using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.SquaringShear.Core;

/// <summary>
/// What the squaring shear's renderer reads of the shear, and nothing else: its facing, the parts
/// fitted and their metals, the plate on the table (class k and the server's W), and whether someone
/// works the treadle. The block entity implements it; the renderer turns it into the rig's inputs
/// through <see cref="SquaringShearClock"/>.
/// </summary>
public interface ISquaringShearView
{
    Side Side { get; }

    /// <summary>Whether a part needing a stage's <c>requires</c> (or null) is drawn. The plates are
    /// the clock's (<see cref="SquaringShearClock.ShowsPlate"/>).</summary>
    bool PartFitted(string? requires);

    /// <summary>The fitted blades' metal (<c>iron</c>, <c>steel</c>), or null.</summary>
    string? BladeMetal { get; }

    /// <summary>The fitted gauge's and hold-down's metal (<c>iron</c>, <c>meteoriciron</c>, <c>steel</c>), or null.</summary>
    string? GaugeMetal { get; }

    /// <summary>The plate on the table: 1 lead, 2 copper, 0 none.</summary>
    int PlateClass { get; }

    /// <summary>The server's W: the plate's cut cycle, synced in steps.</summary>
    double CutWork { get; }

    /// <summary>Someone works the treadle (the server's word, or this client's own player holding
    /// right-click on it).</summary>
    bool Held { get; }

    /// <summary>Treadle strokes a plate of class k takes, as the server runs.</summary>
    double StrokesPerPlate(int k);
}

/// <summary>
/// The renderer's own treadle clock θ, W, presence and class, from the view each frame (the model's
/// contract, "The work"): while the treadle is worked with a plate on, θ turns at
/// <see cref="Cutting.StrokeRadiansPerSecond"/> and W advances by θ's strokes over the plate's
/// strokes, never behind the server's and at most <see cref="Snap"/> ahead of it; with no plate on, W
/// is held at the end, 1, while p eases out and k is held; a new plate starts from the server's W (0)
/// with p easing in.
/// </summary>
public sealed class SquaringShearClock
{
    /// <summary>How far the shown W may stray ahead of the server's, plates.</summary>
    public const double Snap = 0.06;

    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    public double Theta { get; private set; }
    public double Work { get; private set; } = 1;
    public float Presence { get; private set; }
    public int Class { get; private set; }
    /// <summary>The plate on the table as the server last said (0 once delivered).</summary>
    public int PlateOn { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt, int plateClass, double serverWork, bool held, double strokesPerPlate)
    {
        PlateOn = plateClass is 1 or 2 ? plateClass : 0;
        if (PlateOn != 0)
        {
            if (Class != PlateOn || serverWork < Work - Snap)
            {
                // a new plate (or the next one of the same metal, which starts again from 0)
                Class = PlateOn;
                Work = serverWork;
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            if (held)
            {
                double radians = Cutting.StrokeRadiansPerSecond * dt;
                Theta += radians;
                Work += Cutting.PlatesFor(radians, strokesPerPlate);
            }
            if (Work < serverWork || Work > serverWork + Snap)
                Work = serverWork;
            Work = Math.Clamp(Work, 0, 1);
        }
        else
        {
            Work = 1;
            Presence = Math.Max(0, Presence - dt * EaseRate);
            if (Presence <= 0)
                Class = 0;
        }
        if (Math.Abs(Theta) > 1e6)
            Theta = 0;
    }

    /// <summary>Whether a plate part (<c>platelead</c>, <c>platecopper</c>) is drawn: while that
    /// metal's plate is on the table. Once the halves are delivered the sheet is gone (they drop as
    /// items), while the hold-down eases back with p.</summary>
    public bool ShowsPlate(string requires) =>
        PlateOn != 0 && Presence > 0 && SquaringShearRequires.Plate(Class) == requires;

    /// <summary>The rig's inputs at this moment: θ the treadle clock's (ψ its size), the clock's W, k and p.</summary>
    public RigInput Input() =>
        new(Theta, Travel: Math.Abs(Theta), Work: Work, Class: Class, Presence: Presence);
}
