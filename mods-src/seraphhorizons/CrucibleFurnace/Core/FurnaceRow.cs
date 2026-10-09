using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>What a cell next to a hole is, for finding its row.</summary>
public enum FurnaceCell
{
    Other,
    Hole,
    Chimney,
}

/// <summary>Why a hole has no working stack.</summary>
public enum RowProblem
{
    None,
    /// <summary>Neither end of its row (along x or z) has a chimney block.</summary>
    NoChimney,
    /// <summary>The chimney at the end of its row is shorter than the minimum (<see cref="FurnaceRow.ChimneyHeight"/>).</summary>
    ChimneyTooShort,
    /// <summary>The row has more holes than one stack serves (<see cref="FurnaceRow.Holes"/>).</summary>
    TooManyHoles,
}

/// <summary>
/// A row of melting holes on one stack (README "Crucible furnace"): up to
/// <see cref="CrucibleFurnaceConfig.MaxHolesInRow"/> holes side by side along x or z, at one level,
/// and at one end, in line with them, the chimney's base: a chimney block level with the holes, with
/// at least <see cref="CrucibleFurnaceConfig.MinChimneyHeight"/> chimney blocks in a column from it up.
/// The flue runs under the floor from each hole to the stack, so it is not built.
/// </summary>
public sealed record FurnaceRow(IReadOnlyList<Int3> Holes, Int3? Chimney, int ChimneyHeight, RowProblem Problem)
{
    public bool Works => Problem == RowProblem.None;

    /// <summary>The highest chimney block counted for a row (a taller one is just as good).</summary>
    public const int ChimneyScan = 64;

    /// <summary>
    /// The row of the hole at <paramref name="hole"/>, <paramref name="at"/> telling what each cell is.
    /// Along x first, then z: the first axis with a chimney tall enough at either end and no more than
    /// the most holes is the row; failing that, the row along x (or z, if only it has a chimney at all)
    /// with its problem.
    /// </summary>
    public static FurnaceRow Find(Int3 hole, Func<Int3, FurnaceCell> at, int maxHoles, int minChimney)
    {
        FurnaceRow? fallback = null;
        foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
        {
            var holes = new List<Int3> { hole };
            Int3 Step(Int3 p, int k) => new(p.X + dx * k, p.Y, p.Z + dz * k);
            var lo = hole;
            while (holes.Count <= maxHoles && at(Step(lo, -1)) == FurnaceCell.Hole)
            {
                lo = Step(lo, -1);
                holes.Insert(0, lo);
            }
            var hi = hole;
            while (holes.Count <= maxHoles && at(Step(hi, 1)) == FurnaceCell.Hole)
            {
                hi = Step(hi, 1);
                holes.Add(hi);
            }
            if (holes.Count > maxHoles)
            {
                fallback ??= new FurnaceRow(holes, null, 0, RowProblem.TooManyHoles);
                continue;
            }
            FurnaceRow? best = null;
            foreach (var end in new[] { Step(lo, -1), Step(hi, 1) })
            {
                if (at(end) != FurnaceCell.Chimney)
                    continue;
                int height = 1;
                while (height < ChimneyScan && at(new Int3(end.X, end.Y + height, end.Z)) == FurnaceCell.Chimney)
                    height++;
                var row = new FurnaceRow(holes, end, height, height >= minChimney ? RowProblem.None : RowProblem.ChimneyTooShort);
                if (best == null || row.ChimneyHeight > best.ChimneyHeight)
                    best = row;
            }
            if (best is { Works: true })
                return best;
            if (best != null && (fallback == null || fallback.Problem == RowProblem.NoChimney))
                fallback = best;
            fallback ??= new FurnaceRow(holes, null, 0, RowProblem.NoChimney);
        }
        return fallback!;
    }
}
