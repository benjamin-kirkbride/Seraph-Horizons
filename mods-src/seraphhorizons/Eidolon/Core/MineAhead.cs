namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Work ahead of the band (README "Eidolon", working together, *Spare work*): a member that would otherwise
/// wait takes a plan station of the next band where nothing still to dig is above it. A cell is ahead when it
/// is a remaining target that may not be dug yet (<see cref="MineBands.Eligible"/>), no stair cell (the way
/// out is dug last), with nothing left above it in its column but cells ahead with it (of its band, struck
/// first, top down: it never undercuts what is still to dig), and
/// in the open part at most <see cref="AheadLayers"/> under the open floor (one band deeper; never under the
/// floor of the covered band being driven, its approach), or in the covered part the band under the one
/// being driven (the bench behind the face). What keeps it safe is what keeps several members working out of
/// the plan's order safe today: the stands of the earlier stations near it keep their footing and their way
/// out (<see cref="MineDispatch.Before"/>, <see cref="MineDispatch.Footing"/>, <see cref="MineRefusal.CutsOffStation"/>;
/// an ahead station comes after every station of the band, so all of those count), no remaining target loses
/// its last stand (<see cref="MineRefusal.Starves"/>), every member's stands, held and planned, stay out
/// (<see cref="MineRefusal.CutsOffWorker"/> and the routes' planned stands), and the column above is clear of
/// everything but work ahead with it;
/// the plan's later stations were planned with it dug.
/// </summary>
public static class MineAhead
{
    /// <summary>How far under the open floor it goes: one open band deeper at most, so the floors are at most
    /// two climbs apart.</summary>
    public const int AheadLayers = 2;

    /// <summary>Whether a cell may be dug ahead of its band now.</summary>
    public static bool Eligible(MineState live, MarkPos cell)
    {
        if (live.Bands is not { } b || !live.Remaining.Contains(cell))
            return false;
        int band = b.Band(cell);
        if (band == MineBands.Stair || band == MineBands.Never || b.Eligible(cell))
            return false;
        if (band == b.OpenBand)
        {
            if (b.Layer is null || cell.Y < b.OpenFloor - AheadLayers || b.Covered is { } k && cell.Y < b.Bottom(k))
                return false;
        }
        else if (!(band < b.CoveredBands && b.Covered is { } driven && band == driven + 1))
            return false;
        // nothing left over it but cells ahead with it (of its band, not yet eligible), which are struck first
        var area = live.Site.Area;
        for (int y = cell.Y + 1; y <= area.Max.Y; y++)
        {
            var over = cell with { Y = y };
            if (live.Remaining.Contains(over) && (b.Band(over) != band || b.Eligible(over)))
                return false;
        }
        return true;
    }
}
