namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>Why a casting can or cannot go in the cradle.</summary>
public enum BoreLoadVerdict
{
    Loads,
    NotABlank,
    /// <summary>A stage is missing (a spent cutter head counts).</summary>
    Incomplete,
    /// <summary>A casting is already in the cradle.</summary>
    Occupied,
}

/// <summary>
/// The bore's arithmetic (#639): a cast cylinder blank of class k (1 iron, the cylinder of ppex's
/// Watt engine; 2 steel, its Cornish engine's), <c>seraphhorizons:cylinderblank-{metal}</c>, chained
/// in the cradle, is bored in one traverse of the cutter head through it: W, the head's travel in
/// blocks, advances with the shaft's angle at <c>TurnsPerBlock(k)</c> axle turns a block, as the
/// gear cutter's teeth do, from 0 (the head outside the bore's drive end) to <see cref="Travel"/>
/// (its cutters past the far end), when the bored cylinder, <c>seraphhorizons:cylinder-{metal}</c>,
/// comes off.
/// </summary>
public static class Boring
{
    /// <summary>The head's travel in a bore, blocks: 18 voxels, from 0.4 outside the drive end to
    /// 0.6 past the far end of the 16-voxel cylinder (the rig's <c>work.end</c>, which the reader
    /// holds to this).</summary>
    public const double Travel = 18.0 / 16.0;

    public const string BlankIron = "seraphhorizons:cylinderblank-iron";
    public const string BlankSteel = "seraphhorizons:cylinderblank-steel";
    public const string CylinderIron = "seraphhorizons:cylinder-iron";
    public const string CylinderSteel = "seraphhorizons:cylinder-steel";

    /// <summary>The metals there are, by class: 1 iron, 2 steel.</summary>
    public static string? MetalOf(int k) => k switch { 1 => "iron", 2 => "steel", _ => null };

    /// <summary>The class a casting is bored as: 1 iron, 2 steel, 0 not a blank (a bored cylinder
    /// included: what comes off is never taken back on).</summary>
    public static int ClassOfBlank(string? code) => code switch { BlankIron => 1, BlankSteel => 2, _ => 0 };

    public static string? BlankFor(int k) => k switch { 1 => BlankIron, 2 => BlankSteel, _ => null };

    /// <summary>The bored cylinder a blank of class <paramref name="k"/> becomes.</summary>
    public static string? CylinderFor(int k) => k switch { 1 => CylinderIron, 2 => CylinderSteel, _ => null };

    /// <summary>Blocks of the head's travel for <paramref name="radians"/> of axle rotation.</summary>
    public static double BlocksFor(double radians, double turnsPerBlock) =>
        turnsPerBlock > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerBlock) : 0;

    /// <summary>Axle turns a whole bore takes at <paramref name="turnsPerBlock"/>.</summary>
    public static double TurnsPerCylinder(double turnsPerBlock) => Travel * turnsPerBlock;

    /// <summary>Whether a casting <paramref name="code"/> goes in: the borer complete
    /// (<paramref name="complete"/>) and the cradle empty.</summary>
    public static BoreLoadVerdict CanLoad(string? code, bool complete, bool occupied)
    {
        if (ClassOfBlank(code) == 0)
            return BoreLoadVerdict.NotABlank;
        if (!complete)
            return BoreLoadVerdict.Incomplete;
        return occupied ? BoreLoadVerdict.Occupied : BoreLoadVerdict.Loads;
    }

    /// <summary>Whether the bore runs: complete (a cutter head in), a casting in the cradle, the
    /// shaft at <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool complete, bool jobOn, float speed, float minSpeed) =>
        complete && jobOn && Math.Abs(speed) >= minSpeed;
}

/// <summary>A casting in the cradle: its class and the head's travel through it so far, W (blocks).</summary>
public readonly record struct BoreJob(int Class, double Work)
{
    public static readonly BoreJob None = new(0, 0);

    public bool On => Class is 1 or 2;

    /// <summary>The work's end: the head's whole travel.</summary>
    public double End => On ? Boring.Travel : 0;

    public bool Done => On && Work >= End - 1e-9;

    /// <summary>The bore after <paramref name="radians"/> more of the axle, W held at the end; and
    /// whether this step finished the cylinder.</summary>
    public (BoreJob Job, bool Finished) Advance(double radians, double turnsPerBlock)
    {
        if (!On || Done)
            return (this, false);
        double w = Math.Min(End, Work + Boring.BlocksFor(radians, turnsPerBlock));
        var next = this with { Work = w };
        return (next, next.Done);
    }

    /// <summary>A saved bore: W clamped to 0..end; none when the class is not a blank's.</summary>
    public static BoreJob Restore(int k, double work) =>
        k is 1 or 2 ? new BoreJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, Boring.Travel) : 0) : None;
}

/// <summary>
/// The cutter head's wear, as the gear cutter's kit's (#481): <c>HeadWearPerCylinder</c> points a
/// cylinder with a full oil tank, either metal, divided by the tank's fill at the moment the bore
/// finishes (half full, double; a tenth, ten times). An empty tank takes the head's whole remaining
/// durability, so it breaks on that cylinder. The shaft's load never changes with the oil.
/// </summary>
public static class CutterHeadWear
{
    /// <summary>The wear multiplier at <paramref name="fill"/> (0..1): 1 / fill; infinite when empty.</summary>
    public static double Multiplier(double fill) => fill <= 0 ? double.PositiveInfinity : 1 / Math.Min(1, fill);

    /// <summary>Points a cylinder costs a head with <paramref name="left"/> points left at
    /// <paramref name="fill"/>: base / fill, rounded up, at most what is left; all of it when the
    /// tank is empty.</summary>
    public static int WearFor(int basePerCylinder, double fill, int left)
    {
        if (left <= 0)
            return 0;
        if (fill <= 0)
            return left;
        double wear = Math.Max(0, basePerCylinder) * Multiplier(fill);
        return (int)Math.Min(left, Math.Ceiling(wear - 1e-9));
    }

    /// <summary>Cylinders the head still bores at a steady <paramref name="fill"/>: 1 when the tank
    /// is empty (the next breaks it after boring).</summary>
    public static int CylindersLeft(int basePerCylinder, double fill, int left)
    {
        if (left <= 0)
            return 0;
        if (fill <= 0)
            return 1;
        int per = WearFor(basePerCylinder, fill, int.MaxValue);
        if (per <= 0)
            return int.MaxValue;
        return (left + per - 1) / per;
    }
}
