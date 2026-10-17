namespace SeraphHorizons.Mod.Rocker.Core;

/// <summary>
/// The rocker's figures: RockerSettings in ModConfig/seraphhorizons.json. A hand station: no power, no
/// oil, no pipe; its water is the bucket the player holds while rocking it. The recovery (55 %) is not
/// here: it is ore processing's (<c>config/ore-processing.json</c>, <c>concentrators.rocker</c>).
/// </summary>
public class RockerConfig
{
    /// <summary>Rocks (turns of θ, the rig's clock) one item of the charge takes to wash through.</summary>
    public float RocksPerItem { get; set; } = 4f;

    /// <summary>Rocks a second while the player holds right-click with water in the bucket (the model's
    /// viewer rocks it at one a second).</summary>
    public float RocksPerSecond { get; set; } = 1f;

    /// <summary>Litres of water the bucket pours into the hopper for one item of the charge, poured
    /// evenly over its rocks: a full bucket (10 L) washes five.</summary>
    public float LitresPerItem { get; set; } = 2f;

    /// <summary>The most items a charge in the hopper holds (one kind at a time).</summary>
    public int ChargeItems { get; set; } = 4;

    /// <summary>The most concentrate (or placer metal) the riffles hold before they must be cleaned out
    /// by hand: full, the rocker stops.</summary>
    public int RiffleItems { get; set; } = 16;

    public static readonly RockerConfig Defaults = new();

    /// <summary>Seconds of rocking an item takes.</summary>
    public double SecondsPerItem => RocksPerItem / RocksPerSecond;

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!float.IsFinite(RocksPerItem) || RocksPerItem <= 0 || RocksPerItem > 1000)
            Fix(nameof(RocksPerItem), RocksPerItem, RocksPerItem = Defaults.RocksPerItem, fixes);
        if (!float.IsFinite(RocksPerSecond) || RocksPerSecond <= 0 || RocksPerSecond > 4)
            Fix(nameof(RocksPerSecond), RocksPerSecond, RocksPerSecond = Defaults.RocksPerSecond, fixes);
        if (!float.IsFinite(LitresPerItem) || LitresPerItem < 0 || LitresPerItem > 100)
            Fix(nameof(LitresPerItem), LitresPerItem, LitresPerItem = Defaults.LitresPerItem, fixes);
        if (ChargeItems < 1 || ChargeItems > 64)
            Fix(nameof(ChargeItems), ChargeItems, ChargeItems = Defaults.ChargeItems, fixes);
        if (RiffleItems < 1 || RiffleItems > 512)
            Fix(nameof(RiffleItems), RiffleItems, RiffleItems = Defaults.RiffleItems, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
