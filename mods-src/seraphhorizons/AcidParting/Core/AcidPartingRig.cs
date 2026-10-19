using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.AcidParting.Core;

/// <summary>The acid parting vessel's part <c>requires</c> besides its stages' (<c>brickwork</c>, <c>lining</c>,
/// <c>sandbath</c>, <c>vesseltub</c>, <c>services</c>): gameplay's state (AcidParting/README.md, "Rig schema"). The
/// frame block type lists them as its <c>extraRequires</c>.</summary>
public static class VesselRequires
{
    public const string DamperOpen = "damperopen", DamperHalf = "damperhalf", DamperShut = "dampershut";
    public const string Fuel = "fuel", Fire = "fire";
    public const string Charge = "charge", Acid = "acid", Solution = "solution", Gold = "gold", Wash = "wash";
    public const string Copper = "copper", Liquor = "liquor", Silver = "silver";
    public const string OutletCap = "outletcap";

    /// <summary>Every one of them, for <c>extraRequires</c>; <see cref="Copper"/> is the wearing part's (the build file's
    /// <c>wearing</c>), listed so the rig knows it.</summary>
    public static IReadOnlyList<string> Extra =>
    [
        DamperOpen, DamperHalf, DamperShut, Fuel, Fire, Charge, Acid, Solution, Gold, Wash, Copper, Liquor, Silver, OutletCap,
    ];

    /// <summary>The damper's part for its place.</summary>
    public static string Of(Damper d) => d switch
    {
        Damper.Open => DamperOpen,
        Damper.Half => DamperHalf,
        _ => DamperShut,
    };
}

/// <summary>
/// What gameplay reads of <c>config/acidparting-rig.json</c> besides the shared ore machine rig's cells and parts
/// (written by AcidParting/tools/make_shape.py; AcidParting/README.md, "Rig schema"): the vessel's and the tub's
/// mouths (a click in the hood's cell is the nearer one's), the fire door and the ash mouth, the outlet's cell, the
/// cap, the brick types and the row's chimney rules. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="Vessel">The middle of the vessel's mouth, native block units.</param>
/// <param name="Tub">The middle of the tub's mouth.</param>
/// <param name="FireDoor">The middle of the fire door's opening.</param>
/// <param name="Ash">The middle of the ash mouth.</param>
/// <param name="HoodCell">The hood's cell, where the vessel and the tub are worked.</param>
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork may be, and the row's chimney rules.</param>
public sealed record AcidPartingRig(FurnaceFlue Flue, Float3 Vessel, Float3 Tub, Float3 FireDoor, Float3 Ash, Int3 HoodCell)
    : FiredRig(Flue)
{
    /// <summary>Whether a native point (in block units from the controller's corner) is nearer the tub's mouth than
    /// the vessel's: the crucible furnace's two pots' rule.</summary>
    public bool NearerTub(Float3 p) => Dist2(p, Tub) < Dist2(p, Vessel);

    /// <summary>Whether a native point is nearer the ash mouth than the fire door (a click on the controller's front).</summary>
    public bool NearerAsh(Float3 p) => Dist2(p, Ash) < Dist2(p, FireDoor);

    private static float Dist2(Float3 a, Float3 b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    public static AcidPartingRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var flue = FurnaceFlue.Parse(root, Cells(root), "parting", "vessel");
        var hood = Int3Of(Required(root, "vesselCell", JsonValueKind.Array), "vesselCell");
        if (Int3Of(Required(root, "tubCell", JsonValueKind.Array), "tubCell") != hood)
            throw new FormatException("tubCell must be vesselCell: both are worked through the hood's front");
        Float3 Point(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key);
        return new AcidPartingRig(flue, Point("vessel"), Point("tub"), Point("firedoor"), Point("ash"), hood);
    }
}
