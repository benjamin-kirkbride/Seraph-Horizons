namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>
/// The draw bench's figures: DrawBenchSettings in ModConfig/seraphhorizons.json. Its oil tank and
/// drain per section are MachineOil's (<c>MachineOilSettings.DrawBench</c>).
/// </summary>
public class DrawBenchConfig
{
    /// <summary>Hollow sections a new die draws: its durability, one point a hollow (the die item's
    /// durability is set to it when the game loads).</summary>
    public int DieDurability { get; set; } = 100;

    /// <summary>Durability points a die loses per hollow section drawn (four pipe sections),
    /// whatever the oil.</summary>
    public int DieWearPerHollow { get; set; } = 1;

    /// <summary>Axle turns per lead section: the bench's fast gear, as the model is drawn (the rig's
    /// <c>draw.turnsPerSection.thin</c>; a test holds the two together).</summary>
    public float TurnsPerSectionLead { get; set; } = 1.373f;

    /// <summary>Axle turns per copper section: the slow gear, twice the lead's (the rig's
    /// <c>draw.turnsPerSection.thick</c>).</summary>
    public float TurnsPerSectionCopper { get; set; } = 2.746f;

    /// <summary>Resistance the assembled bench puts on its shaft while it is empty or draws lead
    /// (times the dry multiplier while its oil tank is dry). An unassembled frame puts 0.005.</summary>
    public float ResistanceLead { get; set; } = 0.2f;

    /// <summary>Resistance while it draws copper, a harder metal.</summary>
    public float ResistanceCopper { get; set; } = 0.35f;

    /// <summary>The shaft speed below which the bench does not draw.</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>What each die draws, by the die's metal: an iron die lead only, a steel die lead
    /// and copper. Metals other than lead and copper are ignored.</summary>
    public Dictionary<string, string[]> DieMetals { get; set; } = DefaultDieMetals();

    public static Dictionary<string, string[]> DefaultDieMetals() => new()
    {
        ["iron"] = ["lead"],
        ["steel"] = ["lead", "copper"],
    };

    public static readonly DrawBenchConfig Defaults = new();

    /// <summary>Axle turns per section of class <paramref name="k"/> (1 lead, 2 copper); 0 else.</summary>
    public double TurnsPerSection(int k) => k switch { 1 => TurnsPerSectionLead, 2 => TurnsPerSectionCopper, _ => 0 };

    /// <summary>The shaft load drawing class <paramref name="k"/>; lead's when empty.</summary>
    public float Resistance(int k) => k == 2 ? ResistanceCopper : ResistanceLead;

    /// <summary>The metals a die of <paramref name="dieMetal"/> draws; none for an unknown die.</summary>
    public IReadOnlyList<string> MetalsFor(string? dieMetal) =>
        dieMetal != null && DieMetals.TryGetValue(dieMetal, out var metals) ? metals : [];

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (DieDurability < 1 || DieDurability > 100000)
            Fix(nameof(DieDurability), DieDurability, DieDurability = Defaults.DieDurability, fixes);
        if (DieWearPerHollow < 0 || DieWearPerHollow > 100000)
            Fix(nameof(DieWearPerHollow), DieWearPerHollow, DieWearPerHollow = Defaults.DieWearPerHollow, fixes);
        if (!float.IsFinite(TurnsPerSectionLead) || TurnsPerSectionLead <= 0 || TurnsPerSectionLead > 1000)
            Fix(nameof(TurnsPerSectionLead), TurnsPerSectionLead, TurnsPerSectionLead = Defaults.TurnsPerSectionLead, fixes);
        if (!float.IsFinite(TurnsPerSectionCopper) || TurnsPerSectionCopper <= 0 || TurnsPerSectionCopper > 1000)
            Fix(nameof(TurnsPerSectionCopper), TurnsPerSectionCopper, TurnsPerSectionCopper = Defaults.TurnsPerSectionCopper, fixes);
        if (!float.IsFinite(ResistanceLead) || ResistanceLead < 0 || ResistanceLead > 10)
            Fix(nameof(ResistanceLead), ResistanceLead, ResistanceLead = Defaults.ResistanceLead, fixes);
        if (!float.IsFinite(ResistanceCopper) || ResistanceCopper < 0 || ResistanceCopper > 10)
            Fix(nameof(ResistanceCopper), ResistanceCopper, ResistanceCopper = Defaults.ResistanceCopper, fixes);
        if (!float.IsFinite(MinSpeed) || MinSpeed < 0)
            Fix(nameof(MinSpeed), MinSpeed, MinSpeed = Defaults.MinSpeed, fixes);
        DieMetals ??= DefaultDieMetals();
        foreach (var die in Drawing.DieMetalNames)
        {
            if (!DieMetals.TryGetValue(die, out var metals) || metals == null)
            {
                fixes.Add($"DieMetals {die} is missing, using {string.Join(", ", DefaultDieMetals()[die])}");
                DieMetals[die] = DefaultDieMetals()[die];
                continue;
            }
            var known = metals.Where(m => Drawing.ClassOfMetal(m) != 0).Distinct().ToArray();
            if (known.Length != metals.Length)
            {
                fixes.Add($"DieMetals {die} names a metal the bench does not draw ({string.Join(", ", metals.Except(known))}), "
                          + $"using {(known.Length == 0 ? "none" : string.Join(", ", known))}");
                DieMetals[die] = known;
            }
        }
        foreach (var die in DieMetals.Keys.Where(k => !Drawing.DieMetalNames.Contains(k)).ToList())
        {
            fixes.Add($"DieMetals {die} is not a die, ignored");
            DieMetals.Remove(die);
        }
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
