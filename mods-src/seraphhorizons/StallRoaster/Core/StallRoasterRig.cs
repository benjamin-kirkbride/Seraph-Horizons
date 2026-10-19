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
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork (and so the front) may be, and the row's chimney rules.</param>
/// <param name="Charge">Rock a full charge holds, as the model draws it (ore-processing.json's <c>charge</c> is the rule).</param>
/// <param name="Courses">Courses the charge is drawn as.</param>
/// <param name="PerCourse">Rock a course.</param>
/// <param name="FrontHalf">Loose brick the half-walled front takes.</param>
/// <param name="FrontWalled">Loose brick the walled-up front takes.</param>
public sealed record StallRoasterRig(FurnaceFlue Flue, int Charge, int Courses, int PerCourse, int FrontHalf, int FrontWalled)
    : FiredRig(Flue)
{
    public static StallRoasterRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var flue = FurnaceFlue.Parse(root, Cells(root), whose: "stall");
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        var build = Required(root, "build", JsonValueKind.Object);
        var front = Required(build, "front", JsonValueKind.Object);
        var frontMaterials = Required(front, "materials", JsonValueKind.Array);
        if (frontMaterials.GetArrayLength() != 1)
            throw new FormatException("build.front takes one material, the stall's brick");
        int walled = Int(frontMaterials[0], "quantity"), half = Int(front, "half");
        if (half < 1 || walled < half)
            throw new FormatException($"build.front: half {half} and walled up {walled} must be 1 <= half <= walled");
        var rig = new StallRoasterRig(flue, Int(furnace, "charge"), Int(furnace, "courses"), Int(furnace, "perCourse"), half, walled);
        if (rig.Charge < 1 || rig.Courses < 1 || rig.PerCourse < 1 || rig.Courses * rig.PerCourse < rig.Charge)
            throw new FormatException("furnace.charge, courses and perCourse must be positive, the courses holding the charge");
        return rig;
    }
}
