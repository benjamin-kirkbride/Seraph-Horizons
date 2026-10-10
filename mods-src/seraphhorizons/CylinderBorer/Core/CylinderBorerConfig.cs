namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>
/// The cylinder borer's figures: CylinderBorerSettings in ModConfig/seraphhorizons.json. Its oil
/// tank and drain per cylinder are MachineOil's (<c>MachineOilSettings.CylinderBorer</c>); the cutter
/// head's durability is its item's (8 cylinders).
/// </summary>
public class CylinderBorerConfig
{
    /// <summary>Axle turns per block of the cutter head's travel through an iron casting: 72 turns a
    /// cylinder (the rig's <c>bore.turnsPerBlock.thin</c>; a test holds the two together).</summary>
    public float TurnsPerBlockIron { get; set; } = 64f;

    /// <summary>Axle turns per block through a steel casting, a harder cut: 108 turns a cylinder
    /// (the rig's <c>bore.turnsPerBlock.thick</c>).</summary>
    public float TurnsPerBlockSteel { get; set; } = 96f;

    /// <summary>Durability points the cutter head loses per cylinder with a full oil tank, either
    /// metal; divided by the tank's fill as it runs down. The head's 8 points last 8 cylinders at 1.</summary>
    public int HeadWearPerCylinder { get; set; } = 1;

    /// <summary>Resistance the assembled borer puts on its shaft, oiled or dry (its oil spares the
    /// cutter head instead). An unassembled frame puts 0.005.</summary>
    public float Resistance { get; set; } = 0.3f;

    /// <summary>The shaft speed below which the borer does not bore or take a casting from its infeed.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    public static readonly CylinderBorerConfig Defaults = new();

    /// <summary>Axle turns per block of class <paramref name="k"/> (1 iron, 2 steel); 0 else.</summary>
    public double TurnsPerBlock(int k) => k switch { 1 => TurnsPerBlockIron, 2 => TurnsPerBlockSteel, _ => 0 };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(TurnsPerBlockIron) || TurnsPerBlockIron <= 0 || TurnsPerBlockIron > 10000)
            Fix(nameof(TurnsPerBlockIron), TurnsPerBlockIron, TurnsPerBlockIron = Defaults.TurnsPerBlockIron, fixes);
        if (!float.IsFinite(TurnsPerBlockSteel) || TurnsPerBlockSteel <= 0 || TurnsPerBlockSteel > 10000)
            Fix(nameof(TurnsPerBlockSteel), TurnsPerBlockSteel, TurnsPerBlockSteel = Defaults.TurnsPerBlockSteel, fixes);
        if (HeadWearPerCylinder < 0 || HeadWearPerCylinder > 100000)
            Fix(nameof(HeadWearPerCylinder), HeadWearPerCylinder, HeadWearPerCylinder = Defaults.HeadWearPerCylinder, fixes);
        if (!float.IsFinite(Resistance) || Resistance < 0 || Resistance > 10)
            Fix(nameof(Resistance), Resistance, Resistance = Defaults.Resistance, fixes);
        if (!float.IsFinite(MinSpeed) || MinSpeed < 0)
            Fix(nameof(MinSpeed), MinSpeed, MinSpeed = Defaults.MinSpeed, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
