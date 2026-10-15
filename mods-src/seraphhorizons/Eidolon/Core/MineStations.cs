namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>The strikes within a station (README "Eidolon", mining, *Stations*): which group of up to four
/// it strikes next from where it stands, by the same rules as before stations (<see cref="EidolonMining.Next"/>,
/// <see cref="EidolonTunnels.LaneFront"/>).</summary>
public static class MineStations
{
    /// <summary>The group it strikes next of a station from where it stands: in a tunnel lane the
    /// lowest slice first, its top half before its bottom half, two columns across, upright (as
    /// EidolonTunnels.LaneFront); elsewhere the highest layer first, flat or upright as
    /// EidolonMining.Next (the most cells, flat on ties). Only cells left and in reach.</summary>
    public static MineGroup? NextGroup(MineStation station, MineTunnel? tunnel, Func<MarkPos, bool> left,
        double x, int feet, double z)
    {
        var cells = station.Cells.ToHashSet();
        bool Takes(MarkPos c) => cells.Contains(c) && left(c) && EidolonMining.InReach(x, feet, z, c);
        if (station.Kind == MineStationKind.Face && tunnel != null && station.Lane >= 0 && station.Lane < tunnel.Lanes.Count)
        {
            int top = tunnel.Top - station.Band * EidolonTunnels.HeadingHeight;
            var band = tunnel with { Top = top, Bottom = top - EidolonTunnels.HeadingHeight + 1 };
            return EidolonTunnels.LaneFront(band, tunnel.Lanes[station.Lane], Takes)?.Group;
        }
        var live = station.Cells.Where(Takes).ToList();
        if (live.Count == 0)
            return null;
        var area = MarkArea.Between(new MarkPos(live.Min(c => c.X), live.Min(c => c.Y), live.Min(c => c.Z)),
            new MarkPos(live.Max(c => c.X), live.Max(c => c.Y), live.Max(c => c.Z)));
        foreach (int layer in live.Select(c => c.Y).Distinct().OrderDescending())
            if (EidolonMining.Next(area, layer, Takes, x, feet, z) is { } group)
                return group;
        return null;
    }
}
