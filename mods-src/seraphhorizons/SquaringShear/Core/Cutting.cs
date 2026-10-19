using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.SquaringShear.Core;

/// <summary>The squaring shear's <c>requires</c> vocabulary: its two build stages (<c>config/builds/squaringshear.json</c>),
/// and the work's (<c>platelead</c>, <c>platecopper</c>: a plate of that metal on the table).</summary>
public static class SquaringShearRequires
{
    public const string Blade = "blade";
    public const string Gauge = "gauge";
    public const string PlateLead = "platelead";
    public const string PlateCopper = "platecopper";

    /// <summary>The plate <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Plate(int k) => k switch { 1 => PlateLead, 2 => PlateCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { Blade, Gauge, PlateLead, PlateCopper };

    /// <summary>The metal of a fitted part's code (<c>game:rod-steel</c> is <c>steel</c>): a blade plate's or a
    /// gauge rod's, the two drawn in their metal; null for anything else.</summary>
    public static string? MetalOf(string? code) =>
        code is "game:metalplate-iron" or "game:metalplate-steel" or "game:rod-iron" or "game:rod-meteoriciron" or "game:rod-steel"
            ? code[(code.LastIndexOf('-') + 1)..]
            : null;
}

/// <summary>Why a plate can or cannot go on the table.</summary>
public enum CutLoadVerdict
{
    Loads,
    NotAPlate,
    /// <summary>A stage is missing.</summary>
    Incomplete,
    /// <summary>A plate is already on the table.</summary>
    Occupied,
}

/// <summary>
/// The cut's arithmetic: a plate of class k (1 lead, 2 copper) goes on the table and is cut once,
/// across its middle, into <see cref="HalfPlatesPerPlate"/> half plates
/// (<c>seraphhorizons:halfplate-{metal}</c>) by the player working the treadle, holding right-click as
/// on the quern. While held, the treadle clock θ turns at <see cref="StrokesPerSecond"/>, and the cut
/// cycle W (0..1) advances by θ / (2π · strokesPerPlate[k]); at W = 1 the halves come off and the plate
/// is used up. The plate on the table is a <see cref="MachineJob"/> ending at 1 (<see cref="Start"/>).
/// </summary>
public static class Cutting
{
    public const int HalfPlatesPerPlate = 2;
    public const string LeadPlate = "game:metalplate-lead";
    public const string CopperPlate = "game:metalplate-copper";

    /// <summary>The treadle clock's pace while the player holds right-click: one stroke a second, so a
    /// lead plate takes 1 second and a copper one 1.5 at the default settings.</summary>
    public const double StrokesPerSecond = 1;

    /// <summary>Radians of the treadle clock a second while held.</summary>
    public const double StrokeRadiansPerSecond = 2 * Math.PI * StrokesPerSecond;

    /// <summary>The class of a metal: 1 lead, 2 copper, 0 else.</summary>
    public static int ClassOfMetal(string? metal) => metal switch { "lead" => 1, "copper" => 2, _ => 0 };

    public static string? MetalOf(int k) => k switch { 1 => "lead", 2 => "copper", _ => null };

    /// <summary>The class a plate is cut as: 1 lead, 2 copper, 0 not one the shear cuts (a half plate
    /// included: what comes off is never cut again).</summary>
    public static int ClassOfPlate(string? code) => code switch { LeadPlate => 1, CopperPlate => 2, _ => 0 };

    public static string? PlateFor(int k) => k switch { 1 => LeadPlate, 2 => CopperPlate, _ => null };

    /// <summary>The half plate a plate of class <paramref name="k"/> is cut into.</summary>
    public static string? HalfPlateFor(int k) => MetalOf(k) is { } metal ? "seraphhorizons:halfplate-" + metal : null;

    /// <summary>Plates cut by <paramref name="radians"/> of the treadle clock.</summary>
    public static double PlatesFor(double radians, double strokesPerPlate) =>
        strokesPerPlate > 0 ? Math.Max(0, radians) / (2 * Math.PI * strokesPerPlate) : 0;

    /// <summary>Whether a plate <paramref name="code"/> goes on: the shear complete
    /// (<paramref name="complete"/>) and nothing on its table.</summary>
    public static CutLoadVerdict CanLoad(string? code, bool complete, bool occupied)
    {
        if (ClassOfPlate(code) == 0)
            return CutLoadVerdict.NotAPlate;
        if (!complete)
            return CutLoadVerdict.Incomplete;
        return occupied ? CutLoadVerdict.Occupied : CutLoadVerdict.Loads;
    }

    /// <summary>Whether the cut runs: complete, a plate on, and someone working the treadle.</summary>
    public static bool Running(bool complete, bool plateOn, bool held) => complete && plateOn && held;

    /// <summary>The cut of a plate of class <paramref name="k"/> just laid on the table (W ends at 1); none for
    /// a class the shear does not cut.</summary>
    public static MachineJob Start(int k) => MachineJob.Start(k is 1 or 2 ? k : 0, 1);

    /// <summary>A saved cut: W clamped to 0..1; none when the class is not a plate's.</summary>
    public static MachineJob Restore(int k, double work) => MachineJob.Restore(k is 1 or 2 ? k : 0, work, 1);

    /// <summary>The cut after <paramref name="radians"/> more of the treadle at <paramref name="strokesPerPlate"/>,
    /// W held at 1, and whether this step finished the plate.</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double strokesPerPlate) =>
        job.Advance(PlatesFor(radians, strokesPerPlate));
}
