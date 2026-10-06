namespace SeraphHorizons.Mod.Trading.Visitors.Core;

/// <summary>A block position, game-free.</summary>
public readonly record struct InnPos(int X, int Y, int Z)
{
    public InnPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public int Manhattan(InnPos o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y) + Math.Abs(Z - o.Z);

    public static readonly InnPos[] Faces =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    public InnPos Add(InnPos d) => new(X + d.X, Y + d.Y, Z + d.Z);

    public override string ToString() => $"{X},{Y},{Z}";
}

/// <summary>What a block is to the inn rules. A cell can be several (a bed is solid).</summary>
[Flags]
public enum InnCell
{
    /// <summary>Air, plants, liquids, torches: the room's flood fill passes through it.</summary>
    Open = 0,
    /// <summary>Stops the fill and holds a roof up.</summary>
    Solid = 1,
    /// <summary>A door (or trapdoor, gate): stops the fill like a wall, open or shut.</summary>
    Door = 2,
    Bed = 4,
    Table = 8,
    /// <summary>Holds something edible (a crock, a bowl, a pie, food in ground storage) or is edible.</summary>
    Food = 16,
    /// <summary>The pack's inn sign (a market stall entity is passed separately).</summary>
    Stall = 32,
}

/// <summary>The world around an inn as the rules see it.</summary>
public interface IInnArea
{
    InnCell At(InnPos p);

    /// <summary>The light the block at p gives off (0–31; a torch 14).</summary>
    int LightAt(InnPos p);
}

public sealed record InnSettings
{
    /// <summary>How far from the flag a stall is looked for, and the half-size of the box the
    /// room's fill must stay inside.</summary>
    public int Radius { get; init; } = 12;
    /// <summary>A solid block at most this high above the stall's floor cell.</summary>
    public int RoofHeight { get; init; } = 8;
    /// <summary>Block light at the stall, from lamps alone (what it has at night).</summary>
    public int MinLight { get; init; } = 7;
}

public enum InnRule { Stall, Roof, Walls, Bed, Table, Light }

/// <summary>One rule's outcome. <see cref="Reason"/> is a lang key suffix
/// (<c>trading-inn-rule-{reason}</c>), with <see cref="At"/> and <see cref="Value"/> as its arguments.</summary>
public sealed record InnCheck(InnRule Rule, bool Passed, string Reason, InnPos? At = null, int Value = 0);

public sealed class InnReport
{
    public List<InnCheck> Checks { get; } = [];
    /// <summary>The stall the inn is built around (the nearest to where it was asked), if any.</summary>
    public InnPos? Stall { get; set; }
    /// <summary>Where a visitor stands: a floor cell of the room next to the stall.</summary>
    public InnPos? SpawnAt { get; set; }
    public int RoomCells { get; set; }

    public bool Passed => Checks.Count > 0 && Checks.All(c => c.Passed);

    public InnCheck this[InnRule rule] => Checks.First(c => c.Rule == rule);

    public IEnumerable<InnRule> Failed => Checks.Where(c => !c.Passed).Select(c => c.Rule);
}

/// <summary>
/// A small inn (#456): around a stall, a room with a roof and walls, a bed, a table with food on it
/// or next to it, and lamps. The rules, kept simple and checkable:
/// <list type="bullet">
/// <item><b>Stall</b>: a market stall (Cartwright's Caravan's entity) or the pack's inn sign within
/// <see cref="InnSettings.Radius"/> of the flag; the nearest one counts.</item>
/// <item><b>Room</b>: a flood fill from the stall's cell through open cells (not solid, not a door),
/// inside a box of ± radius around the stall. <b>Walls</b>: the fill never leaves the box (a room
/// open to the outside, or with no roof, runs out of it). Doors count as wall, open or shut.
/// <b>Roof</b>: a solid block at most <see cref="InnSettings.RoofHeight"/> above the stall's cell.</item>
/// <item>"In the room" means a room cell or a block touching one, so furniture standing in it counts.
/// <b>Bed</b>: a bed in the room. <b>Table</b>: a table in the room with food in or touching it (on
/// top, or beside).</item>
/// <item><b>Light</b>: block light from lamps in the room as the game spreads it (a source's level
/// less one per block, by Manhattan distance), at the stall's cell; the sun never counts, so it is
/// the light the stall has at night.</item>
/// </list>
/// </summary>
public static class InnRules
{
    public static InnReport Evaluate(IInnArea area, InnPos origin, IEnumerable<InnPos> stallEntities, InnSettings? settings = null)
    {
        var s = settings ?? new InnSettings();
        var report = new InnReport();
        int r = s.Radius;

        InnPos? stall = null;
        int best = int.MaxValue;
        void Consider(InnPos p)
        {
            int d = p.Manhattan(origin);
            if (d < best) (best, stall) = (d, p);
        }
        foreach (var p in stallEntities)
            if (Math.Abs(p.X - origin.X) <= r && Math.Abs(p.Y - origin.Y) <= r && Math.Abs(p.Z - origin.Z) <= r)
                Consider(p);
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        for (int dz = -r; dz <= r; dz++)
        {
            var p = origin.Offset(dx, dy, dz);
            if ((area.At(p) & InnCell.Stall) != 0) Consider(p);
        }
        report.Stall = stall;
        report.Checks.Add(stall is { } sp
            ? new InnCheck(InnRule.Stall, true, "stall", sp)
            : new InnCheck(InnRule.Stall, false, "nostall", Value: r));

        var center = stall ?? origin;
        InnPos? start = null;
        foreach (var p in new[] { center, center.Offset(0, 1, 0) }.Concat(InnPos.Faces.Select(center.Add)))
            if (IsOpen(area.At(p)))
            {
                start = p;
                break;
            }
        if (start is not { } st)
        {
            report.Checks.Add(new InnCheck(InnRule.Roof, false, "noroom", center));
            report.Checks.Add(new InnCheck(InnRule.Walls, false, "noroom", center));
            report.Checks.Add(new InnCheck(InnRule.Bed, false, "nobed"));
            report.Checks.Add(new InnCheck(InnRule.Table, false, "notable"));
            report.Checks.Add(new InnCheck(InnRule.Light, false, "dark", center, 0));
            return report;
        }

        // Roof: straight up from the floor cell.
        int roof = 0;
        for (int dy = 1; dy <= s.RoofHeight; dy++)
            if ((area.At(st.Offset(0, dy, 0)) & InnCell.Solid) != 0)
            {
                roof = dy;
                break;
            }
        report.Checks.Add(roof > 0
            ? new InnCheck(InnRule.Roof, true, "roof", st, roof)
            : new InnCheck(InnRule.Roof, false, "noroof", st, s.RoofHeight));

        // Walls: the fill stays in the box.
        var room = new List<InnPos> { st };
        var seen = new HashSet<InnPos> { st };
        var queue = new Queue<InnPos>();
        queue.Enqueue(st);
        InnPos? leak = null;
        while (queue.Count > 0 && leak is null)
        {
            var p = queue.Dequeue();
            foreach (var f in InnPos.Faces)
            {
                var q = p.Add(f);
                if (seen.Contains(q) || !IsOpen(area.At(q))) continue;
                if (Math.Abs(q.X - center.X) > r || Math.Abs(q.Y - center.Y) > r || Math.Abs(q.Z - center.Z) > r)
                {
                    leak = p;
                    break;
                }
                seen.Add(q);
                room.Add(q);
                queue.Enqueue(q);
            }
        }
        report.RoomCells = room.Count;
        report.Checks.Add(leak is { } lp
            ? new InnCheck(InnRule.Walls, false, "open", lp)
            : new InnCheck(InnRule.Walls, true, "closed", st, room.Count));

        // In the room: room cells and what touches them, in fill order (nearest first).
        var inRoom = new List<InnPos>();
        var inSet = new HashSet<InnPos>();
        foreach (var p in room)
        {
            if (inSet.Add(p)) inRoom.Add(p);
            foreach (var f in InnPos.Faces)
                if (inSet.Add(p.Add(f))) inRoom.Add(p.Add(f));
        }

        var bed = inRoom.Cast<InnPos?>().FirstOrDefault(p => (area.At(p!.Value) & InnCell.Bed) != 0);
        report.Checks.Add(bed is { } bp ? new InnCheck(InnRule.Bed, true, "bed", bp) : new InnCheck(InnRule.Bed, false, "nobed"));

        var tables = inRoom.Where(p => (area.At(p) & InnCell.Table) != 0).ToList();
        var laid = tables.Cast<InnPos?>().FirstOrDefault(t =>
            (area.At(t!.Value) & InnCell.Food) != 0 || InnPos.Faces.Any(f => (area.At(t.Value.Add(f)) & InnCell.Food) != 0));
        report.Checks.Add(laid is { } tp ? new InnCheck(InnRule.Table, true, "table", tp)
            : tables.Count > 0 ? new InnCheck(InnRule.Table, false, "nofood", tables[0])
            : new InnCheck(InnRule.Table, false, "notable"));

        int light = 0;
        foreach (var p in inRoom)
        {
            int l = area.LightAt(p);
            if (l > 0) light = Math.Max(light, l - p.Manhattan(st));
        }
        report.Checks.Add(new InnCheck(InnRule.Light, light >= s.MinLight, light >= s.MinLight ? "lit" : "dark", st, light));

        // A floor cell for the visitor: open, open above, solid below; not the stall's own cell.
        report.SpawnAt = room.Cast<InnPos?>().FirstOrDefault(p => p != center && Standable(area, p!.Value))
                         ?? (Standable(area, st) ? st : null);
        return report;
    }

    public static bool IsOpen(InnCell c) => (c & (InnCell.Solid | InnCell.Door)) == 0;

    private static bool Standable(IInnArea area, InnPos p) =>
        IsOpen(area.At(p)) && IsOpen(area.At(p.Offset(0, 1, 0))) && (area.At(p.Offset(0, -1, 0)) & InnCell.Solid) != 0;
}
