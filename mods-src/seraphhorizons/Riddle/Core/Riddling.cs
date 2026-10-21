using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>Why crushed ore can or cannot go on the riddle as a charge.</summary>
public enum RiddleLoadVerdict
{
    Loads,
    /// <summary>Not the pack's crushed ore.</summary>
    NotCrushedOre,
    /// <summary>Crushed ore already classified: riddling it again does nothing.</summary>
    AlreadyClassified,
    /// <summary>The riddle is not fitted on the box yet.</summary>
    Incomplete,
    /// <summary>A charge (or its oversize) is still on the riddle.</summary>
    Occupied,
    /// <summary>The box still holds the last charge's fines: they are taken out first (the owner's
    /// ruling, 2026-10-14).</summary>
    BoxFull,
    /// <summary>Fewer are held than a charge takes.</summary>
    TooFew,
}

/// <summary>
/// The riddle's rules (#714; Riddle/README.md): a charge is <see cref="RiddleConfig.LumpsPerCharge"/>
/// crushed ore of one kind (<c>seraphhorizons:crushedore-{ore}-{grade}</c>), put on the riddle in one
/// click, and riddled by the player holding right-click, as on the quern. While held, the riddle's clock
/// θ turns a shake every <see cref="ShakeSeconds"/>, and the riddling W (0..1) advances by
/// θ / (2π · shakesPerCharge). At W = 1 the fines are in the box, classified crushed ore, kept there until
/// the player takes them out; the oversize stays on the mesh until taken out, and goes back to spalling.
/// No metal is lost: every item of the charge comes back, as fines or as oversize. The charge on the
/// riddle is a <see cref="MachineJob"/> of class 1 ending at 1 (<see cref="Start"/>).
/// </summary>
public static class Riddling
{
    public const string CrushedOrePrefix = "seraphhorizons:crushedore-";

    /// <summary>A shake every 0.75 s while held, the model viewer's pace: a charge of twelve shakes
    /// takes 9 seconds at the default settings.</summary>
    public const double ShakeSeconds = 0.75;

    /// <summary>Radians of the riddle's clock a second while held.</summary>
    public const double ShakeRadiansPerSecond = 2 * Math.PI / ShakeSeconds;

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>Whether <paramref name="code"/> is the pack's crushed ore, any ore and grade.</summary>
    public static bool IsCrushedOre(string? code) =>
        Normalise(code) is { } c && c.StartsWith(CrushedOrePrefix, StringComparison.Ordinal) && c.Length > CrushedOrePrefix.Length;

    /// <summary>Charges riddled by <paramref name="radians"/> of the clock.</summary>
    public static double ChargesFor(double radians, double shakesPerCharge) =>
        shakesPerCharge > 0 ? Math.Max(0, radians) / (2 * Math.PI * shakesPerCharge) : 0;

    /// <summary>
    /// Whether crushed ore <paramref name="code"/> goes on as a charge, <paramref name="held"/> of it in
    /// hand and <paramref name="classified"/> whether it already is: the riddle fitted
    /// (<paramref name="complete"/>), nothing on the riddle (<paramref name="occupied"/>: a charge or
    /// its oversize) and the box empty (<paramref name="boxFull"/>).
    /// </summary>
    public static RiddleLoadVerdict CanLoad(string? code, bool classified, int held, int lumps, bool complete, bool occupied, bool boxFull)
    {
        if (!IsCrushedOre(code))
            return RiddleLoadVerdict.NotCrushedOre;
        if (classified)
            return RiddleLoadVerdict.AlreadyClassified;
        if (!complete)
            return RiddleLoadVerdict.Incomplete;
        if (occupied)
            return RiddleLoadVerdict.Occupied;
        if (boxFull)
            return RiddleLoadVerdict.BoxFull;
        return held < lumps ? RiddleLoadVerdict.TooFew : RiddleLoadVerdict.Loads;
    }

    /// <summary>
    /// Whether a right-click with crushed ore in hand goes round again (the owner's ask, 2026-10-20): the charge on the riddle
    /// riddled (<paramref name="riddling"/> false), something of it waiting to come out (<paramref name="waiting"/>:
    /// its oversize on the mesh or its fines in the box), and the held ore a whole charge that goes on once
    /// they are out. Then the oversize and the fines go to the player and the next charge goes on, so
    /// holding right-click riddles charge after charge without letting go or emptying the hand.
    /// </summary>
    public static bool CanReload(string? code, bool classified, int held, int lumps, bool complete, bool riddling, bool waiting) =>
        !riddling && waiting && CanLoad(code, classified, held, lumps, complete, false, false) == RiddleLoadVerdict.Loads;

    /// <summary>Whether the riddling runs: complete, a charge on not yet riddled, and someone holding.</summary>
    public static bool Running(bool complete, bool chargeOn, bool done, bool held) => complete && chargeOn && !done && held;

    /// <summary>A charge of <paramref name="lumps"/> riddled: how many come through as fines (to the box,
    /// classified) and how many stay on the mesh as oversize. Every item comes back one way or the
    /// other; the oversize is held between none and the whole charge.</summary>
    public static (int Fines, int Oversize) Split(int lumps, int oversize)
    {
        lumps = Math.Max(0, lumps);
        oversize = Math.Clamp(oversize, 0, lumps);
        return (lumps - oversize, oversize);
    }

    /// <summary>A charge just put on the riddle (W ends at 1).</summary>
    public static MachineJob Start() => MachineJob.Start(1, 1);

    /// <summary>The riddling after <paramref name="radians"/> more of the clock, W held at 1, and whether
    /// this step finished the charge.</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double shakesPerCharge) =>
        job.Advance(ChargesFor(radians, shakesPerCharge));

    /// <summary>A saved charge: W clamped to 0..1; none without a charge.</summary>
    public static MachineJob Restore(bool on, double work) => on ? MachineJob.Restore(1, work, 1) : MachineJob.None;

    /// <summary>The shakes (whole turns of the clock) completed between W <paramref name="before"/> and
    /// <paramref name="after"/>: a shake's sound for each.</summary>
    public static int ShakesCrossed(double before, double after, double shakesPerCharge) =>
        shakesPerCharge > 0 ? Math.Max(0, (int)Math.Floor(after * shakesPerCharge + 1e-9) - (int)Math.Floor(before * shakesPerCharge + 1e-9)) : 0;
}
