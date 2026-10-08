using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>
/// The goods a player's standing does not unlock yet at a trader (the Trade tab's locked stock):
/// what the trader's selling list would shelve at the top tier with rare stock, less what it shelves
/// at the player's own tier, less what is on the shelf anyway (shelves follow the best recent
/// customer, so a stranger may find a trusted customer's stock there). Each with the tier that
/// unlocks it: its <see cref="TradeEntry.StandingTier"/> (schematics and other gated goods), or the
/// first tier with <c>rareStock</c> for a rare good. Special entries (maps, leads) are left to the
/// Maps &amp; leads tab.
/// </summary>
public static class LockedStock
{
    /// <summary>At most this many locked goods are shown.</summary>
    public const int MaxShown = 8;

    /// <param name="selling">The trader's selling side.</param>
    /// <param name="playerTier">The player's tier index here.</param>
    /// <param name="rareByTier">Each tier's <c>rareStock</c> unlock, by tier index.</param>
    /// <param name="shelved">The keys (<see cref="TradeEntry.Key"/>) of what is on the shelf now.</param>
    public static List<LockedRow> Of(TradeSide selling, Region region, int playerTier, IReadOnlyList<bool> rareByTier,
        IReadOnlyCollection<string> shelved, int max = MaxShown)
    {
        bool rareMine = playerTier >= 0 && playerTier < rareByTier.Count && rareByTier[playerTier];
        var all = TradeListResolver.Resolve(selling, region, TradeListResolver.MaxStandingTier, rareStock: true);
        var mine = TradeListResolver.Resolve(selling, region, Math.Max(0, playerTier), rareMine);
        var reachable = mine.Core.Concat(mine.Rotating).Select(e => e.Key).ToHashSet();
        int firstRare = Enumerable.Range(0, rareByTier.Count).FirstOrDefault(i => rareByTier[i], -1);
        var rows = new List<LockedRow>();
        var rotating = all.Rotating.Select(e => e.Key).ToHashSet();
        foreach (var e in all.Core.Concat(all.Rotating))
        {
            if (e.Kind != null || reachable.Contains(e.Key) || shelved.Contains(e.Key)) continue;
            int tier = e.StandingTier;
            var reason = LockReason.Tier;
            if (e.Rare && firstRare > tier)
            {
                tier = firstRare;
                reason = LockReason.Rare;
            }
            if (tier <= playerTier) continue;
            rows.Add(new LockedRow
            {
                Type = e.Type,
                Code = e.Code.Contains(':') ? e.Code : "game:" + e.Code,
                Attributes = e.AttributesKey.Length > 0 ? e.AttributesKey : null,
                StackSize = Math.Max(1, e.StackSize),
                Tier = tier,
                Reason = reason,
                Rotating = rotating.Contains(e.Key),
            });
        }
        return rows.OrderBy(r => r.Tier).ThenBy(r => r.Rotating).Take(Math.Max(0, max)).ToList();
    }

    /// <summary>The lowest tier whose unlock <paramref name="has"/> holds, or -1.</summary>
    public static int FirstTier(IReadOnlyList<bool> has)
    {
        for (int i = 0; i < has.Count; i++)
            if (has[i]) return i;
        return -1;
    }
}
