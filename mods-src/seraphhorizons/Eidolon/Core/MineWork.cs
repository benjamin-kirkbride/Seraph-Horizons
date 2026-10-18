namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// The mine planner's work counted in units, on the thread that does it (README "Eidolon", mining,
/// *Stations*: planning in slices). A unit is about one cell or node looked at: a block read, a node's
/// test, a hash lookup, a few nanoseconds. The code under the planner adds what each loop looks at
/// (<see cref="Add"/>), a stand's reach box or a node's neighbours at once where it is a fixed shape, and
/// an array of the window filled, cleared or copied counts a unit a cache line (<see cref="Bytes"/>), as
/// memory runs at about that speed. It is never read to plan, only to check the slicing: the units
/// between two checkpoints of <see cref="MinePlanner.Step"/> are the same on every machine, so a test
/// bounds the longest gap (a loop with no checkpoint in it) where time would vary with the machine.
/// Counted per thread, so planners on several workers never share a cache line; a counter, never an
/// allocation, so the game's planning costs no more for it.
/// </summary>
public static class MineWork
{
    [ThreadStatic]
    private static long _units;

    /// <summary>The units this thread has done since it started.</summary>
    public static long Units => _units;

    /// <summary>Adds <paramref name="units"/> cells or nodes looked at.</summary>
    public static void Add(int units) => _units += units;

    /// <summary>Adds <paramref name="bytes"/> of arrays filled, cleared or copied: a unit a cache line.</summary>
    public static void Bytes(long bytes) => _units += bytes >> 6;
}
