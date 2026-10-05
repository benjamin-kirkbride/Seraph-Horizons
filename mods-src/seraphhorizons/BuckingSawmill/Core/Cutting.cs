using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>The cut's arithmetic: progress from the shaft's turning, and what a finished trunk
/// gives and costs.</summary>
public static class Cutting
{
    /// <summary>A loaded trunk can be taken back out while its progress is below this.</summary>
    public const float RecoverableBelow = 0.25f;

    // Products of floats like 3 × 0.1 land a hair off the whole number they mean.
    private const double Epsilon = 1e-6;

    public static bool Recoverable(float progress) => progress < RecoverableBelow;

    /// <summary><see cref="ShaftClock.AngleAdvance"/>, which the rosser shares.</summary>
    public static float AngleAdvance(float lastAngle, float angle, float speed, float dt) =>
        ShaftClock.AngleAdvance(lastAngle, angle, speed, dt);

    /// <summary>Progress (0..1 for the whole trunk) that <paramref name="radians"/> of shaft
    /// rotation adds to a trunk of <paramref name="storedLogs"/> logs.</summary>
    public static float ProgressFor(float radians, int storedLogs, float revolutionsPerStoredLog)
    {
        double revolutions = (double)Math.Max(storedLogs, 1) * revolutionsPerStoredLog;
        return revolutions <= 0 ? 1f : (float)(radians / (2 * Math.PI * revolutions));
    }

    /// <summary>The tool tier of the copper saw, the slowest blade: a kit of this tier or below cuts
    /// at 1×.</summary>
    public const int CopperTier = 2;

    /// <summary>How fast a blade kit of tool tier <paramref name="tier"/> cuts, as a multiple of a
    /// copper kit: 1 + <paramref name="speedPerTier"/> per tier above <see cref="CopperTier"/>, never
    /// below 1. An unknown tier (null) cuts at 1×.</summary>
    public static float BladeSpeed(int? tier, float speedPerTier)
    {
        if (tier is not int t || !float.IsFinite(speedPerTier) || speedPerTier <= 0)
            return 1f;
        return 1f + speedPerTier * Math.Max(0, t - CopperTier);
    }

    /// <summary>Shaft turns per stored log with a blade kit cutting at <paramref name="bladeSpeed"/>:
    /// the configured turns over the speed. Only the cut is scaled; the saws' travel up, and down
    /// when empty, is the windlass's and does not depend on the blade.</summary>
    public static float CutRevolutions(float revolutionsPerStoredLog, float bladeSpeed) =>
        bladeSpeed > 0 && float.IsFinite(bladeSpeed) ? revolutionsPerStoredLog / bladeSpeed : revolutionsPerStoredLog;

    /// <summary>Logs a finished trunk gives: floor(storedLogs × logsPerStoredLog).</summary>
    public static int LogYield(int storedLogs, float logsPerStoredLog) =>
        Math.Max(0, (int)Math.Floor(storedLogs * (double)logsPerStoredLog + Epsilon));

    /// <summary>Durability the blade kit loses on a finished trunk:
    /// ceil(storedLogs × bladeWearPerStoredLog).</summary>
    public static int BladeWear(int storedLogs, float bladeWearPerStoredLog) =>
        Math.Max(0, (int)Math.Ceiling(storedLogs * (double)bladeWearPerStoredLog - Epsilon));

    /// <summary><paramref name="total"/> split into stacks of at most <paramref name="maxStackSize"/>.</summary>
    public static IReadOnlyList<int> SplitStacks(int total, int maxStackSize)
    {
        int max = Math.Max(1, maxStackSize);
        var stacks = new List<int>();
        for (int left = total; left > 0; left -= max)
            stacks.Add(Math.Min(left, max));
        return stacks;
    }

    /// <summary><see cref="ShaftClock.WoodOfStoredLog"/>, which the rosser shares.</summary>
    public static string? WoodOfStoredLog(string? logPath) => ShaftClock.WoodOfStoredLog(logPath);
}
