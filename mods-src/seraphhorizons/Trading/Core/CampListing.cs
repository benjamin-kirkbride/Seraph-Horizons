namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>One cell's camp as <c>/sh trade camps</c> shows it.</summary>
public readonly record struct CampRow(CellKey Cell, string Type, CampStatus Status, int X, int? Y, int Z, double Distance, string Region);

/// <summary>
/// The rows and text of <c>/sh trade camps</c>: every cell within the radius, nearest first, with
/// its type, and either the placed camp or, for a cell not decided yet, the spot it waits for next
/// (an open cell, whose spots all missed, and a failed one show the cell's middle).
/// Positions are shown as players see them, relative to the map's middle (<c>offsetX</c>,
/// <c>offsetZ</c>), like the game's own coordinates.
/// </summary>
public static class CampListing
{
    public static List<CampRow> Rows(TraderGrid grid, CampRegistry registry, int x, int z, int radius)
    {
        var rows = new List<CampRow>();
        foreach (var cell in TraderGrid.CellsAround(x, z, radius))
        {
            var spots = grid.Spots(cell);
            var record = registry.Get(cell);
            string type = grid.TypeOf(cell);
            CampRow row;
            if (record is { Status: CampStatus.Placed })
                row = new CampRow(cell, record.Type, CampStatus.Placed, record.X, record.Y, record.Z, 0, record.Region);
            else if (CampRegistry.NextSpot(record, spots.Count) is { } next)
                row = new CampRow(cell, type, CampStatus.Pending, spots[next].X, null, spots[next].Z, 0, "");
            else
            {
                var c = Centre(cell);
                var status = record is { Status: CampStatus.Open } ? CampStatus.Open : CampStatus.Failed;
                row = new CampRow(cell, type, status, c.X, null, c.Z, 0, "");
            }
            double d = Math.Sqrt((double)(row.X - x) * (row.X - x) + (double)(row.Z - z) * (row.Z - z));
            if (d <= radius) rows.Add(row with { Distance = d });
        }
        return rows.OrderBy(r => r.Distance).ThenBy(r => r.Cell.X).ThenBy(r => r.Cell.Z).ToList();
    }

    /// <summary>A row as text: <paramref name="text"/> is the game's Lang.Get, given the lang keys
    /// <c>trading-camp-{placed,pending,open,failed}</c> and <c>trading-type-{type}</c>.</summary>
    public static string Line(CampRow row, int offsetX, int offsetZ, Func<string, object[], string> text)
    {
        string where = row.Y is { } y
            ? $"{row.X - offsetX}, {y}, {row.Z - offsetZ}"
            : $"{row.X - offsetX}, {row.Z - offsetZ}";
        string type = text("seraphhorizons:trading-type-" + row.Type, []);
        string distance = row.Distance.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return row.Status switch
        {
            CampStatus.Placed => text("seraphhorizons:trading-camp-placed", [row.Cell.ToString(), type, where, row.Region, distance]),
            CampStatus.Pending => text("seraphhorizons:trading-camp-pending", [row.Cell.ToString(), type, where, distance]),
            CampStatus.Open => text("seraphhorizons:trading-camp-open", [row.Cell.ToString(), type, distance]),
            _ => text("seraphhorizons:trading-camp-failed", [row.Cell.ToString(), type, distance]),
        };
    }

    private static (int X, int Z) Centre(CellKey cell) =>
        (cell.X * TraderGrid.CellSize + TraderGrid.CellSize / 2, cell.Z * TraderGrid.CellSize + TraderGrid.CellSize / 2);
}
