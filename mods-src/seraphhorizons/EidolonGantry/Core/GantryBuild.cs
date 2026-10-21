using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.EidolonGantry.Core;

/// <summary>A fitted item as breaking the gantry gives it back: its code and how many of it the stage took.</summary>
public readonly record struct GantryDrop(string Code, int Count);

/// <summary>The gantry rig's <c>requires</c> vocabulary: the nine winch stages (its build file's,
/// <c>config/builds/eidolongantry.json</c>), then the seven body stages (the eidolon's,
/// <c>config/eidolon-stages.json</c>), which ride the spine.</summary>
public static class GantryRequires
{
    /// <summary>The winch's stages after the frame, in the only order they go in (README "The build"),
    /// each a <c>requires</c> of the rig and a stage of the build file; the spine is the last.</summary>
    public static readonly IReadOnlyList<string> WinchStages =
        ["axles", "crankshaft", "gears", "drum", "strapping", "ratchet", "crank", "chain", "spine"];

    /// <summary>The eidolon's body stages in build order, each a rig <c>requires</c>: drawn only by the
    /// body's rules (<c>GantryBody.Shows</c>), never the build's.</summary>
    public static readonly IReadOnlyList<string> BodyStages = ["torso", "bones", "joints", "plates", "sash", "skirt", "mind"];

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = WinchStages.Concat(BodyStages).ToHashSet();

    public static bool IsBodyStage(string? name) => name != null && BodyStages.Contains(name);
}

/// <summary>
/// The gantry's woods and its winch's build as a gantry of one wood takes it. The winch is the shared
/// staged build (<c>config/builds/eidolongantry.json</c>, <see cref="MachineBuild"/>): nine stages in order,
/// each taking its whole count from the held stack in one click; the iron parts iron, meteoric iron or
/// steel, as the woodworking machines take them. The drum's planks and the spine's support beams are of the
/// gantry's own wood: the build file writes them with <see cref="WoodToken"/>, which <see cref="ForWood"/>
/// fills in.
/// </summary>
public static class GantryBuild
{
    /// <summary>The machine's id: its build file's, its frame's first code part.</summary>
    public const string MachineId = "eidolongantry";

    /// <summary>The woods a gantry comes in: the game's <c>block/wood</c> property's, which its
    /// support beams and planks come in.</summary>
    public static readonly IReadOnlyList<string> Woods =
        ["birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony", "walnut", "purpleheart"];

    /// <summary>Stands for the gantry's wood in a build file's code (<c>game:plank-{wood}</c>).</summary>
    public const string WoodToken = "{wood}";

    /// <summary>The build as a gantry of <paramref name="wood"/> takes it: every <see cref="WoodToken"/>
    /// in a material's codes made that wood.</summary>
    public static MachineBuild ForWood(MachineBuild build, string wood) =>
        build with
        {
            Stages = build.Stages.Select(s => s with
            {
                Materials = s.Materials.Select(m => m with { Codes = m.Codes.Select(c => c.Replace(WoodToken, wood)).ToList() }).ToList(),
            }).ToList(),
        };
}
