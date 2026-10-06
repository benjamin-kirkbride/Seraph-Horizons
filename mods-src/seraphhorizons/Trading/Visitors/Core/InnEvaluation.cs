namespace SeraphHorizons.Mod.Trading.Visitors.Core;

/// <summary>Everything a day's check of an inn looks at: the building, the owner's standing, and
/// per visitor kind the region's supply of what it buys.</summary>
public sealed record InnEvaluation(InnReport Report, StandingCheck Standing, IReadOnlyDictionary<string, SupplyCheck> Supply)
{
    /// <summary>The kinds that would come: the inn and standing pass, and the kind's supply does.</summary>
    public IReadOnlyList<string> PassingKinds =>
        Report.Passed && Standing.Passed ? Supply.Where(kv => kv.Value.Passed).Select(kv => kv.Key).ToList() : [];

    /// <summary>What is missing, as codes (<c>stall</c>, <c>bed</c>, …, <c>standing</c>, <c>supply</c>):
    /// lang keys <c>trading-inn-missing-{code}</c>.</summary>
    public IEnumerable<string> Missing
    {
        get
        {
            foreach (var rule in Report.Failed) yield return rule.ToString().ToLowerInvariant();
            if (!Standing.Passed) yield return "standing";
            if (Supply.Count > 0 && !Supply.Values.Any(s => s.Passed)) yield return "supply";
        }
    }
}
