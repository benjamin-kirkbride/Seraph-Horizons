namespace SeraphHorizons.Mod.AmalgamPan.Core;

/// <summary>
/// The amalgam pan's figures: AmalgamPanSettings in ModConfig/seraphhorizons.json. A hand station
/// that an axle below may turn instead (as the quern), with no oil. What share of the free metal it
/// catches is ore processing's (<c>config/ore-processing.json</c>, <c>handStations.amalgampan</c>), and
/// the mercury an amalgam takes is the still's (<c>retort.amalgamMercury</c>); these are the pan's own
/// mechanics.
/// </summary>
public class AmalgamPanConfig
{
    /// <summary>The most crushed (or ground) gold or silver quartz a charge holds: a rock stack.</summary>
    public int ChargeItems { get; set; } = 16;

    /// <summary>Litres of water a charge is worked with (poured from a bucket or any water container),
    /// spent with the charge.</summary>
    public double WaterLitresPerCharge { get; set; } = 4;

    /// <summary>Turns of the muller a crushed item of the charge takes, so a full charge takes
    /// 16 turns.</summary>
    public double TurnsPerItem { get; set; } = 1;

    /// <summary>How fast the player turns the muller while holding right-click: 40 turns a minute, as
    /// the model's viewer plays it, so a full charge takes 24 s by hand.</summary>
    public double HandTurnsPerSecond { get; set; } = 2.0 / 3;

    /// <summary>The most mercury the pan holds, in portions (100 to the litre).</summary>
    public int MercuryCapacityPortions { get; set; } = 500;

    /// <summary>The load the pan puts on an axle while it works a charge (the quern's is 0.085);
    /// <see cref="IdleResistance"/> while it turns empty.</summary>
    public float WorkingResistance { get; set; } = 0.1f;

    /// <summary>The load while the muller turns with nothing to work, or before it is fitted.</summary>
    public float IdleResistance { get; set; } = 0.005f;

    public static readonly AmalgamPanConfig Defaults = new();

    /// <summary>Turns a charge of <paramref name="items"/> takes.</summary>
    public double TurnsFor(int items) => Math.Max(0, items) * TurnsPerItem;

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (ChargeItems is < 1 or > 64)
            Fix(nameof(ChargeItems), ChargeItems, ChargeItems = Defaults.ChargeItems, fixes);
        if (!(WaterLitresPerCharge is >= 0 and <= 100))
            Fix(nameof(WaterLitresPerCharge), WaterLitresPerCharge, WaterLitresPerCharge = Defaults.WaterLitresPerCharge, fixes);
        if (!(TurnsPerItem is > 0 and <= 1000))
            Fix(nameof(TurnsPerItem), TurnsPerItem, TurnsPerItem = Defaults.TurnsPerItem, fixes);
        if (!(HandTurnsPerSecond is > 0 and <= 10))
            Fix(nameof(HandTurnsPerSecond), HandTurnsPerSecond, HandTurnsPerSecond = Defaults.HandTurnsPerSecond, fixes);
        if (MercuryCapacityPortions is < 1 or > 100_000)
            Fix(nameof(MercuryCapacityPortions), MercuryCapacityPortions, MercuryCapacityPortions = Defaults.MercuryCapacityPortions, fixes);
        if (!(WorkingResistance is >= 0 and <= 10))
            Fix(nameof(WorkingResistance), WorkingResistance, WorkingResistance = Defaults.WorkingResistance, fixes);
        if (!(IdleResistance is >= 0 and <= 10))
            Fix(nameof(IdleResistance), IdleResistance, IdleResistance = Defaults.IdleResistance, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
