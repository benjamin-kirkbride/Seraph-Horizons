using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.SlittingMill.Core;

/// <summary>Why a plate can or cannot go on the feed table.</summary>
public enum SlitLoadVerdict
{
    Loads,
    NotAPlate,
    /// <summary>A stage is missing (a spent or missing knife counts).</summary>
    Incomplete,
    /// <summary>A plate is already on the table.</summary>
    Occupied,
}

/// <summary>
/// The slitting's arithmetic: one of the game's metal plates (<c>game:metalplate-{metal}</c>, any of
/// <see cref="Metals"/>) laid on the feed table is drawn through the slitters into eight rods, cut into
/// nails by the knife and its last length pushed out as strips (the model's one class, a plate whatever its
/// metal: k is 1 with a plate on). W counts knife strokes since the plate went in and advances with the shaft
/// at <c>TurnsPerStroke</c> axle turns a stroke; the job ends at <see cref="HandOut"/> (5), when the nails
/// and the strips, the game's <c>metalnailsandstrips-{metal}</c>, are taken off the nail box. The metal only
/// changes the load on the shaft and the wear on the knife (<see cref="SlittingMillConfig"/>).
/// </summary>
public static class Slitting
{
    /// <summary>Knife strokes a plate takes: the hand-out, a whole stroke (the rig's <c>slit.handOut</c> and
    /// <c>work.end.thin</c>, which the reader holds to this).</summary>
    public const double HandOut = 5;

    /// <summary>The whole strokes by which a nail has been parted off every rod (the rig's <c>slit.cuts</c>).</summary>
    public static readonly IReadOnlyList<double> Cuts = [3, 4];

    public const string PlatePrefix = "game:metalplate-";
    public const string OutputPrefix = "game:metalnailsandstrips-";

    /// <summary>The metals the mill slits, each a plate the game has: every metal the game's nail recipe
    /// (<c>recipes/smithing/nails.json</c>) allows that has a plate (bismuth has none).</summary>
    public static readonly IReadOnlyList<string> Metals =
    [
        "copper", "tinbronze", "bismuthbronze", "blackbronze", "brass", "silver", "gold",
        "iron", "meteoriciron", "steel", "molybdochalkos", "electrum",
    ];

    /// <summary>The metal of a plate the mill takes; null for anything else (the nails and strips that come
    /// off, an ingot, a lead plate, which makes no nails).</summary>
    public static string? MetalOfPlate(string? code) =>
        code != null && code.StartsWith(PlatePrefix, StringComparison.Ordinal) && Metals.Contains(code[PlatePrefix.Length..])
            ? code[PlatePrefix.Length..]
            : null;

    public static string PlateFor(string metal) => PlatePrefix + metal;

    /// <summary>The nails and strips a plate of <paramref name="metal"/> becomes.</summary>
    public static string OutputFor(string metal) => OutputPrefix + metal;

    /// <summary>Knife strokes made by <paramref name="radians"/> of axle rotation (either way).</summary>
    public static double StrokesFor(double radians, double turnsPerStroke) =>
        turnsPerStroke > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerStroke) : 0;

    /// <summary>Axle turns a plate takes at <paramref name="turnsPerStroke"/>.</summary>
    public static double TurnsPerPlate(double turnsPerStroke) => HandOut * turnsPerStroke;

    /// <summary>Whether a plate <paramref name="code"/> goes on: the mill ready (built, its knife in) and the
    /// table empty.</summary>
    public static SlitLoadVerdict CanLoad(string? code, bool ready, bool occupied)
    {
        if (MetalOfPlate(code) == null)
            return SlitLoadVerdict.NotAPlate;
        if (!ready)
            return SlitLoadVerdict.Incomplete;
        return occupied ? SlitLoadVerdict.Occupied : SlitLoadVerdict.Loads;
    }

    /// <summary>Whether the mill runs: ready, a plate on, the shaft at <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool ready, bool jobOn, float speed, float minSpeed) =>
        ready && jobOn && Math.Abs(speed) >= minSpeed;

    /// <summary>The slitting of a plate just laid on: class 1 (the rig's one class), ending at the hand-out.</summary>
    public static MachineJob Start() => MachineJob.Start(1, HandOut);

    /// <summary>A saved slitting: W clamped to 0..5; none when its class is not 1.</summary>
    public static MachineJob Restore(int k, double work) => MachineJob.Restore(k == 1 ? 1 : 0, work, HandOut);

    /// <summary>The nails parted off every rod so far: one for each of <see cref="Cuts"/> W has reached.</summary>
    public static int CutsDone(MachineJob job) => job.On ? Cuts.Count(c => job.Work >= c - 1e-9) : 0;

    /// <summary>The slitting after <paramref name="radians"/> more of the axle, W held at the end, and whether
    /// this step finished the plate (W reaching the hand-out).</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double turnsPerStroke) =>
        job.Advance(StrokesFor(radians, turnsPerStroke));
}
