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
/// <param name="OutletCell">The stack's top cell; the chimney (or the cap) on its top.</param>
/// <param name="CapCode">The outlet cap's item.</param>
/// <param name="CapCount">How many of it.</param>
/// <param name="Bricks">The fired brick types the brickwork may be.</param>
/// <param name="MinChimney">Chimney blocks counted from the outlet up a row needs.</param>
/// <param name="RowMax">Vessels side by side one chimney serves.</param>
/// <param name="Width">The vessel's width along x, the row's axis.</param>
public sealed record AcidPartingRig(Float3 Vessel, Float3 Tub, Float3 FireDoor, Float3 Ash, Int3 HoodCell, Int3 OutletCell,
    string CapCode, int CapCount, BrickTypes Bricks, int MinChimney, int RowMax, int Width)
{
    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => OutletCell + new Int3(0, 1, 0);

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
        if (Str(root, "outletFace") != "up")
            throw new FormatException("outletFace must be \"up\"");
        var cells = Cells(root);
        var outlet = Int3Of(Required(root, "outletCell", JsonValueKind.Array), "outletCell");
        if (cells.All(c => c.Pos != outlet))
            throw new FormatException($"outletCell {outlet} is not a cell of the footprint");
        if (cells.Any(c => c.Pos == outlet + new Int3(0, 1, 0)))
            throw new FormatException("the cell over the outlet is the vessel's: the chimney stands there");
        var hood = Int3Of(Required(root, "vesselCell", JsonValueKind.Array), "vesselCell");
        if (Int3Of(Required(root, "tubCell", JsonValueKind.Array), "tubCell") != hood)
            throw new FormatException("tubCell must be vesselCell: both are worked through the hood's front");
        Float3 Point(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key);
        var parting = Required(root, "parting", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        if (Str(parting, "rowAxis") != "x")
            throw new FormatException("parting.rowAxis must be x");
        var cap = Required(Required(root, "build", JsonValueKind.Object), "outletCap", JsonValueKind.Object);
        var rig = new AcidPartingRig(Point("vessel"), Point("tub"), Point("firedoor"), Point("ash"), hood, outlet,
            Str(cap, "code") ?? throw new FormatException("build.outletCap has no code"), Int(cap, "quantity"),
            BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object)),
            Int(parting, "minChimney"), Int(parting, "rowMax"),
            cells.Max(c => c.Pos.X) - cells.Min(c => c.Pos.X) + 1);
        if (rig.MinChimney < 1 || rig.RowMax < 1 || rig.CapCount < 1)
            throw new FormatException("parting.minChimney, parting.rowMax and build.outletCap.quantity must be positive");
        return rig;
    }
}
