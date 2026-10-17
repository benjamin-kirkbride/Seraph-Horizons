using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.ReverberatoryRoaster.Core;

/// <summary>The reverberatory roaster's part <c>requires</c> drawn by gameplay's state, not by a stage (ReverberatoryRoaster/
/// README.md, "Rig schema"): the bed on the hearth, the fire, the outlet's cap; the damper's places, which the rig lists
/// as the doors' fit's states; and the frame block type's <c>extraRequires</c>.</summary>
public static class ReverberatoryRequires
{
    public const string Doors = "doors", Charge = "charge", Fire = "fire", OutletCap = "outletcap";

    public static string DamperPart(Damper d) => "damper" + d.ToString().ToLowerInvariant();

    /// <summary>The frame block type's <c>extraRequires</c>.</summary>
    public static IReadOnlyList<string> Extra => [Charge, Fire, OutletCap];
}

/// <summary>
/// What gameplay reads of <c>config/reverberatory-rig.json</c> besides the shared ore machine rig's cells, anchors and parts
/// (written by ReverberatoryRoaster/tools/make_shape.py; its README, "Rig schema"): the outlet's cell, the ash mouth's face
/// (the damper), the cap, the brick types, and the row: two furnaces back to back sharing their west walls, the second
/// turned half round, one chimney. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="OutletCell">The outlet's cell; the chimney (or the cap) on its top.</param>
/// <param name="AshSide">The native face of the controller's cell the ash mouth and its damper are in.</param>
/// <param name="CapCode">The outlet cap's item.</param>
/// <param name="CapCount">How many of it.</param>
/// <param name="Bricks">The fired brick types the brickwork may be (its brick texture).</param>
/// <param name="MinChimney">Chimney blocks counted from the outlet up the row needs.</param>
/// <param name="RowMax">Furnaces one chimney serves (2, back to back).</param>
/// <param name="Partner">The controller of the furnace sharing this one's west wall, in this one's native frame (it faces
/// the other way).</param>
public sealed record ReverberatoryRig(Int3 OutletCell, Side AshSide, string CapCode, int CapCount, BrickTypes Bricks,
    int MinChimney, int RowMax, Int3 Partner)
{
    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => OutletCell + new Int3(0, 1, 0);

    public static ReverberatoryRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        if (Str(root, "outletFace") != "up")
            throw new FormatException("outletFace must be \"up\"");
        var cells = Cells(root);
        var outlet = Int3Of(Required(root, "outletCell", JsonValueKind.Array), "outletCell");
        if (cells.All(c => c.Pos != outlet))
            throw new FormatException($"outletCell {outlet} is not a cell of the footprint");
        if (cells.Any(c => c.Pos == outlet + new Int3(0, 1, 0)))
            throw new FormatException("the cell over the outlet is the furnace's: the chimney stands there");
        if (!Sides.TryParse(Str(root, "ashSide"), out var ash))
            throw new FormatException("ashSide is missing or not a side");
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        if (Str(furnace, "rowAxis") != "x" || Str(furnace, "rowFacing") != "opposite")
            throw new FormatException("furnace.rowAxis must be x and rowFacing opposite: two back to back");
        var cap = Required(Required(root, "build", JsonValueKind.Object), "outletCap", JsonValueKind.Object);
        // back to back on the west: the partner's footprint mirrors this one's across the west wall's outer face,
        // turned half round, so its controller is as far west of that face as this one's is east of it, at the far end
        int minX = cells.Min(c => c.Pos.X), maxZ = cells.Max(c => c.Pos.Z), minZ = cells.Min(c => c.Pos.Z);
        var partner = new Int3(2 * minX - 1, 0, maxZ + minZ);
        var rig = new ReverberatoryRig(outlet, ash, Str(cap, "code") ?? throw new FormatException("build.outletCap has no code"),
            Int(cap, "quantity"), BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object)),
            Int(furnace, "minChimney"), Int(furnace, "rowMax"), partner);
        if (rig.MinChimney < 1 || rig.RowMax < 1 || rig.CapCount < 1)
            throw new FormatException("furnace.minChimney, furnace.rowMax and build.outletCap.quantity must be positive");
        return rig;
    }
}
