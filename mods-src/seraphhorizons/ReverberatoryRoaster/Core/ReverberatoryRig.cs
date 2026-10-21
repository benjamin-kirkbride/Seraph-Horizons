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
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork may be (its brick texture), and the row's chimney rules (two furnaces, back to back).</param>
/// <param name="AshSide">The native face of the controller's cell the ash mouth and its damper are in.</param>
/// <param name="Partner">The controller of the furnace sharing this one's west wall, in this one's native frame (it faces
/// the other way).</param>
public sealed record ReverberatoryRig(FurnaceFlue Flue, Side AshSide, Int3 Partner) : FiredRig(Flue)
{
    public static ReverberatoryRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var cells = Cells(root);
        if (!Sides.TryParse(Str(root, "ashSide"), out var ash))
            throw new FormatException("ashSide is missing or not a side");
        var flue = FurnaceFlue.Parse(root, cells);
        if (Str(Required(root, "furnace", JsonValueKind.Object), "rowFacing") != "opposite")
            throw new FormatException("furnace.rowAxis must be x and rowFacing opposite: two back to back");
        // back to back on the west: the partner's footprint mirrors this one's across the west wall's outer face,
        // turned half round, so its controller is as far west of that face as this one's is east of it, at the far end
        int minX = cells.Min(c => c.Pos.X), maxZ = cells.Max(c => c.Pos.Z), minZ = cells.Min(c => c.Pos.Z);
        return new ReverberatoryRig(flue, ash, new Int3(2 * minX - 1, 0, maxZ + minZ));
    }
}
