using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.PicklingTub.Core;

/// <summary>Why the tub turns gears away.</summary>
public enum TubRefusal
{
    /// <summary>The gears are taken.</summary>
    None,
    /// <summary>Not a gear the tub knows in any liquid: the click is not the tub's.</summary>
    NotAGear,
    /// <summary>A refused gear (the large steel gear): the tub says so.</summary>
    Refused,
    /// <summary>A known gear, but not in the liquid the tub holds.</summary>
    WrongLiquid,
    /// <summary>A different gear from the batch already in.</summary>
    OtherBatch,
    /// <summary>The batch is finished (or being eaten): take it out first.</summary>
    BatchFinished,
    /// <summary>The batch is full.</summary>
    Full,
}

/// <summary>The tub's rules, with code matching: which liquid takes which gear to what.</summary>
public sealed class TubRuleBook
{
    private readonly TubRuleConfig[] _rules;
    private readonly string[] _refused;

    public TubRuleBook(PicklingTubConfig config)
    {
        Config = config;
        _rules = config.AllRules().ToArray();
        _refused = config.RefusedGears.Select(Normalize).ToArray();
    }

    public PicklingTubConfig Config { get; }

    public IReadOnlyList<TubRuleConfig> Rules => _rules;

    /// <summary>A code as rules compare it: trimmed, lower case, <c>game</c> when it has no domain.</summary>
    public static string Normalize(string code)
    {
        code = code.Trim().ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }

    /// <summary>The rule for <paramref name="input"/> in <paramref name="liquid"/>, or null. The first
    /// row that matches wins.</summary>
    public TubRuleConfig? For(string? liquid, string? input)
    {
        if (string.IsNullOrWhiteSpace(liquid) || string.IsNullOrWhiteSpace(input))
            return null;
        string l = Normalize(liquid), i = Normalize(input);
        return _rules.FirstOrDefault(r => Normalize(r.Input) == i && Wildcard.IsMatch(Normalize(r.Liquid), l));
    }

    /// <summary>Whether any rule takes <paramref name="input"/>, in any liquid.</summary>
    public bool IsInput(string? input) =>
        !string.IsNullOrWhiteSpace(input) && _rules.Any(r => Normalize(r.Input) == Normalize(input));

    /// <summary>Whether any rule names <paramref name="liquid"/>.</summary>
    public bool IsLiquid(string? liquid) =>
        !string.IsNullOrWhiteSpace(liquid) && _rules.Any(r => Wildcard.IsMatch(Normalize(r.Liquid), Normalize(liquid)));

    public bool IsRefused(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _refused.Any(p => Wildcard.IsMatch(p, Normalize(code)));

    /// <summary>
    /// Whether <paramref name="input"/> may go into a tub holding <paramref name="liquid"/> (null: none)
    /// and <paramref name="batch"/> (null: none) at <paramref name="nowHours"/>.
    /// </summary>
    public TubRefusal CanAdd(string input, string? liquid, TubBatch? batch, double nowHours)
    {
        if (IsRefused(input))
            return TubRefusal.Refused;
        if (!IsInput(input))
            return TubRefusal.NotAGear;
        if (batch != null)
        {
            if (Normalize(batch.Input) != Normalize(input))
                return TubRefusal.OtherBatch;
            var rule = For(batch.Liquid ?? liquid, batch.Input);
            if (rule != null && batch.StageAt(rule, nowHours).Phase >= TubPhase.Done)
                return TubRefusal.BatchFinished;
            if (batch.Count >= Config.BatchSize)
                return TubRefusal.Full;
        }
        if (liquid != null && For(liquid, input) == null)
            return TubRefusal.WrongLiquid;
        return TubRefusal.None;
    }

    /// <summary>Whether <paramref name="liquid"/> may be poured on <paramref name="batch"/>'s gears
    /// (null: none): any liquid of the rules on an empty tub, else one with a rule for them.</summary>
    public bool CanPour(string liquid, TubBatch? batch) =>
        batch == null ? IsLiquid(liquid) : For(liquid, batch.Input) != null;
}
