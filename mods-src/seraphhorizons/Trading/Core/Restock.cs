namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>Who is restocking, for an <see cref="ISupplyGate"/>.</summary>
public readonly record struct TraderContext(string Type, Region Region, long EntityId, double X, double Z);

/// <summary>
/// Decides whether a player-supplied selling entry is on a trader's shelf (#451's regional supply
/// implements it). <see cref="Stock"/> is asked at every restock for each such entry the trader
/// could stock; 0 or less means not stocked, anything above is the stock it is shelved with.
/// </summary>
public interface ISupplyGate
{
    int Stock(TraderContext trader, TradeEntry entry);
}

/// <summary>Until there is a supply system: player-supplied goods are never stocked.</summary>
public sealed class NoSupply : ISupplyGate
{
    public static readonly NoSupply Instance = new();
    public int Stock(TraderContext trader, TradeEntry entry) => 0;
}

/// <summary>What a slot holds after a restock: nothing, a fresh entry (with the stock the supply
/// gate gave a player-supplied one, else the entry's own), or what another slot held before (a
/// rotating entry kept as it was, stock and price included).</summary>
public readonly record struct SlotPlan(TradeEntry? Entry, int? Stock = null, int? KeptFrom = null)
{
    public static readonly SlotPlan Empty = new(null);
    public bool IsEmpty => Entry is null;
}

/// <summary>A slot before the restock: the key of what it holds (null if empty) and whether any
/// of it is left.</summary>
public readonly record struct SlotState(string? Key, bool InStock);

/// <summary>
/// Plans a side's slots at a restock. The core comes first, in list order, always fresh (full
/// stock, new price), except player-supplied entries the gate does not stock. Then the rotating
/// slots: one still in stock from before stays with chance 1 − <c>refreshChance</c> (vanilla's
/// restock passes 0.5 weekly, 1.1 on spawn, so everything is redrawn then); the rest are drawn from
/// the pool, shuffled, skipping what is already on the shelf and gated entries the gate refuses.
/// Selling sides pass a gate; buying sides pass none (a trader buys player-supplied goods freely).
/// </summary>
public static class RestockPlanner
{
    /// <param name="gate">The supply gate's stock for a player-supplied entry (selling sides), or null.</param>
    /// <param name="accept">Whether an entry may be shelved at all now (the game's
    /// ITradeableCollectible, e.g. a locator map with nothing to point at), or null for all.</param>
    public static SlotPlan[] Plan(ResolvedSide side, IReadOnlyList<SlotState> current, double refreshChance, Random rng,
        Func<TradeEntry, int>? gate, Func<TradeEntry, bool>? accept = null)
    {
        var plan = new List<SlotPlan>(TradeListResolver.Slots);
        var onShelf = new HashSet<string>();
        foreach (var e in side.Core)
        {
            if (accept != null && !accept(e)) continue;
            if (gate != null && e.PlayerSupplied)
            {
                int stock = gate(e);
                if (stock <= 0) continue;
                plan.Add(new SlotPlan(e, stock));
            }
            else plan.Add(new SlotPlan(e));
            onShelf.Add(e.Key);
        }

        var pool = side.Rotating.ToDictionary(e => e.Key);
        int rotating = 0;
        for (int i = 0; i < current.Count && rotating < side.MaxRotating; i++)
        {
            var slot = current[i];
            if (slot.Key is null || !slot.InStock || !pool.TryGetValue(slot.Key, out var entry) || onShelf.Contains(slot.Key)) continue;
            if (rng.NextDouble() <= refreshChance) continue;
            plan.Add(new SlotPlan(entry, KeptFrom: i));
            onShelf.Add(slot.Key);
            rotating++;
        }

        var draw = side.Rotating.Where(e => !onShelf.Contains(e.Key)).ToList();
        Shuffle(draw, rng);
        foreach (var e in draw)
        {
            if (rotating >= side.MaxRotating) break;
            if (accept != null && !accept(e)) continue;
            if (gate != null && e.PlayerSupplied)
            {
                int stock = gate(e);
                if (stock <= 0) continue;
                plan.Add(new SlotPlan(e, stock));
            }
            else plan.Add(new SlotPlan(e));
            onShelf.Add(e.Key);
            rotating++;
        }

        while (plan.Count < TradeListResolver.Slots) plan.Add(SlotPlan.Empty);
        return plan.Take(TradeListResolver.Slots).ToArray();
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
