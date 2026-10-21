namespace SeraphHorizons.Mod.SlittingMill.Core;

/// <summary>One metal's figures on the slitting mill: the load it puts on the shaft and the knife's wear a
/// plate.</summary>
public class SlittingMetalConfig
{
    /// <summary>Resistance the mill puts on its shaft while it slits a plate of the metal (times the dry
    /// multiplier while its oil tank is dry).</summary>
    public float Resistance { get; set; }

    /// <summary>Durability points the knife (a cleaver: steel 2000, meteoric iron 950, iron 800) loses a plate
    /// of the metal, oiled or dry.</summary>
    public int KnifeWear { get; set; }

    public SlittingMetalConfig()
    {
    }

    public SlittingMetalConfig(float resistance, int knifeWear)
    {
        Resistance = resistance;
        KnifeWear = knifeWear;
    }
}

/// <summary>
/// The slitting mill's figures: SlittingMillSettings in ModConfig/seraphhorizons.json. Its oil tank and drain
/// per plate are MachineOil's (<c>MachineOilSettings.SlittingMill</c>); the knife's durability is its cleaver's.
/// </summary>
public class SlittingMillConfig
{
    /// <summary>Axle turns per knife stroke, five strokes a plate: the model's drawn pace (the rig's
    /// <c>slit.turnsPerStroke</c>; a test holds the two together).</summary>
    public float TurnsPerStroke { get; set; } = 1f;

    /// <summary>Nails and strips a plate makes, against the anvil's 16 from a plate's two ingots.</summary>
    public int NailsPerPlate { get; set; } = 20;

    /// <summary>The shaft speed below which the mill does not slit or take a plate from its infeed.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>Each metal's load and knife wear: soft metals light, bronzes and brass harder, iron and steel
    /// hardest. A metal missing is put back at its default; one the mill does not slit is dropped.</summary>
    public Dictionary<string, SlittingMetalConfig> Metals { get; set; } = DefaultMetals();

    public static Dictionary<string, SlittingMetalConfig> DefaultMetals() => new()
    {
        ["gold"] = new(0.2f, 4),
        ["silver"] = new(0.2f, 4),
        ["copper"] = new(0.25f, 6),
        ["electrum"] = new(0.25f, 6),
        ["molybdochalkos"] = new(0.25f, 8),
        ["brass"] = new(0.3f, 8),
        ["tinbronze"] = new(0.3f, 10),
        ["bismuthbronze"] = new(0.3f, 10),
        ["blackbronze"] = new(0.3f, 12),
        ["iron"] = new(0.4f, 20),
        ["meteoriciron"] = new(0.4f, 24),
        ["steel"] = new(0.5f, 40),
    };

    public static readonly SlittingMillConfig Defaults = new();

    /// <summary>The shaft load slitting <paramref name="metal"/>; with none on (or an unknown metal), the
    /// lightest metal's.</summary>
    public float Resistance(string? metal) =>
        metal != null && Metals.TryGetValue(metal, out var m) ? m.Resistance : LightestResistance();

    /// <summary>The lowest of the metals' loads: the mill's load with no plate on. (Methods, not properties:
    /// the settings file is this class serialised.)</summary>
    public float LightestResistance() => Metals.Count == 0 ? 0 : Metals.Values.Min(m => m.Resistance);

    /// <summary>The highest of the metals' loads.</summary>
    public float HeaviestResistance() => Metals.Count == 0 ? 0 : Metals.Values.Max(m => m.Resistance);

    /// <summary>The knife's wear a plate of <paramref name="metal"/>; 0 for an unknown one.</summary>
    public int KnifeWear(string? metal) => metal != null && Metals.TryGetValue(metal, out var m) ? m.KnifeWear : 0;

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(TurnsPerStroke) || TurnsPerStroke <= 0 || TurnsPerStroke > 1000)
            Fix(nameof(TurnsPerStroke), TurnsPerStroke, TurnsPerStroke = Defaults.TurnsPerStroke, fixes);
        if (NailsPerPlate < 1 || NailsPerPlate > 1000)
            Fix(nameof(NailsPerPlate), NailsPerPlate, NailsPerPlate = Defaults.NailsPerPlate, fixes);
        if (!float.IsFinite(MinSpeed) || MinSpeed < 0)
            Fix(nameof(MinSpeed), MinSpeed, MinSpeed = Defaults.MinSpeed, fixes);
        Metals ??= DefaultMetals();
        var defaults = DefaultMetals();
        foreach (var metal in Slitting.Metals)
        {
            if (!Metals.TryGetValue(metal, out var m) || m == null)
            {
                fixes.Add($"Metals {metal} is missing, using its default");
                Metals[metal] = defaults[metal];
                continue;
            }
            if (!float.IsFinite(m.Resistance) || m.Resistance < 0 || m.Resistance > 10)
                Fix($"Metals {metal} Resistance", m.Resistance, m.Resistance = defaults[metal].Resistance, fixes);
            if (m.KnifeWear < 0 || m.KnifeWear > 100000)
                Fix($"Metals {metal} KnifeWear", m.KnifeWear, m.KnifeWear = defaults[metal].KnifeWear, fixes);
        }
        foreach (var metal in Metals.Keys.Where(k => !Slitting.Metals.Contains(k)).ToList())
        {
            fixes.Add($"Metals {metal} is not a metal the mill slits, ignored");
            Metals.Remove(metal);
        }
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
