using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// The precision of an ore map offer, drawn once when the offer is shelved (a restock, or a survey
/// landing in place of "being surveyed") and kept on the offer until the next restock replaces it:
/// the same for every buyer, whatever their standing (<c>precisionWeights</c> in
/// <c>config/trading/map-prices.json</c>).
/// </summary>
public static class MapPrecisionRoll
{
    /// <summary>A precision 1–3 drawn by <paramref name="weights"/> (relative, one per precision
    /// 1, 2, 3). Rough when the weights are not three usable numbers (none negative, sum above 0).</summary>
    public static int Roll(IReadOnlyList<double> weights, Random rand)
    {
        if (weights.Count != 3 || weights.Any(w => w < 0 || !double.IsFinite(w))) return MapPrecision.Rough;
        double total = weights.Sum();
        if (total <= 0) return MapPrecision.Rough;
        double pick = rand.NextDouble() * total;
        int last = MapPrecision.Rough;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;
            last = MapPrecision.Rough + i;
            pick -= weights[i];
            if (pick < 0) return last;
        }
        // Rounding at the top end: the last precision with any weight.
        return last;
    }
}
