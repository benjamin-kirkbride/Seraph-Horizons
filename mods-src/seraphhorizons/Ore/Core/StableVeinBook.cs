using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// The vein an ore cell's spot holds under stable veins (#946): which of Interesting Ore Gen's
/// tries it is and the host rock at its centre, decided once, in the anchor column's own
/// TerrainFeatures pass, and replayed in every column within reach (<c>StableVeins</c>). The try
/// is named by what the game's <c>GenDeposits</c> seeds it with: its source chunk and the world
/// seed it draws for the try (the vein's <c>DepositRand</c> is <c>SetWorldSeed(WorldSeed)</c>
/// then <c>InitPositionSeed(SourceX, SourceZ)</c>), its centre (X, Z) and its deposit variant.
/// <see cref="Holds"/>: whether the vein reaches rock in the anchor column, which is what places
/// the deposit at this spot or moves the cell to its next one.
/// </summary>
public sealed record StableVein(string Metal, CellPos Cell, int Spot, int SourceX, int SourceZ, long WorldSeed,
    int CentreX, int CentreZ, string Host, string Variant, bool Holds)
{
    public ChunkPos Source => new(SourceX, SourceZ);
}

/// <summary>
/// Every decided vein of the world, by (metal, cell), saved under <c>seraphhorizons:stableveins</c>.
/// A cell has at most one: its deposit's (or, if <see cref="StableVein.Holds"/> is false, the
/// failed spot's, until the next spot is decided). Thread-safe: the worldgen thread writes it.
/// </summary>
public sealed class StableVeinBook
{
    private readonly Dictionary<(string Metal, CellPos Cell, int Spot), StableVein> _veins = new();
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _veins.Count; }
    }

    public StableVein? Get(string metal, CellPos cell, int spot)
    {
        lock (_lock) return _veins.GetValueOrDefault((metal, cell, spot));
    }

    /// <summary>Records a decision; the first one for a spot stands (returns false if one was there).</summary>
    public bool Add(StableVein vein)
    {
        lock (_lock) return _veins.TryAdd((vein.Metal, vein.Cell, vein.Spot), vein);
    }

    // One line per spot decided: "metal cellX cellZ spot holds sourceX sourceZ worldSeed centreX centreZ host variant".
    public string Serialize()
    {
        var sb = new StringBuilder("v1\n");
        lock (_lock)
            foreach (var v in _veins.Values.OrderBy(v => v.Metal, StringComparer.Ordinal).ThenBy(v => v.Cell.X).ThenBy(v => v.Cell.Z).ThenBy(v => v.Spot))
                sb.Append(CultureInfo.InvariantCulture,
                    $"{v.Metal} {v.Cell.X} {v.Cell.Z} {v.Spot} {(v.Holds ? 1 : 0)} {v.SourceX} {v.SourceZ} {v.WorldSeed} {v.CentreX} {v.CentreZ} {v.Host} {v.Variant}\n");
        return sb.ToString();
    }

    public static StableVeinBook Parse(string? text)
    {
        var book = new StableVeinBook();
        if (string.IsNullOrWhiteSpace(text)) return book;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines[0] != "v1") throw new FormatException($"unknown stable vein format '{lines[0]}'");
        foreach (var line in lines.Skip(1))
        {
            var f = line.Split(' ', 12);
            if (f.Length != 12) throw new FormatException($"bad stable vein line '{line}'");
            int I(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
            book.Add(new StableVein(f[0], new CellPos(I(1), I(2)), I(3), I(5), I(6), long.Parse(f[7], CultureInfo.InvariantCulture),
                I(8), I(9), f[10], f[11], I(4) != 0));
        }
        return book;
    }
}

/// <summary>
/// How a replayed vein is laid into one real chunk column (#946). The replay runs Interesting Ore
/// Gen's own generator against a world that is the vein's host rock everywhere, so every random
/// draw is made whatever is loaded or placed; it yields IOG's writes in order. In the real column
/// a write of the host rock (IOG's shell around the vein, its "normal" blocks and boulders) lands
/// only on rock (<paramref name="isRock"/>: stone or <c>rock-*</c>, as IOG's own shell test), and
/// any other write (ore, sub-deposit ore, prospect-vein ore) only where the block is then the host
/// rock: as IOG places ore only on a host rock, and its shell has made the rock around every ore
/// position the host first. The result depends on the column's own blocks and the seed alone.
/// </summary>
public static class StableVeinApply
{
    /// <summary>
    /// Where a boulder indicator goes (#946): IOG walks a tracer path up from an ore block until the
    /// block above is air and puts the boulder there. In the replay's world of rock the path never
    /// meets air, so the site is taken from the path as it stands: the first point at or above the
    /// worldgen terrain height of its column (<paramref name="heightAt"/>, the top solid block, fixed
    /// by the Terrain pass), at that height. Only the column the path starts in is walked (IOG let it
    /// drift up to 2 chunks), so the site depends on that column's heights alone; a path that
    /// leaves it first, or whose heights are unknown, has no boulder.
    /// </summary>
    public static (int X, int Y, int Z)? BoulderSite(IReadOnlyList<(int X, int Y, int Z)> path, Func<int, int, int?> heightAt)
    {
        if (path.Count == 0) return null;
        int cx = path[0].X >> 5, cz = path[0].Z >> 5;
        foreach (var (x, y, z) in path)
        {
            if (x >> 5 != cx || z >> 5 != cz) return null;
            if (heightAt(x, z) is not { } h) return null;
            if (y >= h) return (x, h, z);
        }
        return null;
    }

    /// <summary>The blocks of IOG's deformed boulder (<c>PlaceDeformedBoulder</c>) centred on a site:
    /// its own position-seeded <see cref="Random"/>, radii between <paramref name="minRadius"/> and
    /// <paramref name="maxRadius"/>, the same draws in the same order. IOG lowers the centre to a
    /// height between the site and the first rock below it; here the rock is taken at the site
    /// itself (the draw is still made), so nothing is read.</summary>
    public static List<(int X, int Y, int Z)> BoulderBlocks(int x, int y, int z, int minRadius, int maxRadius)
    {
        int seed = unchecked(x * 734287 + y * 912271 + z * 438289);
        var random = new Random(seed);
        int cy = random.Next(y, y + 1);
        int rx = random.Next(minRadius, maxRadius + 1);
        int ry = random.Next(minRadius, maxRadius + 1);
        int rz = random.Next(minRadius, maxRadius + 1);
        const float jitter = 0.25f;
        var blocks = new List<(int, int, int)>();
        var seen = new HashSet<(int, int, int)>();
        for (int i = -rx; i <= rx; i++)
            for (int j = -ry; j <= ry; j++)
                for (int k = -rz; k <= rz; k++)
                {
                    float a = (float)i / rx, b = (float)j / ry, c = (float)k / rz;
                    float d = a * a + b * b + c * c;
                    float n = (float)random.NextDouble() * jitter - jitter * 0.5f;
                    if (d + n <= 1f && seen.Add((x + i, cy + j, z + k))) blocks.Add((x + i, cy + j, z + k));
                }
        return blocks;
    }

    /// <summary>Applies <paramref name="writes"/> (block position, block id, in IOG's order) to the
    /// column whose blocks <paramref name="get"/> / <paramref name="set"/> read and write; positions
    /// outside it are the caller's to skip. Returns the blocks set.</summary>
    public static int Apply(IEnumerable<(int X, int Y, int Z, int Id)> writes, int hostId,
        Func<int, int, int, int> get, Action<int, int, int, int> set, Func<int, bool> isRock)
    {
        int placed = 0;
        foreach (var (x, y, z, id) in writes)
        {
            int real = get(x, y, z);
            bool ok = id == hostId ? isRock(real) : real == hostId;
            if (!ok || real == id) continue;
            set(x, y, z, id);
            placed++;
        }
        return placed;
    }
}
