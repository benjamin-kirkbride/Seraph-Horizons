namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>
/// The rock a spring is drawn as and drops as (README "Springs"): chosen at worldgen from the six
/// blocks round the rivulet's cell, which the generator only uses when five of them are stone.
/// </summary>
public static class HostRock
{
    /// <summary>The rock when no neighbour names one a spring exists for.</summary>
    public const string Fallback = "granite";

    /// <summary>The rock a neighbouring block is of: its <c>rock</c> variant (the game's rock, its
    /// ores, cracked rock, gravel ...), else the rest of a game <c>rock-</c> code; null for a block
    /// that names no rock (air, soil, another mod's stone without one).</summary>
    public static string? RockOf(string? domain, string? path, string? rockVariant)
    {
        if (!string.IsNullOrEmpty(rockVariant))
            return rockVariant;
        if (domain == "game" && path != null && path.StartsWith("rock-", StringComparison.Ordinal) && path.Length > 5)
            return path[5..];
        return null;
    }

    /// <summary>The most common of <paramref name="neighbourRocks"/> that <paramref name="known"/>
    /// has a spring for, the first of them on a tie; else <paramref name="fallback"/>.</summary>
    public static string Choose(IEnumerable<string?> neighbourRocks, Func<string, bool> known, string fallback = Fallback)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var rock in neighbourRocks)
        {
            if (rock == null || !known(rock))
                continue;
            if (counts.TryGetValue(rock, out int n))
                counts[rock] = n + 1;
            else
            {
                counts[rock] = 1;
                order.Add(rock);
            }
        }
        string? best = null;
        foreach (var rock in order)
            if (best == null || counts[rock] > counts[best])
                best = rock;
        return best ?? fallback;
    }
}
