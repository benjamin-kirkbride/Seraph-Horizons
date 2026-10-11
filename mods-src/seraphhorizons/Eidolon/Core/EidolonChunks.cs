using System.Text.Json;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A chunk column, by its chunk coordinates (a block's x and z over <see cref="EidolonChunks.ChunkSize"/>).</summary>
public readonly record struct ChunkColumn(int X, int Z);

/// <summary>
/// Chunk loading's rules (README "Eidolon", chunk loading), game-independent: when an eidolon keeps
/// its chunks loaded, and which. An eidolon at work (an order with work left, <c>HasWorkLeft</c>, and
/// able to work) holds the columns within <see cref="EidolonConfig.ChunkLoadRadius"/> of the one it
/// stands in, and those its order's work areas cover (<c>WorkAreas</c>: an area, a machine's cell),
/// each area only while it covers at most <see cref="EidolonConfig.MaxWorkAreaChunks"/> columns.
/// </summary>
public static class EidolonChunks
{
    /// <summary>The game's chunk size in blocks (<c>GlobalConstants.ChunkSize</c>).</summary>
    public const int ChunkSize = 32;

    /// <summary>The most <see cref="EidolonConfig.ChunkLoadRadius"/> may be: 4 is 81 columns.</summary>
    public const int MaxRadius = 4;

    /// <summary>Whether an eidolon keeps its chunks loaded: the setting on, able to work (not slumped,
    /// dry, docked or otherwise stopped) and its order with work left.</summary>
    public static bool Keeps(bool enabled, bool canWork, bool hasWorkLeft) => enabled && canWork && hasWorkLeft;

    /// <summary>The column the block at (<paramref name="x"/>, <paramref name="z"/>) is in.</summary>
    public static ChunkColumn At(double x, double z) =>
        new((int)Math.Floor(x / ChunkSize), (int)Math.Floor(z / ChunkSize));

    /// <summary>The columns within <paramref name="radius"/> of the one (<paramref name="x"/>,
    /// <paramref name="z"/>) is in, a square of (2 radius + 1)² (radius 1: its own and the 8 round it).</summary>
    public static IEnumerable<ChunkColumn> Around(double x, double z, int radius)
    {
        var centre = At(x, z);
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
                yield return new ChunkColumn(centre.X + dx, centre.Z + dz);
    }

    /// <summary>How many columns <paramref name="area"/> covers.</summary>
    public static long CoveredCount(MarkArea area)
    {
        var min = At(area.Min.X, area.Min.Z);
        var max = At(area.Max.X, area.Max.Z);
        return (long)(max.X - min.X + 1) * (max.Z - min.Z + 1);
    }

    /// <summary>The columns <paramref name="area"/> covers.</summary>
    public static IEnumerable<ChunkColumn> Covering(MarkArea area)
    {
        var min = At(area.Min.X, area.Min.Z);
        var max = At(area.Max.X, area.Max.Z);
        for (int x = min.X; x <= max.X; x++)
            for (int z = min.Z; z <= max.Z; z++)
                yield return new ChunkColumn(x, z);
    }

    /// <summary>The columns an eidolon at work standing at (<paramref name="x"/>, <paramref name="z"/>)
    /// holds: those <see cref="Around"/> it, and those each of <paramref name="areas"/> covers that
    /// covers at most <paramref name="maxAreaColumns"/> (a larger one is left out whole: the eidolon
    /// works it where it stands).</summary>
    public static HashSet<ChunkColumn> Wanted(double x, double z, int radius, IEnumerable<MarkArea> areas, int maxAreaColumns)
    {
        var columns = new HashSet<ChunkColumn>(Around(x, z, radius));
        foreach (var area in areas)
            if (CoveredCount(area) <= maxAreaColumns)
                columns.UnionWith(Covering(area));
        return columns;
    }

    /// <summary>A work area of one block (a machine's cell, a block to carry).</summary>
    public static MarkArea Block(int x, int y, int z) => new(new MarkPos(x, y, z), new MarkPos(x, y, z));
}

/// <summary>
/// The columns the eidolons hold, by holder (an eidolon's entity id), counted: a column two eidolons
/// hold is forced once and let go when the last lets go. A column the server already forced for
/// something else when it was first held here (the spawn's, another mod's) is never forced or let go:
/// the server's list of forced columns is a set, not counted, so letting it go would unforce it for
/// that too. <see cref="Set"/> and <see cref="Release"/> say which columns to force and to let go.
/// </summary>
public sealed class ChunkHolds
{
    private readonly Dictionary<long, HashSet<ChunkColumn>> _byHolder = [];
    private readonly Dictionary<ChunkColumn, int> _count = [];
    private readonly HashSet<ChunkColumn> _foreign = [];

    /// <summary>The eidolons holding columns.</summary>
    public IReadOnlyCollection<long> Holders => _byHolder.Keys;

    /// <summary>The columns <paramref name="holder"/> holds (none when it holds none).</summary>
    public IReadOnlyCollection<ChunkColumn> Of(long holder) =>
        _byHolder.TryGetValue(holder, out var columns) ? columns : [];

    /// <summary>Whether any holder holds <paramref name="column"/>.</summary>
    public bool Holds(ChunkColumn column) => _count.ContainsKey(column);

    /// <summary>Every column held, each once.</summary>
    public IEnumerable<ChunkColumn> All => _count.Keys;

    /// <summary><paramref name="holder"/> now holds exactly <paramref name="columns"/> (none: it lets
    /// all go). Returns the columns newly held by anyone, to force (less those
    /// <paramref name="forcedElsewhere"/> says the server already forces), and those no one holds any
    /// more, to let go (less those it forced for something else).</summary>
    public (List<ChunkColumn> Force, List<ChunkColumn> Release) Set(long holder, IEnumerable<ChunkColumn> columns,
        Func<ChunkColumn, bool> forcedElsewhere)
    {
        var wanted = new HashSet<ChunkColumn>(columns);
        var had = _byHolder.TryGetValue(holder, out var old) ? old : [];
        var force = new List<ChunkColumn>();
        var release = new List<ChunkColumn>();
        foreach (var column in wanted)
        {
            if (had.Contains(column))
                continue;
            int n = _count.GetValueOrDefault(column);
            _count[column] = n + 1;
            if (n > 0)
                continue;
            if (forcedElsewhere(column))
                _foreign.Add(column);
            else
                force.Add(column);
        }
        foreach (var column in had)
        {
            if (wanted.Contains(column))
                continue;
            if (Drop(column))
                release.Add(column);
        }
        if (wanted.Count == 0)
            _byHolder.Remove(holder);
        else
            _byHolder[holder] = wanted;
        return (force, release);
    }

    /// <summary><paramref name="holder"/> lets all its columns go; returns those to let go.</summary>
    public List<ChunkColumn> Release(long holder) => Set(holder, [], _ => false).Release;

    // One holder fewer; true when no one holds it now and it was forced here.
    private bool Drop(ChunkColumn column)
    {
        int n = _count.GetValueOrDefault(column);
        if (n > 1)
        {
            _count[column] = n - 1;
            return false;
        }
        _count.Remove(column);
        return !_foreign.Remove(column);
    }

    /// <summary>What is held, by holder, for the savegame (<see cref="FromBytes"/>), so a restart can
    /// load it again before the eidolons are loaded.</summary>
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(
        _byHolder.Select(h => new SavedHold(h.Key, h.Value.OrderBy(c => c.X).ThenBy(c => c.Z).ToList())).ToList());

    /// <summary>The holds saved by <see cref="ToBytes"/>; none for no data.</summary>
    public static List<SavedHold> FromBytes(byte[]? data) =>
        data == null || data.Length == 0 ? [] : JsonSerializer.Deserialize<List<SavedHold>>(data) ?? [];
}

/// <summary>One eidolon's held columns as saved.</summary>
public sealed record SavedHold(long EntityId, List<ChunkColumn> Columns);
