namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// Why a machine is or is not working, in the order the checks run: the first that holds is the one
/// its block info shows (<c>seraphhorizons:machine-state-{code}</c>, <see cref="MachineRun.LangCode"/>).
/// </summary>
public enum MachineState
{
    /// <summary>A build stage is still to fit.</summary>
    NotBuilt,
    /// <summary>A wearing part is missing or worn out.</summary>
    Worn,
    /// <summary>Its shaft turns slower than <see cref="OreMachinesConfig.MinSpeed"/> (or not at all).</summary>
    NoPower,
    /// <summary>A wet machine whose reservoir is dry.</summary>
    Dry,
    /// <summary>An output with nowhere to go and its buffer full.</summary>
    Blocked,
    /// <summary>Running, with nothing to work.</summary>
    Idle,
    Working,
}

/// <summary>What the shared rules decide about a machine at one moment.</summary>
public readonly record struct MachineCheck(bool Built, bool PartsFitted, bool Powered, float Speed, bool Wet, bool HasWork, bool OutputRoom);

/// <summary>
/// The shared running rules of the ore machines (#711): a machine runs only built, with its wearing
/// parts, its shaft at <see cref="OreMachinesConfig.MinSpeed"/> or faster, its reservoir holding a
/// whole item's water (a wet machine with no water stops, and resumes when water returns: the owner's
/// ruling on #711), and room for what it puts out. Rate is in proportion to speed with no cap: work is
/// counted in shaft turns (<see cref="ShaftClock.AngleAdvance"/>), so the energy an item takes is the
/// same at any speed.
/// </summary>
public static class MachineRun
{
    /// <summary>Radians a network turns per unit of speed per second (speed 1 is 5 rad/s).</summary>
    public const float RadiansPerSpeedSecond = 5f;

    /// <summary>The state the checks give, first failing first. A machine without power
    /// (<paramref name="check"/>'s <see cref="MachineCheck.Powered"/> false: a hand station, or a machine
    /// that takes none) skips the speed check.</summary>
    public static MachineState StateOf(MachineCheck check, float minSpeed)
    {
        if (!check.Built)
            return MachineState.NotBuilt;
        if (!check.PartsFitted)
            return MachineState.Worn;
        if (check.Powered && !Turning(check.Speed, minSpeed))
            return MachineState.NoPower;
        if (!check.Wet)
            return MachineState.Dry;
        if (!check.OutputRoom)
            return MachineState.Blocked;
        return check.HasWork ? MachineState.Working : MachineState.Idle;
    }

    /// <summary>Whether a shaft turning at <paramref name="speed"/> (either way) drives the machine.</summary>
    public static bool Turning(float speed, float minSpeed) => float.IsFinite(speed) && Math.Abs(speed) >= minSpeed && Math.Abs(speed) > 0;

    /// <summary>Whether the machine works in this state (its turns count toward its items).</summary>
    public static bool Works(MachineState state) => state == MachineState.Working;

    /// <summary>The block info's lang code for <paramref name="state"/>.</summary>
    public static string LangCode(MachineState state) => "seraphhorizons:machine-state-" + state.ToString().ToLowerInvariant();

    /// <summary>The whole percent the block info shows next to "Working" for <paramref name="progress"/>, the share
    /// of the current item or charge done (0 to 1, floored, so 100 % only when it is done); null for none.</summary>
    public static int? Percent(double? progress) =>
        progress is double p && double.IsFinite(p) ? (int)Math.Floor(Math.Clamp(p, 0, 1) * 100 + 1e-9) : null;
}

/// <summary>
/// When to send a machine's progress to its clients, so the percent next to "Working" moves in the block
/// info: whenever the whole percent it shows changes, but a progress change alone at most once every
/// <see cref="MinSeconds"/> (a fast machine would otherwise resend itself every tick). Anything else that
/// syncs the machine sends the progress too (<see cref="Sent"/>).
/// </summary>
public readonly record struct ProgressSync(int? Percent, double At)
{
    /// <summary>The fewest seconds between two syncs for progress alone.</summary>
    public const double MinSeconds = 1;

    public static readonly ProgressSync None = new(null, double.NegativeInfinity);

    /// <summary>Whether <paramref name="percent"/> at <paramref name="now"/> (seconds) is to be sent: it differs from
    /// the last sent and, unless it starts or ends (to or from none), <see cref="MinSeconds"/> have passed.</summary>
    public bool Due(int? percent, double now) =>
        percent != Percent && (percent is null || Percent is null || !(now - At < MinSeconds));

    /// <summary>The record after <paramref name="percent"/> was sent at <paramref name="now"/>.</summary>
    public ProgressSync Sent(int? percent, double now) => new(percent, now);
}

/// <summary>
/// Counts shaft turns toward items: <see cref="Add"/> the radians the shaft advanced
/// (<see cref="ShaftClock.AngleAdvance"/>) while the machine works, and it gives the whole items done
/// at <c>turnsPerItem</c> (ore-core's <c>OreMachineSpec.TurnsPerItem</c>), carrying the rest. A
/// turn is 2π radians.
/// </summary>
public readonly record struct WorkTurns(double Turns)
{
    public static readonly WorkTurns None = new(0);

    /// <summary>The counter after <paramref name="radians"/> more, and how many whole items that finished.</summary>
    public (WorkTurns Next, int Items) Add(double radians, double turnsPerItem)
    {
        if (!(turnsPerItem > 0) || !double.IsFinite(radians) || radians <= 0)
            return (this, 0);
        double turns = Turns + radians / (2 * Math.PI);
        int items = (int)Math.Floor(turns / turnsPerItem + 1e-9);
        return (new WorkTurns(Math.Max(0, turns - items * turnsPerItem)), items);
    }

    /// <summary>The share of the current item done, 0 to 1.</summary>
    public double Progress(double turnsPerItem) => turnsPerItem > 0 ? Math.Clamp(Turns / turnsPerItem, 0, 1) : 0;

    /// <summary>Restored from a save: not negative, not past an item.</summary>
    public static WorkTurns Restore(double turns, double turnsPerItem) =>
        new(double.IsFinite(turns) ? Math.Clamp(turns, 0, Math.Max(0, turnsPerItem)) : 0);
}

/// <summary>
/// A machine's load on its shaft (#711): constant once built, the figure the machine's job sets
/// (ore-core's <c>OreRecovery.LoadAt</c>: the full load for every machine but the three that follow
/// their job, the jaw breaker, the reverberatory's rabbles and the Parkes kettle's stirrer, whose
/// load is set per charge, never per item); a token load while the build is not complete, as every
/// pack machine's; and the dry-of-oil multiplier where the machine takes machine oil.
/// </summary>
public static class MachineLoad
{
    /// <summary>The load of a machine whose build is not complete (<c>BEBehaviorMillMP.IncompleteResistance</c>).</summary>
    public const float Unassembled = 0.005f;

    /// <summary>The resistance to put on the shaft: <see cref="Unassembled"/> unbuilt, else
    /// <paramref name="load"/>, times <paramref name="dryMultiplier"/> while its oil tank is dry.</summary>
    public static float Resistance(bool built, double load, bool oilDry = false, float dryMultiplier = 3f)
    {
        if (!built)
            return Unassembled;
        double r = double.IsFinite(load) ? Math.Max(0, load) : 0;
        return (float)(oilDry ? r * Math.Max(1, dryMultiplier) : r);
    }

    /// <summary>The power a load takes at a speed (#940's unit): resistance × speed.</summary>
    public static double Power(double resistance, double speed) => resistance * Math.Abs(speed);
}

/// <summary>
/// Counts working time toward items for a machine with no shaft (the sluice, the amalgamation plates:
/// water and gravity only, #711): <see cref="Add"/> the in-game hours it worked, and it gives the whole
/// items done at <c>itemsPerHour</c> (ore-core's <c>OreMachineSpec.ItemsPerHour</c>), carrying the rest,
/// as <see cref="WorkTurns"/> does for a shaft's turns.
/// </summary>
public readonly record struct WorkHours(double Items)
{
    public static readonly WorkHours None = new(0);

    /// <summary>The counter after <paramref name="hours"/> more, and how many whole items that finished.</summary>
    public (WorkHours Next, int Items) Add(double hours, double itemsPerHour)
    {
        if (!(itemsPerHour > 0) || !double.IsFinite(hours) || hours <= 0)
            return (this, 0);
        double done = Items + hours * itemsPerHour;
        int items = (int)Math.Floor(done + 1e-9);
        return (new WorkHours(Math.Max(0, done - items)), items);
    }

    /// <summary>The share of the current item done, 0 to 1.</summary>
    public double Progress => Math.Clamp(Items, 0, 1);

    /// <summary>Restored from a save: 0 to just under an item.</summary>
    public static WorkHours Restore(double items) => new(double.IsFinite(items) ? Math.Clamp(items, 0, 1) : 0);
}
