using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>
/// How far a map's target is, as players read it: whole kilometres (1 km = 1,000 blocks), rounded
/// to the nearest, "about 3 km"; under half a kilometre, "under a km". Ore maps, gravel maps and
/// leads, their offers in the trade window and the map items alike say it this way, never in
/// blocks or rings (rings stay in pricing, reach and the admin commands).
/// </summary>
public static class MapDistance
{
    /// <summary>The lang key (in the mod's domain) for a distance of whole kilometres; its argument
    /// is the kilometres.</summary>
    public const string KmKey = "map-distance-km";

    /// <summary>The lang key for a distance under half a kilometre (no argument).</summary>
    public const string UnderKey = "map-distance-under";

    /// <summary>Whole kilometres to the nearest (half a kilometre rounds up); 0 under half.</summary>
    public static int Km(double blocks) => (int)Math.Round(Math.Max(0, blocks) / 1000, MidpointRounding.AwayFromZero);

    /// <summary>The lang key and its arguments for <paramref name="blocks"/>: <see cref="UnderKey"/>
    /// when it rounds to 0 km, else <see cref="KmKey"/> with the kilometres.</summary>
    public static (string Key, object[] Args) Of(double blocks) =>
        Km(blocks) is var km && km < 1 ? (UnderKey, []) : (KmKey, [km]);

    /// <summary>A tier's reach in rings of grid cells (<see cref="CampLeads.Ring"/>) as kilometres:
    /// to the far edge of the last ring from the middle of the trader's cell, straight out, (reach +
    /// ½) cells, rounded to whole km: ring 1 about 3 km, 2 about 5, 3 about 7, 5 about 11.</summary>
    public static int ReachKm(int rings) => Km((Math.Max(0, rings) + 0.5) * TraderGrid.CellSize);
}
