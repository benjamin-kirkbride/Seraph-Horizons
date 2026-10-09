namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// The eidolon's figures: EidolonSettings in ModConfig/seraphhorizons.json (Eidolon/README.md).
/// The server's are used.
/// </summary>
public class EidolonConfig
{
    /// <summary>How long one temporal gear runs it, in years of the world's calendar (a quarter:
    /// 27 days at 9 days a month). Charge drains only while it stands; slumped, it keeps what it has.</summary>
    public double ChargeYearsPerGear { get; set; } = 0.25;

    /// <summary>The most charge it holds, in gears' worth: a gear is refused when it would take the
    /// charge above this.</summary>
    public double MaxChargeGears { get; set; } = 2;

    /// <summary>At 0 HP it slumps; repaired, it stands up once its health is back to this share of its
    /// most.</summary>
    public double StandUpHealthShare { get; set; } = 0.25;

    /// <summary>Its walking speed, on the game's scale for creatures (a player walks at about 0.03).</summary>
    public float WalkSpeed { get; set; } = 0.022f;

    /// <summary>Its running speed, the same scale.</summary>
    public float RunSpeed { get; set; } = 0.04f;

    /// <summary>The most nodes one path search may visit before it gives up (each costs a few
    /// collision tests of its 2 × 4 box, on the server's thread).</summary>
    public int PathSearchNodes { get; set; } = 3000;

    /// <summary>The highest drop it walks off on a path, in blocks.</summary>
    public int MaxFallBlocks { get; set; } = 3;

    public static readonly EidolonConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        void Check(string name, double value, double lo, double hi, Action reset, double fallback)
        {
            if (!double.IsFinite(value) || value < lo || value > hi)
            {
                fixes.Add($"{name} {value} is out of range, using {fallback}");
                reset();
            }
        }
        Check(nameof(ChargeYearsPerGear), ChargeYearsPerGear, 0.001, 100, () => ChargeYearsPerGear = Defaults.ChargeYearsPerGear, Defaults.ChargeYearsPerGear);
        Check(nameof(MaxChargeGears), MaxChargeGears, 1, 100, () => MaxChargeGears = Defaults.MaxChargeGears, Defaults.MaxChargeGears);
        Check(nameof(StandUpHealthShare), StandUpHealthShare, 0.001, 1, () => StandUpHealthShare = Defaults.StandUpHealthShare, Defaults.StandUpHealthShare);
        Check(nameof(WalkSpeed), WalkSpeed, 0.001, 0.5, () => WalkSpeed = Defaults.WalkSpeed, Defaults.WalkSpeed);
        Check(nameof(RunSpeed), RunSpeed, 0.001, 0.5, () => RunSpeed = Defaults.RunSpeed, Defaults.RunSpeed);
        Check(nameof(PathSearchNodes), PathSearchNodes, 50, 100000, () => PathSearchNodes = Defaults.PathSearchNodes, Defaults.PathSearchNodes);
        Check(nameof(MaxFallBlocks), MaxFallBlocks, 0, 16, () => MaxFallBlocks = Defaults.MaxFallBlocks, Defaults.MaxFallBlocks);
        return fixes;
    }
}
