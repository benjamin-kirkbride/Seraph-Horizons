using SeraphHorizons.Mod.GearReclamation.Core;

namespace SeraphHorizons.Mod.PicklingTub.Core;

/// <summary>What a finished batch is, for the block info's words: a pickle (the scale is off and the
/// metal looks grey and clean) or a passivation (the nitric acid has left the surface passive).</summary>
public enum TubRuleKind
{
    Pickle,
    Passivate,
}

/// <summary>
/// One row of the tub's rule table (<c>PicklingTubSettings.AcidRules</c>): gears of
/// <see cref="Input"/> in a liquid matching <see cref="Liquid"/> become <see cref="Output"/> after
/// <see cref="Hours"/> in-game hours. Past done plus <see cref="GraceHours"/> one gear turns into
/// <see cref="FailureQuantity"/> of <see cref="Failure"/> every <see cref="LossEveryHours"/>
/// (0: never), and each gear has <see cref="LossChance"/> to come out as the failure item at done.
/// </summary>
public class TubRuleConfig
{
    public TubRuleConfig()
    {
    }

    public TubRuleConfig(string liquid, string input, string output, double hours, double graceHours, double lossEveryHours,
        TubRuleKind kind = TubRuleKind.Pickle)
    {
        Liquid = liquid;
        Input = input;
        Output = output;
        Hours = hours;
        GraceHours = graceHours;
        LossEveryHours = lossEveryHours;
        Kind = kind;
    }

    /// <summary>Liquid code pattern (<c>domain:path</c>, <c>*</c> wildcards; no domain is <c>game</c>).</summary>
    public string Liquid { get; set; } = "";

    /// <summary>The gear that goes in (an exact item code).</summary>
    public string Input { get; set; } = "";

    /// <summary>The gear that comes out at done.</summary>
    public string Output { get; set; } = "";

    /// <summary>In-game hours from the batch going in with liquid to done.</summary>
    public double Hours { get; set; }

    /// <summary>What a lost gear becomes.</summary>
    public string Failure { get; set; } = GearCodes.StainlessBit;

    /// <summary>Failure items per lost gear.</summary>
    public int FailureQuantity { get; set; } = 1;

    /// <summary>In-game hours after done before the first gear is lost.</summary>
    public double GraceHours { get; set; }

    /// <summary>In-game hours between lost gears once the grace is over; 0 loses none by time.</summary>
    public double LossEveryHours { get; set; }

    /// <summary>Each gear's chance to come out as the failure item at done (0 to 1).</summary>
    public double LossChance { get; set; }

    public TubRuleKind Kind { get; set; } = TubRuleKind.Pickle;
}

/// <summary>PicklingTubSettings in ModConfig/seraphhorizons.json (under the <c>GearReclamation</c>
/// switch).</summary>
public class PicklingTubConfig
{
    public const string Vinegar = "game:vinegarportion";
    public const string Sulfuric = "game:acid-full-sulfuric";
    public const string Hydrochloric = "game:acid-full-hydrochloric";

    /// <summary>Expanded Matter's nitric acid (it adds the game's unused acid variant).</summary>
    public const string Nitric = "game:acid-full-nitric";

    // The gears are GearCodes' (the reclamation line's contract); the tub's rules name them by these.
    public const string Bits = GearCodes.StainlessBit;
    public const string Degreased = GearCodes.Degreased;
    public const string Pickled = GearCodes.Pickled;
    public const string Passivated = GearCodes.Passivated;

    /// <summary>Gears a batch holds.</summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>Litres of liquid the tub holds.</summary>
    public double CapacityLitres { get; set; } = 10;

    /// <summary>Litres a batch uses up when it is done (none when it is taken out early).</summary>
    public double LitresPerBatch { get; set; } = 1;

    /// <summary>The acid table: degreased gears to pickled ones (vinegar slowest, hydrochloric
    /// fastest), then pickled gears to passivated ones in nitric acid. The acid eats a batch left
    /// past done plus its grace, a gear at a time, to stainless bits.</summary>
    public TubRuleConfig[] AcidRules { get; set; } =
    [
        new(Vinegar, Degreased, Pickled, 24, 12, 3),
        new(Sulfuric, Degreased, Pickled, 8, 4, 1),
        new(Hydrochloric, Degreased, Pickled, 2, 1, 0.25),
        new(Nitric, Pickled, Passivated, 6, 3, 1, TubRuleKind.Passivate),
    ];

    /// <summary>Gear code patterns the tub refuses with a word (a large gear is cut new, never
    /// reclaimed).</summary>
    public string[] RefusedGears { get; set; } = ["seraphhorizons:largegear-*"];

    public static readonly PicklingTubConfig Defaults = new();

    /// <summary>Every rule the tub knows: the acid table.</summary>
    public IEnumerable<TubRuleConfig> AllRules() => AcidRules ?? [];

    /// <summary>Replaces values out of range with the default and drops broken rules; returns a
    /// line per fix.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (BatchSize is < 1 or > 64)
        {
            fixes.Add($"BatchSize {BatchSize} is out of range, using {Defaults.BatchSize}");
            BatchSize = Defaults.BatchSize;
        }
        if (!double.IsFinite(CapacityLitres) || CapacityLitres < 1 || CapacityLitres > 1000)
        {
            fixes.Add($"CapacityLitres {CapacityLitres} is out of range, using {Defaults.CapacityLitres}");
            CapacityLitres = Defaults.CapacityLitres;
        }
        if (!double.IsFinite(LitresPerBatch) || LitresPerBatch < 0 || LitresPerBatch > CapacityLitres)
        {
            fixes.Add($"LitresPerBatch {LitresPerBatch} is out of range, using {Defaults.LitresPerBatch}");
            LitresPerBatch = Math.Min(Defaults.LitresPerBatch, CapacityLitres);
        }
        RefusedGears = (RefusedGears ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var kept = new List<TubRuleConfig>();
        var rules = AcidRules ?? [];
        for (int i = 0; i < rules.Length; i++)
        {
            string? problem = Problem(rules[i]);
            if (problem == null)
                kept.Add(rules[i]);
            else
                fixes.Add($"AcidRules[{i}] {problem}, so it is dropped");
        }
        AcidRules = kept.ToArray();
        return fixes;
    }

    private static string? Problem(TubRuleConfig? rule)
    {
        if (rule == null)
            return "is empty";
        if (string.IsNullOrWhiteSpace(rule.Liquid) || string.IsNullOrWhiteSpace(rule.Input) || string.IsNullOrWhiteSpace(rule.Output))
            return "lacks a Liquid, Input or Output";
        if (!double.IsFinite(rule.Hours) || rule.Hours <= 0)
            return $"has Hours {rule.Hours}";
        if (!double.IsFinite(rule.GraceHours) || rule.GraceHours < 0)
            return $"has GraceHours {rule.GraceHours}";
        if (!double.IsFinite(rule.LossEveryHours) || rule.LossEveryHours < 0)
            return $"has LossEveryHours {rule.LossEveryHours}";
        if (!double.IsFinite(rule.LossChance) || rule.LossChance < 0 || rule.LossChance > 1)
            return $"has LossChance {rule.LossChance}";
        if ((rule.LossEveryHours > 0 || rule.LossChance > 0) && (string.IsNullOrWhiteSpace(rule.Failure) || rule.FailureQuantity < 0))
            return "loses gears but has no Failure item";
        return null;
    }
}
