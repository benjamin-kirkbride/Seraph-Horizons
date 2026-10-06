namespace SeraphHorizons.Mod.Trading.Visitors.Core;

public sealed record ConditionSettings
{
    /// <summary>The flag owner's best standing tier with a camp near the inn (2 = regular).</summary>
    public int MinTier { get; init; } = 2;
    /// <summary>How far from the inn a camp counts, in blocks.</summary>
    public double CampRadius { get; init; } = 6000;
    /// <summary>The region's summed supply level of the goods a visitor comes to buy (a level is
    /// 10 gears' worth sold and not yet drained or decayed).</summary>
    public double MinSupply { get; init; } = 2;
}

/// <summary>A condition's outcome; <see cref="Skipped"/> when its feature is off (it then passes).</summary>
public readonly record struct StandingCheck(bool Passed, int BestTier, string? CampId, int Camps, bool Skipped = false);

public readonly record struct SupplyCheck(bool Passed, double Level, bool Skipped = false);

/// <summary>A camp near an inn, as the grid's registry holds it.</summary>
public readonly record struct CampSite(string Id, double X, double Z);

/// <summary>
/// What calls a visitor besides the inn itself (#456): the flag owner is known in the area (their
/// best standing tier among the placed camps within <see cref="ConditionSettings.CampRadius"/>,
/// at least <see cref="ConditionSettings.MinTier"/>), and the region trades in what the visitor
/// buys (the summed supply level of its buying list in the inn's supply region, at least
/// <see cref="ConditionSettings.MinSupply"/>). The visitor's own specials are sold nowhere else, so
/// they have no supply to read; what it buys is the trade it comes for.
/// </summary>
public static class VisitConditions
{
    public static StandingCheck Standing(IEnumerable<CampSite> camps, double x, double z, Func<string, int> tierOf, ConditionSettings? settings = null)
    {
        var s = settings ?? new ConditionSettings();
        int best = 0, count = 0;
        string? bestId = null;
        foreach (var c in camps)
        {
            double dx = c.X - x, dz = c.Z - z;
            if (dx * dx + dz * dz > s.CampRadius * s.CampRadius) continue;
            count++;
            int tier = tierOf(c.Id);
            if (bestId is null || tier > best) (best, bestId) = (tier, c.Id);
        }
        return new StandingCheck(bestId != null && best >= s.MinTier, best, bestId, count);
    }

    public static SupplyCheck Supply(IEnumerable<string> codes, Func<string, double> level, ConditionSettings? settings = null)
    {
        var s = settings ?? new ConditionSettings();
        double sum = codes.Distinct().Sum(level);
        return new SupplyCheck(sum >= s.MinSupply, sum);
    }
}
