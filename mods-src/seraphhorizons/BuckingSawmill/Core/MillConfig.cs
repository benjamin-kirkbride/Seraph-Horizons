namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>The bucking sawmill's figures: BuckingSawmillSettings in ModConfig/seraphhorizons.json.</summary>
public class MillConfig
{
    /// <summary>Resistance the assembled mill puts on its shaft (twice Immersive Woodworking's
    /// sawmill). An unassembled frame puts 0.005.</summary>
    public float Resistance { get; set; } = 0.17f;

    /// <summary>The shaft speed below which the mill does not cut, or pull trunks from a rack.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>Shaft revolutions to cut through one log's worth of a trunk.</summary>
    public float RevolutionsPerStoredLog { get; set; } = 8f;

    /// <summary>Shaft revolutions for the saws' whole travel: the windlass winds them from the bed
    /// to the top in this many, and with no trunk on the bed they sink back down in as many, so an
    /// empty cycle takes twice this.</summary>
    public float RaiseRevolutions { get; set; } = 6f;

    /// <summary>Logs a trunk gives per log stored in it, rounded down over the whole trunk.</summary>
    public float LogsPerStoredLog { get; set; } = 2f;

    /// <summary>Durability the blade kit loses per log stored in a cut trunk, rounded up over the
    /// whole trunk.</summary>
    public float BladeWearPerStoredLog { get; set; } = 1f;

    /// <summary>How much faster a blade kit cuts per tool tier above copper's (the game's saw of the
    /// kit's metal: copper, gold and silver 2, the bronzes 3, iron 4, steel 5), so a steel kit cuts
    /// in 1/(1 + 3 × this) of the turns. 0 makes every metal cut at copper's speed.</summary>
    public float BladeSpeedPerTier { get; set; } = 0.35f;

    /// <summary>Whether the mill takes trunks from a Trunk Storage Rack, or from a rosser in line
    /// (an <c>ITrunkFeeder</c>), at its infeed end (the far end, under the axle). Off, a rosser in
    /// line does not count the mill as its taker either.</summary>
    public bool AutoPullFromRack { get; set; } = true;

    public static readonly MillConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        Resistance = Check(nameof(Resistance), Resistance, v => v >= 0 && v <= 10, Defaults.Resistance, fixes);
        MinSpeed = Check(nameof(MinSpeed), MinSpeed, v => v >= 0, Defaults.MinSpeed, fixes);
        RevolutionsPerStoredLog = Check(nameof(RevolutionsPerStoredLog), RevolutionsPerStoredLog, v => v > 0, Defaults.RevolutionsPerStoredLog, fixes);
        RaiseRevolutions = Check(nameof(RaiseRevolutions), RaiseRevolutions, v => v > 0, Defaults.RaiseRevolutions, fixes);
        LogsPerStoredLog = Check(nameof(LogsPerStoredLog), LogsPerStoredLog, v => v >= 0, Defaults.LogsPerStoredLog, fixes);
        BladeWearPerStoredLog = Check(nameof(BladeWearPerStoredLog), BladeWearPerStoredLog, v => v >= 0, Defaults.BladeWearPerStoredLog, fixes);
        BladeSpeedPerTier = Check(nameof(BladeSpeedPerTier), BladeSpeedPerTier, v => v >= 0 && v <= 10, Defaults.BladeSpeedPerTier, fixes);
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
