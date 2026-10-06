using SeraphHorizons.Mod.GearReclamation.Core;

namespace SeraphHorizons.Mod.PicklingTub.Core;

/// <summary>What a finished batch is, for the block info's words: an acid pickle (the metal looks
/// grey and clean) or a brine rust (the gears are rusted through).</summary>
public enum TubRuleKind
{
    Pickle,
    Rust,
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
    public string Failure { get; set; } = GearCodes.SteelBit;

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
    public const string Brine = "game:brineportion";

    // The gears are GearCodes' (the reclamation line's contract); the tub's rules name them by these.
    public const string Bits = GearCodes.SteelBit;
    public const string Degreased = GearCodes.Degreased;
    public const string Pickled = GearCodes.Pickled;
    public const string Steel = GearCodes.Steel;
    public const string SteelBare = GearCodes.SteelBare;
    public const string Rusty = GearCodes.Rusty;

    /// <summary>Gears a batch holds.</summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>Litres of liquid the tub holds.</summary>
    public double CapacityLitres { get; set; } = 10;

    /// <summary>Litres a batch uses up when it is done (none when it is taken out early).</summary>
    public double LitresPerBatch { get; set; } = 1;

    /// <summary>The acid table: degreased gears to pickled ones (vinegar slowest, hydrochloric
    /// fastest), and a short dip that takes a steel gear bare. The acid eats a batch left past
    /// done plus its grace, a gear at a time.</summary>
    public TubRuleConfig[] AcidRules { get; set; } =
    [
        new(Vinegar, Degreased, Pickled, 24, 12, 3),
        new(Sulfuric, Degreased, Pickled, 8, 4, 1),
        new(Hydrochloric, Degreased, Pickled, 2, 1, 0.25),
        new(Vinegar, Steel, SteelBare, 6, 3, 0.75),
        new(Sulfuric, Steel, SteelBare, 2, 1, 0.25),
        new(Hydrochloric, Steel, SteelBare, 0.5, 0.25, 0.0625),
    ];

    /// <summary>Liquid code patterns of the brine bath.</summary>
    public string[] BrineLiquids { get; set; } = [Brine];

    /// <summary>In-game hours brine takes to rust a batch of steel gears.</summary>
    public double BrineRustHours { get; set; } = 48;

    /// <summary>In-game hours brine takes to rust a batch of bare (acid-dipped) steel gears.</summary>
    public double BareBrineRustHours { get; set; } = 4;

    /// <summary>Each brine-rusted gear's chance to over-rust to steel bits.</summary>
    public double OverRustChance { get; set; } = 0.1;

    /// <summary>Gear code patterns the tub refuses with a word (the large steel gear has no
    /// currency form).</summary>
    public string[] RefusedGears { get; set; } = ["seraphhorizons:largegear-*"];

    public static readonly PicklingTubConfig Defaults = new();

    /// <summary>Every rule the tub knows: the acid table, then the brine bath's two per brine.</summary>
    public IEnumerable<TubRuleConfig> AllRules()
    {
        foreach (var rule in AcidRules ?? [])
            yield return rule;
        foreach (string liquid in BrineLiquids ?? [])
        {
            yield return new TubRuleConfig(liquid, Steel, Rusty, BrineRustHours, 0, 0, TubRuleKind.Rust) { LossChance = OverRustChance };
            yield return new TubRuleConfig(liquid, SteelBare, Rusty, BareBrineRustHours, 0, 0, TubRuleKind.Rust) { LossChance = OverRustChance };
        }
    }

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
        if (!double.IsFinite(BrineRustHours) || BrineRustHours <= 0 || BrineRustHours > 24 * 365)
        {
            fixes.Add($"BrineRustHours {BrineRustHours} is out of range, using {Defaults.BrineRustHours}");
            BrineRustHours = Defaults.BrineRustHours;
        }
        if (!double.IsFinite(BareBrineRustHours) || BareBrineRustHours <= 0 || BareBrineRustHours > 24 * 365)
        {
            fixes.Add($"BareBrineRustHours {BareBrineRustHours} is out of range, using {Defaults.BareBrineRustHours}");
            BareBrineRustHours = Defaults.BareBrineRustHours;
        }
        if (!double.IsFinite(OverRustChance) || OverRustChance < 0 || OverRustChance > 1)
        {
            fixes.Add($"OverRustChance {OverRustChance} is out of range, using {Defaults.OverRustChance}");
            OverRustChance = Defaults.OverRustChance;
        }
        BrineLiquids = (BrineLiquids ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
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
