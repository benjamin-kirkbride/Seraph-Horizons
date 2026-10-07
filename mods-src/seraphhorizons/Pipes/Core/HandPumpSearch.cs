namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>A block cell, in world coordinates.</summary>
public readonly record struct PipeCell(int X, int Y, int Z)
{
    /// <summary>The six faces in the game's <c>BlockFacing.ALLFACES</c> order: north, east, south,
    /// west, up, down.</summary>
    public static readonly PipeCell[] Faces =
    [
        new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0),
    ];

    /// <summary>The face opposite face <paramref name="face"/> (an index into <see cref="Faces"/>).</summary>
    public static int Opposite(int face) => face switch { 0 => 2, 1 => 3, 2 => 0, 3 => 1, 4 => 5, _ => 4 };

    public PipeCell Step(int face) => new(X + Faces[face].X, Y + Faces[face].Y, Z + Faces[face].Z);
}

/// <summary>What a hand pump's search found: the wellspring's cell and the pipes from the pump to
/// it, counted as Hydrate or Diedrate counts its own (the cell under the pump is the first, the one
/// beside the spring the last).</summary>
public readonly record struct PumpPath(PipeCell Spring, int Pipes);

/// <summary>
/// Hydrate or Diedrate's hand pump, on Pipes and Power Expanded's pipes (the <c>UnifiedPipes</c>
/// switch). Hydrate's own search went breadth first through its pipes from the cell under the pump
/// and stopped at the first wellspring face-adjacent to a pipe; its priming was that distance (the
/// pipes walked) over <c>HandPumpPrimingBlocksPerStroke</c>. These are the same rules, over whatever
/// says which ppex cells are linked.
/// </summary>
public static class HandPumpSearch
{
    /// <summary>Hydrate or Diedrate's search limit (<c>FindWellViaNetwork</c>'s 0x1000).</summary>
    public const int MaxVisited = 4096;

    /// <summary>Hydrate or Diedrate's fallback when <c>HandPumpPrimingBlocksPerStroke</c> is 0 or less.</summary>
    public const int DefaultBlocksPerStroke = 3;

    /// <summary>The nearest wellspring beside a pipe connected to <paramref name="start"/> (the cell
    /// under the pump), breadth first: pipe cells are those <paramref name="isPipe"/> accepts, and a
    /// step from a pipe across a face is taken only when <paramref name="linked"/> says the two
    /// cells couple there. A spring may sit beside any face of a pipe. Null when
    /// <paramref name="start"/> is not a pipe, or no spring is found within
    /// <paramref name="maxVisited"/> pipes. Of springs at the same distance the first found wins
    /// (faces in <see cref="PipeCell.Faces"/> order).</summary>
    public static PumpPath? Nearest(PipeCell start, Func<PipeCell, bool> isPipe, Func<PipeCell, int, bool> linked,
        Func<PipeCell, bool> isSpring, int maxVisited = MaxVisited)
    {
        if (!isPipe(start))
            return null;
        var seen = new HashSet<PipeCell> { start };
        var queue = new Queue<(PipeCell Cell, int Depth)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (cell, depth) = queue.Dequeue();
            for (int face = 0; face < PipeCell.Faces.Length; face++)
            {
                var next = cell.Step(face);
                if (isSpring(next))
                    return new PumpPath(next, depth + 1);
            }
            for (int face = 0; face < PipeCell.Faces.Length; face++)
            {
                var next = cell.Step(face);
                if (seen.Count >= maxVisited || seen.Contains(next) || !isPipe(next) || !linked(cell, face))
                    continue;
                seen.Add(next);
                queue.Enqueue((next, depth + 1));
            }
        }
        return null;
    }

    /// <summary>Priming strokes for a spring <paramref name="pipes"/> pipes away, as Hydrate or
    /// Diedrate's <c>ComputePrimingStrokes</c> works them out: none with priming off or no pipe,
    /// else the pipes over <paramref name="blocksPerStroke"/> (3 when that is 0 or less), rounded
    /// down.</summary>
    public static int PrimingStrokes(int pipes, bool primingEnabled, int blocksPerStroke)
    {
        if (pipes <= 0 || !primingEnabled)
            return 0;
        return Math.Max(0, pipes / (blocksPerStroke <= 0 ? DefaultBlocksPerStroke : blocksPerStroke));
    }

    /// <summary>Whether a pipe network may join a pump to a spring: an empty one (no medium yet) or
    /// one holding water. A steam, exhaust or air line never reaches a well.</summary>
    public static bool CarriesWater(string? medium) => string.IsNullOrEmpty(medium) || medium == "Water";

    /// <summary>The cells under a fluid intake where its water is looked for, as offsets from the
    /// intake: from one below to <paramref name="depth"/> below, and out to half the depth
    /// sideways, as ppex's <c>ScanWaterBelow</c> scans; nearest first (the column under the
    /// intake first at each depth).</summary>
    public static IEnumerable<PipeCell> IntakeCells(int depth)
    {
        int half = Math.Max(0, depth / 2);
        for (int dy = 1; dy <= Math.Max(1, depth); dy++)
            for (int ring = 0; ring <= 2 * half; ring++)
                for (int dx = -half; dx <= half; dx++)
                    for (int dz = -half; dz <= half; dz++)
                        if (Math.Abs(dx) + Math.Abs(dz) == ring)
                            yield return new PipeCell(dx, -dy, dz);
    }

    /// <summary>How much an intake over a well may give: what it asks for, capped at what the
    /// governing spring holds; nothing from an empty spring.</summary>
    public static float IntakeAllowance(float asked, float springLitres) =>
        Math.Max(0f, Math.Min(asked, springLitres));
}

/// <summary>Type names tried in order for one thing another mod owns, the newest layout first, so a
/// pinned version and the one before it both bind (as <c>PpexWater</c> does for ppex's move between
/// namespaces).</summary>
public static class NameCandidates
{
    /// <summary>The first of <paramref name="names"/> that <paramref name="resolve"/> finds, and its
    /// name; null when none does (an empty or null name is skipped).</summary>
    public static (string Name, T Value)? First<T>(IEnumerable<string?> names, Func<string, T?> resolve) where T : class
    {
        foreach (var name in names)
            if (!string.IsNullOrEmpty(name) && resolve(name) is { } value)
                return (name, value);
        return null;
    }
}
