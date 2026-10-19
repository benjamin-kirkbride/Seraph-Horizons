using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// What every fired machine's rig says of its flue and brickwork (the crucible furnace's and the fired ore
/// machines': the stall roaster, the reverberatory roaster, the cupellation and liquation furnaces, the Parkes
/// kettle, the acid parting vessel): the outlet's cell, with the chimney (or the cap) on its top, the cap and
/// how many of it, the fired brick types the brickwork may be, and the row's rules (<see cref="FurnaceRow"/>).
/// </summary>
/// <param name="OutletCell">The stack's top cell; the chimney (or the cap) on its top.</param>
/// <param name="CapCode">The outlet cap's item.</param>
/// <param name="CapCount">How many of it.</param>
/// <param name="Bricks">The fired brick types the brickwork may be.</param>
/// <param name="MinChimney">Chimney blocks counted from the outlet up a row needs.</param>
/// <param name="RowMax">Machines side by side one chimney serves.</param>
/// <param name="Width">The machine's width along x, the row's axis: a neighbour's controller stands this far along.</param>
public sealed record FurnaceFlue(Int3 OutletCell, string CapCode, int CapCount, BrickTypes Bricks, int MinChimney, int RowMax, int Width)
{
    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => OutletCell + new Int3(0, 1, 0);

    /// <summary>
    /// The flue of a fired ore machine's rig: <c>outletFace</c> up, <c>outletCell</c> a cell of the footprint with
    /// none over it (the chimney stands there), <c>build.outletCap</c>, <c>bricks</c>, and <paramref name="figures"/>'s
    /// (<c>furnace</c>, the acid parting vessel's <c>parting</c>) <c>minChimney</c>, <c>rowMax</c> and <c>rowAxis</c>
    /// (x). <paramref name="whose"/> names the machine in the messages (<c>kettle</c>). Throws
    /// <see cref="FormatException"/> naming what is wrong.
    /// </summary>
    public static FurnaceFlue Parse(JsonElement root, IReadOnlyList<RigCell> cells, string figures = "furnace", string whose = "furnace")
    {
        if (Str(root, "outletFace") != "up")
            throw new FormatException("outletFace must be \"up\"");
        var outlet = Int3Of(Required(root, "outletCell", JsonValueKind.Array), "outletCell");
        if (cells.All(c => c.Pos != outlet))
            throw new FormatException($"outletCell {outlet} is not a cell of the footprint");
        if (cells.Any(c => c.Pos == outlet + new Int3(0, 1, 0)))
            throw new FormatException($"the cell over the outlet is the {whose}'s: the chimney stands there");
        var f = Required(root, figures, JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        if (Str(f, "rowAxis") != "x")
            throw new FormatException($"{figures}.rowAxis must be x");
        var cap = Required(Required(root, "build", JsonValueKind.Object), "outletCap", JsonValueKind.Object);
        var flue = new FurnaceFlue(outlet, Str(cap, "code") ?? throw new FormatException("build.outletCap has no code"), Int(cap, "quantity"),
            BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object)), Int(f, "minChimney"), Int(f, "rowMax"),
            cells.Max(c => c.Pos.X) - cells.Min(c => c.Pos.X) + 1);
        if (flue.MinChimney < 1 || flue.RowMax < 1 || flue.CapCount < 1)
            throw new FormatException($"{figures}.minChimney, {figures}.rowMax and build.outletCap.quantity must be positive");
        return flue;
    }
}

/// <summary>A fired ore machine's rig: its <see cref="Flue"/>, whose figures it shows as its own.</summary>
public abstract record FiredRig(FurnaceFlue Flue)
{
    public Int3 OutletCell => Flue.OutletCell;
    public Int3 ChimneyBase => Flue.ChimneyBase;
    public string CapCode => Flue.CapCode;
    public int CapCount => Flue.CapCount;
    public BrickTypes Bricks => Flue.Bricks;
    public int MinChimney => Flue.MinChimney;
    public int RowMax => Flue.RowMax;
    public int Width => Flue.Width;
}
