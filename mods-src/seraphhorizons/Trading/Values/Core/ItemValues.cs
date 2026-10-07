using System.Collections.Concurrent;
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
/// One answer from <see cref="ItemValues.Lookup"/>. <see cref="Value"/> is gears per item (for a
/// liquid, per portion item), as trading prices stacks; <see cref="Effective"/> is what trading
/// should use, 0 for items worth less than a gear per full stack (<see cref="FloorZero"/>).
/// <see cref="PerLitre"/> is the items per litre when the value is priced per litre (the table's
/// <c>perLitre</c>), and <see cref="Display"/> the value in the unit it is priced in: gears per
/// litre for a liquid, else gears per item.
/// </summary>
public readonly record struct ValueLookup(string Code, double Value, bool FloorZero, ValueSource Source, string? Family, int Members,
    int? PerLitre = null, double? PerLitreValue = null)
{
    public double Effective => Source == ValueSource.Missing || FloorZero ? 0 : Value;

    /// <summary>Gears per litre for a liquid, gears per item otherwise.</summary>
    public double Display => PerLitreValue ?? Value;
}

/// <summary>
/// The item base value table (#449), as <c>tools/item-values</c> writes it to
/// <c>assets/seraphhorizons/config/item-values.json</c>: <c>values</c> maps a full code to gears per
/// item, except for the codes in <c>perLitre</c> (code -> items per litre, the liquids), whose value
/// is gears per litre; <c>floorZero</c> lists the codes worth under a gear per full stack, and <c>switches</c>
/// (optional) maps a code whose value exists only with some <c>ModConfig/seraphhorizons.json</c>
/// switches on to their names (its cheapest route takes a recipe or an item those switches add,
/// README "Switch ownership").
///
/// A code missing from the table falls back to its variant family: the longest prefix of the code
/// that ends at a '-' and that table codes share (<c>game:plank-oak</c> falls back to the
/// average of <c>game:plank-*</c>; <c>game:axe-felling-silver</c> to the felling axes before all
/// axes). A family must keep at least the first segment of the path, so <c>game:plank-oak</c>
/// never averages over everything in <c>game:</c>. A code with <c>*</c> averages every table code
/// it matches.
///
/// Every answer is gears per item: a per-litre value is divided by its items per litre on load, so
/// <see cref="ValueOf"/> and <see cref="ValueLookup.Value"/> price a stack of portions like any
/// stack. A family or wildcard averages its members' per-item values; when every member is priced
/// per litre at the same items per litre, the answer is per litre too (the average of their
/// per-litre values, rounded to three decimals, and per item that over the items per litre). Any
/// other family is per item, rounded to three decimals as before.
///
/// A table never changes once built (a new asset load builds a new one), so each table caches its
/// wildcard answers: <c>/sh trade values suspicious</c> looks up the same few hundred patterns for
/// some 80,000 grid recipes, and each uncached one scans every table code.
/// </summary>
public sealed class ItemValues
{
    private readonly Dictionary<string, double> _values;
    private readonly HashSet<string> _floorZero;
    private readonly Dictionary<string, string[]> _switches;
    // Code -> items per litre, for the codes the table prices per litre; _values holds them per item.
    private readonly Dictionary<string, int> _perLitre;
    // Family prefix ("game:plank-") -> its members' aggregate.
    private readonly Dictionary<string, Aggregate> _families = new(StringComparer.Ordinal);
    // Normalised wildcard pattern -> its answer. Sound only because the table is immutable.
    private readonly ConcurrentDictionary<string, ValueLookup> _wildcards = new(StringComparer.Ordinal);

    public static readonly ItemValues Empty = new(new Dictionary<string, double>(), []);

    /// <summary>
    /// <paramref name="values"/> as the table has them: gears per item, or gears per litre for the
    /// codes in <paramref name="perLitre"/> (code -> items per litre, at least 1).
    /// </summary>
    public ItemValues(IReadOnlyDictionary<string, double> values, IEnumerable<string> floorZero,
        IReadOnlyDictionary<string, string[]>? switches = null, IReadOnlyDictionary<string, int>? perLitre = null)
    {
        _perLitre = new Dictionary<string, int>(StringComparer.Ordinal);
        if (perLitre != null)
            foreach (var (code, n) in perLitre)
            {
                if (n < 1) throw new ArgumentException($"perLitre[{code}] is {n}; items per litre must be at least 1");
                if (values.ContainsKey(code)) _perLitre[code] = n;
            }
        _values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (code, value) in values)
            _values[code] = _perLitre.TryGetValue(code, out int n) ? value / n : value;
        _floorZero = new HashSet<string>(floorZero, StringComparer.Ordinal);
        _switches = switches == null
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            : new Dictionary<string, string[]>(switches, StringComparer.Ordinal);
        foreach (var code in _values.Keys)
            foreach (var prefix in FamilyPrefixes(code))
            {
                _families.TryGetValue(prefix, out var f);
                _families[prefix] = f.Add(this, code);
            }
    }

    /// <summary>A family's or a wildcard's members: per-item sum, count, zeroed members, and the
    /// items per litre they share (0 before the first member, -1 once they differ or one is per item).</summary>
    private readonly record struct Aggregate(double Sum, int Count, int Zero, int PerLitre)
    {
        public Aggregate Add(ItemValues t, string code)
        {
            int n = t._perLitre.TryGetValue(code, out int p) ? p : -1;
            return new Aggregate(Sum + t._values[code], Count + 1, Zero + (t._floorZero.Contains(code) ? 1 : 0),
                Count == 0 ? n : PerLitre == n ? n : -1);
        }

        public ValueLookup Answer(string code, string family)
        {
            if (PerLitre > 0)
            {
                double litre = Math.Round(Sum / Count * PerLitre, 3);
                return new ValueLookup(code, litre / PerLitre, Zero == Count, ValueSource.Family, family, Count, PerLitre, litre);
            }
            return new ValueLookup(code, Math.Round(Sum / Count, 3), Zero == Count, ValueSource.Family, family, Count);
        }
    }

    public int Count => _values.Count;

    public IEnumerable<string> Codes => _values.Keys;

    /// <summary>Items per litre when <paramref name="code"/>'s value (direct, family or wildcard) is
    /// priced per litre; null when it is priced per item or has no value.</summary>
    public int? PerLitre(string code) => Lookup(code).PerLitre;

    /// <summary>The value in the unit it is priced in (gears per litre for a liquid, per item
    /// otherwise) and the items per litre (null for per item). Not what trading uses: see
    /// <see cref="ValueOf"/>.</summary>
    public (double Value, int? PerLitre) DisplayValue(string code)
    {
        var l = Lookup(code);
        return (l.Display, l.PerLitre);
    }

    /// <summary>The switches a code's own table value depends on (the table's <c>switches</c>);
    /// empty when none, or when the code is not in the table.</summary>
    public IReadOnlyList<string> SwitchesOf(string code) =>
        _switches.TryGetValue(NormalizeCode(code), out var s) ? s : [];

    /// <summary>
    /// What an item's handbook page shows: its value (direct or family fallback) in the unit it is
    /// priced in (<see cref="ValueLookup.Display"/>: gears per litre for a liquid), or null for "No
    /// trade value" when the code has none or when any switch its value depends on is off
    /// (<paramref name="isOff"/>, by switch name).
    /// </summary>
    public double? Shown(string code, Func<string, bool> isOff) => ShownLookup(code, isOff)?.Display;

    /// <summary><see cref="Shown"/> with the whole answer, for the unit.</summary>
    public ValueLookup? ShownLookup(string code, Func<string, bool> isOff)
    {
        var l = Lookup(code);
        if (l.Source == ValueSource.Missing)
            return null;
        return SwitchesOf(code).Any(isOff) ? null : l;
    }

    private static string NormalizeCode(string code)
    {
        code = code.ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }

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
        code = NormalizeCode(code);
        if (code.Contains('*')) return _wildcards.GetOrAdd(code, WildcardUncached);
        if (_values.TryGetValue(code, out var v))
            return _perLitre.TryGetValue(code, out int n)
                ? new ValueLookup(code, v, _floorZero.Contains(code), ValueSource.Direct, null, 1, n, Math.Round(v * n, 6))
                : new ValueLookup(code, v, _floorZero.Contains(code), ValueSource.Direct, null, 1);
        foreach (var prefix in FamilyPrefixes(code))
            if (_families.TryGetValue(prefix, out var f))
                return f.Answer(code, prefix + "*");
        return new ValueLookup(code, 0, false, ValueSource.Missing, null, 0);
    }

    /// <summary>A wildcard's answer, computed afresh, without the cache (internal for its tests).</summary>
    internal ValueLookup WildcardUncached(string pattern)
    {
        var rx = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
        // The text before the first '*' and after the last must match literally: a cheap filter
        // before the regex, which still decides.
        string head = pattern[..pattern.IndexOf('*')], tail = pattern[(pattern.LastIndexOf('*') + 1)..];
        var a = default(Aggregate);
        foreach (var code in _values.Keys)
        {
            if (!code.StartsWith(head, StringComparison.Ordinal) || !code.EndsWith(tail, StringComparison.Ordinal) || !rx.IsMatch(code)) continue;
            a = a.Add(this, code);
        }
        return a.Count == 0
            ? new ValueLookup(pattern, 0, false, ValueSource.Missing, null, 0)
            : a.Answer(pattern, pattern);
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
        var switches = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (root.TryGetProperty("switches", out var sw))
            foreach (var p in sw.EnumerateObject())
                switches[p.Name] = p.Value.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToArray();
        var perLitre = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("perLitre", out var pl))
            foreach (var p in pl.EnumerateObject())
                // The game's itemsPerLitre is an int; a tool that writes 100.0 means the same.
                perLitre[p.Name] = p.Value.TryGetInt32(out int n) ? n : (int)Math.Round(p.Value.GetDouble());
        return new ItemValues(values, zero, switches, perLitre);
    }
}
