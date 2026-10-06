namespace SeraphHorizons.Mod.Trading.Core;

public enum CampStatus { Pending, Placed, Failed }

/// <summary>One cell's camp, as saved: where its placement is (the spot being waited for, the spots
/// whose chunks were generated before their turn), and once placed, where it is and what it is.</summary>
public sealed class CampRecord
{
    public int CellX { get; set; }
    public int CellZ { get; set; }
    public CampStatus Status { get; set; }
    /// <summary>The spot (index into <see cref="TraderGrid.Spots"/>) whose chunk decides next.</summary>
    public int Attempt { get; set; }
    /// <summary>Spots whose chunk was generated while an earlier one was still waiting: too late to
    /// use, so skipped when their turn comes.</summary>
    public List<int> Passed { get; set; } = [];
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Region { get; set; } = "";
    /// <summary>The worldgen structure and schematic placed (<c>schematic/structurecode</c>, as the
    /// game names generated structures).</summary>
    public string Structure { get; set; } = "";

    public CellKey Cell => new(CellX, CellZ);
}

/// <summary>What the worldgen side should do in a chunk column for one cell's spot.</summary>
public enum AttemptVerdict
{
    /// <summary>Nothing: the camp is placed or given up, or this spot's turn is over.</summary>
    Skip,
    /// <summary>Try to place the camp at this spot now, then report with <see cref="CampRegistry.Placed"/>
    /// or <see cref="CampRegistry.Missed"/>.</summary>
    Try,
}

/// <summary>
/// Every cell's camp placement state (#447), saved with the world. Chunks generate in whatever order
/// players explore, so a cell's spots are decided spot by spot as their chunks come: a spot is tried
/// only when its turn has come (every earlier spot missed); a spot whose chunk generates earlier is
/// marked passed, as its chunk can't take a camp any more. A cell whose spots all miss has no camp.
/// The outcome thus follows the seed and the order chunks were generated in; <c>/sh trade camps</c>
/// shows both what is placed and the spot a pending cell waits for. Thread-safe: worldgen threads
/// call in, commands read.
/// </summary>
public sealed class CampRegistry
{
    private readonly Dictionary<CellKey, CampRecord> _cells = new();
    private readonly object _lock = new();

    public CampRegistry() { }

    public CampRegistry(IEnumerable<CampRecord> records)
    {
        foreach (var r in records) _cells[r.Cell] = r;
    }

    public List<CampRecord> Snapshot()
    {
        lock (_lock) return _cells.Values.Select(Clone).ToList();
    }

    public CampRecord? Get(CellKey cell)
    {
        lock (_lock) return _cells.TryGetValue(cell, out var r) ? Clone(r) : null;
    }

    /// <summary>A spot's chunk is being generated: whether to try it.</summary>
    public AttemptVerdict OnChunk(CellKey cell, int attempt, int spotCount)
    {
        lock (_lock)
        {
            var r = GetOrAdd(cell);
            if (r.Status != CampStatus.Pending || attempt < r.Attempt) return AttemptVerdict.Skip;
            if (attempt > r.Attempt)
            {
                if (!r.Passed.Contains(attempt)) r.Passed.Add(attempt);
                return AttemptVerdict.Skip;
            }
            return AttemptVerdict.Try;
        }
    }

    public void Placed(CellKey cell, int attempt, string type, int x, int y, int z, string region, string structure)
    {
        lock (_lock)
        {
            var r = GetOrAdd(cell);
            if (r.Status != CampStatus.Pending || r.Attempt != attempt) return;
            r.Status = CampStatus.Placed;
            r.Type = type;
            (r.X, r.Y, r.Z) = (x, y, z);
            r.Region = region;
            r.Structure = structure;
            r.Passed.Clear();
        }
    }

    /// <summary>The spot did not take a camp: on to the next spot not passed, or give up.</summary>
    public void Missed(CellKey cell, int attempt, int spotCount)
    {
        lock (_lock)
        {
            var r = GetOrAdd(cell);
            if (r.Status != CampStatus.Pending || r.Attempt != attempt) return;
            int next = attempt + 1;
            while (next < spotCount && r.Passed.Contains(next)) next++;
            if (next >= spotCount)
            {
                r.Status = CampStatus.Failed;
                r.Passed.Clear();
            }
            r.Attempt = next;
        }
    }

    private CampRecord GetOrAdd(CellKey cell)
    {
        if (!_cells.TryGetValue(cell, out var r))
            _cells[cell] = r = new CampRecord { CellX = cell.X, CellZ = cell.Z };
        return r;
    }

    private static CampRecord Clone(CampRecord r) => new()
    {
        CellX = r.CellX, CellZ = r.CellZ, Status = r.Status, Attempt = r.Attempt, Passed = [.. r.Passed],
        Type = r.Type, X = r.X, Y = r.Y, Z = r.Z, Region = r.Region, Structure = r.Structure,
    };
}
