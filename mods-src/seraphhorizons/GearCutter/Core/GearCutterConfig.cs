namespace SeraphHorizons.Mod.GearCutter.Core;

/// <summary>
/// The gear cutter's figures: GearCutterSettings in ModConfig/seraphhorizons.json. Its oil tank and
/// drain per gear are MachineOil's (<c>MachineOilSettings.GearCutter</c>).
/// </summary>
public class GearCutterConfig
{
    /// <summary>Axle turns per tooth cut: 144 a small gear, 240 a large one at 12, as the model's
    /// gears are drawn (the rig's <c>cut.turnsPerTooth</c>; a test holds the two together).</summary>
    public float TurnsPerTooth { get; set; } = 12f;

    /// <summary>Durability points the cutter kit loses per small gear with a full oil tank (a large
    /// one 20/12 of it); divided by the tank's fill as it runs down. The kit's 500 points last 50
    /// small gears at 10.</summary>
    public int CutterWearPerGear { get; set; } = 10;

    /// <summary>Resistance the assembled cutter puts on its shaft, oiled or dry. An unassembled frame puts 0.005.</summary>
    public float Resistance { get; set; } = 0.2f;

    /// <summary>The shaft speed below which the cutter does not cut or take a blank from its infeed.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    public static readonly GearCutterConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(TurnsPerTooth) || TurnsPerTooth <= 0 || TurnsPerTooth > 1000)
            Fix(nameof(TurnsPerTooth), TurnsPerTooth, TurnsPerTooth = Defaults.TurnsPerTooth, fixes);
        if (CutterWearPerGear < 0 || CutterWearPerGear > 100000)
            Fix(nameof(CutterWearPerGear), CutterWearPerGear, CutterWearPerGear = Defaults.CutterWearPerGear, fixes);
        if (!float.IsFinite(Resistance) || Resistance < 0 || Resistance > 10)
            Fix(nameof(Resistance), Resistance, Resistance = Defaults.Resistance, fixes);
        if (!float.IsFinite(MinSpeed) || MinSpeed < 0)
            Fix(nameof(MinSpeed), MinSpeed, MinSpeed = Defaults.MinSpeed, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
