namespace SeraphHorizons.Mod.Riddle.Core;

/// <summary>
/// The riddle's figures: RiddleSettings in ModConfig/seraphhorizons.json. A hand station: no power, no
/// oil, and nothing on it wears.
/// </summary>
public class RiddleConfig
{
    /// <summary>Crushed ore a charge takes, all of one kind (#714, 2026-10-14: three lumps, the rig's
    /// <c>riddling.lumpsPerCharge</c>; a test holds the two together).</summary>
    public int LumpsPerCharge { get; set; } = 3;

    /// <summary>Shakes a charge takes (#714: twelve, the rig's <c>riddling.shakesPerCharge.thin</c>), a
    /// shake every <see cref="Riddling.ShakeSeconds"/> while the player holds right-click.</summary>
    public float ShakesPerCharge { get; set; } = 12f;

    /// <summary>Of a charge's crushed ore, how many stay on the mesh as oversize, to go back to spalling
    /// (#714, 2026-10-13); the rest go through as classified fines.</summary>
    public int OversizePerCharge { get; set; } = 1;

    public static readonly RiddleConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (LumpsPerCharge is < 1 or > 16)
            Fix(nameof(LumpsPerCharge), LumpsPerCharge, LumpsPerCharge = Defaults.LumpsPerCharge, fixes);
        if (!float.IsFinite(ShakesPerCharge) || ShakesPerCharge <= 0 || ShakesPerCharge > 1000)
            Fix(nameof(ShakesPerCharge), ShakesPerCharge, ShakesPerCharge = Defaults.ShakesPerCharge, fixes);
        if (OversizePerCharge < 0 || OversizePerCharge >= LumpsPerCharge)
            Fix(nameof(OversizePerCharge), OversizePerCharge, OversizePerCharge = Math.Min(Defaults.OversizePerCharge, LumpsPerCharge - 1), fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
