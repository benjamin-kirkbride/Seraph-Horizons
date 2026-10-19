using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.LiquationFurnace.Core;

/// <summary>The liquation furnace's part <c>requires</c> besides its stages' (<c>brickwork</c>, <c>lining</c>,
/// <c>fittings</c>): gameplay's state (LiquationFurnace/README.md, "Rig schema"). The frame block type lists them as
/// its <c>extraRequires</c>.</summary>
public static class LiquationRequires
{
    public const string Brickwork = "brickwork", Lining = "lining", Fittings = "fittings";
    public const string Cakes = "cakes", TinRun = "tinrun", Tin = "tin", Lead = "lead", OutletCap = "outletcap";

    public static string Fuel(int layer) => "fuel" + layer;
    public static string Fire(int layer) => "fire" + layer;

    public static string DamperPart(Damper d) => "damper" + d.ToString().ToLowerInvariant();

    /// <summary>Every one of them, for <c>extraRequires</c>.</summary>
    public static IReadOnlyList<string> Extra(int fuelLayers) =>
    [
        Cakes, TinRun, Tin, Lead, OutletCap,
        .. Enum.GetValues<Damper>().Select(DamperPart),
        .. Enumerable.Range(1, fuelLayers).Select(Fuel),
        .. Enumerable.Range(1, fuelLayers).Select(Fire),
    ];
}

/// <summary>
/// What gameplay reads of <c>config/liquationfurnace-rig.json</c> besides the shared ore machine rig's cells and parts
/// (written by LiquationFurnace/tools/make_shape.py; LiquationFurnace/README.md, "Rig schema"): the outlet's cell, the
/// back face the fire door and the ash mouth are on, the fuel's layers, the cap, the brick types, and the row's
/// chimney rules. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork may be, and the row's chimney rules.</param>
/// <param name="BackSide">The face the fire door and the ash mouth are on (native).</param>
/// <param name="FrontSide">The face the plate is worked from (native).</param>
/// <param name="FireDoorY">The fire door's middle, block units up from the ground.</param>
/// <param name="AshY">The ash mouth's middle.</param>
/// <param name="FuelLayers">Layers the fuel on the grate is drawn as.</param>
/// <param name="Cakes">Cakes the model draws on the plate.</param>
public sealed record LiquationFurnaceRig(FurnaceFlue Flue, Side BackSide, Side FrontSide, double FireDoorY, double AshY, int FuelLayers,
    int Cakes) : FiredRig(Flue)
{
    /// <summary>Whether a click at <paramref name="y"/> (block units up from the ground) on the back face is the ash
    /// mouth's (its damper), not the fire door's: under the midpoint between them.</summary>
    public bool IsAsh(double y) => y < (FireDoorY + AshY) / 2;

    public static LiquationFurnaceRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var flue = FurnaceFlue.Parse(root, Cells(root));
        Side SideAt(string key) => Sides.TryParse(Str(root, key), out var s) ? s : throw new FormatException($"{key} is not a side");
        double Y(string key)
        {
            var pos = Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array);
            return pos.GetArrayLength() == 3 ? pos[1].GetDouble() : throw new FormatException($"{key}.pos is not a point");
        }
        var back = SideAt("fireDoorSide");
        if (SideAt("ashSide") != back)
            throw new FormatException("the fire door and the ash mouth must be on one face");
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        var rig = new LiquationFurnaceRig(flue, back, SideAt("plateSide"), Y("fireDoor"), Y("ash"), Int(furnace, "fuelLayers"),
            Int(furnace, "cakes"));
        if (rig.FrontSide == rig.BackSide)
            throw new FormatException("the plate is worked from the face opposite the fire door");
        if (rig.FuelLayers < 1 || rig.Cakes < 1)
            throw new FormatException("furnace.fuelLayers and cakes must be positive");
        if (!(rig.AshY < rig.FireDoorY))
            throw new FormatException("the ash mouth must be under the fire door");
        return rig;
    }
}
