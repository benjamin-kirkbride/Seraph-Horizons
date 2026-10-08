namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>An entity type with a vanilla-format trade list (<c>tradePropsFile</c> or
/// <c>tradeProps</c>), which the game's handbook lists under "Sold by" and "Purchased by".</summary>
/// <param name="Code">Its full code, <c>domain:path</c>.</param>
/// <param name="Class">Its entity class, e.g. <c>EntityTrader</c>.</param>
public readonly record struct ListedTrader(string Code, string? Class);

/// <summary>
/// Which traders the handbook and the recipe browser leave out of "Sold by" and "Purchased by"
/// (README "Traders"): in a world with the trader grid, every spawner of another mod's or the
/// game's trader is rewritten to one of the pack's types (<see cref="IsOtherTrader"/>), so those
/// traders are never met, unless a story structure's spawner spawns them (vanilla's treasure
/// hunter), which the grid leaves alone.
/// </summary>
public static class HandbookTraders
{
    private const string PackDomain = "seraphhorizons";

    /// <summary>Climate suffixes of vanilla's trader codes: its climate system turns a spawner's
    /// <c>-temperate</c> into the other two.</summary>
    private static readonly string[] Climates = ["-temperate", "-cold", "-desert"];

    /// <summary>A trader entity code that is not the pack's: vanilla's, Culinary Artillery's,
    /// Domestic Animal Trader's (by entity class <c>EntityTrader</c> or a trader code). The grid
    /// rewrites spawners of these.</summary>
    public static bool IsOtherTrader(string domain, string path, string? entityClass) =>
        domain != PackDomain && (entityClass == "EntityTrader" || path.StartsWith("trader-", StringComparison.Ordinal)
                                 || path.Contains("-trader-", StringComparison.Ordinal));

    /// <summary>The codes of the listed traders never met in the world: other traders, less those
    /// a story structure's spawner spawns (in any of the three climates). Sorted.</summary>
    /// <param name="storyCodes">The entity codes of the story structures' spawners, with or
    /// without the <c>game:</c> domain.</param>
    public static List<string> Hidden(IEnumerable<ListedTrader> listed, IEnumerable<string> storyCodes)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (string code in storyCodes)
        {
            string full = code.Contains(':') ? code : "game:" + code;
            kept.Add(full);
            foreach (string from in Climates)
                if (full.EndsWith(from, StringComparison.Ordinal))
                    foreach (string to in Climates)
                        kept.Add(full[..^from.Length] + to);
        }
        var hidden = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var trader in listed)
        {
            int colon = trader.Code.IndexOf(':');
            string domain = colon < 0 ? "game" : trader.Code[..colon];
            string path = colon < 0 ? trader.Code : trader.Code[(colon + 1)..];
            string full = domain + ":" + path;
            if (IsOtherTrader(domain, path, trader.Class) && !kept.Contains(full)) hidden.Add(full);
        }
        return [.. hidden];
    }

    /// <summary>The handbook names its traders, not their codes: the names to leave out are those of
    /// hidden traders that no listed trader still met shares.</summary>
    /// <param name="names">Each listed trader's code and handbook name.</param>
    public static HashSet<string> HiddenNames(IEnumerable<(string Code, string Name)> names, IReadOnlyCollection<string> hiddenCodes)
    {
        var hidden = new HashSet<string>(hiddenCodes, StringComparer.Ordinal);
        var drop = new HashSet<string>(StringComparer.Ordinal);
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, name) in names)
            (hidden.Contains(code) ? drop : keep).Add(name);
        drop.ExceptWith(keep);
        return drop;
    }
}
