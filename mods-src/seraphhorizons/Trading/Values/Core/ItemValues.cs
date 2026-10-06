using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.Trading.Values.Core;

/// <summary>Where a value came from.</summary>
public enum ValueSource
{
    /// <summary>The code is in the table.</summary>
    Direct,
    /// <summary>Not in the table: the average of its closest variant family (or of a wildcard's matches).</summary>
    Family,
    /// <summary>Neither the code nor any family it belongs to is in the table.</summary>
    Missing,
}

/// <summary>
/// One answer from <see cref="ItemValues.Lookup"/>. <see cref="Value"/> is gears per item, as the
/// tool derived it; <see cref="Effective"/> is what trading should use, 0 for items worth less than
/// a gear per full stack (<see cref="FloorZero"/>).
/// </summary>
public readonly record struct ValueLookup(string Code, double Value, bool FloorZero, ValueSource Source, string? Family, int Members)
{
    public double Effective => Source == ValueSource.Missing || FloorZero ? 0 : Value;
}

/// <summary>
/// The item base value table (#449), as <c>tools/item-values</c> writes it to
/// <c>assets/seraphhorizons/config/item-values.json</c>: <c>values</c> maps a full code to gears per
/// item, <c>floorZero</c> lists the codes worth under a gear per full stack.
///
/// A code missing from the table falls back to its variant family: the longest prefix of the code
/// that ends at a '-' and that table codes share (<c>game:plank-oak</c> falls back to the
/// average of <c>game:plank-*</c>; <c>game:axe-felling-silver</c> to the felling axes before all
/// axes). A family must keep at least the first segment of the path, so <c>game:plank-oak</c>
/// never averages over everything in <c>game:</c>. A code with <c>*</c> averages every table code
/// it matches.
/// </summary>
public sealed class ItemValues
{
    private readonly Dictionary<string, double> _values;
    private readonly HashSet<string> _floorZero;
    // Family prefix ("game:plank-") -> (sum, count, zeroed members).
    private readonly Dictionary<string, (double Sum, int Count, int Zero)> _families = new(StringComparer.Ordinal);

    public static readonly ItemValues Empty = new(new Dictionary<string, double>(), []);

    public ItemValues(IReadOnlyDictionary<string, double> values, IEnumerable<string> floorZero)
    {
        _values = new Dictionary<string, double>(values, StringComparer.Ordinal);
        _floorZero = new HashSet<string>(floorZero, StringComparer.Ordinal);
        foreach (var (code, value) in _values)
            foreach (var prefix in FamilyPrefixes(code))
            {
                _families.TryGetValue(prefix, out var f);
                _families[prefix] = (f.Sum + value, f.Count + 1, f.Zero + (_floorZero.Contains(code) ? 1 : 0));
            }
    }

    public int Count => _values.Count;

    public IEnumerable<string> Codes => _values.Keys;

    /// <summary>Gears per item trading should use: 0 when worthless or unknown.</summary>
    public double ValueOf(string code) => Lookup(code).Effective;

    /// <summary>Known, and worth less than a gear per full stack. An unknown code is not worthless.</summary>
    public bool IsWorthless(string code)
    {
        var l = Lookup(code);
        return l.Source != ValueSource.Missing && (l.FloorZero || l.Value <= 0);
    }

    public ValueLookup Lookup(string code)
    {
        code = code.ToLowerInvariant();
        if (!code.Contains(':')) code = "game:" + code;
        if (code.Contains('*')) return Wildcard(code);
        if (_values.TryGetValue(code, out var v))
            return new ValueLookup(code, v, _floorZero.Contains(code), ValueSource.Direct, null, 1);
        foreach (var prefix in FamilyPrefixes(code))
            if (_families.TryGetValue(prefix, out var f))
                return new ValueLookup(code, Math.Round(f.Sum / f.Count, 3), f.Zero == f.Count, ValueSource.Family, prefix + "*", f.Count);
        return new ValueLookup(code, 0, false, ValueSource.Missing, null, 0);
    }

    private ValueLookup Wildcard(string pattern)
    {
        var rx = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
        double sum = 0;
        int n = 0, zero = 0;
        foreach (var (code, value) in _values)
        {
            if (!rx.IsMatch(code)) continue;
            sum += value;
            n++;
            if (_floorZero.Contains(code)) zero++;
        }
        return n == 0
            ? new ValueLookup(pattern, 0, false, ValueSource.Missing, null, 0)
            : new ValueLookup(pattern, Math.Round(sum / n, 3), zero == n, ValueSource.Family, pattern, n);
    }

    /// <summary>
    /// The family prefixes of a code, longest first: every prefix ending in '-', down to the one that
    /// ends after the path's first segment. The code itself is not one of them.
    /// </summary>
    public static IEnumerable<string> FamilyPrefixes(string code)
    {
        int colon = code.IndexOf(':');
        int first = code.IndexOf('-', colon + 1);
        if (first < 0) yield break;
        for (int i = code.LastIndexOf('-'); i >= first; i = i == 0 ? -1 : code.LastIndexOf('-', i - 1))
            yield return code[..(i + 1)];
    }

    /// <summary>Reads the table's JSON (the tool's output; strict JSON).</summary>
    public static ItemValues Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("values", out var v))
            foreach (var p in v.EnumerateObject())
                values[p.Name] = p.Value.GetDouble();
        var zero = new List<string>();
        if (root.TryGetProperty("floorZero", out var z))
            foreach (var e in z.EnumerateArray())
                if (e.GetString() is { } s) zero.Add(s);
        return new ItemValues(values, zero);
    }
}
