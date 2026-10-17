namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>One machine's tailings in the settings: <c>Form</c> <c>gravel</c> or <c>sand</c>, and the
/// layers one rock discarded makes.</summary>
public class TailingsSetting
{
    public string Form { get; set; } = "gravel";
    public double LayersPerRock { get; set; } = 1;
}

/// <summary>
/// The shared figures of the ore machines and hand stations (#711, #694): OreMachinesSettings in
/// ModConfig/seraphhorizons.json. The per-machine figures (recovery, load, water an item, rate) are
/// ore-core's (#765, <c>config/ore-processing.json</c>); these are the rules every machine shares.
/// </summary>
public class OreMachinesConfig
{
    /// <summary>The shaft speed under which a machine stops ("no power" in its block info).</summary>
    public float MinSpeed { get; set; } = 0.05f;

    /// <summary>Litres a wet machine's reservoir holds (at least two items' water, <see cref="MachineReservoir.Capacity"/>).</summary>
    public double ReservoirLitres { get; set; } = 20;

    /// <summary>Litres per second a reservoir draws from its pipe until full.</summary>
    public double WaterIntakeLitresPerSecond { get; set; } = 10;

    /// <summary>
    /// Each machine's tailings, by machine id (#694, starting values accepted by the owner, to tune in
    /// play): the form, in the ore's host rock, and the layers a rock discarded makes. Tailings passed on
    /// by chute stay an item; they are discarded only where a machine's tailings outlet is not chuted on.
    /// The jaw breaker, stamp battery, arrastra and ball mill discard nothing; the roasters and furnaces
    /// leave no rock tailings. A machine not listed discards nothing.
    /// </summary>
    public Dictionary<string, TailingsSetting> Tailings { get; set; } = DefaultTailings();

    public static Dictionary<string, TailingsSetting> DefaultTailings() => new(StringComparer.Ordinal)
    {
        ["sluice"] = new() { Form = "gravel", LayersPerRock = 1 },
        ["jig"] = new() { Form = "gravel", LayersPerRock = 1 },
        ["shakingtable"] = new() { Form = "sand", LayersPerRock = 0.75 },
        ["vanner"] = new() { Form = "sand", LayersPerRock = 0.5 },
        ["amalgamationplates"] = new() { Form = "sand", LayersPerRock = 1 },
        ["rocker"] = new() { Form = "gravel", LayersPerRock = 1 },
    };

    public static readonly OreMachinesConfig Defaults = new();

    /// <summary>The tailings rule of machine <paramref name="machine"/>, or null when it discards none
    /// (not listed, or listed with no layers).</summary>
    public TailingsRule? TailingsFor(string machine)
    {
        if (Tailings == null || !Tailings.TryGetValue(machine, out var t) || t == null || !(t.LayersPerRock > 0))
            return null;
        return new TailingsRule(ParseForm(t.Form) ?? TailingsForm.Gravel, t.LayersPerRock);
    }

    public static TailingsForm? ParseForm(string? form) => form switch
    {
        "gravel" => TailingsForm.Gravel,
        "sand" => TailingsForm.Sand,
        _ => null,
    };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(MinSpeed) || MinSpeed < 0 || MinSpeed > 10)
        {
            fixes.Add($"MinSpeed {MinSpeed} is out of range, using {Defaults.MinSpeed}");
            MinSpeed = Defaults.MinSpeed;
        }
        if (!double.IsFinite(ReservoirLitres) || ReservoirLitres < 0 || ReservoirLitres > 100000)
        {
            fixes.Add($"ReservoirLitres {ReservoirLitres} is out of range, using {Defaults.ReservoirLitres}");
            ReservoirLitres = Defaults.ReservoirLitres;
        }
        if (!double.IsFinite(WaterIntakeLitresPerSecond) || WaterIntakeLitresPerSecond < 0 || WaterIntakeLitresPerSecond > 100000)
        {
            fixes.Add($"WaterIntakeLitresPerSecond {WaterIntakeLitresPerSecond} is out of range, using {Defaults.WaterIntakeLitresPerSecond}");
            WaterIntakeLitresPerSecond = Defaults.WaterIntakeLitresPerSecond;
        }
        Tailings ??= DefaultTailings();
        foreach (var (machine, t) in Tailings.ToList())
        {
            if (t == null)
            {
                Tailings.Remove(machine);
                continue;
            }
            if (ParseForm(t.Form) == null)
            {
                fixes.Add($"Tailings.{machine}.Form {t.Form} is not gravel or sand, using gravel");
                t.Form = "gravel";
            }
            if (!double.IsFinite(t.LayersPerRock) || t.LayersPerRock < 0 || t.LayersPerRock > 64)
            {
                fixes.Add($"Tailings.{machine}.LayersPerRock {t.LayersPerRock} is out of range, using 1");
                t.LayersPerRock = 1;
            }
        }
        return fixes;
    }
}
