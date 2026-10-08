using System.Text.Json;

namespace SeraphHorizons.Mod.Trading.Economy.Core;

/// <summary>
/// How much a trader type cares for goods it does not list (#450), read from
/// <c>assets/seraphhorizons/config/trading/trader-relations.json</c>. An item's fit at a trader:
/// <see cref="Listed"/> if the trader's type buys it (any region of its list), else the best
/// relation between that type and a type that does buy it (<see cref="Related"/> for a listed pair,
/// or a pair's own weight), else <see cref="Unrelated"/>. A type in <see cref="ToAll"/> relates to
/// every other type at its weight, both ways (the curio dealer, who takes anything another type buys at 0.6).
/// </summary>
public sealed class TraderRelations
{
    public double Listed { get; init; } = 1.0;
    public double Related { get; init; } = 0.75;
    public double Unrelated { get; init; } = 0.5;

    /// <summary>Code prefixes that are refused outright, wherever listed or not: maps and leads.</summary>
    public IReadOnlyList<string> Refused { get; init; } = [];

    private readonly Dictionary<(string, string), double> _pairs = new();
    private readonly Dictionary<string, double> _toAll = new();

    public IReadOnlyDictionary<string, double> ToAll => _toAll;

    public void Relate(string a, string b, double weight)
    {
        _pairs[(a, b)] = weight;
        _pairs[(b, a)] = weight;
    }

    public void RelateToAll(string type, double weight) => _toAll[type] = weight;

    /// <summary>The weight between two types; 0 when unrelated (and for a type and itself, whose
    /// fit is <see cref="Listed"/>).</summary>
    public double Relation(string a, string b)
    {
        if (a == b) return 0;
        double w = _pairs.GetValueOrDefault((a, b));
        if (_toAll.TryGetValue(a, out double x)) w = Math.Max(w, x);
        if (_toAll.TryGetValue(b, out double y)) w = Math.Max(w, y);
        return w;
    }

    /// <summary>The fit factor of an item at a trader of <paramref name="type"/>, given every type
    /// whose list buys the item.</summary>
    public double Fit(string type, IReadOnlyCollection<string> buyers)
    {
        if (buyers.Contains(type)) return Listed;
        double best = Unrelated;
        foreach (string other in buyers)
            best = Math.Max(best, Relation(type, other));
        return best;
    }

    /// <summary>Whether a trader of <paramref name="type"/> pays for an item off its shelf's list
    /// from its main wallet: its own type buys the item (in another region's list), or a type it is
    /// related to does (a pair, with or without its own weight; not a <see cref="ToAll"/> link).
    /// Everything else it takes off its list is paid from its side budget.</summary>
    public bool PaysFromMain(string type, IReadOnlyCollection<string> buyers) =>
        buyers.Contains(type) || buyers.Any(other => _pairs.ContainsKey((type, other)));

    public bool IsRefused(string code) => Refused.Any(p => code.StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// Reads the file: <c>{ "listed", "related", "unrelated", "pairs": [["smith", "mechanic"], …
    /// or ["a", "b", 0.6]], "toAll": { "curiodealer": 0.6 }, "refused": ["seraphhorizons:oremap", …] }</c>.
    /// Comments and trailing commas are allowed, as in the game's own JSON.
    /// </summary>
    public static TraderRelations Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var root = doc.RootElement;
        double Num(string name, double fallback) => root.TryGetProperty(name, out var p) ? p.GetDouble() : fallback;
        var refused = new List<string>();
        if (root.TryGetProperty("refused", out var r))
            foreach (var e in r.EnumerateArray())
                if (e.GetString() is { Length: > 0 } s) refused.Add(s);
        var rel = new TraderRelations
        {
            Listed = Num("listed", 1.0),
            Related = Num("related", 0.75),
            Unrelated = Num("unrelated", 0.5),
            Refused = refused,
        };
        if (root.TryGetProperty("pairs", out var pairs))
            foreach (var pair in pairs.EnumerateArray())
            {
                var items = pair.EnumerateArray().ToList();
                if (items.Count < 2) throw new FormatException("a pair needs two types");
                rel.Relate(items[0].GetString()!, items[1].GetString()!, items.Count > 2 ? items[2].GetDouble() : rel.Related);
            }
        if (root.TryGetProperty("toAll", out var all))
            foreach (var p in all.EnumerateObject())
                rel.RelateToAll(p.Name, p.Value.GetDouble());
        return rel;
    }

    /// <summary>What is wrong with a parsed table: types that are not the eleven, weights out of
    /// order (unrelated ≤ a relation ≤ listed).</summary>
    public List<string> Problems(IReadOnlyCollection<string> types)
    {
        var problems = new List<string>();
        foreach (var ((a, b), w) in _pairs)
        {
            if (!types.Contains(a)) problems.Add($"unknown type '{a}'");
            if (w < Unrelated || w > Listed) problems.Add($"{a}-{b}: weight {w} outside [{Unrelated}, {Listed}]");
        }
        foreach (var (t, w) in _toAll)
        {
            if (!types.Contains(t)) problems.Add($"unknown type '{t}'");
            if (w < Unrelated || w > Listed) problems.Add($"{t}: weight {w} outside [{Unrelated}, {Listed}]");
        }
        if (!(Unrelated <= Related && Related <= Listed)) problems.Add("needs unrelated <= related <= listed");
        return problems.Distinct().ToList();
    }
}

/// <summary>Which trader types buy each item, by full code (<c>game:ingot-iron</c>), from every
/// region of every list; attributes are ignored (the fit is per item).</summary>
public sealed class BuyerIndex
{
    private readonly Dictionary<string, HashSet<string>> _buyers = new(StringComparer.Ordinal);
    private static readonly IReadOnlyCollection<string> None = Array.Empty<string>();

    public void Add(string code, string type)
    {
        code = FullCode(code);
        if (!_buyers.TryGetValue(code, out var set)) _buyers[code] = set = [];
        set.Add(type);
    }

    public IReadOnlyCollection<string> BuyersOf(string code) => _buyers.TryGetValue(FullCode(code), out var s) ? s : None;

    public int Count => _buyers.Count;

    public static string FullCode(string code) => code.Contains(':') ? code : "game:" + code;
}
