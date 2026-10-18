using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.StallRoaster.Core;

/// <summary>The stall roaster's part <c>requires</c> besides its stages' (<c>brickwork</c>, <c>lining</c>): gameplay's
/// state (StallRoaster/README.md, "Rig schema"). The frame block type lists them as its <c>extraRequires</c>.</summary>
public static class StallRequires
{
    public const string Brickwork = "brickwork", Lining = "lining";
    public const string Wood = "wood", Kindling = "kindling", Sulfur = "sulfur";
    public const string FrontHalf = "fronthalf", FrontWalled = "frontwalled", OutletCap = "outletcap";

    public static string Ore(int course) => "ore" + course;
    public static string Fire(int course) => "fire" + course;
    public static string Roasted(int course) => "roasted" + course;

    /// <summary>Every one of them, for <c>extraRequires</c> (four courses).</summary>
    public static IReadOnlyList<string> Extra(int courses) =>
    [
        Wood, Kindling, Sulfur, FrontHalf, FrontWalled, OutletCap,
        .. Enumerable.Range(1, courses).Select(Ore),
        .. Enumerable.Range(1, courses).Select(Fire),
        .. Enumerable.Range(1, courses).Select(Roasted),
    ];
}

/// <summary>
/// What gameplay reads of <c>config/stallroaster-rig.json</c> besides the shared ore machine rig's cells and parts
/// (written by StallRoaster/tools/make_shape.py; StallRoaster/README.md, "Rig schema"): the outlet's cell, the
/// charge's courses, the front's walling, the cap, the brick types, and the row's chimney rules. Throws
/// <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="OutletCell">The stack's top cell; the chimney (or the cap) on its top.</param>
/// <param name="Charge">Rock a full charge holds, as the model draws it (ore-processing.json's <c>charge</c> is the rule).</param>
/// <param name="Courses">Courses the charge is drawn as.</param>
/// <param name="PerCourse">Rock a course.</param>
/// <param name="FrontHalf">Loose brick the half-walled front takes.</param>
/// <param name="FrontWalled">Loose brick the walled-up front takes.</param>
/// <param name="CapCode">The outlet cap's item.</param>
/// <param name="CapCount">How many of it.</param>
/// <param name="Bricks">The fired brick types the brickwork (and so the front) may be.</param>
/// <param name="MinChimney">Chimney blocks counted from the outlet up a row needs.</param>
/// <param name="RowMax">Stalls side by side one chimney serves.</param>
/// <param name="Width">The stall's width along x, the row's axis.</param>
public sealed record StallRoasterRig(Int3 OutletCell, int Charge, int Courses, int PerCourse, int FrontHalf, int FrontWalled,
    string CapCode, int CapCount, BrickTypes Bricks, int MinChimney, int RowMax, int Width)
{
    /// <summary>The first cell of the chimney, over the outlet.</summary>
    public Int3 ChimneyBase => OutletCell + new Int3(0, 1, 0);

    public static StallRoasterRig Parse(string json)
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
            throw new FormatException("the cell over the outlet is the stall's: the chimney stands there");
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        if (Str(furnace, "rowAxis") != "x")
            throw new FormatException("furnace.rowAxis must be x");
        var build = Required(root, "build", JsonValueKind.Object);
        var front = Required(build, "front", JsonValueKind.Object);
        var frontMaterials = Required(front, "materials", JsonValueKind.Array);
        if (frontMaterials.GetArrayLength() != 1)
            throw new FormatException("build.front takes one material, the stall's brick");
        int walled = Int(frontMaterials[0], "quantity"), half = Int(front, "half");
        if (half < 1 || walled < half)
            throw new FormatException($"build.front: half {half} and walled up {walled} must be 1 <= half <= walled");
        var cap = Required(build, "outletCap", JsonValueKind.Object);
        var rig = new StallRoasterRig(outlet, Int(furnace, "charge"), Int(furnace, "courses"), Int(furnace, "perCourse"), half, walled,
            Str(cap, "code") ?? throw new FormatException("build.outletCap has no code"), Int(cap, "quantity"),
            BrickTypes.Parse(Required(root, "bricks", JsonValueKind.Object)),
            Int(furnace, "minChimney"), Int(furnace, "rowMax"),
            cells.Max(c => c.Pos.X) - cells.Min(c => c.Pos.X) + 1);
        if (rig.Charge < 1 || rig.Courses < 1 || rig.PerCourse < 1 || rig.Courses * rig.PerCourse < rig.Charge)
            throw new FormatException("furnace.charge, courses and perCourse must be positive, the courses holding the charge");
        if (rig.MinChimney < 1 || rig.RowMax < 1 || rig.CapCount < 1)
            throw new FormatException("furnace.minChimney, furnace.rowMax and build.outletCap.quantity must be positive");
        return rig;
    }
}
