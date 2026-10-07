namespace SeraphHorizons.Mod.PressBrake.Core;

/// <summary>
/// The press brake's figures: PressBrakeSettings in ModConfig/seraphhorizons.json. A hand machine:
/// no oil, no power, and its iron edges do not wear (the model's contract gives them none).
/// </summary>
public class PressBrakeConfig
{
    /// <summary>Turns of the lever clock (<see cref="Folding.LeverTurnsPerSecond"/> a second while
    /// the player holds right-click) a lead plate takes, as the model is drawn (the rig's
    /// <c>fold.leverTurnsPerPlate.thin</c>; a test holds the two together).</summary>
    public float LeverTurnsPerPlateLead { get; set; } = 6f;

    /// <summary>Lever turns a copper plate takes: half as much again as lead's (the rig's
    /// <c>fold.leverTurnsPerPlate.thick</c>).</summary>
    public float LeverTurnsPerPlateCopper { get; set; } = 9f;

    public static readonly PressBrakeConfig Defaults = new();

    /// <summary>Lever turns a plate of class <paramref name="k"/> takes (1 lead, 2 copper); 0 else.</summary>
    public double LeverTurnsPerPlate(int k) => k switch { 1 => LeverTurnsPerPlateLead, 2 => LeverTurnsPerPlateCopper, _ => 0 };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(LeverTurnsPerPlateLead) || LeverTurnsPerPlateLead <= 0 || LeverTurnsPerPlateLead > 1000)
            Fix(nameof(LeverTurnsPerPlateLead), LeverTurnsPerPlateLead, LeverTurnsPerPlateLead = Defaults.LeverTurnsPerPlateLead, fixes);
        if (!float.IsFinite(LeverTurnsPerPlateCopper) || LeverTurnsPerPlateCopper <= 0 || LeverTurnsPerPlateCopper > 1000)
            Fix(nameof(LeverTurnsPerPlateCopper), LeverTurnsPerPlateCopper, LeverTurnsPerPlateCopper = Defaults.LeverTurnsPerPlateCopper, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
