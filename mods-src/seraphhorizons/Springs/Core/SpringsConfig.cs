using System.Globalization;

namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>
/// The springs' figures: SpringsSettings in ModConfig/seraphhorizons.json (README "Springs"). The
/// litres a second a tapped spring feeds the pipes coupled to it. ppex's fluid intake has no rate of
/// its own (the pump drawing on it sets it: the hand-cranked pump 2 L/s, the mechanical pump 20 per
/// unit of axle speed, about 3.6 on the waterwheel it is sized for, the engine pump 20 to 80), so a
/// spring gives about what an intake does under a waterwheel pump, and a rapids spring 2.5 times that.
/// </summary>
public class SpringsConfig
{
    /// <summary>A spring in place of a still-water rivulet.</summary>
    public float NormalLitresPerSecond { get; set; } = 4f;

    /// <summary>A spring in place of a rapids rivulet.</summary>
    public float RapidLitresPerSecond { get; set; } = 10f;

    public static readonly SpringsConfig Defaults = new();

    /// <summary>The rate of a spring of <paramref name="kind"/>.</summary>
    public float LitresPerSecond(SpringKind kind) => kind == SpringKind.Rapid ? RapidLitresPerSecond : NormalLitresPerSecond;

    /// <summary>Replaces values out of range (not a number, 0 or less, above 1000) with the
    /// default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        NormalLitresPerSecond = Check(nameof(NormalLitresPerSecond), NormalLitresPerSecond, Defaults.NormalLitresPerSecond, fixes);
        RapidLitresPerSecond = Check(nameof(RapidLitresPerSecond), RapidLitresPerSecond, Defaults.RapidLitresPerSecond, fixes);
        return fixes;
    }

    private static float Check(string name, float value, float fallback, List<string> fixes)
    {
        if (float.IsFinite(value) && value > 0 && value <= 1000)
            return value;
        fixes.Add($"{name} {value.ToString(CultureInfo.InvariantCulture)} is out of range, using {fallback.ToString(CultureInfo.InvariantCulture)}");
        return fallback;
    }

    /// <summary>A rate as the text prints it: "4", "2.5".</summary>
    public static string Format(float litresPerSecond) => litresPerSecond.ToString("0.##", CultureInfo.InvariantCulture);
}
