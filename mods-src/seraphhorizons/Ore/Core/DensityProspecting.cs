namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// The figures of <c>WideDensityProspecting</c> (README "Density prospecting reads 64 blocks, off the
/// main thread"): BetterEr Prospecting's block-based density mode counts the deposit blocks in a
/// square column around the sample, <see cref="ShippedRadius"/> blocks each way as it ships, and
/// divides by the column's volume. The switch widens it to <see cref="WideRadius"/>.
/// </summary>
public static class DensityProspecting
{
    /// <summary>BetterEr's <c>ItemBetterErProspectingPick.densityRadius</c> (the game's chunk size).</summary>
    public const int ShippedRadius = 32;

    /// <summary>The switch's radius: two chunks each way.</summary>
    public const int WideRadius = 64;

    /// <summary>The game's chunk size, in blocks.</summary>
    public const int ChunkSize = 32;

    /// <summary>The area of the zone a reading is normalised over: BetterEr's
    /// <c>zoneDiameter * zoneDiameter</c> with <c>zoneDiameter = 2 * radius</c>, which the compiler
    /// folds into one constant (4096 as it ships).</summary>
    public static int ZoneArea(int radius) => 2 * radius * (2 * radius);

    /// <summary>The block offsets, on each axis, around a saved reading whose chunks
    /// <c>/btrpr reprospect</c> loads before walking it again: a chunk apart, from
    /// <c>-radius</c> to <c>radius</c>, so every chunk a walk of <paramref name="radius"/> touches is
    /// one of them (BetterEr's own <c>{ -32, 0, 32 }</c> at 32).</summary>
    public static int[] ChunkOffsets(int radius)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        int steps = (radius + ChunkSize - 1) / ChunkSize;
        var offsets = new int[2 * steps + 1];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = Math.Clamp((i - steps) * ChunkSize, -radius, radius);
        return offsets;
    }

    /// <summary>The chunk columns (x, z) a walk of <paramref name="radius"/> around block column
    /// (<paramref name="x"/>, <paramref name="z"/>) reads: the game's <c>WalkBlocks</c> takes every
    /// chunk from <c>(x - radius) / 32</c> to <c>(x + radius) / 32</c>, clamped at 0.</summary>
    public static IEnumerable<(int Cx, int Cz)> ChunksWalked(int x, int z, int radius)
    {
        for (int cx = Math.Max(0, x - radius) / ChunkSize; cx <= (x + radius) / ChunkSize; cx++)
            for (int cz = Math.Max(0, z - radius) / ChunkSize; cz <= (z + radius) / ChunkSize; cz++)
                yield return (cx, cz);
    }

    /// <summary>The chunk columns reprospect loads around (<paramref name="x"/>, <paramref name="z"/>)
    /// for <paramref name="offsets"/>, as BetterEr computes them.</summary>
    public static IEnumerable<(int Cx, int Cz)> ChunksLoaded(int x, int z, IReadOnlyList<int> offsets)
    {
        foreach (int dx in offsets)
            foreach (int dz in offsets)
                yield return ((x + dx) / ChunkSize, (z + dz) / ChunkSize);
    }
}

/// <summary>
/// One density walk in flight per player: a hit while that player's last walk is still running is
/// turned away. Thread-safe; the walk itself ends on the server's main thread.
/// </summary>
public sealed class ProbeGate
{
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Takes the player's slot. False when a walk of theirs is already in flight.</summary>
    public bool TryBegin(string playerUid)
    {
        lock (_lock)
            return _busy.Add(playerUid);
    }

    /// <summary>Frees the player's slot (nothing if it was free).</summary>
    public void End(string playerUid)
    {
        lock (_lock)
            _busy.Remove(playerUid);
    }

    /// <summary>Whether a walk of the player's is in flight.</summary>
    public bool Busy(string playerUid)
    {
        lock (_lock)
            return _busy.Contains(playerUid);
    }

    /// <summary>How many walks are in flight.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _busy.Count;
        }
    }

    /// <summary>Frees every slot (the server is going away).</summary>
    public void Clear()
    {
        lock (_lock)
            _busy.Clear();
    }
}
