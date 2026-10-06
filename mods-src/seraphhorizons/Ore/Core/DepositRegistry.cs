using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// A deposit's identity: a metal's (or <see cref="PlacerCells.Kind"/>'s, for a gravel field) cell.
/// One deposit per (kind, cell) is the whole point of the cells, so the pair names it for good,
/// whichever fallback spot it ended up at. Written <c>copper:12,-3</c>.
/// </summary>
public readonly record struct DepositKey(string Kind, CellPos Cell)
{
    public bool IsGravel => Kind == PlacerCells.Kind;

    public string Id => string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Cell.X},{Cell.Z}");

    public override string ToString() => Id;

    public static bool TryParse(string? text, out DepositKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        int colon = text.IndexOf(':');
        if (colon <= 0) return false;
        var xz = text[(colon + 1)..].Split(',');
        if (xz.Length != 2
            || !int.TryParse(xz[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(xz[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            return false;
        key = new DepositKey(text[..colon].Trim().ToLowerInvariant(), new CellPos(x, z));
        return true;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<DepositState>))]
public enum DepositState { Unsold, Sold, SoldOut }

[JsonConverter(typeof(JsonStringEnumConverter<SizeTier>))]
public enum SizeTier { Small = 1, Medium = 2, Large = 3 }

/// <summary>What the world knows of one deposit beyond the seed: whether a map to it was sold
/// (to whom, when, in game days) or it is worked out, and its last measurement (remaining ingots,
/// its tier then, and the centre of the ore counted).</summary>
public sealed record DepositRecord
{
    public DepositState State { get; init; }
    public string? SoldToUid { get; init; }
    public string? SoldToName { get; init; }
    public double? SoldAtDays { get; init; }
    public double? Ingots { get; init; }
    public SizeTier? Tier { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Z { get; init; }
    public double? MeasuredAtDays { get; init; }

    public static readonly DepositRecord Fresh = new();
}

/// <summary>
/// The world-wide record of deposits (#443), kept in the savegame like the game's consumed
/// structure locations: per (metal, cell), and per gravel cell, unsold → sold (by whom, when) →
/// and, when a check finds it worked out, sold out. A deposit is sold at most once: every tier
/// of map points at the same one, and a second sale is refused (<see cref="MarkSold"/>). Sold out
/// is final but for an admin reset. Thread-safe.
/// </summary>
public sealed class DepositRegistry
{
    private readonly Dictionary<DepositKey, DepositRecord> _records = new();
    private readonly object _lock = new();

    public DepositRecord Get(DepositKey key)
    {
        lock (_lock) return _records.TryGetValue(key, out var r) ? r : DepositRecord.Fresh;
    }

    public IReadOnlyList<(DepositKey Key, DepositRecord Record)> All()
    {
        lock (_lock) return _records.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Marks an unsold deposit sold; false (and no change) if it is sold or sold out.</summary>
    public bool MarkSold(DepositKey key, string? uid, string? name, double days)
    {
        lock (_lock)
        {
            var r = Get(key);
            if (r.State != DepositState.Unsold) return false;
            _records[key] = r with { State = DepositState.Sold, SoldToUid = uid, SoldToName = name, SoldAtDays = days };
            return true;
        }
    }

    public void MarkSoldOut(DepositKey key)
    {
        lock (_lock) _records[key] = Get(key) with { State = DepositState.SoldOut };
    }

    /// <summary>Back to unsold, keeping the last measurement (admin).</summary>
    public void Reset(DepositKey key)
    {
        lock (_lock)
        {
            var r = Get(key);
            _records[key] = r with { State = DepositState.Unsold, SoldToUid = null, SoldToName = null, SoldAtDays = null };
        }
    }

    /// <summary>Records a measurement; a worked-out one (<see cref="DepositSizing.IsWorkedOut"/>,
    /// decided by the caller) marks it sold out.</summary>
    public void RecordMeasure(DepositKey key, double ingots, SizeTier tier, int x, int y, int z, double days, bool workedOut)
    {
        lock (_lock)
        {
            var r = Get(key) with { Ingots = ingots, Tier = tier, X = x, Y = y, Z = z, MeasuredAtDays = days };
            if (workedOut) r = r with { State = DepositState.SoldOut };
            _records[key] = r;
        }
    }

    private sealed record Row(string Id, DepositRecord Record);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Serialize()
    {
        lock (_lock)
            return JsonSerializer.Serialize(
                _records.OrderBy(kv => kv.Key.Id, StringComparer.Ordinal).Select(kv => new Row(kv.Key.Id, kv.Value)).ToList(), Json);
    }

    public static DepositRegistry Parse(string? text)
    {
        var registry = new DepositRegistry();
        if (string.IsNullOrWhiteSpace(text)) return registry;
        foreach (var row in JsonSerializer.Deserialize<List<Row>>(text, Json) ?? [])
        {
            if (!DepositKey.TryParse(row.Id, out var key)) throw new FormatException($"bad deposit id '{row.Id}'");
            registry._records[key] = row.Record;
        }
        return registry;
    }
}

/// <summary>A metal's deposit sizes in ingots (epic #435: copper 150 / 400 / 1,000, ...).</summary>
public readonly record struct SizeTargets(double Small, double Typical, double Large);

/// <summary>
/// Turning counted ore into ingots, and ingots into a size tier, the way the survey does
/// (tools/ore-survey): an ore block drops about 1.25 ore chunks (and a little crystallised ore),
/// each worth its grade's <c>metalUnits</c>; 5 units make a nugget, 20 nuggets an ingot.
/// </summary>
public static class DepositSizing
{
    public const double OreChunksPerBlock = 1.25;
    public const double UnitsPerNugget = 5;
    public const double NuggetsPerIngot = 20;

    /// <summary>Below this share of a metal's small size a deposit counts as worked out.</summary>
    public const double WorkedOutShare = 0.1;

    public static double Ingots(double metalUnits) => metalUnits / UnitsPerNugget / NuggetsPerIngot;

    /// <summary>Small, medium or large: the bottom, middle or top third of the metal's range from
    /// small to large.</summary>
    public static SizeTier Classify(double ingots, SizeTargets targets)
    {
        double third = (targets.Large - targets.Small) / 3;
        if (ingots < targets.Small + third) return SizeTier.Small;
        if (ingots < targets.Small + 2 * third) return SizeTier.Medium;
        return SizeTier.Large;
    }

    public static bool IsWorkedOut(double ingots, SizeTargets targets) => ingots < WorkedOutShare * targets.Small;
}

/// <summary>
/// How far an ore map's marker may be from the deposit (#444): tier 1 within 400 m, tier 2
/// within 150 m, tier 3 on it. The offset is a fixed point of the deposit (from the seed and its
/// key), scaled by the tier's reach: every copy of a tier marks the same place, and a better tier's
/// marker lies between the worse one's and the deposit.
/// </summary>
public static class MapPrecision
{
    public const int Rough = 1;
    public const int Fair = 2;
    public const int Exact = 3;

    public static bool IsValid(int precision) => precision is >= Rough and <= Exact;

    public static int ReachOf(int precision) => precision switch
    {
        Rough => 400,
        Fair => 150,
        _ => 0,
    };

    private static readonly ulong Salt = OreCells.Fnv1a("oremap:offset");

    public static (int Dx, int Dz) Offset(long seed, DepositKey key, int precision)
    {
        int reach = ReachOf(precision);
        if (reach == 0) return (0, 0);
        ulong h = OreCells.Hash(seed, Salt ^ OreCells.Fnv1a(key.Kind), key.Cell.X, key.Cell.Z, 0);
        // Uniform over the disc: angle uniform, radius by the square root.
        double angle = (h & 0xffffffffUL) / 4294967296.0 * 2 * Math.PI;
        double r = Math.Sqrt((h >> 32) / 4294967296.0);
        return ((int)Math.Round(Math.Cos(angle) * r * reach), (int)Math.Round(Math.Sin(angle) * r * reach));
    }
}

/// <summary>Which cells of a grid a circle reaches.</summary>
public static class CellSearch
{
    public static IEnumerable<CellPos> Within(int cellSize, int x, int z, int radius)
    {
        int x0 = OreCells.FloorDiv(x - radius, cellSize), x1 = OreCells.FloorDiv(x + radius, cellSize);
        int z0 = OreCells.FloorDiv(z - radius, cellSize), z1 = OreCells.FloorDiv(z + radius, cellSize);
        for (int cx = x0; cx <= x1; cx++)
            for (int cz = z0; cz <= z1; cz++)
            {
                // The cell's nearest point to (x, z).
                long nx = Math.Clamp(x, (long)cx * cellSize, (long)cx * cellSize + cellSize - 1);
                long nz = Math.Clamp(z, (long)cz * cellSize, (long)cz * cellSize + cellSize - 1);
                if ((nx - x) * (nx - x) + (nz - z) * (nz - z) <= (long)radius * radius)
                    yield return new CellPos(cx, cz);
            }
    }
}
