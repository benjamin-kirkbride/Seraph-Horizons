using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>
/// What the mandrel station's renderer reads of the station, and nothing else: its facing, the
/// mandrel fitted and its metal, the hollow on it (class k), the server's W and the blows struck on
/// it. The block entity implements it; the renderer turns it into the rig's inputs through
/// <see cref="MandrelStationClock"/>.
/// </summary>
public interface IMandrelStationView
{
    Side Side { get; }

    /// <summary>Whether a part needing <c>requires</c> (null or <c>mandrel</c>) is drawn. The
    /// hollows are the clock's (<see cref="MandrelStationClock.ShowsHollow"/>).</summary>
    bool PartFitted(string? requires);

    /// <summary>The fitted mandrel's metal (<c>iron</c>, <c>meteoriciron</c>, <c>steel</c>), or null.</summary>
    string? MandrelMetal { get; }

    /// <summary>The hollow on the mandrel: 1 lead, 2 copper, 0 none.</summary>
    int HollowClass { get; }

    /// <summary>The server's W: the forging of the hollow on, 0..1.</summary>
    double ForgeWork { get; }

    /// <summary>The blows struck on the hollow on so far.</summary>
    int Blows { get; }
}

/// <summary>
/// The renderer's θ, W, presence and class, from the view each frame (the model's contract, "The
/// work"). Blows land as the server says; the clock eases W and the hammer's clock θ (2π a blow) to
/// them over about a tenth of a second (<see cref="WorkEaseRate"/>), so the walls close and the tube
/// creeps rather than jump. With no hollow on, W is held at the end, 1, while p eases out and k is
/// held; a new hollow starts from the server's W (0) with p easing in.
/// </summary>
public sealed class MandrelStationClock
{
    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    /// <summary>The share of the way to the server's W and blows covered a second, as an
    /// exponential ease (about 0.1 s to settle).</summary>
    public const double WorkEaseRate = 25;

    public double Theta { get; private set; }
    public double Work { get; private set; } = 1;
    public float Presence { get; private set; }
    public int Class { get; private set; }
    /// <summary>The hollow on the mandrel as the server last said (0 once delivered).</summary>
    public int HollowOn { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt, int hollowClass, double serverWork, int serverBlows)
    {
        HollowOn = hollowClass is 1 or 2 ? hollowClass : 0;
        double targetTheta = 2 * Math.PI * Math.Max(0, serverBlows);
        if (HollowOn != 0)
        {
            serverWork = Math.Clamp(serverWork, 0, 1);
            if (Class != HollowOn || Presence <= 0 || serverWork < Work - 1e-9)
            {
                // a new hollow (or the next of the same metal, which starts again from 0)
                Class = HollowOn;
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
            Presence = Math.Max(0, Presence - dt * EaseRate);
            if (Presence <= 0)
                Class = 0;
        }
    }

    /// <summary>Whether a hollow part (<c>hollowlead</c>, <c>hollowcopper</c>) is drawn: while that
    /// metal's hollow is on the mandrel. Once the sections are delivered they are items on the
    /// ground, and the work is not drawn while p eases out.</summary>
    public bool ShowsHollow(string requires) =>
        HollowOn != 0 && Presence > 0 && MandrelRequires.Hollow(Class) == requires;

    /// <summary>The rig's inputs at this moment: θ the hammer's clock (ψ its size), the clock's W, k and p.</summary>
    public RigInput Input() =>
        new(Theta, Travel: Math.Abs(Theta), Work: Work, Class: Class, Presence: Presence);
}
