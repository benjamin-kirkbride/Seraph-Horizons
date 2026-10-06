using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Trading.Economy.Core;

/// <summary>
/// The tuning of regional supply (#451). A level is measured in <see cref="ReferenceGears"/>'
/// worth of goods: selling an item adds its value / ReferenceGears, so a stack of planks
/// (64 × 0.06) and an iron ingot (3.5) move their items' levels about alike.
/// </summary>
public sealed record SupplySettings
{
    /// <summary>Days for a level to halve on its own (exponential decay, once a day).</summary>
    public double HalfLifeDays { get; init; } = 10;
    /// <summary>The share of a region's level that moves to its eight neighbours each day.</summary>
    public double SpreadFraction { get; init; } = 0.1;
    /// <summary>Gears' worth of goods that make one level.</summary>
    public double ReferenceGears { get; init; } = 10;
    public PriceCurve Curve { get; init; } = PriceCurve.Default;
    /// <summary>The level from which player-supplied goods are shelved at all.</summary>
    public double ShelfMinLevel { get; init; } = 1;
    /// <summary>The share of the region's supply one trader shelves at a restock.</summary>
    public double ShelfShare { get; init; } = 0.5;
    /// <summary>A shelf holds at most this many times the entry's own stock.</summary>
    public double ShelfCapMultiple { get; init; } = 2;
    /// <summary>Levels under this are dropped at the daily tick.</summary>
    public double Prune { get; init; } = 0.01;
    /// <summary>Events kept per entry for <c>/sh trade supply trace</c>.</summary>
    public int HistorySize { get; init; } = 16;

    public double DailyDecay => Math.Pow(0.5, 1 / Math.Max(0.01, HalfLifeDays));
}

/// <summary>
/// Supply regions: 8192-block squares (<see cref="Size"/>), the grid's settlement cells, 4 × 4 camp
/// cells, so about sixteen camps share a level; a key is <c>rx,rz</c> (block coordinate floor-divided
/// by the size). Supply spreads to the eight neighbouring regions, orthogonal ones at weight 1 and
/// diagonal ones at 1/√2 (thinning with distance), normalised.
/// </summary>
public static class SupplyRegion
{
    public const int Size = 8192;

    public static string KeyOf(double x, double z) =>
        $"{(int)Math.Floor(x / Size)},{(int)Math.Floor(z / Size)}";

    public static bool TryParse(string key, out int rx, out int rz)
    {
        rx = rz = 0;
        var parts = key.Split(',');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rx)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rz);
    }

    private static readonly (int Dx, int Dz, double W)[] Ring = BuildRing();

    private static (int, int, double)[] BuildRing()
    {
        var ring = new List<(int, int, double)>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dz != 0) ring.Add((dx, dz, 1 / Math.Sqrt(dx * dx + dz * dz)));
        double sum = ring.Sum(r => r.Item3);
        return ring.Select(r => (r.Item1, r.Item2, r.Item3 / sum)).ToArray();
    }

    /// <summary>The eight neighbours of a region with the share of its outflow each gets (sum 1).</summary>
    public static IEnumerable<(string Key, double Share)> Neighbours(string key)
    {
        if (!TryParse(key, out int rx, out int rz)) yield break;
        foreach (var (dx, dz, w) in Ring) yield return ($"{rx + dx},{rz + dz}", w);
    }
}

public enum SupplyEventKind { Sold, Bought, Set, Added, Reset, Day }

/// <summary>One change to a level: the book's day, what, by how much, and the level after.</summary>
public readonly record struct SupplyEvent(int Day, SupplyEventKind Kind, double Delta, double Level);

public sealed class SupplyEntry
{
    public double Level { get; set; }
    public List<SupplyEvent> History { get; set; } = [];
}

/// <summary>
/// Regional supply levels (#451): per (region key, item code), a level in [0, ∞). Selling raises it
/// (<see cref="Sold"/>), a trader's sale to a player drains it (<see cref="Bought"/>), and once a day
/// (<see cref="Tick"/>) every level decays by <see cref="SupplySettings.DailyDecay"/> and passes
/// <see cref="SupplySettings.SpreadFraction"/> of itself to the neighbouring regions. Per item, not per
/// metal: selling iron bars lowers the bar's price, not the pick's. Not thread-safe: the server's main
/// thread owns it.
/// </summary>
public sealed class SupplyBook
{
    private readonly Dictionary<string, Dictionary<string, SupplyEntry>> _regions = new(StringComparer.Ordinal);

    public SupplyBook(SupplySettings? settings = null) => Settings = settings ?? new SupplySettings();

    public SupplySettings Settings { get; set; }

    /// <summary>Days ticked so far (calendar days and simulated ones); history is stamped with it.</summary>
    public int Day { get; private set; }

    public IEnumerable<string> Regions => _regions.Keys;

    public double Level(string region, string item) =>
        _regions.TryGetValue(region, out var r) && r.TryGetValue(item, out var e) ? e.Level : 0;

    public double Factor(string region, string item) => Settings.Curve.Factor(Level(region, item));

    public SupplyEntry? Entry(string region, string item) =>
        _regions.TryGetValue(region, out var r) ? r.GetValueOrDefault(item) : null;

    /// <summary>A region's entries, highest level first.</summary>
    public IEnumerable<(string Item, SupplyEntry Entry)> In(string region) =>
        _regions.TryGetValue(region, out var r) ? r.OrderByDescending(kv => kv.Value.Level).Select(kv => (kv.Key, kv.Value)) : [];

    /// <summary>What one item adds to its level: its value in <see cref="SupplySettings.ReferenceGears"/>;
    /// an item without a value counts a full stack as one level.</summary>
    public double Weight(double valuePerItem, int maxStackSize) =>
        valuePerItem > 0 ? valuePerItem / Settings.ReferenceGears : 1.0 / Math.Max(1, maxStackSize);

    public void Sold(string region, string item, int count, double weight) => Change(region, item, count * weight, SupplyEventKind.Sold);

    public void Bought(string region, string item, int count, double weight) => Change(region, item, -count * weight, SupplyEventKind.Bought);

    public void Add(string region, string item, double delta) => Change(region, item, delta, SupplyEventKind.Added);

    public void Set(string region, string item, double level)
    {
        var e = GetOrAdd(region, item);
        double before = e.Level;
        e.Level = Math.Max(0, level);
        Record(e, SupplyEventKind.Set, e.Level - before);
    }

    /// <summary>Drops one item's level in a region, or (item null) the whole region's.</summary>
    public int Reset(string region, string? item = null)
    {
        if (!_regions.TryGetValue(region, out var r)) return 0;
        if (item is null)
        {
            int n = r.Count;
            _regions.Remove(region);
            return n;
        }
        int removed = r.Remove(item) ? 1 : 0;
        if (r.Count == 0) _regions.Remove(region);
        return removed;
    }

    public void Clear() => _regions.Clear();

    /// <summary>Takes the levels, history and day of <paramref name="other"/> in place of this book's,
    /// keeping its own settings (admin import, #459).</summary>
    public void ReplaceWith(SupplyBook other)
    {
        _regions.Clear();
        foreach (var (region, items) in other._regions)
            _regions[region] = items.ToDictionary(kv => kv.Key,
                kv => new SupplyEntry { Level = kv.Value.Level, History = [.. kv.Value.History] }, StringComparer.Ordinal);
        Day = other.Day;
    }

    private void Change(string region, string item, double delta, SupplyEventKind kind)
    {
        if (delta == 0) return;
        var e = GetOrAdd(region, item);
        double before = e.Level;
        e.Level = Math.Max(0, e.Level + delta);
        Record(e, kind, e.Level - before);
    }

    private SupplyEntry GetOrAdd(string region, string item)
    {
        if (!_regions.TryGetValue(region, out var r)) _regions[region] = r = new Dictionary<string, SupplyEntry>(StringComparer.Ordinal);
        if (!r.TryGetValue(item, out var e)) r[item] = e = new SupplyEntry();
        return e;
    }

    private void Record(SupplyEntry e, SupplyEventKind kind, double delta)
    {
        e.History.Add(new SupplyEvent(Day, kind, delta, e.Level));
        int over = e.History.Count - Math.Max(1, Settings.HistorySize);
        if (over > 0) e.History.RemoveRange(0, over);
    }

    /// <summary>
    /// One day: every level decays, then <see cref="SupplySettings.SpreadFraction"/> of what is left
    /// moves to the neighbouring regions (from the decayed levels of all regions at once, so the order
    /// regions are visited in does not matter); levels under <see cref="SupplySettings.Prune"/> are dropped.
    /// </summary>
    public void Tick()
    {
        Day++;
        double decay = Settings.DailyDecay, spread = Math.Clamp(Settings.SpreadFraction, 0, 1);
        var inflow = new Dictionary<(string, string), double>();
        foreach (var (region, items) in _regions)
            foreach (var (item, e) in items)
            {
                double level = e.Level * decay;
                double outflow = level * spread;
                if (outflow > 0)
                    foreach (var (n, share) in SupplyRegion.Neighbours(region))
                    {
                        inflow.TryGetValue((n, item), out double v);
                        inflow[(n, item)] = v + outflow * share;
                    }
                e.Level = level - outflow;
            }
        foreach (var ((region, item), add) in inflow)
        {
            if (add < Settings.Prune && Level(region, item) <= 0) continue;
            GetOrAdd(region, item).Level += add;
        }
        foreach (var region in _regions.Keys.ToList())
        {
            var items = _regions[region];
            foreach (var item in items.Keys.ToList())
            {
                var e = items[item];
                if (e.Level < Settings.Prune) items.Remove(item);
                else if (e.History.Count == 0 || e.History[^1].Kind != SupplyEventKind.Day || Math.Abs(e.History[^1].Level - e.Level) > 1e-4)
                    Record(e, SupplyEventKind.Day, e.Level - (e.History.Count > 0 ? e.History[^1].Level : 0));
            }
            if (items.Count == 0) _regions.Remove(region);
        }
    }

    /// <summary>
    /// How many of a player-supplied entry a trader shelves at a restock (0: not at all). Nothing
    /// under <see cref="SupplySettings.ShelfMinLevel"/>; above it, <see cref="SupplySettings.ShelfShare"/>
    /// of the region's supply counted in the entry's stacks, at most
    /// <see cref="SupplySettings.ShelfCapMultiple"/> × the entry's own stock.
    /// </summary>
    /// <param name="stackValueGears">The value of one entry stack (value per item × stack size).</param>
    public int ShelfStock(double level, double stackValueGears, double entryStock)
    {
        if (level < Settings.ShelfMinLevel) return 0;
        double stacks = stackValueGears > 0 ? level * Settings.ReferenceGears / stackValueGears : level;
        int stock = (int)Math.Floor(stacks * Settings.ShelfShare + 1e-9);
        int cap = Math.Max(1, (int)Math.Ceiling(Math.Max(1, entryStock) * Settings.ShelfCapMultiple));
        return Math.Clamp(stock, 0, cap);
    }

    private sealed class Dto
    {
        [JsonPropertyName("day")] public int Day { get; set; }
        [JsonPropertyName("regions")] public Dictionary<string, Dictionary<string, EntryDto>> Regions { get; set; } = new();
    }

    private sealed class EntryDto
    {
        [JsonPropertyName("level")] public double Level { get; set; }
        // [day, kind, delta, level]
        [JsonPropertyName("history")] public List<double[]> History { get; set; } = [];
    }

    public string ToJson()
    {
        var dto = new Dto { Day = Day };
        foreach (var (region, items) in _regions)
            dto.Regions[region] = items.ToDictionary(kv => kv.Key, kv => new EntryDto
            {
                Level = kv.Value.Level,
                History = kv.Value.History.Select(h => new[] { h.Day, (double)(int)h.Kind, h.Delta, h.Level }).ToList(),
            });
        return JsonSerializer.Serialize(dto);
    }

    public static SupplyBook FromJson(string json, SupplySettings? settings = null)
    {
        var book = new SupplyBook(settings);
        var dto = JsonSerializer.Deserialize<Dto>(json) ?? new Dto();
        book.Day = dto.Day;
        foreach (var (region, items) in dto.Regions)
            foreach (var (item, e) in items)
            {
                var entry = book.GetOrAdd(region, item);
                entry.Level = Math.Max(0, e.Level);
                entry.History = e.History.Where(h => h.Length >= 4)
                    .Select(h => new SupplyEvent((int)h[0], (SupplyEventKind)(int)h[1], h[2], h[3])).ToList();
            }
        return book;
    }
}
