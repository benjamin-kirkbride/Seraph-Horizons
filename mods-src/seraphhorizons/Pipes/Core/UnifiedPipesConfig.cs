using System.Globalization;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// The unified pipes' figures: UnifiedPipesSettings in ModConfig/seraphhorizons.json. The burst
/// pressure, in atm, of each pipe metal on Pipes and Power Expanded's network: the pressure a gas
/// run reaches that metal's pipe at before it bursts, and the weakest pipe caps the run. A pressure
/// valve's gate goes up to its own metal's figure; valves never burst. Liquids never burst a pipe.
/// </summary>
public class UnifiedPipesConfig
{
    /// <summary>Lead: below every gas producer, so a gas run bursts it (and steam or exhaust bursts
    /// it at once, whatever the figure, <see cref="PipeRules.LeadBursts"/>).</summary>
    public float LeadBurstPressure { get; set; } = 0.5f;

    /// <summary>Copper: carries exhaust and blast air, fails on a stressed boiler.</summary>
    public float CopperBurstPressure { get; set; } = 3f;

    /// <summary>Iron: ppex's own figure.</summary>
    public float IronBurstPressure { get; set; } = 5f;

    /// <summary>Steel: ppex's own figure.</summary>
    public float SteelBurstPressure { get; set; } = 10f;

    /// <summary>Bronze valves and pressure valves (tin, bismuth and black bronze): a pressure
    /// valve's gate goes up to it.</summary>
    public float BronzeBurstPressure { get; set; } = 5f;

    public static readonly UnifiedPipesConfig Defaults = new();

    /// <summary>The figure for a pipe's <c>material</c> variant, or null for a material this does
    /// not know (ppex's own figure then stands).</summary>
    public float? BurstPressureFor(string? material) => material switch
    {
        PipeRules.Lead => LeadBurstPressure,
        PipeRules.Copper => CopperBurstPressure,
        PipeRules.Iron => IronBurstPressure,
        PipeRules.Steel => SteelBurstPressure,
        _ when PipeRules.IsBronze(material) => BronzeBurstPressure,
        _ => null,
    };

    /// <summary>Replaces values out of range (not a number, 0 or less, above 1000) with the
    /// default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        LeadBurstPressure = Check(nameof(LeadBurstPressure), LeadBurstPressure, Defaults.LeadBurstPressure, fixes);
        CopperBurstPressure = Check(nameof(CopperBurstPressure), CopperBurstPressure, Defaults.CopperBurstPressure, fixes);
        IronBurstPressure = Check(nameof(IronBurstPressure), IronBurstPressure, Defaults.IronBurstPressure, fixes);
        SteelBurstPressure = Check(nameof(SteelBurstPressure), SteelBurstPressure, Defaults.SteelBurstPressure, fixes);
        BronzeBurstPressure = Check(nameof(BronzeBurstPressure), BronzeBurstPressure, Defaults.BronzeBurstPressure, fixes);
        return fixes;
    }

    private static float Check(string name, float value, float fallback, List<string> fixes)
    {
        if (float.IsFinite(value) && value > 0 && value <= 1000)
            return value;
        fixes.Add($"{name} {value.ToString(CultureInfo.InvariantCulture)} is out of range, using {fallback.ToString(CultureInfo.InvariantCulture)}");
        return fallback;
    }

    /// <summary>A figure as the handbook prints it: "0.5", "3".</summary>
    public static string Format(float atm) => atm.ToString("0.##", CultureInfo.InvariantCulture);
}
