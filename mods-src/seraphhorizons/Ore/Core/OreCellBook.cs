using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>What became of a cell's spots so far, for one metal.</summary>
public sealed class CellState
{
    /// <summary>How many spots the cell has (<see cref="OreCells.SpotCount"/> for ore cells; placer
    /// fields have more, <see cref="PlacerCells.SpotCount"/>).</summary>
    public int SpotCount { get; init; } = OreCells.SpotCount;

    /// <summary>The spot whose anchor chunk may hold the deposit; <see cref="SpotCount"/> or more
    /// means none is left: the cell has no deposit of the metal.</summary>
    public int Active { get; set; }

    /// <summary>The active spot's anchor was generated and the vein is there.</summary>
    public bool Placed { get; set; }

    /// <summary>Spots whose anchor was generated while they were active but held no vein
    /// (no try of the metal from that chunk, or the rock there could not host it).</summary>
    public int FailedMask { get; set; }

    /// <summary>Fallback spots whose anchor was generated before their turn came: a vein there
    /// can no longer be whole, so they are skipped.</summary>
    public int ConsumedMask { get; set; }

    public bool None => Active >= SpotCount;

    public CellState Clone() => (CellState)MemberwiseClone();
}

/// <summary>How a spot stands, for the admin trace.</summary>
public enum SpotStatus { Waiting, Placed, Failed, Consumed, Pending }

/// <summary>
/// The state of every (metal, cell) touched so far, kept in the savegame. Each cell starts at its
/// primary spot. When a spot's anchor chunk column is generated (<see cref="OnAnchorGenerated"/>),
/// the caller says whether the metal's ore is in it: if so the deposit is placed and the cell
/// settles; if not the spot failed and the next fallback whose anchor is still ungenerated becomes
/// active; with none left the cell has no deposit of that metal. Fallback anchors generated before
/// their turn are remembered (<see cref="OnFallbackGenerated"/>) and skipped.
///
/// The primary spot depends only on the seed. Which fallback ends up used depends on what was
/// generated first, which is why the state is saved rather than recomputed.
/// </summary>
public sealed class OreCellBook
{
    private readonly Dictionary<(string Metal, CellPos Cell), CellState> _states = new();
    private readonly int _spotCount;

    /// <param name="spotCount">Spots per cell, at most 31 (the masks are ints).</param>
    public OreCellBook(int spotCount = OreCells.SpotCount)
    {
        if (spotCount is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(spotCount));
        _spotCount = spotCount;
    }

    public int Count => _states.Count;

    /// <summary>The state of a cell (a fresh one, unsaved, if the cell was never touched).</summary>
    public CellState Get(string metal, CellPos cell) =>
        _states.TryGetValue((metal, cell), out var state) ? state : new CellState { SpotCount = _spotCount };

    /// <summary>The active spot's index, or null if the cell has none left.</summary>
    public int? ActiveIndex(string metal, CellPos cell) => Get(metal, cell) is { None: false } s ? s.Active : null;

    /// <summary>
    /// A spot's anchor column has been generated. <paramref name="hasOre"/>: whether the metal's ore
    /// is in that column. Returns true if the state changed (it needs saving).
    /// </summary>
    public bool OnAnchorGenerated(string metal, CellPos cell, int spot, bool hasOre)
    {
        var state = Get(metal, cell);
        if (state.None || state.Placed || spot < state.Active)
            return false;
        if (spot > state.Active)
            return OnFallbackGenerated(metal, cell, spot);
        state = state.Clone();
        if (hasOre)
            state.Placed = true;
        else
        {
            state.FailedMask |= 1 << spot;
            Advance(state);
        }
        _states[(metal, cell)] = state;
        return true;
    }

    /// <summary>A fallback spot's anchor was generated before its turn.</summary>
    public bool OnFallbackGenerated(string metal, CellPos cell, int spot)
    {
        var state = Get(metal, cell);
        if (state.None || state.Placed || spot <= state.Active || (state.ConsumedMask & (1 << spot)) != 0)
            return false;
        state = state.Clone();
        state.ConsumedMask |= 1 << spot;
        _states[(metal, cell)] = state;
        return true;
    }

    public SpotStatus StatusOf(string metal, CellPos cell, int spot)
    {
        var state = Get(metal, cell);
        int bit = 1 << spot;
        if ((state.FailedMask & bit) != 0) return SpotStatus.Failed;
        if ((state.ConsumedMask & bit) != 0) return SpotStatus.Consumed;
        if (spot == state.Active) return state.Placed ? SpotStatus.Placed : SpotStatus.Waiting;
        return SpotStatus.Pending;
    }

    private static void Advance(CellState state)
    {
        do state.Active++;
        while (state.Active < state.SpotCount && (state.ConsumedMask & (1 << state.Active)) != 0);
    }

    // One line per touched cell: "metal cellX cellZ active placed failedMask consumedMask".
    public string Serialize()
    {
        var sb = new StringBuilder("v1\n");
        foreach (var ((metal, cell), s) in _states.OrderBy(kv => kv.Key.Metal, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.Cell.X).ThenBy(kv => kv.Key.Cell.Z))
            sb.Append(CultureInfo.InvariantCulture,
                $"{metal} {cell.X} {cell.Z} {s.Active} {(s.Placed ? 1 : 0)} {s.FailedMask} {s.ConsumedMask}\n");
        return sb.ToString();
    }

    public static OreCellBook Parse(string? text, int spotCount = OreCells.SpotCount)
    {
        var book = new OreCellBook(spotCount);
        if (string.IsNullOrWhiteSpace(text)) return book;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != "v1")
            throw new FormatException($"unknown ore cell format '{(lines.Length > 0 ? lines[0] : "")}'");
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split(' ');
            if (f.Length != 7) throw new FormatException($"bad ore cell line '{line}'");
            int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
            book._states[(f[0], new CellPos(I(1), I(2)))] = new CellState
            {
                SpotCount = spotCount,
                Active = I(3), Placed = I(4) != 0, FailedMask = I(5), ConsumedMask = I(6),
            };
        }
        return book;
    }
}
