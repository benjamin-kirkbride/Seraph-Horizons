namespace SeraphHorizons.Mod.CreativeItems.Core;

/// <summary>The creative item sink's count: items received by key (the item or block's class and
/// code), and their total.</summary>
public sealed class ItemTally
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);

    public long Total { get; private set; }

    public int Kinds => _counts.Count;

    public long this[string key] => _counts.GetValueOrDefault(key);

    public void Add(string key, long count)
    {
        if (count <= 0 || string.IsNullOrEmpty(key))
            return;
        _counts[key] = _counts.GetValueOrDefault(key) + count;
        Total += count;
    }

    public void Reset()
    {
        _counts.Clear();
        Total = 0;
    }

    /// <summary>Every key and its count, most first, then by key.</summary>
    public IReadOnlyList<KeyValuePair<string, long>> Ordered() =>
        _counts.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();

    /// <summary>Replaces the counts with saved ones, dropping any that are not positive; the total
    /// is their sum.</summary>
    public void Restore(IEnumerable<KeyValuePair<string, long>> counts)
    {
        Reset();
        foreach (var (key, count) in counts)
            Add(key, count);
    }
}
