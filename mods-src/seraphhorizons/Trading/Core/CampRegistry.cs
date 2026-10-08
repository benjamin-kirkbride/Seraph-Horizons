namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>A cell's camp state. <see cref="Open"/> is last so the numbers saved before it came
/// (#599) keep their meaning.</summary>
public enum CampStatus
{
    /// <summary>Some spot's chunk is not generated yet: that spot will try when it is.</summary>
    Pending,
    Placed,
    /// <summary>Every spot and every second chance missed: no camp, for good.</summary>
    Failed,
    /// <summary>Every spot was tried and missed; chunks generated in the cell from now on may still
    /// take the camp (<see cref="TraderGrid.SecondChanceChunk"/>), up to
    /// <see cref="TraderGrid.SecondChances"/> of them.</summary>
    Open,
}

/// <summary>One cell's camp, as saved: which spots have been tried, how many second chances were
/// spent, and once placed, where it is and what it is.</summary>
public sealed class CampRecord
{
    public int CellX { get; set; }
    public int CellZ { get; set; }
    public CampStatus Status { get; set; }
    /// <summary>Pending: the first spot (index into <see cref="TraderGrid.Spots"/>) not tried yet,
    /// the one to generate to decide the cell. Open and Failed: the spot count. Placed: the spot that
    /// took the camp, or -1 for a second chance.</summary>
    public int Attempt { get; set; }
    /// <summary>The spots tried (their chunk was generated while the cell had no camp). Null only in
    /// a record saved before #599, which the registry migrates on load.</summary>
    public List<int>? Tried { get; set; }
    /// <summary>Saved before #599 (spots whose chunk generated before their turn); read on load to
    /// migrate, always empty after.</summary>
    public List<int> Passed { get; set; } = [];
    /// <summary>Second chances spent (chunks tried after every spot missed).</summary>
    public int Retries { get; set; }
    public string Type { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Region { get; set; } = "";
    /// <summary>The worldgen structure and schematic placed (<c>schematic/structurecode</c>, as the
    /// game names generated structures).</summary>
    public string Structure { get; set; } = "";
    /// <summary>How uneven the ground the camp was seated on was: highest minus lowest of the
    /// game's five samples (0 for ground the game itself would have taken).</summary>
    public int Slope { get; set; }

    public CellKey Cell => new(CellX, CellZ);
}

/// <summary>What the worldgen side should do in a chunk column for one cell.</summary>
public enum AttemptVerdict
{
    /// <summary>Nothing: the camp is placed or given up, or this spot was tried already.</summary>
    Skip,
    /// <summary>Try to place the camp here now, then report with <see cref="CampRegistry.Placed"/>
    /// or <see cref="CampRegistry.Missed"/>.</summary>
    Try,
}

/// <summary>
/// Every cell's camp placement state (#447, #599), saved with the world. Chunks generate in whatever
/// order players explore, so a cell is decided as its spots' chunks come: any spot whose chunk
/// generates while the cell has no camp tries at once, and the first that places wins. A spot is
/// tried once (its chunk generates once). When every spot has missed, the cell is
/// <see cref="CampStatus.Open"/>: chunks of the cell generated from then on that
/// <see cref="TraderGrid.SecondChanceChunk"/> picks may still try, up to
/// <see cref="TraderGrid.SecondChances"/> of them, and after the last of those misses the cell has
/// no camp (<see cref="CampStatus.Failed"/>). The outcome follows the seed and the order chunks were
/// generated in; <c>/sh trade camps</c> shows it. Thread-safe: worldgen threads call in, commands
/// read.
///
/// Saves from before #599 tried spots strictly in order and burnt (<c>Passed</c>) a spot whose chunk
/// generated before its turn. On load such a record is migrated: the spots before its
/// <c>Attempt</c> and the passed ones count as tried (their chunks are generated, so they cannot try
/// again), the rest stay open to try when their chunks generate; a cell that had failed is
/// <see cref="CampStatus.Open"/>, so its chunks not generated yet get second chances.
/// </summary>
public sealed class CampRegistry
{
    private readonly Dictionary<CellKey, CampRecord> _cells = new();
    private readonly object _lock = new();

    public CampRegistry() { }

    public CampRegistry(IEnumerable<CampRecord> records)
    {
        foreach (var r in records)
        {
            Migrate(r);
            _cells[r.Cell] = r;
        }
    }

    /// <summary>A record saved before #599 (no <see cref="CampRecord.Tried"/>) in today's terms.</summary>
    public static void Migrate(CampRecord r)
    {
        if (r.Tried is not null)
        {
            r.Passed.Clear();
            return;
        }
        var tried = new SortedSet<int>(r.Passed);
        switch (r.Status)
        {
            case CampStatus.Pending:
                // Every spot before the one it waited for was tried and missed; the passed ones were
                // generated untried. The one it waited for, and the later ones not passed, have not
                // generated yet.
                for (int i = 0; i < r.Attempt; i++) tried.Add(i);
                break;
            case CampStatus.Failed:
                // Failed only once every spot was tried or passed (Attempt was the spot count): all
                // generated, so the cell is open to the chunks it has not generated yet.
                for (int i = 0; i < r.Attempt; i++) tried.Add(i);
                r.Status = CampStatus.Open;
                r.Retries = 0;
                break;
        }
        r.Tried = [.. tried];
        r.Passed.Clear();
    }

    public List<CampRecord> Snapshot()
    {
        lock (_lock) return _cells.Values.Select(Clone).ToList();
    }

    public CampRecord? Get(CellKey cell)
    {
        lock (_lock) return _cells.TryGetValue(cell, out var r) ? Clone(r) : null;
    }

    /// <summary>The spot a cell waits for next (the first not tried), or null when it has a camp,
    /// has none for good, or every spot was tried.</summary>
    public static int? NextSpot(CampRecord? record, int spotCount)
    {
        if (spotCount == 0) return null;
        if (record is null) return 0;
        if (record.Status != CampStatus.Pending) return null;
        int next = FirstUntried(record.Tried ?? [], spotCount);
        return next < spotCount ? next : null;
    }

    /// <summary>A spot's chunk is being generated: whether to try it. A try claims the spot (it
    /// counts as tried from now on), so a spot is never tried twice.</summary>
    public AttemptVerdict OnSpot(CellKey cell, int spot, int spotCount)
    {
        lock (_lock)
        {
            var r = GetOrAdd(cell);
            if (r.Status != CampStatus.Pending || spot < 0 || spot >= spotCount || r.Tried!.Contains(spot))
                return AttemptVerdict.Skip;
            r.Tried.Add(spot);
            r.Tried.Sort();
            r.Attempt = FirstUntried(r.Tried, spotCount);
            return AttemptVerdict.Try;
        }
    }

    /// <summary>A chunk that <see cref="TraderGrid.SecondChanceChunk"/> picks is being generated in
    /// a cell: whether to try it. Only an <see cref="CampStatus.Open"/> cell with second chances
    /// left; the try spends one.</summary>
    public AttemptVerdict OnSecondChance(CellKey cell)
    {
        lock (_lock)
        {
            if (!_cells.TryGetValue(cell, out var r) || r.Status != CampStatus.Open || r.Retries >= TraderGrid.SecondChances)
                return AttemptVerdict.Skip;
            r.Retries++;
            return AttemptVerdict.Try;
        }
    }

    /// <summary>A try placed the camp. <paramref name="spot"/> is the spot tried, or -1 for a second
    /// chance. Ignored unless the cell is still waiting and that try was claimed. Returns whether it
    /// was recorded.</summary>
    public bool Placed(CellKey cell, int spot, string type, int x, int y, int z, string region, string structure, int slope = 0)
    {
        lock (_lock)
        {
            if (!_cells.TryGetValue(cell, out var r)) return false;
            bool claimed = spot >= 0
                ? r.Status == CampStatus.Pending && r.Tried!.Contains(spot)
                : r.Status == CampStatus.Open && r.Retries > 0;
            if (!claimed) return false;
            r.Status = CampStatus.Placed;
            r.Attempt = spot;
            r.Type = type;
            (r.X, r.Y, r.Z) = (x, y, z);
            r.Region = region;
            r.Structure = structure;
            r.Slope = slope;
            return true;
        }
    }

    /// <summary>A try did not place the camp: once every spot has been tried the cell is open to
    /// second chances, and once those are spent it has no camp.</summary>
    public void Missed(CellKey cell, int spotCount)
    {
        lock (_lock)
        {
            if (!_cells.TryGetValue(cell, out var r)) return;
            if (r.Status == CampStatus.Pending && FirstUntried(r.Tried!, spotCount) >= spotCount)
            {
                r.Status = CampStatus.Open;
                r.Attempt = spotCount;
            }
            if (r.Status == CampStatus.Open && r.Retries >= TraderGrid.SecondChances)
                r.Status = CampStatus.Failed;
        }
    }

    private static int FirstUntried(List<int> tried, int spotCount)
    {
        for (int i = 0; i < spotCount; i++)
            if (!tried.Contains(i)) return i;
        return spotCount;
    }

    private CampRecord GetOrAdd(CellKey cell)
    {
        if (!_cells.TryGetValue(cell, out var r))
            _cells[cell] = r = new CampRecord { CellX = cell.X, CellZ = cell.Z, Tried = [] };
        return r;
    }

    private static CampRecord Clone(CampRecord r) => new()
    {
        CellX = r.CellX, CellZ = r.CellZ, Status = r.Status, Attempt = r.Attempt, Tried = r.Tried is null ? null : [.. r.Tried],
        Passed = [.. r.Passed], Retries = r.Retries, Type = r.Type, X = r.X, Y = r.Y, Z = r.Z, Region = r.Region,
        Structure = r.Structure, Slope = r.Slope,
    };
}
