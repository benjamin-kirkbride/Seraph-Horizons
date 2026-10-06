namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>One side of a list for one region: what is always stocked, the pool rotating slots are
/// drawn from, and how many rotating slots there are.</summary>
public sealed record ResolvedSide(IReadOnlyList<TradeEntry> Core, IReadOnlyList<TradeEntry> Rotating, int MaxRotating);

/// <summary>A whole list for one region.</summary>
public sealed record ResolvedList(string Type, Region Region, ResolvedSide Selling, ResolvedSide Buying, NatSpec Wallet);

/// <summary>
/// Turns a <see cref="TradeListDef"/> into what a trader in a given <see cref="Region"/> stocks.
/// The core is the list's core, then its climate key's, then its rock key's; the rotating pool
/// likewise. An entry appears once (by <see cref="TradeEntry.Key"/>, the first wins, and a core entry
/// wins over a rotating one). A trader has <see cref="Slots"/> slots a side (vanilla's
/// InventoryTrader), so the core is cut to that and the rotating slots to what is left. An entry
/// with a <see cref="TradeEntry.StandingTier"/> above the buyer's tier is left out; the ones the
/// tier reaches come after the ungated core, lowest tier first, so a cut takes the highest tiers.
/// A <see cref="TradeEntry.Rare"/> entry is left out unless <c>rareStock</c> is set.
/// </summary>
public static class TradeListResolver
{
    public const int Slots = 16;

    /// <summary>The highest standing tier an entry may ask for.</summary>
    public const int MaxStandingTier = 4;

    public static ResolvedList Resolve(TradeListDef def, Region region, int standingTier = 0, bool rareStock = false) =>
        new(def.Type, region, Resolve(def.Selling, region, standingTier, rareStock), Resolve(def.Buying, region, standingTier, rareStock),
            def.WalletFor(standingTier));

    public static ResolvedSide Resolve(TradeSide side, Region region, int standingTier = 0, bool rareStock = false)
    {
        var keys = new HashSet<string>();
        var core = new List<TradeEntry>();
        // OrderBy is stable: list order within a tier.
        foreach (var e in side.Core.Concat(Regional(side, region).SelectMany(r => r.Core))
                     .Where(e => e.StandingTier <= standingTier && (rareStock || !e.Rare)).OrderBy(e => e.StandingTier))
            if (keys.Add(e.Key)) core.Add(e);
        if (core.Count > Slots) core.RemoveRange(Slots, core.Count - Slots);
        var rotating = new List<TradeEntry>();
        foreach (var e in side.Rotating.List.Concat(Regional(side, region).SelectMany(r => r.Rotating))
                     .Where(e => e.StandingTier <= standingTier && (rareStock || !e.Rare)))
            if (keys.Add(e.Key)) rotating.Add(e);
        int max = Math.Max(0, Math.Min(Math.Min(side.Rotating.MaxItems, Slots - core.Count), rotating.Count));
        return new ResolvedSide(core, rotating, max);
    }

    private static IEnumerable<RegionalList> Regional(TradeSide side, Region region)
    {
        if (side.Regional.TryGetValue(region.Climate, out var c)) yield return c;
        if (side.Regional.TryGetValue(region.Rock, out var r)) yield return r;
    }

    /// <summary>What is wrong with a list, for the loader to log: unknown region keys, entries
    /// without a code, an entry twice in one table, more core than slots, no wallet.</summary>
    public static List<string> Problems(TradeListDef def)
    {
        var problems = new List<string>();
        if (!TraderTypes.All.Contains(def.Type) && !TraderTypes.Visitors.Contains(def.Type))
            problems.Add($"type '{def.Type}' is not one of the eleven or a visitor");
        if (def.Wallet.Count == 0) problems.Add("no wallet");
        foreach (var (name, side) in new[] { ("selling", def.Selling), ("buying", def.Buying) })
        {
            foreach (string key in side.Regional.Keys)
                if (!Region.Climates.Contains(key) && !Region.Rocks.Contains(key))
                    problems.Add($"{name}: unknown region key '{key}'");
            var tables = new List<(string, List<TradeEntry>)> { ($"{name}.core", side.Core), ($"{name}.rotating", side.Rotating.List) };
            tables.AddRange(side.Regional.SelectMany(kv => new[] { ($"{name}.{kv.Key}.core", kv.Value.Core), ($"{name}.{kv.Key}.rotating", kv.Value.Rotating) }));
            foreach (var (table, entries) in tables)
            {
                foreach (var e in entries.Where(e => string.IsNullOrWhiteSpace(e.Code)))
                    problems.Add($"{table}: an entry has no code");
                foreach (var dup in entries.GroupBy(e => e.Key).Where(g => g.Count() > 1))
                    problems.Add($"{table}: {dup.Key} is listed {dup.Count()} times");
                foreach (var e in entries.Where(e => e.Price is null || e.Price.Avg <= 0))
                    problems.Add($"{table}: {e.Key} has no price");
            }
            foreach (var region in Region.All)
            {
                // At the highest tier any entry names: every gated entry is in the core then.
                int core = side.Core.Concat(Regional(side, region).SelectMany(r => r.Core)).Select(e => e.Key).Distinct().Count();
                if (core > Slots) problems.Add($"{name}: {core} core entries in {region}, more than the {Slots} slots");
            }
            foreach (var e in side.Core.Concat(side.Rotating.List).Concat(side.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))))
                if (e.StandingTier < 0 || e.StandingTier > MaxStandingTier)
                    problems.Add($"{name}: {e.Key} has standing tier {e.StandingTier}, outside 0..{MaxStandingTier}");
        }
        return problems;
    }
}
