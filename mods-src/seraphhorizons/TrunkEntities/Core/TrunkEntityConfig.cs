namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// The trunk entities' figures: TrunkEntitiesSettings in ModConfig/seraphhorizons.json. A value out
/// of range falls back to its default with a warning (<see cref="Sanitise"/>); the server's are used.
/// </summary>
public class TrunkEntityConfig
{
    /// <summary>Weight a stored log adds to a trunk: its weight is 10 + logs × this, so a 10-log
    /// trunk (90) weighs about as much as a cart and a 48-log one 394. The weight is what a rope pulls
    /// against: the game's pull is 50 / weight, between 0.1 and 2. (Driving a trunk goes by its
    /// logs, <see cref="TrunkDrive"/>.)</summary>
    public float WeightPerLog { get; set; } = 8f;

    /// <summary>Walk speed, as a multiple of the normal one, while carrying a trunk of one log (or
    /// none): 1 is the normal walk.</summary>
    public float CarrySpeedAtOneLog { get; set; } = 1f;

    /// <summary>Walk speed while carrying a trunk of 48 logs or more; between 1 and 48 logs it goes
    /// linearly from <see cref="CarrySpeedAtOneLog"/> to this.</summary>
    public float CarrySpeedAtMaxLogs { get; set; } = 0.5f;

    /// <summary>Seconds the bark spud is held per stored log to debark a whole trunk (2 at least).</summary>
    public float SpudSecondsPerLog { get; set; } = 0.5f;

    public static readonly TrunkEntityConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        WeightPerLog = Check(nameof(WeightPerLog), WeightPerLog, v => v >= 0 && v <= 1000, Defaults.WeightPerLog, fixes);
        CarrySpeedAtOneLog = Check(nameof(CarrySpeedAtOneLog), CarrySpeedAtOneLog, v => v >= 0 && v <= 1, Defaults.CarrySpeedAtOneLog, fixes);
        CarrySpeedAtMaxLogs = Check(nameof(CarrySpeedAtMaxLogs), CarrySpeedAtMaxLogs, v => v >= 0 && v <= 1, Defaults.CarrySpeedAtMaxLogs, fixes);
        SpudSecondsPerLog = Check(nameof(SpudSecondsPerLog), SpudSecondsPerLog, v => v >= 0 && v <= 60, Defaults.SpudSecondsPerLog, fixes);
        return fixes;
    }

    private static float Check(string name, float value, Func<float, bool> valid, float fallback, List<string> fixes)
    {
        if (float.IsFinite(value) && valid(value))
            return value;
        fixes.Add($"{name} {value} is out of range, using {fallback}");
        return fallback;
    }
}
