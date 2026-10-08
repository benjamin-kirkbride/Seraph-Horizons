namespace SeraphHorizons.Mod.SquaringShear.Core;

/// <summary>
/// The squaring shear's figures: SquaringShearSettings in ModConfig/seraphhorizons.json. A hand
/// machine: no oil, no power, and its blades do not wear (the model's contract gives them none).
/// </summary>
public class SquaringShearConfig
{
    /// <summary>Strokes of the treadle clock (<see cref="Cutting.StrokesPerSecond"/> a second while
    /// the player holds right-click) a lead plate takes, as the model is drawn (the rig's
    /// <c>cut.strokesPerPlate.thin</c>; a test holds the two together).</summary>
    public float StrokesPerPlateLead { get; set; } = 1f;

    /// <summary>Treadle strokes a copper plate takes: half as much again as lead's (the rig's
    /// <c>cut.strokesPerPlate.thick</c>).</summary>
    public float StrokesPerPlateCopper { get; set; } = 1.5f;

    public static readonly SquaringShearConfig Defaults = new();

    /// <summary>Treadle strokes a plate of class <paramref name="k"/> takes (1 lead, 2 copper); 0 else.</summary>
    public double StrokesPerPlate(int k) => k switch { 1 => StrokesPerPlateLead, 2 => StrokesPerPlateCopper, _ => 0 };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(StrokesPerPlateLead) || StrokesPerPlateLead <= 0 || StrokesPerPlateLead > 1000)
            Fix(nameof(StrokesPerPlateLead), StrokesPerPlateLead, StrokesPerPlateLead = Defaults.StrokesPerPlateLead, fixes);
        if (!float.IsFinite(StrokesPerPlateCopper) || StrokesPerPlateCopper <= 0 || StrokesPerPlateCopper > 1000)
            Fix(nameof(StrokesPerPlateCopper), StrokesPerPlateCopper, StrokesPerPlateCopper = Defaults.StrokesPerPlateCopper, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
