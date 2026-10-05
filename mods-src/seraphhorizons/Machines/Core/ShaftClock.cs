namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>Reading a machine's shaft and a trunk's stored logs, shared by the bucking mill and the
/// rosser.</summary>
public static class ShaftClock
{
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
