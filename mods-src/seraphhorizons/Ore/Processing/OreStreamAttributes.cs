using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// An item's <see cref="OreStream"/> on its stack's attributes (#765), for every station and machine on the line:
/// what was done to the rock (<c>oreState</c>, <see cref="OreState"/> flags: classified 1, ground 2,
/// amalgamated 4), its units where they differ from its grade's (<c>oreUnits</c>), its locked units once split
/// (<c>oreLocked</c>), the concentrator kinds that worked it (<c>oreWorked</c>), its host rock (<c>rock</c>), and
/// for an item whose code doesn't name them its ore and grade (<c>ore</c>, <c>grade</c>). A concentrate off the
/// whole ideal route is marked <c>oreWhole</c> (<see cref="ConcentratorPass.Whole"/>). Stacks that differ in
/// any of these don't merge, which is intended: classified and unclassified rock are different feed.
/// </summary>
public static class OreStreamAttributes
{
    public const string State = "oreState", Units = "oreUnits", Locked = "oreLocked", Worked = "oreWorked",
        Rock = ItemOreMap.AttrRock, Ore = "ore", Grade = "grade", Whole = "oreWhole";

    public static OreState StateOf(ItemStack stack) => (OreState)stack.Attributes.GetInt(State);

    /// <summary>Sets flags on a stack (the riddle's <see cref="OreState.Classified"/>, the amalgam pan's
    /// <see cref="OreState.Amalgamated"/>). A stream already split is lifted by <see cref="OreRecovery.Lift"/>
    /// instead, through <see cref="Read"/> and <see cref="Write"/>.</summary>
    public static void AddState(ItemStack stack, OreState added) =>
        stack.Attributes.SetInt(State, (int)(StateOf(stack) | added));

    public static bool IsWhole(ItemStack stack) => stack.Attributes.GetBool(Whole);

    public static void MarkWhole(ItemStack stack, bool whole)
    {
        if (whole) stack.Attributes.SetBool(Whole, true);
        else stack.Attributes.RemoveAttribute(Whole);
    }

    /// <summary>The form of an ore item: the pack's rock forms by their <c>oreForm</c>, vanilla's ore item raw.</summary>
    public static OreForm? FormOf(CollectibleObject item) => item.Attributes?["oreForm"].AsString(null) switch
    {
        "crushed" => OreForm.Crushed,
        "ground" => OreForm.Ground,
        "roastedrock" or "stallroastedrock" => OreForm.RoastedRock,
        null when item.FirstCodePart() == "ore" && OreProducts.IsGrade(item.Variant["grade"]) => OreForm.Raw,
        _ => null,
    };

    /// <summary>
    /// The stream a stack carries: its ore and grade from its code (or the attributes), its units from
    /// <c>oreUnits</c> or its grade's (<see cref="OreProducts.RawUnits"/>), the rest from the attributes. Ground
    /// ore is <see cref="OreState.Ground"/> by its form. Null for a stack that names no ore.
    /// </summary>
    public static OreStream? Read(ItemStack stack)
    {
        var item = stack.Collectible;
        var a = stack.Attributes;
        string? ore = item.Variant["ore"] ?? a.GetString(Ore);
        if (ore == null) return null;
        string grade = item.Variant["grade"] ?? a.GetString(Grade) ?? "-";
        var state = StateOf(stack);
        if (FormOf(item) == OreForm.Ground) state |= OreState.Ground;
        double units = a.HasAttribute(Units) ? a.GetDouble(Units) : OreProducts.RawUnits(ore, grade) ?? 0;
        double? locked = a.HasAttribute(Locked) ? a.GetDouble(Locked) : null;
        string? rock = item.Variant["rock"] ?? a.GetString(Rock);
        return new OreStream(ore, grade, units, locked, state, (WorkedBy)a.GetInt(Worked), rock);
    }

    /// <summary>Writes a stream onto a stack, leaving out what its code already says (ore, grade, units equal to
    /// the grade's).</summary>
    public static void Write(ItemStack stack, OreStream s)
    {
        var item = stack.Collectible;
        var a = stack.Attributes;
        if (item.Variant["ore"] != s.Ore) a.SetString(Ore, s.Ore); else a.RemoveAttribute(Ore);
        if (item.Variant["grade"] != s.Grade) a.SetString(Grade, s.Grade); else a.RemoveAttribute(Grade);
        var state = s.State;
        if (FormOf(item) == OreForm.Ground) state &= ~OreState.Ground;
        if (state != OreState.None) a.SetInt(State, (int)state); else a.RemoveAttribute(State);
        bool gradeUnits = item.Variant["grade"] == s.Grade && OreProducts.RawUnits(s.Ore, s.Grade) is { } raw && Math.Abs(raw - s.Units) < 1e-9;
        if (!gradeUnits) a.SetDouble(Units, s.Units); else a.RemoveAttribute(Units);
        if (s.Locked is { } locked) a.SetDouble(Locked, locked); else a.RemoveAttribute(Locked);
        if (s.Worked != WorkedBy.None) a.SetInt(Worked, (int)s.Worked); else a.RemoveAttribute(Worked);
        if (s.Rock != null && item.Variant["rock"] != s.Rock) a.SetString(Rock, s.Rock); else a.RemoveAttribute(Rock);
    }
}
