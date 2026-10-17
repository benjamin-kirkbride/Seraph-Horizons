namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum RiddleFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>The riddle is already on the box.</summary>
    AlreadyFitted,
    /// <summary>Fewer are held than the stage takes (<see cref="RiddleParts.Needed"/>).</summary>
    TooFew,
}

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
/// boards are the frame recipe's). Recognised by full code (<c>domain:path</c>), all from the held stack
/// in one click. The fitted code is kept, so breaking returns exactly what went in; nothing else takes it
/// back out.
/// </summary>
public sealed class RiddleParts
{
    /// <summary>The mesh's wire: nails and strips of iron, meteoric iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> MeshCodes =
        ["game:metalnailsandstrips-iron", "game:metalnailsandstrips-meteoriciron", "game:metalnailsandstrips-steel"];

    /// <summary>How many the stage takes, all at once: two, the wires each way.</summary>
    public const int Needed = 2;

    private string? _mesh;

    public static bool IsPart(string? code) => Riddling.Normalise(code) is { } c && MeshCodes.Contains(c);

    /// <summary>The riddle is fitted: the station is complete.</summary>
    public bool Complete => _mesh != null;

    public string? Fitted => _mesh;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn as far as the build goes:
    /// null always; the riddle when fitted. A charge and the fines are what the station holds, never here.</summary>
    public bool Draws(string? requires) => requires == null || requires == RiddleRequires.Riddle && Complete;

    /// <summary>Whether <paramref name="code"/> can be fitted now, <paramref name="held"/> of it in hand.</summary>
    public RiddleFitVerdict CanFit(string? code, int held = int.MaxValue)
    {
        if (!IsPart(code))
            return RiddleFitVerdict.NotAPart;
        if (Complete)
            return RiddleFitVerdict.AlreadyFitted;
        return held < Needed ? RiddleFitVerdict.TooFew : RiddleFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> if the rules allow.</summary>
    public RiddleFitVerdict Fit(string? code, int held = int.MaxValue)
    {
        var verdict = CanFit(code, held);
        if (verdict == RiddleFitVerdict.Fits)
            _mesh = Riddling.Normalise(code);
        return verdict;
    }

    /// <summary>The creative shortcut's item, null when complete.</summary>
    public string? NextPart => Complete ? null : MeshCodes[0];

    /// <summary>What breaking gives back besides the frame: the fitted item and how many.</summary>
    public (string Code, int Count)? Returns() => _mesh is { } m ? (m, Needed) : null;

    /// <summary>Restored state: a code that is not the stage's part is dropped.</summary>
    public static RiddleParts Restore(string? code) => new() { _mesh = IsPart(code) ? Riddling.Normalise(code) : null };
}
