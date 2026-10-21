using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>What a steel rod (or another mandrel rod) does when it is right-clicked on the station.</summary>
public enum RodVerdict
{
    /// <summary>Not a rod the station knows: the item's own business.</summary>
    NotARod,
    /// <summary>The station has no mandrel: the rod is fitted as the mandrel.</summary>
    Mandrel,
    /// <summary>A mandrel is fitted, and this rod is not wound (not steel, or no springs in this world).</summary>
    AlreadyFitted,
    /// <summary>A hot steel rod on a fitted, bare mandrel: a spring job.</summary>
    Winds,
    /// <summary>Work is already on the mandrel.</summary>
    Occupied,
    /// <summary>A steel rod below its working temperature: refused.</summary>
    TooCold,
}

/// <summary>
/// The spring job's arithmetic: one hot steel rod (<see cref="SteelRod"/>) goes on a fitted, bare mandrel
/// and is wound round it by hand, with tongs, holding right-click as on the quern, into
/// <see cref="SpringsPerRod"/> spring (<see cref="Spring"/>). It is a job of its own class,
/// <see cref="SpringClass"/> (the hollows are 1 and 2). While held and hot, the winding clock θ turns at
/// <see cref="TurnsPerSecond"/> and W (0..1) advances by θ / (2π · turnsPerRod); at W = 1 the rod is used
/// up and the spring comes off at the rod's temperature. Heat follows the anvil (vanilla's
/// <c>ItemIngot.CanWork</c>): the rod works at its <c>workableTemperature</c> attribute, else at half its
/// melting point (steel: 1502 °C, so 751 °C); below that the winding stops until it is reheated. The rod on
/// the mandrel is a <see cref="MachineJob"/> of class <see cref="SpringClass"/> ending at 1 (<see cref="Start"/>).
/// </summary>
public static class Winding
{
    public const string SteelRod = "game:rod-steel";
    public const string Spring = "seraphhorizons:spring-steel";
    public const int SpringClass = 3;
    public const int SpringsPerRod = 1;

    /// <summary>The winding clock's pace while held: a turn a second (the press brake's lever's).</summary>
    public const double TurnsPerSecond = 1;

    /// <summary>Radians of the winding clock a second while held.</summary>
    public const double RadiansPerSecond = 2 * Math.PI * TurnsPerSecond;

    /// <summary>Whether <paramref name="code"/> is the rod a spring is wound from: steel only.</summary>
    public static bool IsRod(string? code) => MandrelPart.Normalise(code) == SteelRod;

    /// <summary>The temperature a rod works from: its <c>workableTemperature</c> attribute when it has
    /// one, else half its melting point (vanilla's <c>ItemIngot.CanWork</c>).</summary>
    public static float WorkableTemperature(float meltingPoint, float? workableTemperature) =>
        workableTemperature ?? meltingPoint / 2;

    /// <summary>Whether a rod at <paramref name="temperature"/> can be worked (the anvil's rule).</summary>
    public static bool Workable(float temperature, float meltingPoint, float? workableTemperature = null) =>
        temperature >= WorkableTemperature(meltingPoint, workableTemperature);

    /// <summary>Rods wound by <paramref name="radians"/> of the winding clock.</summary>
    public static double RodsFor(double radians, double turnsPerRod) =>
        turnsPerRod > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerRod) : 0;

    /// <summary>
    /// What a rod <paramref name="code"/> does on a station whose mandrel is <paramref name="fitted"/> (null
    /// with none), with work on it or not (<paramref name="workOn"/>), springs existing in this world or not
    /// (<paramref name="springsExist"/>: the switch), and the rod hot enough to work
    /// (<paramref name="hot"/>). On a bare station any mandrel rod, steel included, is fitted as the mandrel;
    /// on a fitted one only a steel rod is wound, and only hot.
    /// </summary>
    public static RodVerdict ForRod(string? code, string? fitted, bool workOn, bool springsExist, bool hot)
    {
        if (!MandrelPart.IsMandrel(code))
            return RodVerdict.NotARod;
        if (fitted == null)
            return RodVerdict.Mandrel;
        if (!IsRod(code) || !springsExist)
            return RodVerdict.AlreadyFitted;
        if (workOn)
            return RodVerdict.Occupied;
        return hot ? RodVerdict.Winds : RodVerdict.TooCold;
    }

    /// <summary>The glow vanilla's renderers give hot metal at <paramref name="temperature"/> °C
    /// (<c>AnvilWorkItemRenderer</c> and the like): Clamp((t − 550) / 2, 0, 255), integer arithmetic.</summary>
    public static int ExtraGlow(float temperature) => Math.Clamp(((int)temperature - 550) / 2, 0, 255);

    /// <summary>The winding of a rod just put on the mandrel (W ends at 1).</summary>
    public static MachineJob Start() => MachineJob.Start(SpringClass, 1);

    /// <summary>The job after <paramref name="radians"/> more of the winding clock with the rod
    /// <paramref name="hot"/> or not (a cold rod does not wind), W held at 1, and whether this step finished it.</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double turnsPerRod, bool hot) =>
        hot ? job.Advance(RodsFor(radians, turnsPerRod)) : (job, false);

    /// <summary>A saved job: W clamped to 0..1; none without a rod.</summary>
    public static MachineJob Restore(bool on, double work) => on ? MachineJob.Restore(SpringClass, work, 1) : MachineJob.None;
}
