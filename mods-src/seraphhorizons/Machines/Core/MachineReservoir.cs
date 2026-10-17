namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A wet machine's own reservoir (#711, #694; the rosser's pattern): it fills from a Pipes and Power
/// Expanded pipe on the machine's water face at up to <see cref="OreMachinesConfig.WaterIntakeLitresPerSecond"/>
/// and spends a fixed amount per item worked (ore-core's <c>OreMachineSpec.WaterPerItem</c>). With
/// less than an item's water in it the machine stops; it resumes when water returns. No water comes
/// back: what an item takes is used up.
/// <para>The reservoir holds <see cref="OreMachinesConfig.ReservoirLitres"/> (20 L), or two items'
/// water where an item takes more than half of that (the stamp battery's 40 L a rock), so every
/// machine can hold the water for the item it is working on.</para>
/// </summary>
public readonly record struct MachineReservoir(double Litres)
{
    public static readonly MachineReservoir Empty = new(0);

    /// <summary>The reservoir's size for a machine spending <paramref name="perItem"/> litres an item.</summary>
    public static double Capacity(OreMachinesConfig config, double perItem) =>
        Math.Max(config.ReservoirLitres, 2 * Math.Max(0, perItem));

    /// <summary>Restored from a save: clamped to 0..capacity.</summary>
    public static MachineReservoir Restore(double litres, double capacity) =>
        new(double.IsFinite(litres) ? Math.Clamp(litres, 0, Math.Max(0, capacity)) : 0);

    /// <summary>Whether it holds an item's water. A machine that spends none is always wet.</summary>
    public bool Holds(double perItem) => perItem <= 0 || Litres + 1e-9 >= perItem;

    /// <summary>Litres to ask the pipe for over <paramref name="seconds"/>: the intake rate, never past full.</summary>
    public double Wanted(double capacity, double intakePerSecond, double seconds) =>
        Math.Max(0, Math.Min(capacity - Litres, Math.Max(0, intakePerSecond) * Math.Max(0, seconds)));

    /// <summary>The reservoir after <paramref name="litres"/> came in, never past full.</summary>
    public MachineReservoir Fill(double litres, double capacity) =>
        new(Math.Clamp(Litres + (double.IsFinite(litres) ? Math.Max(0, litres) : 0), 0, Math.Max(Litres, capacity)));

    /// <summary>Spends an item's water if it holds it: the reservoir after, and whether it did.</summary>
    public (MachineReservoir Next, bool Spent) Spend(double perItem)
    {
        if (perItem <= 0)
            return (this, true);
        return Holds(perItem) ? (new MachineReservoir(Math.Max(0, Litres - perItem)), true) : (this, false);
    }
}
