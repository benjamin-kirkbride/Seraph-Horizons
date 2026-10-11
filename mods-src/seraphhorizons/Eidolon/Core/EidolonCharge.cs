namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// The eidolon's charge, in game days left: a temporal gear adds <see cref="DaysPerGear"/>, up to a
/// cap; it drains with game time while the eidolon stands (not while it is slumped), and at 0 it
/// slumps until it is recharged.
/// </summary>
public static class EidolonCharge
{
    /// <summary>The days one gear runs it: the calendar's year times <paramref name="yearsPerGear"/>.</summary>
    public static double DaysPerGear(double daysPerYear, double yearsPerGear) => Math.Max(0, daysPerYear * yearsPerGear);

    /// <summary>The charge left after <paramref name="elapsedDays"/> of running; never below 0, and
    /// time running backwards (a calendar set back) drains nothing.</summary>
    public static double Drain(double remainingDays, double elapsedDays) =>
        !double.IsFinite(elapsedDays) || elapsedDays <= 0 ? Math.Max(0, remainingDays) : Math.Max(0, remainingDays - elapsedDays);

    /// <summary>One gear's worth added, when that keeps the charge within
    /// <paramref name="maxGears"/> gears' worth; false (and the charge unchanged) when it would
    /// not, so a gear is never wasted on a full eidolon.</summary>
    public static bool TryAddGear(double remainingDays, double daysPerGear, double maxGears, out double after)
    {
        after = Math.Max(0, remainingDays);
        if (daysPerGear <= 0 || after + daysPerGear > daysPerGear * maxGears + 1e-9)
            return false;
        after += daysPerGear;
        return true;
    }

    /// <summary>The charge as a share of one gear's worth (1 is a full gear; can be above 1).</summary>
    public static double Gears(double remainingDays, double daysPerGear) => daysPerGear <= 0 ? 0 : Math.Max(0, remainingDays) / daysPerGear;
}
