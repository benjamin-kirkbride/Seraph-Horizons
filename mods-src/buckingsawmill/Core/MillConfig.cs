namespace BuckingSawmill.Core;

/// <summary>ModConfig/buckingsawmill.json.</summary>
public class MillConfig
{
    /// <summary>Resistance the assembled mill puts on its shaft (twice Immersive Woodworking's
    /// sawmill). An unassembled frame puts 0.005.</summary>
    public float Resistance { get; set; } = 0.17f;

    /// <summary>The shaft speed below which the mill does not cut, or pull trunks from a rack.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>Shaft revolutions to cut through one log's worth of a trunk.</summary>
    public float RevolutionsPerStoredLog { get; set; } = 8f;

    /// <summary>Logs a trunk gives per log stored in it, rounded down over the whole trunk.</summary>
    public float LogsPerStoredLog { get; set; } = 2f;

    /// <summary>Durability each blade kit loses per log stored in a cut trunk, rounded up over the
    /// whole trunk.</summary>
    public float BladeWearPerStoredLog { get; set; } = 0.25f;

    /// <summary>Whether the mill takes trunks from a Trunk Storage Rack at its infeed side.</summary>
    public bool AutoPullFromRack { get; set; } = true;

    public static readonly MillConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        Resistance = Check(nameof(Resistance), Resistance, v => v >= 0 && v <= 10, Defaults.Resistance, fixes);
        MinSpeed = Check(nameof(MinSpeed), MinSpeed, v => v >= 0, Defaults.MinSpeed, fixes);
        RevolutionsPerStoredLog = Check(nameof(RevolutionsPerStoredLog), RevolutionsPerStoredLog, v => v > 0, Defaults.RevolutionsPerStoredLog, fixes);
        LogsPerStoredLog = Check(nameof(LogsPerStoredLog), LogsPerStoredLog, v => v >= 0, Defaults.LogsPerStoredLog, fixes);
        BladeWearPerStoredLog = Check(nameof(BladeWearPerStoredLog), BladeWearPerStoredLog, v => v >= 0, Defaults.BladeWearPerStoredLog, fixes);
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
