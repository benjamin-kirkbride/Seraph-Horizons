using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.PressBrake.Core;

/// <summary>
/// The renderer's own lever clock θ, W, presence and class, from the brake each frame (the model's
/// contract, "The work"; <c>BEPressBrake.FrameInput</c>). While the lever is worked with a plate on, θ turns at
/// <see cref="Folding.LeverRadiansPerSecond"/>, and W is predicted at the plate's pace and eased
/// toward the server's (<see cref="HeldWorkFollower"/>), so the model moves at frame rate rather
/// than in the server's synced steps; with no plate on, W is held at the end, 1, while p eases out and k is held; a new
/// plate starts from the server's W (0) with p easing in.
/// </summary>
public sealed class PressBrakeClock
{
    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    private readonly HeldWorkFollower _work = new();

    public double Theta { get; private set; }
    public double Work { get; private set; } = 1;
    public float Presence { get; private set; }
    public int Class { get; private set; }
    /// <summary>The plate on the bed as the server last said (0 once delivered).</summary>
    public int PlateOn { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt, int plateClass, double serverWork, bool held, double leverTurnsPerPlate)
    {
        PlateOn = plateClass is 1 or 2 ? plateClass : 0;
        if (PlateOn != 0)
        {
            if (Class != PlateOn)
            {
                // a new plate (the next one of the same metal, its W starting again from 0, the
                // follower takes at once)
                Class = PlateOn;
                _work.Reset(serverWork);
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            double rate = 0;
            if (held)
            {
                double radians = Folding.LeverRadiansPerSecond * dt;
                Theta += radians;
                rate = Folding.PlatesFor(Folding.LeverRadiansPerSecond, leverTurnsPerPlate);
            }
            _work.Advance(dt, serverWork, rate);
            Work = _work.Work;
        }
        else
        {
            Work = 1;
            _work.Reset(1);
            Presence = Math.Max(0, Presence - dt * EaseRate);
            if (Presence <= 0)
                Class = 0;
        }
        if (Math.Abs(Theta) > 1e6)
            Theta = 0;
    }

    /// <summary>Whether a plate part (<c>platelead</c>, <c>platecopper</c>) is drawn: while that
    /// metal's plate is on the bed. Once the angle is delivered the sheet is gone (it drops as an
    /// item), while the bar and screws ease back with p.</summary>
    public bool ShowsPlate(string requires) =>
        PlateOn != 0 && Presence > 0 && PressBrakeRequires.Plate(Class) == requires;

    /// <summary>The rig's inputs at this moment: θ the lever clock's (ψ its size), the clock's W, k and p.</summary>
    public RigInput Input() =>
        new(Theta, Travel: Math.Abs(Theta), Work: Work, Class: Class, Presence: Presence);
}
