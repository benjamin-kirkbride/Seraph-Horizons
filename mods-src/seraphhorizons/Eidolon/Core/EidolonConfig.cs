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

    // ---- Oil and repair (#674; Eidolon/README.md, "Oil", "Repair") ----

    /// <summary>Points its oil reservoir holds, 100 to the litre as the machines' tanks
    /// (MachineOilSettings): 1000 is a full bucket. With the MachineOil switch off it has none.</summary>
    public double OilTank { get; set; } = 1000;

    /// <summary>The share of its reservoir a new eidolon wakes with.</summary>
    public double InitialOilShare { get; set; } = 0.25;

    /// <summary>Points a tree it fells costs.</summary>
    public double OilPerTreeFelled { get; set; } = 5;

    /// <summary>Points a trunk it delivers costs.</summary>
    public double OilPerTrunkDelivered { get; set; } = 3;

    /// <summary>Points a load it carries somewhere and sets down costs.</summary>
    public double OilPerLoadCarried { get; set; } = 2;

    /// <summary>The share of its most health one item of metal parts or one iron plate restores.</summary>
    public double RepairShare { get; set; } = 0.1;

    /// <summary>What a repair is multiplied by while it stands inside a gantry, its dock.</summary>
    public double GantryRepairMultiplier { get; set; } = 2;

    // ---- Commanding (#675; README "Eidolon", "Commanding") ----

    /// <summary>How far a command tool's orders reach, in blocks: a bound eidolon further from its
    /// holder is not ordered.</summary>
    public double CommandRange { get; set; } = 64;

    /// <summary>Following, it keeps this many blocks from the one it follows.</summary>
    public double FollowDistance { get; set; } = 4;

    /// <summary>Following, it runs while further than this, in blocks.</summary>
    public double FollowRunDistance { get; set; } = 10;

    /// <summary>The damage of each punch or kick when it defends itself (before the world's creature
    /// damage multiplier, as a creature's).</summary>
    public float DefenceDamage { get; set; } = 10;

    // ---- Guarding (#680; README "Eidolon", "Guarding") ----

    /// <summary>The damage of each slam, every third blow in a fight (before the world's creature
    /// damage multiplier); the punches and kicks are <see cref="DefenceDamage"/>.</summary>
    public float SlamDamage { get; set; } = 16;

    /// <summary>Guarding, how far from its point, in blocks, it goes for a hostile creature.</summary>
    public double GuardRadius { get; set; } = 16;

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
        Check(nameof(OilTank), OilTank, 1, 1_000_000, () => OilTank = Defaults.OilTank, Defaults.OilTank);
        Check(nameof(InitialOilShare), InitialOilShare, 0, 1, () => InitialOilShare = Defaults.InitialOilShare, Defaults.InitialOilShare);
        Check(nameof(OilPerTreeFelled), OilPerTreeFelled, 0, OilTank, () => OilPerTreeFelled = Defaults.OilPerTreeFelled, Defaults.OilPerTreeFelled);
        Check(nameof(OilPerTrunkDelivered), OilPerTrunkDelivered, 0, OilTank, () => OilPerTrunkDelivered = Defaults.OilPerTrunkDelivered, Defaults.OilPerTrunkDelivered);
        Check(nameof(OilPerLoadCarried), OilPerLoadCarried, 0, OilTank, () => OilPerLoadCarried = Defaults.OilPerLoadCarried, Defaults.OilPerLoadCarried);
        Check(nameof(RepairShare), RepairShare, 0.001, 1, () => RepairShare = Defaults.RepairShare, Defaults.RepairShare);
        Check(nameof(GantryRepairMultiplier), GantryRepairMultiplier, 1, 100, () => GantryRepairMultiplier = Defaults.GantryRepairMultiplier, Defaults.GantryRepairMultiplier);

        Check(nameof(CommandRange), CommandRange, 1, 1024, () => CommandRange = Defaults.CommandRange, Defaults.CommandRange);
        Check(nameof(FollowDistance), FollowDistance, 2, 32, () => FollowDistance = Defaults.FollowDistance, Defaults.FollowDistance);
        Check(nameof(FollowRunDistance), FollowRunDistance, 2, 256, () => FollowRunDistance = Defaults.FollowRunDistance, Defaults.FollowRunDistance);
        Check(nameof(DefenceDamage), DefenceDamage, 0, 1000, () => DefenceDamage = Defaults.DefenceDamage, Defaults.DefenceDamage);

        Check(nameof(SlamDamage), SlamDamage, 0, 1000, () => SlamDamage = Defaults.SlamDamage, Defaults.SlamDamage);
        Check(nameof(GuardRadius), GuardRadius, 2, 64, () => GuardRadius = Defaults.GuardRadius, Defaults.GuardRadius);
        return fixes;
    }
}
