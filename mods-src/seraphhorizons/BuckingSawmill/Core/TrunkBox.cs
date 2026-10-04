namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>How a loaded trunk is shown: none, thin (Logging Expanded's sizes xs to lg, one block
/// across) or thick (xl and xxl, two blocks across).</summary>
public enum TrunkClass { None, Thin, Thick }

/// <summary>
/// The trunk as the mill shows it, whatever its real size: a thin trunk as Logging Expanded's
/// <c>lg</c> model (1×1×4 blocks), a thick one as its <c>xxl</c> model (2×2×5), lying on the bed.
/// The same box is the trunk's collision and selection box, clipped into the mill's cells. Only
/// the display changes: the stored stack stays as it was loaded.
/// </summary>
public static class TrunkBox
{
    public const string ThinDisplaySize = "lg";
    public const string ThickDisplaySize = "xxl";

    /// <summary>The class of a Logging Expanded <c>size</c> variant: <c>xl</c> and <c>xxl</c> are
    /// thick, everything else thin.</summary>
    public static TrunkClass ClassOf(string? size) => size is "xl" or "xxl" ? TrunkClass.Thick : TrunkClass.Thin;

    /// <summary>The size variant whose model is shown for a class; null for none.</summary>
    public static string? DisplaySize(TrunkClass trunk) => trunk switch
    {
        TrunkClass.Thin => ThinDisplaySize,
        TrunkClass.Thick => ThickDisplaySize,
        _ => null,
    };

    /// <summary>The shown model's length along the bed, width across it and height, in blocks.</summary>
    public static (int Length, int Width, int Height) Size(TrunkClass trunk) => trunk switch
    {
        TrunkClass.Thin => (4, 1, 1),
        TrunkClass.Thick => (5, 2, 2),
        _ => (0, 0, 0),
    };

    /// <summary>The trunk's box on <paramref name="bed"/>, native-frame blocks: centred on the bed's
    /// origin along and across it, its underside on the bed.</summary>
    public static (Float3 Min, Float3 Max) Bounds(TrunkBed bed, TrunkClass trunk)
    {
        var (length, width, height) = Size(trunk);
        float along = length / 2f, across = width / 2f;
        var o = bed.Origin;
        return bed.Axis == Axis.Z
            ? (new Float3(o.X - across, o.Y, o.Z - along), new Float3(o.X + across, o.Y + height, o.Z + along))
            : (new Float3(o.X - along, o.Y, o.Z - across), new Float3(o.X + along, o.Y + height, o.Z + across));
    }

    /// <summary>
    /// The trunk's box clipped into each of <paramref name="cells"/> it passes through, cell-local
    /// (native frame). A cell with nothing of its own above it reaches up into that cell too, as
    /// the game finds a block's boxes up to a block above it when it collides: the mill leaves the
    /// cells over the bed empty where no frame is, and the trunk there is then the cell's below.
    /// Parts of the trunk in an empty cell with no mill cell under it get no box.
    /// </summary>
    public static IReadOnlyDictionary<Int3, Box> CellBoxes(IEnumerable<Int3> cells, (Float3 Min, Float3 Max) trunk)
    {
        var set = cells.ToHashSet();
        var boxes = new Dictionary<Int3, Box>();
        var (min, max) = trunk;
        foreach (var c in set)
        {
            int reach = set.Contains(new Int3(c.X, c.Y + 1, c.Z)) ? 1 : 2;
            float x1 = Math.Max(min.X, c.X), y1 = Math.Max(min.Y, c.Y), z1 = Math.Max(min.Z, c.Z);
            float x2 = Math.Min(max.X, c.X + 1), y2 = Math.Min(max.Y, c.Y + reach), z2 = Math.Min(max.Z, c.Z + 1);
            if (x2 - x1 > 1e-4f && y2 - y1 > 1e-4f && z2 - z1 > 1e-4f)
                boxes[c] = new Box(x1 - c.X, y1 - c.Y, z1 - c.Z, x2 - c.X, y2 - c.Y, z2 - c.Z);
        }
        return boxes;
    }

    /// <summary>Whether a native-frame point is on or in the trunk's box (with a hair to spare, for a
    /// hit on its face).</summary>
    public static bool Contains((Float3 Min, Float3 Max) trunk, Float3 p, float slack = 1e-3f) =>
        p.X >= trunk.Min.X - slack && p.X <= trunk.Max.X + slack
        && p.Y >= trunk.Min.Y - slack && p.Y <= trunk.Max.Y + slack
        && p.Z >= trunk.Min.Z - slack && p.Z <= trunk.Max.Z + slack;
}
