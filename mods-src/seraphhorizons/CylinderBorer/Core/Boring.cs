using SeraphHorizons.Mod.Machines.Core;

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

    /// <summary>The bore of a casting of class <paramref name="k"/> just put in the cradle: a <see cref="MachineJob"/>
    /// ending at the head's whole travel; none for a class the borer does not bore.</summary>
    public static MachineJob Start(int k) => MachineJob.Start(k is 1 or 2 ? k : 0, Travel);

    /// <summary>A saved bore: W clamped to 0..the travel; none when the class is not a blank's.</summary>
    public static MachineJob Restore(int k, double work) => MachineJob.Restore(k is 1 or 2 ? k : 0, work, Travel);

    /// <summary>The bore after <paramref name="radians"/> more of the axle, W held at the end, and whether this step
    /// finished the cylinder.</summary>
    public static (MachineJob Job, bool Finished) Advance(MachineJob job, double radians, double turnsPerBlock) =>
        job.Advance(BlocksFor(radians, turnsPerBlock));
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
