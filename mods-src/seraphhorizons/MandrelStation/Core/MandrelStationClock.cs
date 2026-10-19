using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>
/// The renderer's θ, W, presence and class, from the station each frame (the model's contract, "The
/// work"; <c>BEMandrelStation.FrameInput</c>). For a hollow, blows land as the server says; the clock eases W and the hammer's clock θ (2π a
/// blow) to them over about a tenth of a second (<see cref="WorkEaseRate"/>), so the walls close and the
/// tube creeps rather than jump; a new hollow starts from the server's W (0) with p easing in. For a
/// spring (class 3) p is 1 at once (the spring's parts are drawn only at p 1: a side's stretch is 1 − p ×
/// E), θ turns at <see cref="Winding.RadiansPerSecond"/> while held, and W is predicted at the rod's pace
/// and eased to the server's (<see cref="HeldWorkFollower"/>), as the press brake's. With no job on, W is
/// held at the end, 1, while p eases out and k is held, and the work is not drawn.
/// </summary>
public sealed class MandrelStationClock
{
    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    /// <summary>The share of the way to the server's W and blows covered a second, as an
    /// exponential ease (about 0.1 s to settle).</summary>
    public const double WorkEaseRate = 25;

    private readonly HeldWorkFollower _wind = new();

    public double Theta { get; private set; }
    public double Work { get; private set; } = 1;
    public float Presence { get; private set; }
    public int Class { get; private set; }
    /// <summary>The job on the mandrel as the server last said (0 once delivered).</summary>
    public int JobOn { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds: <paramref name="jobClass"/> the job on (1,
    /// 2 a hollow, 3 a spring, 0 none), the server's W and blows, and for a spring whether someone winds
    /// and the turns a rod takes.</summary>
    public void Advance(float dt, int jobClass, double serverWork, int serverBlows, bool held = false, double turnsPerRod = 0)
    {
        JobOn = jobClass is 1 or 2 or Winding.SpringClass ? jobClass : 0;
        serverWork = Math.Clamp(serverWork, 0, 1);
        if (JobOn == Winding.SpringClass)
        {
            if (Class != JobOn || serverWork < _wind.Work - MinWindDrop)
            {
                // a new rod: the spring's parts are drawn at p 1 only, so p is 1 from the start
                Class = JobOn;
                _wind.Reset(serverWork);
            }
            Presence = 1;
            double rate = 0;
            if (held)
            {
                Theta += Winding.RadiansPerSecond * dt;
                rate = Winding.RodsFor(Winding.RadiansPerSecond, turnsPerRod);
            }
            _wind.Advance(dt, serverWork, rate);
            Work = _wind.Work;
        }
        else if (JobOn != 0)
        {
            double targetTheta = 2 * Math.PI * Math.Max(0, serverBlows);
            if (Class != JobOn || Presence <= 0 || serverWork < Work - 1e-9)
            {
                // a new hollow (or the next of the same metal, which starts again from 0)
                Class = JobOn;
                Work = serverWork;
                Theta = targetTheta;
            }
            Presence = Math.Min(1, Presence + dt * EaseRate);
            double share = 1 - Math.Exp(-WorkEaseRate * Math.Max(0, dt));
            Work += (serverWork - Work) * share;
            Theta += (targetTheta - Theta) * share;
            if (Math.Abs(serverWork - Work) < 1e-4)
                Work = serverWork;
            Work = Math.Clamp(Work, 0, 1);
        }
        else
        {
            Work = 1;
            _wind.Reset(1);
            Presence = Math.Max(0, Presence - dt * EaseRate);
            if (Presence <= 0)
                Class = 0;
        }
        if (Math.Abs(Theta) > 1e6)
            Theta = 0;
    }

    // A drop in the server's W this large is a new rod, not the follower running a little ahead.
    private const double MinWindDrop = 0.3;

    /// <summary>Whether a work part (<c>hollowlead</c>, <c>hollowcopper</c>, <c>rodsteel</c>) is drawn:
    /// while that job is on the mandrel, and a spring's only at p 1. Once the job is delivered its output is
    /// items, and the work is not drawn while p eases out.</summary>
    public bool ShowsWork(string requires) =>
        JobOn != 0 && Presence > 0 && MandrelRequires.Work(Class) == requires
        && (Class != Winding.SpringClass || Presence >= 1);

    /// <summary>Whether a hollow part is drawn (<see cref="ShowsWork"/> for a hollow's <c>requires</c>).</summary>
    public bool ShowsHollow(string requires) => MandrelRequires.Hollow(Class) == requires && ShowsWork(requires);

    /// <summary>The rig's inputs at this moment: θ the hammer's or the winding's clock (ψ its size), the
    /// clock's W, k and p.</summary>
    public RigInput Input() =>
        new(Theta, Travel: Math.Abs(Theta), Work: Work, Class: Class, Presence: Presence);
}
