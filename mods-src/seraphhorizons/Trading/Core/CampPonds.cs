namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>A placed camp's recorded location (the game's <c>Cuboidi</c>: minimum inclusive, maximum
/// exclusive, as <c>LastPlacedSchematicLocation</c> is set), skirt included.</summary>
public readonly record struct CampBox(int X1, int Y1, int Z1, int X2, int Y2, int Z2);

/// <summary>
/// Whether a pond the game's pond generator (<c>GenPonds.TryPlacePondAt</c>, VSEssentials 1.22.7) has
/// just flood-filled would put water in a trader camp, game-independent. A pond is one layer of
/// water at its height, every cell of the fill, and lake bed in the layer under it; so it touches a
/// camp when that two-layer slab meets the camp's location grown by <see cref="Margin"/> on every
/// side (water against a camp's wall or in its doorway is as wrong as water inside).
/// </summary>
public static class CampPonds
{
    public const int Margin = 1;

    /// <summary>The world area (max exclusive) a pond started in chunk (<paramref name="chunkX"/>,
    /// <paramref name="chunkZ"/>) can fill: the generator gives up on a fill that reaches its
    /// boundaries, -31 and 63 blocks from the chunk's corner, so cells lie strictly between.</summary>
    public static (int X1, int Z1, int X2, int Z2) Window(int chunkX, int chunkZ) =>
        (chunkX * 32 - 30, chunkZ * 32 - 30, chunkX * 32 + 63, chunkZ * 32 + 63);

    /// <summary>Whether a pond at <paramref name="pondY"/> can touch <paramref name="camp"/> at all:
    /// the layers overlap and the camp meets the window (<see cref="Window"/>, max exclusive).</summary>
    public static bool Near(CampBox camp, int pondY, (int X1, int Z1, int X2, int Z2) window) =>
        pondY >= camp.Y1 - Margin && pondY - 1 < camp.Y2 + Margin
        && window.X1 < camp.X2 + Margin && camp.X1 - Margin < window.X2
        && window.Z1 < camp.Z2 + Margin && camp.Z1 - Margin < window.Z2;

    /// <summary>Whether the pond's water at (<paramref name="x"/>, <paramref name="pondY"/>,
    /// <paramref name="z"/>), or the lake bed under it, is in <paramref name="camp"/> or next to it.</summary>
    public static bool Touches(CampBox camp, int x, int pondY, int z) =>
        pondY >= camp.Y1 - Margin && pondY - 1 < camp.Y2 + Margin
        && x >= camp.X1 - Margin && x < camp.X2 + Margin
        && z >= camp.Z1 - Margin && z < camp.Z2 + Margin;

    /// <summary>Whether any cell of a pond's fill touches any of <paramref name="camps"/>.</summary>
    public static bool Floods(IReadOnlyList<CampBox> camps, int pondY, IEnumerable<(int X, int Z)> cells)
    {
        if (camps.Count == 0) return false;
        foreach (var (x, z) in cells)
            foreach (var camp in camps)
                if (Touches(camp, x, pondY, z))
                    return true;
        return false;
    }
}
