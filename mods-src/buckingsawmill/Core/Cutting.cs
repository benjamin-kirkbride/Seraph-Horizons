namespace BuckingSawmill.Core;

/// <summary>The cut's arithmetic: progress from the shaft's turning, and what a finished trunk
/// gives and costs.</summary>
public static class Cutting
{
    /// <summary>A loaded trunk can be taken back out while its progress is below this.</summary>
    public const float RecoverableBelow = 0.25f;

    // Products of floats like 3 × 0.1 land a hair off the whole number they mean.
    private const double Epsilon = 1e-6;

    public static bool Recoverable(float progress) => progress < RecoverableBelow;

    /// <summary>How far the shaft turned since the last reading, in radians, unsigned. Readings are
    /// angles modulo 2π, so a step of more than half a turn is ambiguous: when the speed says the
    /// shaft turned that far (a network turns 5·speed rad/s), the speed's figure is used instead,
    /// as Immersive Woodworking's sawmill does.</summary>
    public static float AngleAdvance(float lastAngle, float angle, float speed, float dt)
    {
        float delta = angle - lastAngle;
        if (delta > MathF.PI)
            delta -= 2 * MathF.PI;
        else if (delta < -MathF.PI)
            delta += 2 * MathF.PI;
        float bySpeed = Math.Abs(speed) * 5f * dt;
        return bySpeed > MathF.PI ? bySpeed : Math.Abs(delta);
    }

    /// <summary>Progress (0..1 for the whole trunk) that <paramref name="radians"/> of shaft
    /// rotation adds to a trunk of <paramref name="storedLogs"/> logs.</summary>
    public static float ProgressFor(float radians, int storedLogs, float revolutionsPerStoredLog)
    {
        double revolutions = (double)Math.Max(storedLogs, 1) * revolutionsPerStoredLog;
        return revolutions <= 0 ? 1f : (float)(radians / (2 * Math.PI * revolutions));
    }

    /// <summary>Logs a finished trunk gives: floor(storedLogs × logsPerStoredLog).</summary>
    public static int LogYield(int storedLogs, float logsPerStoredLog) =>
        Math.Max(0, (int)Math.Floor(storedLogs * (double)logsPerStoredLog + Epsilon));

    /// <summary>Durability each blade kit loses on a finished trunk:
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

    /// <summary>The wood of a Logging Expanded trunk from the code path of the log stack it
    /// stores, parsed as Logging Expanded's TreeTrunkInventory.WoodType does
    /// (<c>log-placed-oak-ud</c> is <c>oak</c>), with the <c>grown-</c> prefix its callers strip
    /// removed. Null when the path is not a log's.</summary>
    public static string? WoodOfStoredLog(string? logPath)
    {
        if (string.IsNullOrEmpty(logPath))
            return null;
        string[] parts;
        string? wood = null;
        if (logPath.StartsWith("log-placed-", StringComparison.Ordinal))
        {
            parts = logPath["log-placed-".Length..].Split('-');
            if (parts.Length >= 2)
                wood = string.Join("-", parts, 0, parts.Length - 1);
        }
        else if (logPath.StartsWith("log-", StringComparison.Ordinal))
        {
            parts = logPath.Split('-');
            if (parts.Length >= 3)
                wood = string.Join("-", parts, 1, parts.Length - 2);
        }
        if (wood != null && wood.StartsWith("grown-", StringComparison.Ordinal))
            wood = wood["grown-".Length..];
        return string.IsNullOrEmpty(wood) ? null : wood;
    }
}
