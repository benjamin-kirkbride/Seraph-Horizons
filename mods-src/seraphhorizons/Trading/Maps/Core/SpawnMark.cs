using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>What a player's join does about their spawn mark (<see cref="SpawnMark.Decide"/>).</summary>
public enum SpawnMarkDecision
{
    /// <summary>Had it already (or was found a veteran): nothing.</summary>
    Done,
    /// <summary>Never had it and has no trader camp marked: mark the nearest prospector.</summary>
    Give,
    /// <summary>Never had it, from before the mark existed, with a trader camp marked already: the
    /// mark is spent without being given.</summary>
    Veteran,
}

/// <summary>
/// The spawn mark (switch <c>SpawnProspectorMark</c>): on a player's first join to a world with the
/// camp grid, the nearest prospector's camp goes on their map at a lead's precision, as if they had
/// bought a lead to it; meeting its trader makes it exact. Once per player, ever
/// (<see cref="LeadBook.SpawnMarkGiven"/>): not on respawn, not on rejoin, not again if they delete
/// the waypoint.
/// </summary>
public static class SpawnMark
{
    /// <summary>Rings of grid cells around the player's cell searched for a prospector. The lattice
    /// puts one within ring 2 of every cell; ring 3 lets a nearer one just past ring 2 win, and keeps
    /// the cells a world without camps tries few.</summary>
    public const int Reach = 3;

    /// <summary>
    /// Whether a joining player gets the mark: not when they had it; when they never had it but
    /// their remembered markers (<paramref name="markKeys"/>, the targets of the
    /// <see cref="MarkBook"/>'s records) include a trader camp (<c>camp:x,z</c>), they are a player
    /// from before the mark existed who has found camps already, and the mark is only spent.
    /// </summary>
    public static SpawnMarkDecision Decide(bool given, IEnumerable<string> markKeys) =>
        given ? SpawnMarkDecision.Done
        : markKeys.Any(k => k.StartsWith("camp:", StringComparison.Ordinal)) ? SpawnMarkDecision.Veteran
        : SpawnMarkDecision.Give;

    /// <summary>
    /// The nearest seeded prospector cell's camp to (<paramref name="x"/>, <paramref name="z"/>) by
    /// real distance (<see cref="CampOption.Distance"/>, which <paramref name="site"/> measures from
    /// there), among the cells within <paramref name="reach"/> rings of the player's own cell (its
    /// own included) that can take a camp or have one and that the player lacks. Rings are searched
    /// outward until no cell of the next ring can be nearer than the best found
    /// (<see cref="Clearance"/>), so a camp in ring 2 beats a further one in ring 1. Null when none.
    /// </summary>
    /// <param name="isProspector">Whether a cell is seeded a prospector (<c>TraderGrid.IsProspector</c>).</param>
    /// <param name="site">A cell's camp (placed, or the spot it waits for), its distance from the
    /// player, or null if it can take none.</param>
    /// <param name="have">The camps the player has.</param>
    public static CampOption? Target(double x, double z, int reach, Func<CellKey, bool> isProspector, Func<CellKey, CampOption?> site,
        ISet<CellKey> have)
    {
        var own = TraderGrid.CellOf((int)Math.Floor(x), (int)Math.Floor(z));
        CampOption? best = null;
        for (int r = 0; r <= Math.Max(0, reach); r++)
        {
            if (best is { } found && Clearance(own, r, x, z) >= found.Distance) break;
            foreach (var cell in CampLeads.RingCells(own, r))
            {
                if (!isProspector(cell) || have.Contains(cell) || site(cell) is not { } s) continue;
                if (best is not { } b || s.Distance < b.Distance
                    || (s.Distance == b.Distance && (s.Cell.X, s.Cell.Z).CompareTo((b.Cell.X, b.Cell.Z)) < 0))
                    best = s;
            }
        }
        return best;
    }

    /// <summary>The least distance from (<paramref name="x"/>, <paramref name="z"/>), in the cell
    /// <paramref name="own"/>, to any point of a cell <paramref name="ring"/> rings out: to the edge
    /// of the square of the rings inside it. 0 for ring 0.</summary>
    public static double Clearance(CellKey own, int ring, double x, double z)
    {
        if (ring <= 0) return 0;
        double s = TraderGrid.CellSize;
        double west = x - (own.X - (ring - 1)) * s, east = (own.X + ring) * s - x;
        double north = z - (own.Z - (ring - 1)) * s, south = (own.Z + ring) * s - z;
        return Math.Max(0, Math.Min(Math.Min(west, east), Math.Min(north, south)));
    }
}
