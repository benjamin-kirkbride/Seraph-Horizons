namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>The boxes the command tool outlines a set of cells with (README "Eidolon", mining: what is out
/// of its reach, outlined in red; <c>EidolonMarkOutlines</c> draws them).</summary>
public static class MineOutlines
{
    public const int MaxBoxes = 64;

    /// <summary>Boxes covering exactly <paramref name="cells"/> (runs along x, merged along z, then y);
    /// past MaxBoxes, each layer's bounding box (over-covering).</summary>
    public static IReadOnlyList<MarkArea> Boxes(IEnumerable<MarkPos> cells, int max = MaxBoxes)
    {
        var set = cells.ToHashSet();
        if (set.Count == 0)
            return [];
        var flat = new List<(int X0, int X1, int Z0, int Z1, int Y)>();
        foreach (var layer in set.GroupBy(c => c.Y).OrderBy(g => g.Key))
        {
            var open = new List<(int X0, int X1, int Z0, int Z1)>();
            foreach (var row in layer.GroupBy(c => c.Z).OrderBy(g => g.Key))
            {
                var xs = row.Select(c => c.X).Order().ToList();
                var runs = new List<(int X0, int X1)>();
                foreach (int x in xs)
                    if (runs.Count > 0 && runs[^1].X1 == x - 1)
                        runs[^1] = (runs[^1].X0, x);
                    else
                        runs.Add((x, x));
                foreach (var (x0, x1) in runs)
                {
                    int i = open.FindIndex(b => b.X0 == x0 && b.X1 == x1 && b.Z1 == row.Key - 1);
                    if (i >= 0)
                        open[i] = open[i] with { Z1 = row.Key };
                    else
                        open.Add((x0, x1, row.Key, row.Key));
                }
            }
            flat.AddRange(open.Select(b => (b.X0, b.X1, b.Z0, b.Z1, layer.Key)));
        }
        var boxes = new List<(int X0, int X1, int Z0, int Z1, int Y0, int Y1)>();
        foreach (var b in flat.OrderBy(b => b.Y))
        {
            int i = boxes.FindIndex(o => o.X0 == b.X0 && o.X1 == b.X1 && o.Z0 == b.Z0 && o.Z1 == b.Z1 && o.Y1 == b.Y - 1);
            if (i >= 0)
                boxes[i] = boxes[i] with { Y1 = b.Y };
            else
                boxes.Add((b.X0, b.X1, b.Z0, b.Z1, b.Y, b.Y));
        }
        if (boxes.Count <= max)
            return boxes.Select(b => new MarkArea(new MarkPos(b.X0, b.Y0, b.Z0), new MarkPos(b.X1, b.Y1, b.Z1))).ToList();
        var layers = set.GroupBy(c => c.Y).OrderBy(g => g.Key)
            .Select(g => new MarkArea(new MarkPos(g.Min(c => c.X), g.Key, g.Min(c => c.Z)), new MarkPos(g.Max(c => c.X), g.Key, g.Max(c => c.Z))))
            .ToList();
        if (layers.Count <= max)
            return layers;
        return [MarkArea.Between(new MarkPos(set.Min(c => c.X), set.Min(c => c.Y), set.Min(c => c.Z)),
            new MarkPos(set.Max(c => c.X), set.Max(c => c.Y), set.Max(c => c.Z)))];
    }
}
