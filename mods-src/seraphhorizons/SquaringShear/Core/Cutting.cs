namespace SeraphHorizons.Mod.SquaringShear.Core;

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
/// is used up.
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

    /// <summary>How long a player counts as working the treadle after the game last said they hold
    /// right-click on it (the quern forgets a grinding player after a second).</summary>
    public const long HoldTimeoutMs = 600;

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
}

/// <summary>A plate on the table: its class and the cut cycle so far, W (0..1).</summary>
public readonly record struct CutJob(int Class, double Work)
{
    public static readonly CutJob None = new(0, 0);

    public bool On => Class is 1 or 2;

    public bool Done => On && Work >= 1 - 1e-9;

    /// <summary>Whether the plate is still whole: nothing has been done to it (it can be taken back).</summary>
    public bool Untouched => On && Work <= 0;

    /// <summary>The cut after <paramref name="radians"/> more of the treadle, W held at 1, and
    /// whether this step finished the plate.</summary>
    public (CutJob Job, bool Finished) Advance(double radians, double strokesPerPlate)
    {
        if (!On || Done)
            return (this, false);
        var next = this with { Work = Math.Min(1, Work + Cutting.PlatesFor(radians, strokesPerPlate)) };
        return (next, next.Done);
    }

    /// <summary>A saved cut: W clamped to 0..1; none when the class is not a plate's.</summary>
    public static CutJob Restore(int k, double work) =>
        k is 1 or 2 ? new CutJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, 1) : 0) : None;

    /// <summary>The moments in <paramref name="moments"/> (cut cycle positions) that W passed going
    /// from <paramref name="before"/> to <paramref name="after"/>: a cut made in this step.</summary>
    public static int Crossed(IEnumerable<double> moments, double before, double after) =>
        moments.Count(m => before < m && after >= m);
}

/// <summary>
/// The players working the treadle, as the quern keeps its grinders: each with the time the game last
/// said they held right-click on the shear. A player counts until <see cref="Cutting.HoldTimeoutMs"/>
/// after that, or until they let go.
/// </summary>
public sealed class TreadleHolds
{
    private readonly Dictionary<string, long> _holds = [];

    public void Hold(string player, long nowMs) => _holds[player] = nowMs;

    public void Release(string player) => _holds.Remove(player);

    public void Clear() => _holds.Clear();

    /// <summary>Forgets every player not heard from for <see cref="Cutting.HoldTimeoutMs"/>, and
    /// says whether anyone is still working the treadle.</summary>
    public bool Any(long nowMs)
    {
        foreach (var stale in _holds.Where(h => nowMs - h.Value > Cutting.HoldTimeoutMs).Select(h => h.Key).ToList())
            _holds.Remove(stale);
        return _holds.Count > 0;
    }
}
