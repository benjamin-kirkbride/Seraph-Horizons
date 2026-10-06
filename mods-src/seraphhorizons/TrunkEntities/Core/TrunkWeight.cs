namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// How much a trunk's stored logs count: the entity's weight (what a rope or a grab pulls against),
/// the walk speed while carrying it, and how long the bark spud takes over it.
/// </summary>
public static class TrunkWeight
{
    /// <summary>The weight of a trunk with no logs.</summary>
    public const float BaseWeight = 10f;

    /// <summary>The log count the carry speed reaches its slowest at (Logging Expanded's largest
    /// trees hold about this many).</summary>
    public const int MaxLogs = 48;

    /// <summary>The log count the carry speed starts slowing from.</summary>
    public const int FewLogs = 4;

    /// <summary>The shortest bark spud hold, seconds.</summary>
    public const float MinSpudSeconds = 2f;

    /// <summary><see cref="BaseWeight"/> + logs × <see cref="TrunkEntityConfig.WeightPerLog"/>
    /// (negative counts as none).</summary>
    public static float Weight(int logs, TrunkEntityConfig config) => BaseWeight + Math.Max(0, logs) * config.WeightPerLog;

    /// <summary>The walk speed multiplier while carrying a trunk of <paramref name="logs"/> logs:
    /// <see cref="TrunkEntityConfig.CarrySpeedAtFourLogs"/> up to <see cref="FewLogs"/>, linearly
    /// down to <see cref="TrunkEntityConfig.CarrySpeedAtMaxLogs"/> at <see cref="MaxLogs"/>, and
    /// that beyond.</summary>
    public static float CarrySpeed(int logs, TrunkEntityConfig config)
    {
        float t = Math.Clamp((logs - FewLogs) / (float)(MaxLogs - FewLogs), 0f, 1f);
        return config.CarrySpeedAtFourLogs + (config.CarrySpeedAtMaxLogs - config.CarrySpeedAtFourLogs) * t;
    }

    /// <summary>Seconds the bark spud is held to debark a trunk of <paramref name="logs"/> logs:
    /// logs × <see cref="TrunkEntityConfig.SpudSecondsPerLog"/>, <see cref="MinSpudSeconds"/> at least.</summary>
    public static float SpudSeconds(int logs, TrunkEntityConfig config) =>
        Math.Max(MinSpudSeconds, Math.Max(0, logs) * config.SpudSecondsPerLog);

    /// <summary>Whether a trunk of <paramref name="weight"/> may be grabbed by hand
    /// (<see cref="TrunkEntityConfig.MaxGrabWeight"/> 0 means any).</summary>
    public static bool Grabbable(float weight, TrunkEntityConfig config) =>
        config.MaxGrabWeight <= 0 || weight <= config.MaxGrabWeight;
}
