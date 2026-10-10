namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Fractional carry-over (#685): a station turns metal units into whole items and holds the rest
/// for the next item, per output (an item code, or anything the station keys its outputs by), so
/// no metal is lost to rounding. A 20-unit raw ore at 22 % is 4.4 units, no concentrate item yet;
/// the next one makes 8.8, one item out and 3.8 held. A station keeps it with its own state
/// (<see cref="Held"/>, <see cref="Restore"/>) so it survives a save.
/// </summary>
public sealed class UnitCarry
{
    // Units within this of a whole item count as one, so 0.22 × 1600 doesn't come out an item short.
    private const double Epsilon = 1e-9;

    private readonly Dictionary<string, double> _held = new(StringComparer.Ordinal);

    /// <summary>Adds <paramref name="units"/> to what is held for <paramref name="output"/> and
    /// takes out as many whole items of <paramref name="unitsPerItem"/> as that makes.</summary>
    public int Add(string output, double units, double unitsPerItem)
    {
        if (!(unitsPerItem > 0)) throw new ArgumentOutOfRangeException(nameof(unitsPerItem), unitsPerItem, "must be positive");
        double total = _held.GetValueOrDefault(output) + Math.Max(0, units);
        int items = (int)Math.Floor((total + Epsilon) / unitsPerItem);
        double rest = Math.Max(0, total - items * unitsPerItem);
        if (rest > Epsilon) _held[output] = rest;
        else _held.Remove(output);
        return items;
    }

    /// <summary>Units held toward the next item of an output.</summary>
    public double HeldFor(string output) => _held.GetValueOrDefault(output);

    /// <summary>Everything held, for saving.</summary>
    public IReadOnlyDictionary<string, double> Held => _held;

    /// <summary>Replaces what is held with a saved state.</summary>
    public void Restore(IEnumerable<KeyValuePair<string, double>> held)
    {
        _held.Clear();
        foreach (var (k, v) in held)
            if (v > Epsilon) _held[k] = v;
    }
}
