namespace SeraphHorizons.Mod.GearCutter.Core;

/// <summary>Why a blank can or cannot go on the arbor.</summary>
public enum GearCutterBlankVerdict
{
    Loads,
    NotABlank,
    /// <summary>A stage is missing (the kit spent counts).</summary>
    Incomplete,
    /// <summary>A blank is already on the arbor.</summary>
    Occupied,
    /// <summary>The blank is the other size from the master fitted.</summary>
    WrongSize,
}

/// <summary>
/// The cut's arithmetic (#480, "Work"): a blank of class k (1 the small blank, 12 teeth; 2 the large,
/// 20) is cut one tooth per <c>TurnsPerTooth</c> axle turns, its progress W in teeth advancing with
/// the shaft's angle as the mill's saws do, so a small gear takes 144 turns and a large one 240 at
/// the default 12. The master fitted decides which blank the arbor takes.
/// </summary>
public static class GearCut
{
    public const string Blank = "seraphhorizons:gearblank-stainlesssteel";
    public const string LargeBlank = "seraphhorizons:largegearblank-stainlesssteel";
    public const string Gear = "seraphhorizons:gear-stainless";
    public const string LargeGear = "seraphhorizons:largegear-stainless";

    /// <summary>Teeth a gear of class <paramref name="k"/> gets: 12 small, 20 large, 0 else.</summary>
    public static int Teeth(int k) => k switch { 1 => 12, 2 => 20, _ => 0 };

    /// <summary>Axle turns a whole gear of class <paramref name="k"/> takes.</summary>
    public static double TurnsPerGear(int k, double turnsPerTooth) => Teeth(k) * turnsPerTooth;

    /// <summary>The class a blank is cut to: 1 small, 2 large, 0 not a blank.</summary>
    public static int ClassOfBlank(string? code) => code switch
    {
        Blank => 1,
        LargeBlank => 2,
        _ => 0,
    };

    public static string? BlankFor(int k) => k switch { 1 => Blank, 2 => LargeBlank, _ => null };

    /// <summary>The gear a blank of class <paramref name="k"/> becomes.</summary>
    public static string? GearFor(int k) => k switch { 1 => Gear, 2 => LargeGear, _ => null };

    /// <summary>Teeth cut by <paramref name="radians"/> of axle rotation.</summary>
    public static double TeethFor(double radians, double turnsPerTooth) =>
        turnsPerTooth > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerTooth) : 0;

    /// <summary>Whether a blank <paramref name="code"/> goes on: the machine complete
    /// (<paramref name="complete"/>), the arbor empty, and the blank the master's size.</summary>
    public static GearCutterBlankVerdict CanLoad(string? code, bool complete, bool occupied, int master)
    {
        int k = ClassOfBlank(code);
        if (k == 0)
            return GearCutterBlankVerdict.NotABlank;
        if (!complete)
            return GearCutterBlankVerdict.Incomplete;
        if (occupied)
            return GearCutterBlankVerdict.Occupied;
        return k == master ? GearCutterBlankVerdict.Loads : GearCutterBlankVerdict.WrongSize;
    }

    /// <summary>Whether the cut runs: complete (kit and master in), a blank on, the shaft at
    /// <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool complete, bool blankOn, float speed, float minSpeed) =>
        complete && blankOn && Math.Abs(speed) >= minSpeed;
}

/// <summary>A blank on the arbor: its class and the teeth cut so far, W.</summary>
public readonly record struct CutJob(int Class, double Work)
{
    public static readonly CutJob None = new(0, 0);

    public bool On => Class is 1 or 2;

    /// <summary>The teeth this gear gets, the work's end.</summary>
    public int End => GearCut.Teeth(Class);

    public bool Done => On && Work >= End - 1e-9;

    /// <summary>The cut after <paramref name="radians"/> more of the axle, W held at the end; and
    /// whether this step finished the gear.</summary>
    public (CutJob Job, bool Finished) Advance(double radians, double turnsPerTooth)
    {
        if (!On || Done)
            return (this, false);
        double w = Math.Min(End, Work + GearCut.TeethFor(radians, turnsPerTooth));
        var next = this with { Work = w };
        return (next, next.Done);
    }

    /// <summary>A saved cut: W clamped to 0..end; none when the class is not a blank's.</summary>
    public static CutJob Restore(int k, double work) =>
        k is 1 or 2 ? new CutJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, GearCut.Teeth(k)) : 0) : None;
}

/// <summary>
/// The cutter kit's wear (#481): <c>CutterWearPerGear</c> points per small gear with a full oil
/// tank, divided by the tank's fill at the moment the gear finishes (half full, double; a tenth, ten
/// times), a large gear in proportion to its teeth (20/12). An empty tank takes the kit's whole
/// remaining durability, so it breaks on that gear. Shaft load never changes with the oil.
/// </summary>
public static class GearCutterWear
{
    /// <summary>The wear multiplier at <paramref name="fill"/> (0..1): 1 / fill; infinite when empty.</summary>
    public static double Multiplier(double fill) => fill <= 0 ? double.PositiveInfinity : 1 / Math.Min(1, fill);

    /// <summary>Points a gear of <paramref name="teeth"/> costs a kit with <paramref name="left"/>
    /// points left at <paramref name="fill"/>: base × teeth / 12 / fill, rounded up, at most what is
    /// left; all of it when the tank is empty.</summary>
    public static int WearFor(int basePerGear, int teeth, double fill, int left)
    {
        if (left <= 0)
            return 0;
        if (fill <= 0)
            return left;
        double wear = Math.Max(0, basePerGear) * (teeth / 12.0) * Multiplier(fill);
        return (int)Math.Min(left, Math.Ceiling(wear - 1e-9));
    }

    /// <summary>Gears of <paramref name="teeth"/> the kit still cuts at a steady
    /// <paramref name="fill"/>: 0 when the tank is empty (the next breaks it after cutting).</summary>
    public static int GearsLeft(int basePerGear, int teeth, double fill, int left)
    {
        if (left <= 0)
            return 0;
        int per = WearFor(basePerGear, teeth, fill, int.MaxValue);
        if (fill <= 0 || per <= 0)
            return fill <= 0 ? 1 : int.MaxValue;
        return (left + per - 1) / per;
    }
}
