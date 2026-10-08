namespace SeraphHorizons.Mod.Core;

/// <summary>
/// The height a surface ruin is seated on under <c>RuinsOnMedianGround</c>: the median of the
/// terrain heights the game sampled around its footprint, in place of their minimum. The game's own
/// rejection (highest minus lowest sample above the schematic's <c>MaxYDiff</c>) is untouched, so
/// the median is never more than <c>MaxYDiff</c> above the lowest sample. Game-independent;
/// <c>RuinSurfaceMedian</c> applies it.
/// </summary>
public static class RuinSurfaceHeight
{
    /// <summary>The median of the samples: for an even count the lower of the two middle values,
    /// so the result is always a height the game sampled, and a tie leans towards the game's own
    /// choice (the lowest). <paramref name="fallback"/> when there are no samples.</summary>
    public static int Median(IReadOnlyList<int> samples, int fallback)
    {
        if (samples.Count == 0)
            return fallback;
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        return sorted[(sorted.Length - 1) / 2];
    }

    /// <summary>The base height for a ruin whose samples the game reduced to <paramref name="min"/>:
    /// the median of <paramref name="samples"/>, or <paramref name="min"/> itself when the samples
    /// are missing or do not hold it (they were not taken by the same call, so the median would be
    /// of some other terrain).</summary>
    public static int Base(IReadOnlyList<int> samples, int min)
    {
        if (samples.Count == 0 || samples.Min() != min)
            return min;
        return Median(samples, min);
    }
}
