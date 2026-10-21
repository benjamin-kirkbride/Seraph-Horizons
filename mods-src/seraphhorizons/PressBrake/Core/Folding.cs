using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.PressBrake.Core;

/// <summary>The press brake's <c>requires</c> vocabulary: its two build stages (<c>config/builds/pressbrake.json</c>),
/// and the work's (<c>platelead</c>, <c>platecopper</c>: a half plate of that metal on the bed).</summary>
public static class PressBrakeRequires
{
    public const string Screws = "screws";
    public const string Edge = "edge";
    public const string PlateLead = "platelead";
    public const string PlateCopper = "platecopper";

    /// <summary>The plate <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Plate(int k) => k switch { 1 => PlateLead, 2 => PlateCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { Screws, Edge, PlateLead, PlateCopper };

    /// <summary>The metal of the edges' plate (<c>game:metalplate-steel</c> is <c>steel</c>); null for anything else.</summary>
    public static string? EdgeMetal(string? code) =>
        code is "game:metalplate-iron" or "game:metalplate-steel" ? code[(code.LastIndexOf('-') + 1)..] : null;
}

/// <summary>Why a half plate can or cannot go on the bed.</summary>
public enum FoldLoadVerdict
{
    Loads,
    /// <summary>Not a lead or copper half plate (a whole plate included: the squaring shear halves it first).</summary>
    NotAPlate,
    /// <summary>A stage is missing.</summary>
    Incomplete,
    /// <summary>A half plate is already on the bed.</summary>
    Occupied,
}

/// <summary>
/// The fold's arithmetic: a half plate of class k (1 lead, 2 copper; <c>seraphhorizons:halfplate-{metal}</c>,
/// cut on the squaring shear) goes on the bed and is bent once across its middle, at a right angle, into
/// <see cref="AnglesPerPlate"/> angle (<c>seraphhorizons:angle-{metal}</c>) by the player working the
/// lever, holding right-click as on the quern. While held, the lever clock θ turns at
/// <see cref="LeverTurnsPerSecond"/>, and the fold cycle W (0..1) advances by
/// θ / (2π · leverTurnsPerPlate[k]); at W = 1 the angle comes off and the half plate is used up. The
/// half plate on the bed is a <see cref="MachineJob"/> ending at 1 (<see cref="Start"/>).
/// "Plate" in the names here is the brake's work, a half plate.
/// </summary>
public static class Folding
{
    public const int AnglesPerPlate = 1;
    public const string LeadHalfPlate = "seraphhorizons:halfplate-lead";
    public const string CopperHalfPlate = "seraphhorizons:halfplate-copper";

    /// <summary>The lever clock's pace while the player holds right-click: one turn a second, so a
    /// lead half plate takes 1.5 seconds and a copper one 2.25 at the default settings.</summary>
    public const double LeverTurnsPerSecond = 1;

    /// <summary>Radians of the lever clock a second while held.</summary>
    public const double LeverRadiansPerSecond = 2 * Math.PI * LeverTurnsPerSecond;

    /// <summary>The class of a metal: 1 lead, 2 copper, 0 else.</summary>
    public static int ClassOfMetal(string? metal) => metal switch { "lead" => 1, "copper" => 2, _ => 0 };

    public static string? MetalOf(int k) => k switch { 1 => "lead", 2 => "copper", _ => null };

    /// <summary>The class a half plate is folded as: 1 lead, 2 copper, 0 not one the brake folds (a
    /// whole plate, an angle or a hollow section included: what comes off is never taken back on).</summary>
    public static int ClassOfPlate(string? code) => code switch { LeadHalfPlate => 1, CopperHalfPlate => 2, _ => 0 };

    /// <summary>The half plate of class <paramref name="k"/>.</summary>
    public static string? PlateFor(int k) => k switch { 1 => LeadHalfPlate, 2 => CopperHalfPlate, _ => null };

    /// <summary>The angle a half plate of class <paramref name="k"/> is folded into.</summary>
    public static string? AngleFor(int k) => MetalOf(k) is { } metal ? "seraphhorizons:angle-" + metal : null;

    /// <summary>Half plates folded by <paramref name="radians"/> of the lever clock.</summary>
    public static double PlatesFor(double radians, double leverTurnsPerPlate) =>
        leverTurnsPerPlate > 0 ? Math.Max(0, radians) / (2 * Math.PI * leverTurnsPerPlate) : 0;

    /// <summary>Whether a half plate <paramref name="code"/> goes on: the brake complete
    /// (<paramref name="complete"/>) and nothing on its bed.</summary>
    public static FoldLoadVerdict CanLoad(string? code, bool complete, bool occupied)
    {
        if (ClassOfPlate(code) == 0)
            return FoldLoadVerdict.NotAPlate;
        if (!complete)
            return FoldLoadVerdict.Incomplete;
        return occupied ? FoldLoadVerdict.Occupied : FoldLoadVerdict.Loads;
    }

    /// <summary>Whether the fold runs: complete, a half plate on, and someone working the lever.</summary>
    public static bool Running(bool complete, bool plateOn, bool held) => complete && plateOn && held;

    /// <summary>The fold of a half plate of class <paramref name="k"/> just laid on the bed (W ends at 1); none
    /// for a class the brake does not fold.</summary>
    public static MachineJob Start(int k) => MachineJob.Start(k is 1 or 2 ? k : 0, 1);

    /// <summary>A saved fold: W clamped to 0..1; none when the class is not a plate's.</summary>
    public static MachineJob Restore(int k, double work) => MachineJob.Restore(k is 1 or 2 ? k : 0, work, 1);

    /// <summary>The fold after <paramref name="radians"/> more of the lever at <paramref name="leverTurnsPerPlate"/>,
    /// W held at 1, and whether this step finished the half plate.</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double leverTurnsPerPlate) =>
        job.Advance(PlatesFor(radians, leverTurnsPerPlate));
}
