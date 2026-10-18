namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>What becomes of a product an ore machine's outlet could not push on (<see cref="Outlet.Fate"/>).</summary>
public enum OutletFate
{
    /// <summary>Kept in the outlet's slots for the chute or container there to take when it has room.</summary>
    Held,
    /// <summary>Tailings discarded on the heap beyond the outlet.</summary>
    Discarded,
    /// <summary>Dropped on the ground just beyond the outlet's face.</summary>
    Dropped,
}

/// <summary>
/// An ore machine's outlet (the owner's ruling): a product goes into a chute or container beyond the
/// outlet's face when one is there (<c>MachineChutes</c> in the game half), and waits for it while it is
/// full, so a backed-up chute stops the machine. With nothing there it never waits: tailings go on the heap
/// (when the machine makes one) and everything else drops on the ground in front of the outlet.
/// </summary>
public static class Outlet
{
    /// <summary>What becomes of a product left over at an outlet: <paramref name="attached"/>, a chute laid to take
    /// from it or a container there; <paramref name="tailings"/>, the outlet discards tailings on a heap.</summary>
    public static OutletFate Fate(bool attached, bool tailings) =>
        attached ? OutletFate.Held : tailings ? OutletFate.Discarded : OutletFate.Dropped;

    /// <summary>Where a dropped product appears, from the outlet cell's own origin, for an outlet face of normal
    /// (<paramref name="nx"/>, <paramref name="ny"/>, <paramref name="nz"/>): the middle of the cell beyond the
    /// face, low in it so it settles where it appears, or just above the face of an outlet facing up.</summary>
    public static (double X, double Y, double Z) DropOffset(int nx, int ny, int nz) =>
        (0.5 + nx, ny > 0 ? 1.1 : ny < 0 ? -0.5 : 0.25, 0.5 + nz);
}
