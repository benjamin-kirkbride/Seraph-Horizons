namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>
/// The rosser's figures: RosserSettings in ModConfig/seraphhorizons.json.
///
/// <para><b>Pace.</b> The feed is geared and never slips: the trunk travels a fixed distance per
/// shaft turn for each trunk class, the thin speed faster and the thick slower (the cradle's
/// weight shifts a two-speed change), times the scraper heads' metal factor
/// (<see cref="HeadSpeedPerTier"/>). The two class speeds are derived from turns per stored log
/// and per branch on a typical trunk of each class: a typical trunk's whole trip takes
/// <see cref="TypicalTurns"/> turns at copper speed, so the class speed is that trip's length
/// over those turns (<see cref="RosserPace.BlocksPerTurn"/>). Every other trunk of the class travels
/// at the same speed.</para>
///
/// <para>Tuned so one rosser keeps one bucking mill of the same metal fed: the mill takes
/// 8 × logs / speed + 6 turns for a trunk (its <c>RevolutionsPerStoredLog</c>, then its saws wound
/// back up in <c>RaiseRevolutions</c>), and the rosser a trip of typical turns / speed. With the
/// defaults (thin 10 logs and 25 branches = 67.9 turns, thick 25 and 61 = 169.3) the rosser is
/// never the slower for a thick trunk and for a thin one of 8 logs or more; only the smallest thin
/// trunks (which the mill cuts in under 70 turns) leave the mill waiting. The branch counts are
/// measured: Logging Expanded gives felled trees about 2.45 branches per log (an Atlas scenario,
/// <c>Felled_trees_branch_counts_are_measured</c>), and the turns per branch were set so the two
/// typical trips stay at the 68 and 169 turns the model's gears are drawn for.</para>
/// </summary>
public class RosserConfig
{
    /// <summary>Resistance the assembled rosser puts on its shaft. An unassembled frame puts 0.005.</summary>
    public float Resistance { get; set; } = 0.2f;

    /// <summary>The shaft speed below which the rosser does not feed, or take trunks from or give them to a rack.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>Shaft turns a typical trunk's trip takes per log stored in it (at copper speed).</summary>
    public float RevolutionsPerStoredLog { get; set; } = 6f;

    /// <summary>Shaft turns a typical trunk's trip takes per branch on it (at copper speed).</summary>
    public float RevolutionsPerBranch { get; set; } = 0.316f;

    /// <summary>The typical thin trunk (Logging Expanded's xs to lg) the thin speed is set on: its stored logs.</summary>
    public int TypicalThinLogs { get; set; } = 10;

    /// <summary>The typical thin trunk's branches.</summary>
    public int TypicalThinBranches { get; set; } = 25;

    /// <summary>The typical thick trunk (xl and xxl) the thick speed is set on: its stored logs.</summary>
    public int TypicalThickLogs { get; set; } = 25;

    /// <summary>The typical thick trunk's branches.</summary>
    public int TypicalThickBranches { get; set; } = 61;

    /// <summary>How much faster the feed runs per tool tier above copper's of the scraper heads'
    /// metal (the game's saw of that metal: copper, gold and silver 2, the bronzes 3, iron 4, steel
    /// 5), as the mill's <c>BladeSpeedPerTier</c>. 0 makes every metal feed at copper's speed.</summary>
    public float HeadSpeedPerTier { get; set; } = 0.35f;

    /// <summary>Sticks the limb breaker gives per branch, rounded down over the trunk (Logging
    /// Expanded's knife gives one).</summary>
    public float StickFraction { get; set; } = 0.5f;

    /// <summary>Immersive Woodworking's bark roll's chance multiplier when the trunk is scraped dry.</summary>
    public float DryBarkMultiplier { get; set; } = 1f;

    /// <summary>The chance multiplier when wet (the drip had <see cref="WaterPerLog"/> for the log).</summary>
    public float WetBarkMultiplier { get; set; } = 1.5f;

    /// <summary>How many more bark pieces a wet log gives: the count times this, with the fractional
    /// part a chance of one more.</summary>
    public float WetBarkCountMultiplier { get; set; } = 1.5f;

    /// <summary>The heads' capacity, as a multiple of the durability of the bark spud of their metal.</summary>
    public float HeadWearMultiple { get; set; } = 4f;

    /// <summary>Capacity the heads lose per log stored in a debarked trunk, rounded up over the trunk.</summary>
    public float HeadWearPerStoredLog { get; set; } = 1f;

    /// <summary>Litres the drip spends on one log.</summary>
    public float WaterPerLog { get; set; } = 2f;

    /// <summary>Litres the rosser's reservoir holds.</summary>
    public float ReservoirLitres { get; set; } = 20f;

    /// <summary>Litres per second it draws from a connected pipe until full.</summary>
    public float WaterIntakeLitresPerSecond { get; set; } = 10f;

    /// <summary>Whether the rosser takes trunks from a Trunk Storage Rack at its infeed end.</summary>
    public bool AutoPullFromRack { get; set; } = true;

    /// <summary>Whether the rosser puts finished trunks on a Trunk Storage Rack at its outfeed end.</summary>
    public bool AutoPushToRack { get; set; } = true;

    public static readonly RosserConfig Defaults = new();

    /// <summary>The typical trunk's trip in shaft turns at copper speed for class
    /// <paramref name="k"/> (1 thin, 2 thick): logs × <see cref="RevolutionsPerStoredLog"/> +
    /// branches × <see cref="RevolutionsPerBranch"/>. 0 for anything else.</summary>
    public double TypicalTurns(int k) => k switch
    {
        1 => TypicalThinLogs * (double)RevolutionsPerStoredLog + TypicalThinBranches * (double)RevolutionsPerBranch,
        2 => TypicalThickLogs * (double)RevolutionsPerStoredLog + TypicalThickBranches * (double)RevolutionsPerBranch,
        _ => 0,
    };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        Resistance = Check(nameof(Resistance), Resistance, v => v >= 0 && v <= 10, Defaults.Resistance, fixes);
        MinSpeed = Check(nameof(MinSpeed), MinSpeed, v => v >= 0, Defaults.MinSpeed, fixes);
        RevolutionsPerStoredLog = Check(nameof(RevolutionsPerStoredLog), RevolutionsPerStoredLog, v => v > 0, Defaults.RevolutionsPerStoredLog, fixes);
        RevolutionsPerBranch = Check(nameof(RevolutionsPerBranch), RevolutionsPerBranch, v => v >= 0, Defaults.RevolutionsPerBranch, fixes);
        TypicalThinLogs = Check(nameof(TypicalThinLogs), TypicalThinLogs, 1, 48, Defaults.TypicalThinLogs, fixes);
        TypicalThinBranches = Check(nameof(TypicalThinBranches), TypicalThinBranches, 0, 10000, Defaults.TypicalThinBranches, fixes);
        TypicalThickLogs = Check(nameof(TypicalThickLogs), TypicalThickLogs, 1, 48, Defaults.TypicalThickLogs, fixes);
        TypicalThickBranches = Check(nameof(TypicalThickBranches), TypicalThickBranches, 0, 10000, Defaults.TypicalThickBranches, fixes);
        HeadSpeedPerTier = Check(nameof(HeadSpeedPerTier), HeadSpeedPerTier, v => v >= 0 && v <= 10, Defaults.HeadSpeedPerTier, fixes);
        StickFraction = Check(nameof(StickFraction), StickFraction, v => v >= 0 && v <= 10, Defaults.StickFraction, fixes);
        DryBarkMultiplier = Check(nameof(DryBarkMultiplier), DryBarkMultiplier, v => v >= 0 && v <= 100, Defaults.DryBarkMultiplier, fixes);
        WetBarkMultiplier = Check(nameof(WetBarkMultiplier), WetBarkMultiplier, v => v >= 0 && v <= 100, Defaults.WetBarkMultiplier, fixes);
        WetBarkCountMultiplier = Check(nameof(WetBarkCountMultiplier), WetBarkCountMultiplier, v => v >= 0 && v <= 100, Defaults.WetBarkCountMultiplier, fixes);
        HeadWearMultiple = Check(nameof(HeadWearMultiple), HeadWearMultiple, v => v > 0 && v <= 1000, Defaults.HeadWearMultiple, fixes);
        HeadWearPerStoredLog = Check(nameof(HeadWearPerStoredLog), HeadWearPerStoredLog, v => v >= 0 && v <= 1000, Defaults.HeadWearPerStoredLog, fixes);
        WaterPerLog = Check(nameof(WaterPerLog), WaterPerLog, v => v > 0 && v <= 1000, Defaults.WaterPerLog, fixes);
        ReservoirLitres = Check(nameof(ReservoirLitres), ReservoirLitres, v => v >= 0 && v <= 100000, Defaults.ReservoirLitres, fixes);
        WaterIntakeLitresPerSecond = Check(nameof(WaterIntakeLitresPerSecond), WaterIntakeLitresPerSecond, v => v >= 0 && v <= 100000, Defaults.WaterIntakeLitresPerSecond, fixes);
        return fixes;
    }

    private static float Check(string name, float value, Func<float, bool> valid, float fallback, List<string> fixes)
    {
        if (float.IsFinite(value) && valid(value))
            return value;
        fixes.Add($"{name} {value} is out of range, using {fallback}");
        return fallback;
    }

    private static int Check(string name, int value, int min, int max, int fallback, List<string> fixes)
    {
        if (value >= min && value <= max)
            return value;
        fixes.Add($"{name} {value} is out of range, using {fallback}");
        return fallback;
    }
}
