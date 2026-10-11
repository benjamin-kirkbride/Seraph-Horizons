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
    public int MaxFallBlocks { get; set; } = 2;

    /// <summary>The highest step it goes up on a path, in blocks: one it walks, two it climbs
    /// (<see cref="EidolonClimb"/>; the climb animation is made for two, so no more).</summary>
    public int MaxClimbBlocks { get; set; } = 2;

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

    // ---- Felling (#677; README "Eidolon", felling) ----

    /// <summary>Felling an area, the fewest logs a tree has to be grown: younger ones are left
    /// standing (a sapling is no log at all).</summary>
    public int FellMinLogs { get; set; } = 5;

    // ---- Chunk loading (README "Eidolon", chunk loading; EidolonChunks) ----

    /// <summary>An eidolon with work left in its order (fell, haul, crew, carry; not following,
    /// staying, guarding, docking, slumped or dry) keeps its chunks loaded, so it works on with no
    /// player near. Off, it works only while a player keeps them loaded.</summary>
    public bool KeepWorkChunksLoaded { get; set; } = true;

    /// <summary>Working, the columns it holds round the one it stands in: 1 is its own and the 8 round it.</summary>
    public int ChunkLoadRadius { get; set; } = 1;

    /// <summary>Working, its order's area (and machine) is held too while it covers at most this many
    /// columns; a larger one is worked where it stands.</summary>
    public int MaxWorkAreaChunks { get; set; } = 16;

    // ---- Fittings (the layers' stats; Eidolon/README.md, "Fittings") ----
    // Every multiplier is against iron and they stack by multiplying (EidolonStats). Bones and
    // sash are looks only.

    /// <summary>An all-steel eidolon's most health (steel joints, plates and skirt): the base the
    /// multipliers scale is this over the three steel multipliers, so changing one keeps this.</summary>
    public double AllSteelHealth { get; set; } = 300;

    /// <summary>Health by the plates' metal.</summary>
    public PlateMetalFactors PlateHealth { get; set; } = new() { Iron = 1, Steel = 1.5, Stainless = 2, Brass = 0.8 };

    /// <summary>Health by the joints' metal.</summary>
    public MetalFactors JointHealth { get; set; } = new() { Iron = 1, Steel = 1.5, Stainless = 2 };

    /// <summary>Health by the chain skirt's metal.</summary>
    public MetalFactors SkirtHealth { get; set; } = new() { Iron = 1, Steel = 1.25, Stainless = 1.5 };

    /// <summary>The punches', kicks' and slams' damage by the plates' metal (brass hits softer).</summary>
    public PlateMetalFactors PlateDamage { get; set; } = new() { Iron = 1, Steel = 1, Stainless = 1, Brass = 0.8 };

    /// <summary>Walking and running speed by the plates' metal (brass is lighter); work and strikes
    /// keep their pace.</summary>
    public PlateMetalFactors PlateMoveSpeed { get; set; } = new() { Iron = 1, Steel = 1, Stainless = 1, Brass = 1.25 };

    /// <summary>Armour by the plates' metal, as a full suit of the game's plate armour of that metal
    /// (itemtypes/wearable/seraph/armor.json): iron and steel the game's; the game has no stainless
    /// or brass plate armour, so stainless one step above steel and brass with the bronzes.</summary>
    public PlateArmourByMetal PlateArmour { get; set; } = new();

    /// <summary>How armour weakens against a stronger attack, per damage tier: the game's plate
    /// armour's (its defaultProtLoss, highDamageTierResistant).</summary>
    public ArmourTierLoss ArmourLoss { get; set; } = new();

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
        Check(nameof(MaxClimbBlocks), MaxClimbBlocks, 1, 2, () => MaxClimbBlocks = Defaults.MaxClimbBlocks, Defaults.MaxClimbBlocks);
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

        Check(nameof(FellMinLogs), FellMinLogs, 1, 1000, () => FellMinLogs = Defaults.FellMinLogs, Defaults.FellMinLogs);

        Check(nameof(ChunkLoadRadius), ChunkLoadRadius, 0, EidolonChunks.MaxRadius, () => ChunkLoadRadius = Defaults.ChunkLoadRadius, Defaults.ChunkLoadRadius);
        Check(nameof(MaxWorkAreaChunks), MaxWorkAreaChunks, 0, 256, () => MaxWorkAreaChunks = Defaults.MaxWorkAreaChunks, Defaults.MaxWorkAreaChunks);

        Check(nameof(AllSteelHealth), AllSteelHealth, 1, 100000, () => AllSteelHealth = Defaults.AllSteelHealth, Defaults.AllSteelHealth);
        PlateHealth = SanitiseFactors(nameof(PlateHealth), PlateHealth, Defaults.PlateHealth, fixes);
        JointHealth = SanitiseFactors(nameof(JointHealth), JointHealth, Defaults.JointHealth, fixes);
        SkirtHealth = SanitiseFactors(nameof(SkirtHealth), SkirtHealth, Defaults.SkirtHealth, fixes);
        PlateDamage = SanitiseFactors(nameof(PlateDamage), PlateDamage, Defaults.PlateDamage, fixes);
        PlateMoveSpeed = SanitiseFactors(nameof(PlateMoveSpeed), PlateMoveSpeed, Defaults.PlateMoveSpeed, fixes);
        PlateArmour = PlateArmourByMetal.Sanitise(PlateArmour, fixes);
        ArmourLoss = ArmourTierLoss.Sanitise(ArmourLoss, fixes);
        return fixes;
    }

    // A multiplier from 0.01 to 100; a missing table is the default's.
    private static T SanitiseFactors<T>(string name, T? factors, T defaults, List<string> fixes) where T : MetalFactors, new()
    {
        if (factors == null)
        {
            fixes.Add($"{name} is missing, using the defaults");
            factors = new T();
            defaults.CopyTo(factors);
            return factors;
        }
        foreach (var (metal, value, set, fallback) in factors.Entries(defaults))
            if (!double.IsFinite(value) || value < 0.01 || value > 100)
            {
                fixes.Add($"{name} {metal} {value} is out of range, using {fallback}");
                set(fallback);
            }
        return factors;
    }
}

/// <summary>A multiplier for each metal the joints and the skirt come in, against iron.</summary>
public class MetalFactors
{
    public double Iron { get; set; } = 1;
    public double Steel { get; set; } = 1;
    public double Stainless { get; set; } = 1;

    /// <summary>The multiplier for a metal code (<see cref="EidolonFittings"/>'s); 1 for none or an
    /// unknown one.</summary>
    public virtual double For(string? metal) => metal switch
    {
        EidolonFittings.Iron => Iron,
        EidolonFittings.Steel => Steel,
        EidolonFittings.Stainless => Stainless,
        _ => 1,
    };

    /// <summary>Each metal's value with a setter and its default, for sanitising.</summary>
    internal virtual IEnumerable<(string Metal, double Value, Action<double> Set, double Default)> Entries(MetalFactors defaults) =>
    [
        (nameof(Iron), Iron, v => Iron = v, defaults.Iron),
        (nameof(Steel), Steel, v => Steel = v, defaults.Steel),
        (nameof(Stainless), Stainless, v => Stainless = v, defaults.Stainless),
    ];

    internal virtual void CopyTo(MetalFactors other)
    {
        other.Iron = Iron;
        other.Steel = Steel;
        other.Stainless = Stainless;
    }
}

/// <summary>A multiplier for each metal the plates come in, against iron: the joints' and brass.</summary>
public class PlateMetalFactors : MetalFactors
{
    public double Brass { get; set; } = 1;

    public override double For(string? metal) => metal == EidolonFittings.Brass ? Brass : base.For(metal);

    internal override IEnumerable<(string Metal, double Value, Action<double> Set, double Default)> Entries(MetalFactors defaults) =>
        base.Entries(defaults).Append((nameof(Brass), Brass, v => Brass = v, ((PlateMetalFactors)defaults).Brass));

    internal override void CopyTo(MetalFactors other)
    {
        base.CopyTo(other);
        if (other is PlateMetalFactors plate)
            plate.Brass = Brass;
    }
}

/// <summary>One metal's armour, as the game's <c>protectionModifiers</c>: the highest damage tier it
/// fully protects against, the share of a hit it takes away and the flat damage it takes off first.</summary>
public class PlateArmour
{
    public int ProtectionTier { get; set; }
    public double RelativeProtection { get; set; }
    public double FlatDamageReduction { get; set; }

    public PlateArmour() { }

    public PlateArmour(int tier, double relative, double flat)
    {
        ProtectionTier = tier;
        RelativeProtection = relative;
        FlatDamageReduction = flat;
    }
}

/// <summary>Armour by the plates' metal.</summary>
public class PlateArmourByMetal
{
    public PlateArmour Iron { get; set; } = new(3, 0.96, 1.7);
    public PlateArmour Steel { get; set; } = new(4, 0.97, 1.8);
    public PlateArmour Stainless { get; set; } = new(5, 0.98, 1.9);
    public PlateArmour Brass { get; set; } = new(2, 0.92, 1.6);

    private static readonly PlateArmourByMetal Defaults = new();

    /// <summary>The armour for a plate metal code, or null for none or an unknown one.</summary>
    public PlateArmour? For(string? metal) => metal switch
    {
        EidolonFittings.Iron => Iron,
        EidolonFittings.Steel => Steel,
        EidolonFittings.Stainless => Stainless,
        EidolonFittings.Brass => Brass,
        _ => null,
    };

    internal static PlateArmourByMetal Sanitise(PlateArmourByMetal? armour, List<string> fixes)
    {
        if (armour == null)
        {
            fixes.Add($"{nameof(EidolonConfig.PlateArmour)} is missing, using the defaults");
            return new PlateArmourByMetal();
        }
        armour.Iron = One(nameof(Iron), armour.Iron, Defaults.Iron, fixes);
        armour.Steel = One(nameof(Steel), armour.Steel, Defaults.Steel, fixes);
        armour.Stainless = One(nameof(Stainless), armour.Stainless, Defaults.Stainless, fixes);
        armour.Brass = One(nameof(Brass), armour.Brass, Defaults.Brass, fixes);
        return armour;
    }

    private static PlateArmour One(string metal, PlateArmour? a, PlateArmour d, List<string> fixes)
    {
        string name = $"{nameof(EidolonConfig.PlateArmour)} {metal}";
        if (a == null)
        {
            fixes.Add($"{name} is missing, using the default");
            return new PlateArmour(d.ProtectionTier, d.RelativeProtection, d.FlatDamageReduction);
        }
        if (a.ProtectionTier < 0 || a.ProtectionTier > 10)
        {
            fixes.Add($"{name} ProtectionTier {a.ProtectionTier} is out of range, using {d.ProtectionTier}");
            a.ProtectionTier = d.ProtectionTier;
        }
        if (!double.IsFinite(a.RelativeProtection) || a.RelativeProtection < 0 || a.RelativeProtection > 1)
        {
            fixes.Add($"{name} RelativeProtection {a.RelativeProtection} is out of range, using {d.RelativeProtection}");
            a.RelativeProtection = d.RelativeProtection;
        }
        if (!double.IsFinite(a.FlatDamageReduction) || a.FlatDamageReduction < 0 || a.FlatDamageReduction > 100)
        {
            fixes.Add($"{name} FlatDamageReduction {a.FlatDamageReduction} is out of range, using {d.FlatDamageReduction}");
            a.FlatDamageReduction = d.FlatDamageReduction;
        }
        return a;
    }
}

/// <summary>
/// What armour loses for each tier of an attack's damage tier, as the game's armour does: for each
/// tier from 1 to the attack's, the flat reduction is cut by a loss and the relative protection
/// multiplied by one less a loss, each the "within" loss for a tier up to the armour's protection
/// tier and the "above" loss for a tier past it (halved for armour resistant to high tiers, which
/// the game's plate armour is). The defaults are the game's plate armour's
/// (armor.json <c>defaultProtLoss</c>: relative [0.03, 0.15], flat [0.1, 0.2]).
/// </summary>
public class ArmourTierLoss
{
    public double RelativeLossWithinTier { get; set; } = 0.03;
    public double RelativeLossAboveTier { get; set; } = 0.15;
    public double FlatLossWithinTier { get; set; } = 0.1;
    public double FlatLossAboveTier { get; set; } = 0.2;
    public bool HighDamageTierResistant { get; set; } = true;

    private static readonly ArmourTierLoss Defaults = new();

    internal static ArmourTierLoss Sanitise(ArmourTierLoss? loss, List<string> fixes)
    {
        if (loss == null)
        {
            fixes.Add($"{nameof(EidolonConfig.ArmourLoss)} is missing, using the defaults");
            return new ArmourTierLoss();
        }
        void Check(string name, double value, double hi, Action<double> set, double fallback)
        {
            if (!double.IsFinite(value) || value < 0 || value > hi)
            {
                fixes.Add($"{nameof(EidolonConfig.ArmourLoss)} {name} {value} is out of range, using {fallback}");
                set(fallback);
            }
        }
        Check(nameof(RelativeLossWithinTier), loss.RelativeLossWithinTier, 1, v => loss.RelativeLossWithinTier = v, Defaults.RelativeLossWithinTier);
        Check(nameof(RelativeLossAboveTier), loss.RelativeLossAboveTier, 1, v => loss.RelativeLossAboveTier = v, Defaults.RelativeLossAboveTier);
        Check(nameof(FlatLossWithinTier), loss.FlatLossWithinTier, 100, v => loss.FlatLossWithinTier = v, Defaults.FlatLossWithinTier);
        Check(nameof(FlatLossAboveTier), loss.FlatLossAboveTier, 100, v => loss.FlatLossAboveTier = v, Defaults.FlatLossAboveTier);
        return loss;
    }
}
