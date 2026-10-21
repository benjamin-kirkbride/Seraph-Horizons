namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>The riddle's <c>requires</c> vocabulary: its one stage after the frame, and what it holds
/// (the rig's <c>contents</c>): a charge on the mesh, fines in the box.</summary>
public static class RiddleRequires
{
    public const string Riddle = "riddle";
    public const string Charge = "charge";
    public const string Fines = "fines";

    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { Riddle, Charge, Fines };
}

/// <summary>
/// The riddle's build (Riddle/README.md, "Fitted parts"): the frame (the plank box with its iron corner
/// straps and the two oak bearers) is placed, and the one stage after it, the riddle on its bearers, is
/// fitted by right-click with <see cref="Needed"/> iron nails and strips, its woven mesh (the riddle's oak
/// boards are the frame recipe's), all from the held stack in one click. The stage is
/// <c>config/builds/riddle.json</c>, kept by the shared machine base, which keeps the fitted code, so
/// breaking returns exactly what went in; nothing else takes it back out.
/// </summary>
public static class RiddleBuild
{
    /// <summary>The mesh's wire: nails and strips of iron, meteoric iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> MeshCodes =
        ["game:metalnailsandstrips-iron", "game:metalnailsandstrips-meteoriciron", "game:metalnailsandstrips-steel"];

    /// <summary>How many the stage takes, all at once: two, the wires each way.</summary>
    public const int Needed = 2;
}
