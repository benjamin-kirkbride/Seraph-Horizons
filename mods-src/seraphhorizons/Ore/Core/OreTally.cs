using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>One row of <see cref="OreTally"/>: a metal at a grade (<c>-</c> for ungraded ores).</summary>
public sealed record OreTallyRow(string Metal, string Grade, long Blocks, double Units)
{
    public double Ingots => DepositSizing.Ingots(Units);
}

/// <summary>
/// Ore blocks counted by metal and grade (<c>/sh ore count</c>, #458), with their metal units, so
/// they read in blocks and in ingots as the deposit registry measures them (<see cref="DepositSizing"/>).
/// </summary>
public sealed class OreTally
{
    public static readonly IReadOnlyList<string> Grades = ["poor", "medium", "rich", "bountiful"];

    private readonly Dictionary<(string Metal, string Grade), (long Blocks, double Units)> _rows = new();

    /// <summary>The metal and grade of an ore block's path, or null for anything that isn't a
    /// managed ore: <c>ore-rich-nativecopper-granite</c> is (copper, rich), <c>ore-lignite-shale</c>
    /// (coal, -).</summary>
    public static (string Metal, string Grade)? Classify(string path)
    {
        if (OreMetals.MetalOf(OreMetals.OreOfBlockPath(path)) is not { } metal) return null;
        var parts = path.Split('-');
        string grade = parts.Length >= 4 && Grades.Contains(parts[1]) ? parts[1] : "-";
        return (metal, grade);
    }

    public void Add(string metal, string grade, long blocks, double units)
    {
        _rows.TryGetValue((metal, grade), out var r);
        _rows[(metal, grade)] = (r.Blocks + blocks, r.Units + units);
    }

    public long Blocks => _rows.Values.Sum(r => r.Blocks);

    /// <summary>Metals in name order, grades poor to bountiful.</summary>
    public IReadOnlyList<OreTallyRow> Rows() => _rows
        .OrderBy(kv => kv.Key.Metal, StringComparer.Ordinal)
        .ThenBy(kv => kv.Key.Grade == "-" ? -1 : Grades.ToList().IndexOf(kv.Key.Grade))
        .Select(kv => new OreTallyRow(kv.Key.Metal, kv.Key.Grade, kv.Value.Blocks, kv.Value.Units))
        .ToList();

    /// <summary>Per metal, every grade summed.</summary>
    public IReadOnlyList<OreTallyRow> ByMetal() => _rows
        .GroupBy(kv => kv.Key.Metal)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => new OreTallyRow(g.Key, "*", g.Sum(kv => kv.Value.Blocks), g.Sum(kv => kv.Value.Units)))
        .ToList();
}

/// <summary>
/// The survey tool's counting (<c>tools/ore-survey/OreSurvey.cs</c>), for <c>/sh ore survey</c> on a
/// live server: every block id's count and height sum, and each recorded block (ores, loose ores,
/// rich gravel) by 8-block cell with the shallowest depth below the worldgen surface seen there.
/// It writes the tool's two files, so <c>ore_survey.py summary</c> reads a live survey like a
/// tool run: <c>&lt;file&gt;</c> (JSON) and <c>&lt;file&gt;.cells.csv</c>.
/// </summary>
public sealed class SurveyCounter
{
    private readonly long[] _counts;
    private readonly long[] _ySum;
    private readonly bool[] _recorded;
    private readonly Dictionary<long, (int N, int MinDepth)> _cells = new();

    /// <param name="recorded">Per block id, whether its cells are recorded.</param>
    /// <param name="originChunkX">The survey's first chunk column; cells count from it, as the tool's.</param>
    public SurveyCounter(bool[] recorded, int originChunkX, int originChunkZ)
    {
        _recorded = recorded;
        _counts = new long[recorded.Length];
        _ySum = new long[recorded.Length];
        OriginX = originChunkX;
        OriginZ = originChunkZ;
    }

    public int OriginX { get; }
    public int OriginZ { get; }

    /// <summary>The survey tool's rule for recorded blocks. Interesting Ore Gen's saltpeter is not a
    /// BlockOre and has no ore- prefix; loose ores are the surface signs.</summary>
    public static bool Recorded(string path, string? blockClass) =>
        path.StartsWith("ore-", StringComparison.Ordinal) || blockClass == "BlockOre" || path.Contains("saltpeterore")
        || path.StartsWith("looseores", StringComparison.Ordinal) || path.StartsWith("richgravel-", StringComparison.Ordinal);

    /// <summary>Counts one 32³ chunk; <paramref name="blockAt"/> gives the id at a chunk index
    /// (<c>(y * 32 + z) * 32 + x</c>), <paramref name="heightMap"/> the column's worldgen terrain
    /// heights (z * 32 + x).</summary>
    public void Count(int chunkX, int chunkY, int chunkZ, Func<int, int> blockAt, ushort[] heightMap)
    {
        for (int i = 0; i < 32 * 32 * 32; i++)
        {
            int id = blockAt(i);
            if (id <= 0 || id >= _counts.Length) continue;
            int y = chunkY * 32 + (i >> 10);
            _counts[id]++;
            _ySum[id] += y;
            if (!_recorded[id]) continue;
            int lx = i & 31, lz = (i >> 5) & 31;
            // id: bits 40+, cell x: 26..39, cell z: 12..25, cell y: 0..11 (the tool's packing)
            long key = ((long)id << 40) | ((long)(((chunkX - OriginX) * 32 + lx) >> 3) << 26)
                       | ((long)(((chunkZ - OriginZ) * 32 + lz) >> 3) << 12) | (long)(y >> 3);
            int depth = heightMap[lz * 32 + lx] - y;
            _cells[key] = _cells.TryGetValue(key, out var c) ? (c.N + 1, Math.Min(c.MinDepth, depth)) : (1, depth);
        }
    }

    public long BlocksCounted => _counts.Sum();

    public int Cells => _cells.Count;

    /// <summary>The tool's JSON document's fields, ids named by <paramref name="codeOf"/>.</summary>
    public SortedDictionary<string, object> Blocks(Func<int, (string Code, string? Class)> codeOf)
    {
        var blocks = new SortedDictionary<string, object>(StringComparer.Ordinal);
        for (int id = 0; id < _counts.Length; id++)
        {
            if (_counts[id] == 0) continue;
            var (code, cls) = codeOf(id);
            blocks[code] = new { count = _counts[id], meanY = Math.Round((double)_ySum[id] / _counts[id], 1), cls };
        }
        return blocks;
    }

    /// <summary>The tool's cells file: code, cell x, cell z, cell y, blocks, shallowest depth.</summary>
    public string CellsCsv(Func<int, string> codeOf)
    {
        var sb = new StringBuilder();
        foreach (var (key, c) in _cells)
            sb.Append(codeOf((int)(key >> 40))).Append(',')
              .Append(((key >> 26) & 0x3fff).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(((key >> 12) & 0x3fff).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append((key & 0xfff).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(c.N.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(c.MinDepth.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }
}
