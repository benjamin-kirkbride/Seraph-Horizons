namespace SeraphHorizons.Mod.PressBrake.Core;

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
/// θ / (2π · leverTurnsPerPlate[k]); at W = 1 the angle comes off and the half plate is used up.
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

    /// <summary>How long a player counts as working the lever after the game last said they hold
    /// right-click on it (the quern forgets a grinding player after a second).</summary>
    public const long HoldTimeoutMs = 600;

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
}

/// <summary>A half plate on the bed: its class and the fold cycle so far, W (0..1).</summary>
public readonly record struct FoldJob(int Class, double Work)
{
    public static readonly FoldJob None = new(0, 0);

    public bool On => Class is 1 or 2;

    public bool Done => On && Work >= 1 - 1e-9;

    /// <summary>Whether the half plate is still flat: nothing has been done to it (it can be taken back).</summary>
    public bool Untouched => On && Work <= 0;

    /// <summary>The fold after <paramref name="radians"/> more of the lever, W held at 1, and
    /// whether this step finished the half plate.</summary>
    public (FoldJob Job, bool Finished) Advance(double radians, double leverTurnsPerPlate)
    {
        if (!On || Done)
            return (this, false);
        var next = this with { Work = Math.Min(1, Work + Folding.PlatesFor(radians, leverTurnsPerPlate)) };
        return (next, next.Done);
    }

    /// <summary>A saved fold: W clamped to 0..1; none when the class is not a plate's.</summary>
    public static FoldJob Restore(int k, double work) =>
        k is 1 or 2 ? new FoldJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, 1) : 0) : None;

    /// <summary>The moments in <paramref name="moments"/> (fold cycle positions) that W passed going
    /// from <paramref name="before"/> to <paramref name="after"/>: a fold made in this step.</summary>
    public static int Crossed(IEnumerable<double> moments, double before, double after) =>
        moments.Count(m => before < m && after >= m);
}

/// <summary>
/// The players working the lever, as the quern keeps its grinders: each with the time the game last
/// said they held right-click on the brake. A player counts until <see cref="Folding.HoldTimeoutMs"/>
/// after that, or until they let go.
/// </summary>
public sealed class LeverHolds
{
    private readonly Dictionary<string, long> _holds = [];

    public void Hold(string player, long nowMs) => _holds[player] = nowMs;

    public void Release(string player) => _holds.Remove(player);

    public void Clear() => _holds.Clear();

    /// <summary>Forgets every player not heard from for <see cref="Folding.HoldTimeoutMs"/>, and
    /// says whether anyone is still working the lever.</summary>
    public bool Any(long nowMs)
    {
        foreach (var stale in _holds.Where(h => nowMs - h.Value > Folding.HoldTimeoutMs).Select(h => h.Key).ToList())
            _holds.Remove(stale);
        return _holds.Count > 0;
    }
}
