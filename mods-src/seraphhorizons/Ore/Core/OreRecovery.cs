namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>Sulfide roasting: the vanilla firepit (a hand station), the stall roaster, the reverberatory
/// roaster.</summary>
public enum Roaster { Firepit, Stall, Reverberatory }

/// <summary>How a by-product is parted from the main metal: cupellation (silver from lead, and
/// from copper or silver ores with lead added), liquation (tin sweats out first, leaving the lead),
/// acid parting (silver from gold).</summary>
public enum PartingMethod { Cupellation, Liquation, AcidParting }

/// <summary>The design's ore classes: the path an ore takes.</summary>
public enum OreClass { Oxide, Sulfide, Native, Placer }

/// <summary>The forms ore takes on the line. The rock forms (raw ore, crushed and ground ore, and a
/// sulfide's roasted rock) hold the raw ore's units, which its grade and ore set, from one form to the
/// next (#847); the rest hold <see cref="OreRecovery.ConcentrateUnits"/>. A sponge is the gold or
/// silver amalgam leaves in the still once its mercury is driven off (#726).</summary>
public enum OreForm { Raw, Crushed, Ground, RoastedRock, Concentrate, RoastedConcentrate, Sponge }

/// <summary>A by-product of an ore, won only at a parting step.</summary>
/// <param name="Share">Units of it per unit of the main metal recovered. The ore itself carries its
/// share: argentiferous galena 38 %, plain galena 3 % (#690).</param>
public sealed record ByProduct(string Metal, double Share, PartingMethod PartedBy);

/// <summary>One ore's processing properties (<c>ores</c> in the config).</summary>
/// <param name="Ore">The ore part of its code (<c>galena</c>).</param>
/// <param name="Metal">Its main metal (<see cref="OreMetals"/>' group, or the ore itself).</param>
/// <param name="Free">Free gold or silver, which wants amalgamation.</param>
/// <param name="Unparted">The share of the main metal a smelt gives without parting.</param>
public sealed record OreSpec(string Ore, string Metal, OreClass Class, double Density, bool Free, double Unparted,
    IReadOnlyList<ByProduct> ByProducts)
{
    public bool IsSulfide => Class == OreClass.Sulfide;

    /// <summary>Its sulfur, units per unit of its main metal (config <c>ores.{ore}.sulfur</c>, from its
    /// formula); 0 for an ore with none.</summary>
    public double Sulfur { get; init; }
}

/// <summary>A metal and its units out of a smelt (fractional; <see cref="UnitCarry"/> makes items).</summary>
public readonly record struct MetalUnits(string Metal, double Units);

/// <summary>
/// The recovery core (#685, reworked per machine in #765): every station and machine asks it how much metal it
/// keeps, and what a machine draws. Recovery belongs to the machine that did the work; there are no tiers.
/// Recoveries are fractions of metal units; turning units into items, with the fraction carried over to the
/// next item, is <see cref="UnitCarry"/>'s job.
/// <list type="bullet">
/// <item>Concentrators chain on tailings (<see cref="Concentrate"/>): each recovers its figure r (× the ore's
/// density, capped at 1) of the catchable units in its feed, so a line recovers 1 − Π(1 − rᵢ), and nothing
/// from its own kind's tailings (<see cref="WorkedBy"/>).</item>
/// <item>Locked shares apply once (<see cref="LockFactor"/>): the first concentrator splits the stream into
/// what gravity can catch and what it can't, which no number of passes catches. The table puts the locked
/// units out as middlings; a lift (<see cref="Lift"/>: screening, grinding, amalgamating) frees them.</item>
/// <item>The last step of a class's ideal route recovers whatever is left (<see cref="FinalStep"/>): on the
/// whole route the vanner takes all the rest, or marks its concentrate for a later final step, which
/// restores it whole (<see cref="WholeRestore"/>).</item>
/// </list>
/// </summary>
public sealed class OreRecovery
{
    /// <summary>Shaft turns an in-game hour at speed 1 (#711).</summary>
    public const double TurnsPerHourAtSpeed1 = 95.5;

    /// <summary>The speed the machines' rates are given at (#765): 57 turns an hour.</summary>
    public const double ReferenceSpeed = 0.6;

    /// <summary>The ideal route's concentrators, in the period's order (grain size).</summary>
    public static readonly IReadOnlyList<OreMachine> Concentrators =
        [OreMachine.Sluice, OreMachine.Jig, OreMachine.Table, OreMachine.Vanner];

    private readonly OreProcessingConfig _config;
    private readonly Dictionary<string, double> _hand;
    private readonly Dictionary<OreMachine, OreMachineSpec> _machines = new();
    private readonly Dictionary<OreMachine, double> _sulfur = new();
    private readonly Dictionary<OreMachine, double> _nativeSulfur = new();
    private readonly Dictionary<string, double> _smelt;
    private readonly Dictionary<string, double> _smeltByGrade;
    private readonly Dictionary<string, OreSpec> _ores = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];

    public OreRecovery(OreProcessingConfig config)
    {
        _config = config;
        _hand = new(config.HandStations ?? new(), StringComparer.OrdinalIgnoreCase);
        _smelt = new(config.Smelt ?? new(), StringComparer.OrdinalIgnoreCase);
        _smeltByGrade = new(config.SmeltByGrade ?? new(), StringComparer.OrdinalIgnoreCase);

        if (!(config.ConcentrateUnits > 0)) _problems.Add($"concentrateUnits {config.ConcentrateUnits} is not positive");
        if (!(config.SulfurUnitsPerPowder > 0)) _problems.Add($"sulfurUnitsPerPowder {config.SulfurUnitsPerPowder} is not positive");
        if (!(config.NativeSulfurUnits > 0)) _problems.Add($"nativeSulfurUnits {config.NativeSulfurUnits} is not positive");
        foreach (var key in HandKeys) CheckShare("handStations", _hand, key);
        foreach (var key in _hand.Keys.Where(k => !HandKeys.Contains(k))) _problems.Add($"handStations: unknown station '{key}'");
        foreach (var f in Enum.GetValues<OreForm>().Where(f => !IsRock(f))) CheckShare("smelt", _smelt, Key(f));
        foreach (var grade in OreProducts.Grades) CheckShare("smeltByGrade", _smeltByGrade, grade);
        CheckShare("unclassified", config.Unclassified);
        CheckShare("fineUnground", config.FineUnground);
        CheckShare("freeUnamalgamated", config.FreeUnamalgamated);

        var machines = new Dictionary<string, OreProcessingConfig.MachineEntry>(config.Machines ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in machines.Keys)
            if (!TryParse<OreMachine>(key, out _)) _problems.Add($"machines: unknown machine '{key}'");
        foreach (var m in Enum.GetValues<OreMachine>())
        {
            if (!machines.TryGetValue(Key(m), out var e) || e == null)
            {
                _problems.Add($"machines: no entry for '{Key(m)}'");
                e = new OreProcessingConfig.MachineEntry { Recovery = 0 };
            }
            _machines[m] = Spec(m, e);
            if (m == OreMachine.ParkesKettle) ZincReturn = e.ZincReturn;
        }

        foreach (var (ore, entry) in config.Ores ?? new())
            _ores[ore] = Spec(ore, entry ?? new());
        var retort = config.Retort ?? new();
        CheckShare("retort.mercuryReturn", retort.MercuryReturn);
        if (string.IsNullOrWhiteSpace(retort.Mercury)) _problems.Add("retort.mercury: no mercury item");
        if (!(retort.AmalgamMercury * retort.MercuryReturn >= 1))
            _problems.Add($"retort: an amalgam returns {retort.AmalgamMercury * retort.MercuryReturn} portions of mercury, under one");
        foreach (var (code, portions) in retort.Cinnabar ?? new())
            if (!(portions >= 1)) _problems.Add($"retort.cinnabar.{code}: {portions} portions is under one");
        if (!(retort.PortionsPerSecond > 0)) _problems.Add($"retort.portionsPerSecond {retort.PortionsPerSecond} is not positive");

        var cupel = config.Cupel ?? new OreProcessingConfig.CupelEntry();
        Cupel = new CupelSettings(cupel.CapacityUnits, cupel.LeadPerOreUnit, cupel.MeltingPoint, cupel.SecondsPerIngot);
        if (!(Cupel.CapacityUnits > 0)) _problems.Add($"cupel.capacityUnits {Cupel.CapacityUnits} is not positive");
        if (!(Cupel.LeadPerOreUnit >= 0)) _problems.Add($"cupel.leadPerOreUnit {Cupel.LeadPerOreUnit} is negative");
        if (!(Cupel.MeltingPoint > 0)) _problems.Add($"cupel.meltingPoint {Cupel.MeltingPoint} is not positive");
        if (!(Cupel.SecondsPerIngot > 0)) _problems.Add($"cupel.secondsPerIngot {Cupel.SecondsPerIngot} is not positive");

        RichLeadSilver = (config.Bullion ?? new OreProcessingConfig.BullionEntry()).RichLeadSilver;
        if (!(RichLeadSilver > 0 && RichLeadSilver <= 1)) _problems.Add($"bullion.richLeadSilver {RichLeadSilver} is not above 0 and at most 1");

        var pan = config.Liquation ?? new OreProcessingConfig.LiquationEntry();
        Liquation = new LiquationSettings(pan.CapacityUnits, pan.TinPoint, pan.LeadPoint, pan.SecondsPerIngot);
        if (!(Liquation.CapacityUnits > 0)) _problems.Add($"liquation.capacityUnits {Liquation.CapacityUnits} is not positive");
        if (!(Liquation.TinPoint > 0)) _problems.Add($"liquation.tinPoint {Liquation.TinPoint} is not positive");
        if (!(Liquation.LeadPoint > Liquation.TinPoint)) _problems.Add($"liquation.leadPoint {Liquation.LeadPoint} is not above the tin point");
        if (!(Liquation.SecondsPerIngot > 0)) _problems.Add($"liquation.secondsPerIngot {Liquation.SecondsPerIngot} is not positive");
    }

    /// <summary>The <c>handStations</c> keys: the <see cref="HandStation"/>s, the firepit's roast and the
    /// hand parting stations.</summary>
    private static readonly string[] HandKeys =
        [.. Enum.GetValues<HandStation>().Select(h => Key(h)), "firepit", Key(PartingStation.Cupel), Key(PartingStation.LiquationPan)];

    /// <summary>The still's retort (#726): the mercury, what an amalgam holds and returns, what
    /// cinnabar gives, the pace. <see cref="MercuryRetort"/> reads it.</summary>
    public OreProcessingConfig.RetortEntry Retort => _config.Retort ?? new();

    /// <summary>What is wrong with the config, for the server log; empty when it is sound. Missing
    /// figures read as 0 (a device that recovers nothing), so they show in play too.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Units of metal in one concentrate, roasted concentrate, amalgam or sponge item.</summary>
    public double ConcentrateUnits => _config.ConcentrateUnits;

    /// <summary>The bone-ash cupel's figures (#722).</summary>
    public CupelSettings Cupel { get; }

    /// <summary>The share of a rich lead bar's lead that is silver (<c>bullion.richLeadSilver</c>, #740;
    /// <see cref="Bullion"/>).</summary>
    public double RichLeadSilver { get; }

    /// <summary>The clay liquation pan's figures (#724).</summary>
    public LiquationSettings Liquation { get; }

    /// <summary>An ore's properties; an ore the config doesn't list is an oxide at density 1.</summary>
    public OreSpec Ore(string ore) =>
        _ores.TryGetValue(ore, out var spec) ? spec : new OreSpec(ore, OreMetals.MetalOf(ore) ?? ore, OreClass.Oxide, 1, false, 1, []);

    /// <summary>The ores the config lists.</summary>
    public IEnumerable<OreSpec> Ores => _ores.Values;

    // ---- Figures, draws and rates ----

    /// <summary>A machine's figures, draws and rate (<c>machines</c>).</summary>
    public OreMachineSpec Machine(OreMachine m) => _machines[m];

    /// <summary>The Parkes kettle's share of its zinc returned (a rule, #927).</summary>
    public double ZincReturn { get; }

    /// <summary>A machine's recovery figure.</summary>
    public double Figure(OreMachine m) => _machines[m].Recovery;

    /// <summary>A hand station's figure (<c>handStations</c>): the pan's and rocker's concentration, the amalgam
    /// pan's catch.</summary>
    public double Figure(HandStation h) => _hand.GetValueOrDefault(Key(h));

    /// <summary>
    /// A machine's resistance with its job <paramref name="fill"/> full (0 to 1: the breaker's charge, the
    /// reverberatory's bed, the kettle's contents): from empty to working for the three machines whose load
    /// follows the job, the working load for every other (constant once assembled, working or idle). The
    /// unassembled load and the dry-of-oil penalty are the shared machine code's (#711).
    /// </summary>
    public double LoadAt(OreMachine m, double fill)
    {
        var spec = _machines[m];
        return spec.LoadFollowsJob ? spec.EmptyLoad + (spec.Load - spec.EmptyLoad) * Math.Clamp(fill, 0, 1) : spec.Load;
    }

    // ---- The stream: locked shares and lifts ----

    /// <summary>
    /// The share of a stream's units gravity can catch at all (#765): × <c>fineUnground</c> (0.4) for poor ore
    /// not ground, × <c>unclassified</c> (0.85) not classified, × <c>freeUnamalgamated</c> (0.7) for free gold
    /// or silver not amalgamated. Placer is fine and loose already: 1.
    /// </summary>
    public double LockFactor(OreSpec ore, string? grade, OreState state)
    {
        if (ore.Class == OreClass.Placer) return 1;
        double f = 1;
        if (grade == "poor" && !state.HasFlag(OreState.Ground)) f *= _config.FineUnground;
        if (!state.HasFlag(OreState.Classified)) f *= _config.Unclassified;
        if (ore.Free && !state.HasFlag(OreState.Amalgamated)) f *= _config.FreeUnamalgamated;
        return f;
    }

    /// <summary>The units of a stream that are locked: its split's, or what the split would make.</summary>
    public double LockedOf(OreStream s) => s.Locked ?? s.Units * (1 - LockFactor(Ore(s.Ore), s.Grade, s.State));

    /// <summary>
    /// What screening (<see cref="OreState.Classified"/>), grinding (<see cref="OreState.Ground"/>) or
    /// amalgamation (<see cref="OreState.Amalgamated"/>) does to a stream: the flags set and, once it is split,
    /// its locked units released in proportion to the locked share lifted (the ball mill on middlings frees all
    /// of them). A stream that had units released goes back to the head of the concentrators: its worked
    /// kinds are cleared. Losing nothing, it keeps its units.
    /// </summary>
    public OreStream Lift(OreStream s, OreState added)
    {
        var state = s.State | added;
        if (s.Locked is not { } locked || state == s.State) return s with { State = state };
        var ore = Ore(s.Ore);
        double before = 1 - LockFactor(ore, s.Grade, s.State), after = 1 - LockFactor(ore, s.Grade, state);
        double now = before > 0 ? locked * after / before : 0;
        bool released = locked - now > 1e-12;
        return s with { State = state, Locked = now, Worked = released ? WorkedBy.None : s.Worked };
    }

    // ---- Concentration ----

    /// <summary>The kind a concentrator is, for its tailings' record.</summary>
    public static WorkedBy KindOf(OreMachine m) => m switch
    {
        OreMachine.Sluice => WorkedBy.Sluice,
        OreMachine.Jig => WorkedBy.Jig,
        OreMachine.Table => WorkedBy.Table,
        OreMachine.Vanner => WorkedBy.Vanner,
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, "not a concentrator"),
    };

    /// <summary>A concentrator's r on an ore: its figure × the ore's density, capped at 1 (per pass).</summary>
    public double PassShare(OreSpec ore, OreMachine m) => KindOf(m) is var _ ? Math.Clamp(Figure(m) * ore.Density, 0, 1) : 0;

    /// <summary>
    /// Whether a stream is on its class's whole ideal route as it reaches the vanner: classified, ground if poor,
    /// amalgamated if free (no locked share left), and worked by the sluice, jig and table before.
    /// </summary>
    public bool OnWholeRoute(OreStream s)
    {
        var ore = Ore(s.Ore);
        const WorkedBy before = WorkedBy.Sluice | WorkedBy.Jig | WorkedBy.Table;
        return LockFactor(ore, s.Grade, s.State) >= 1 - 1e-12 && LockedOf(s) <= 1e-9
               && (s.Worked & before) == before && !s.Worked.HasFlag(WorkedBy.Vanner);
    }

    /// <summary>
    /// One concentrator's pass (#765). The first concentrator splits the stream (the locked share, once);
    /// the machine recovers its r (<see cref="PassShare"/>) of the catchable units, nothing if its kind
    /// already worked the stream. The table puts the locked units out as middlings. On the whole route the
    /// vanner recovers all that is left when it is the class's final step (<see cref="FinalStep"/>), and
    /// otherwise marks its concentrate <see cref="ConcentratorPass.Whole"/>.
    /// </summary>
    public ConcentratorPass Concentrate(OreMachine m, OreStream feed)
    {
        var kind = KindOf(m);
        var ore = Ore(feed.Ore);
        double locked = LockedOf(feed);
        var split = feed with { Locked = locked };
        if (feed.Worked.HasFlag(kind)) return new ConcentratorPass(0, split, null, false);

        bool whole = m == OreMachine.Vanner && OnWholeRoute(feed);
        bool finalHere = whole && FinalStep(ore) == OreMachine.Vanner;
        double catchable = Math.Max(0, feed.Units - locked);
        double caught = catchable * (finalHere ? 1 : PassShare(ore, m));

        OreStream? middlings = null;
        double left = feed.Units - caught;
        if (m == OreMachine.Table && locked > 1e-12)
        {
            middlings = feed with { Units = locked, Locked = locked, Worked = WorkedBy.None };
            left -= locked;
            locked = 0;
        }
        var tailings = feed with { Units = Math.Max(0, left), Locked = locked, Worked = feed.Worked | kind };
        return new ConcentratorPass(caught, tailings, middlings, whole && !finalHere);
    }

    /// <summary>
    /// A hand concentrator (the gold pan, the rocker) on rock (#765): its figure × the ore's density, capped,
    /// of the catchable units (<see cref="LockFactor"/>). Hand stations do not chain: what it misses is
    /// spent.
    /// </summary>
    public double HandConcentrate(HandStation h, OreStream feed)
    {
        if (h == HandStation.AmalgamPan) throw new ArgumentOutOfRangeException(nameof(h), h, "the amalgam pan amalgamates: Amalgamate");
        double catchable = Math.Max(0, feed.Units - LockedOf(feed));
        return catchable * Math.Clamp(Figure(h) * Ore(feed.Ore).Density, 0, 1);
    }

    /// <summary>The amalgamation plates' pass (#917): see <see cref="Amalgamate(HandStation, OreStream)"/>.</summary>
    public AmalgamPass Amalgamate(OreMachine m, OreStream feed) =>
        m == OreMachine.AmalgamationPlates ? Amalgamate(Figure(m), feed)
            : throw new ArgumentOutOfRangeException(nameof(m), m, "not the amalgamation plates");

    /// <summary>
    /// Amalgamation (the amalgam pan #718, the plates #917): the free gold or silver's × 0.7 lifted, its figure
    /// of the catchable units caught as amalgam, and the rest on, amalgamated. An ore without free metal gives
    /// no amalgam and goes on as it came (flagged).
    /// </summary>
    public AmalgamPass Amalgamate(HandStation h, OreStream feed) =>
        h == HandStation.AmalgamPan ? Amalgamate(Figure(h), feed)
            : throw new ArgumentOutOfRangeException(nameof(h), h, "not the amalgam pan");

    private AmalgamPass Amalgamate(double figure, OreStream feed)
    {
        var lifted = Lift(feed, OreState.Amalgamated);
        if (!Ore(feed.Ore).Free) return new AmalgamPass(0, lifted);
        double catchable = Math.Max(0, lifted.Units - LockedOf(lifted));
        double caught = catchable * Math.Clamp(figure, 0, 1);
        return new AmalgamPass(caught, lifted with { Units = lifted.Units - caught });
    }

    // ---- The ideal route ----

    /// <summary>
    /// The final step of an ore's ideal route (#765), which recovers whatever is left: the vanner for oxides,
    /// placer and free silver; the reverberatory roaster for a sulfide with no by-product; for a by-product
    /// ore its parting machine (the Parkes kettle, with the cupellation furnace, for lead with silver; the
    /// cupellation furnace for the rest it parts; the liquation furnace; the acid parting vessel).
    /// </summary>
    public OreMachine FinalStep(OreSpec ore)
    {
        if (ore.ByProducts.Count > 0)
            return ore.ByProducts[0].PartedBy switch
            {
                PartingMethod.Cupellation => ore.Metal == "lead" ? OreMachine.ParkesKettle : OreMachine.CupellationFurnace,
                PartingMethod.Liquation => OreMachine.LiquationFurnace,
                _ => OreMachine.AcidParting,
            };
        return ore.IsSulfide ? OreMachine.Reverberatory : OreMachine.Vanner;
    }

    /// <summary>The share of the units entering the four concentrators the chain alone recovers: 1 − Π(1 − rᵢ)
    /// with density per pass (98.9 %; smithsonite 97.6 %).</summary>
    public double ChainShare(OreSpec ore) => 1 - Concentrators.Aggregate(1.0, (left, m) => left * (1 - PassShare(ore, m)));

    /// <summary>
    /// What a later final step multiplies a whole-route concentrate's units by to make them whole
    /// (1 / <see cref="ChainShare"/>): the concentrate, or what was made of it (roasted concentrate, bullion),
    /// marked <see cref="ConcentratorPass.Whole"/>. Amalgam is not marked: the plates caught all of theirs.
    /// </summary>
    public double WholeRestore(OreSpec ore) => ChainShare(ore) is > 0 and var c ? 1 / c : 1;

    // ---- Roasting, smelting, parting ----

    /// <summary>The firepit's, stall roaster's or reverberatory roaster's figure.</summary>
    public double RoasterFigure(Roaster r) => r switch
    {
        Roaster.Firepit => _hand.GetValueOrDefault("firepit"),
        Roaster.Stall => Figure(OreMachine.StallRoaster),
        _ => Figure(OreMachine.Reverberatory),
    };

    /// <summary>The share roasting keeps: 1 for an ore that isn't a sulfide, the roaster's figure for one that
    /// is, and 0 for a sulfide not roasted (it doesn't smelt). <paramref name="whole"/>: the concentrate came off
    /// the whole route, and the reverberatory roaster, when it is the ore's final step, makes it whole.</summary>
    public double Roasting(OreSpec ore, Roaster? roaster, bool whole = false)
    {
        if (!ore.IsSulfide) return 1;
        if (roaster is not { } r) return 0;
        double share = RoasterFigure(r);
        return whole && r == Roaster.Reverberatory && FinalStep(ore) == OreMachine.Reverberatory ? share * WholeRestore(ore) : share;
    }

    /// <summary>Units of sulfur in one sulfur powder (config <c>sulfurUnitsPerPowder</c>, 5).</summary>
    public double SulfurUnitsPerPowder => _config.SulfurUnitsPerPowder;

    /// <summary>A roaster's share of a sulfide's sulfur it recovers as sulfur powder (#765): the firepit none
    /// (open roasting loses it to the air), the stall roaster ⅓ (from the heap's crust, #925), the
    /// reverberatory 0.7 (config <c>machines.{roaster}.sulfur</c>).</summary>
    public double RoastSulfur(Roaster r) => r switch
    {
        Roaster.Firepit => 0,
        Roaster.Stall => _sulfur.GetValueOrDefault(OreMachine.StallRoaster),
        _ => _sulfur.GetValueOrDefault(OreMachine.Reverberatory),
    };

    /// <summary>The units of sulfur roasting <paramref name="metalUnits"/> of a sulfide's metal in
    /// <paramref name="roaster"/> recovers: its sulfur a unit of metal (<see cref="OreSpec.Sulfur"/>) × the
    /// roaster's share (<see cref="RoastSulfur"/>); 0 for an ore that is not a sulfide.</summary>
    public double SulfurUnits(OreSpec ore, double metalUnits, Roaster roaster) =>
        ore.IsSulfide && metalUnits > 0 ? metalUnits * ore.Sulfur * RoastSulfur(roaster) : 0;

    /// <summary>Units of sulfur in one native sulfur rock (config <c>nativeSulfurUnits</c>, 13; #938).</summary>
    public double NativeSulfurUnits => _config.NativeSulfurUnits;

    /// <summary>A machine's share of native sulfur rock's sulfur it melts out as sulfur powder (config
    /// <c>machines.{machine}.nativeSulfur</c>: the reverberatory 0.65, #938); 0 for one that does not take it.</summary>
    public double NativeSulfurMelt(OreMachine m) => _nativeSulfur.GetValueOrDefault(m);

    /// <summary>The method a parting station works by.</summary>
    public static PartingMethod MethodOf(PartingStation s) => s switch
    {
        PartingStation.Cupel or PartingStation.CupellationFurnace or PartingStation.ParkesKettle => PartingMethod.Cupellation,
        PartingStation.LiquationPan or PartingStation.LiquationFurnace => PartingMethod.Liquation,
        _ => PartingMethod.AcidParting,
    };

    /// <summary>A parting station's recovery of the by-product: the cupel's and liquation pan's (85 %), the
    /// machines' (cupellation and liquation furnaces and acid parting 95 %, the Parkes kettle with cupellation
    /// 100 %).</summary>
    public double Parting(PartingStation s) => s switch
    {
        PartingStation.Cupel or PartingStation.LiquationPan => _hand.GetValueOrDefault(Key(s)),
        _ => Figure(MachineOf(s)),
    };

    /// <summary>The machine a parting station is, or null for a hand one.</summary>
    public static OreMachine MachineOf(PartingStation s) => s switch
    {
        PartingStation.CupellationFurnace => OreMachine.CupellationFurnace,
        PartingStation.ParkesKettle => OreMachine.ParkesKettle,
        PartingStation.LiquationFurnace => OreMachine.LiquationFurnace,
        PartingStation.AcidParting => OreMachine.AcidParting,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "a hand station"),
    };

    /// <summary>Whether a form is rock (#847): raw, crushed or ground ore, or roasted rock. A slot of
    /// rock holds the same rock at any grade, and its grade is how much metal is in it.</summary>
    public static bool IsRock(OreForm form) => form is OreForm.Raw or OreForm.Crushed or OreForm.Ground or OreForm.RoastedRock;

    /// <summary>The share of its units rock of <paramref name="grade"/> gives in a furnace (config
    /// <c>smeltByGrade</c>: poor 10 %, medium 25 %, rich 40 %, bountiful 50 %); 0 for no grade.</summary>
    public double GradeShare(string? grade) => grade != null ? _smeltByGrade.GetValueOrDefault(grade) : 0;

    /// <summary>
    /// The share of its units a form gives in a furnace (#847). Rock smelts by its grade
    /// (<see cref="GradeShare"/>), whether raw, crushed or ground: the furnace slags the rock off and
    /// loses metal with it, so how rich the ore is matters, not how fine. Sulfide rock does not smelt
    /// until roasted, and roasted rock smelts at its grade's share times its roaster's
    /// (<see cref="Roasting"/>: the firepit's 85 %, the stall roaster's 92 %). Concentrate, roasted concentrate
    /// and sponge are config <c>smelt</c> (1), a sulfide's concentrate nothing until roasted (#720).
    /// </summary>
    public double SmeltShare(OreSpec ore, OreForm form, string? grade = null, Roaster rockRoaster = Roaster.Firepit) => form switch
    {
        OreForm.Raw or OreForm.Crushed or OreForm.Ground => ore.IsSulfide ? 0 : GradeShare(grade),
        OreForm.RoastedRock => ore.IsSulfide ? GradeShare(grade) * Roasting(ore, rockRoaster) : 0,
        OreForm.Concentrate when ore.IsSulfide => 0,
        _ => _smelt.GetValueOrDefault(Key(form)),
    };

    /// <summary>
    /// What smelting and parting give from concentrate holding <paramref name="mainUnits"/> of the main metal.
    /// Unparted (<paramref name="station"/> null, or a station that doesn't part the ore's by-products): the
    /// main metal × the ore's <c>unparted</c> share, by-products lost. Parted: all of the main metal, plus each
    /// by-product the station's method parts, at its share × the station's recovery. At the ore's final step
    /// (<see cref="FinalStep"/>) with <paramref name="whole"/> units (off the whole route), the main metal is
    /// made whole (<see cref="WholeRestore"/>) and the by-product comes out whole with it.
    /// </summary>
    public IReadOnlyList<MetalUnits> Smelted(OreSpec ore, double mainUnits, PartingStation? station, bool whole = false)
    {
        if (station is not { } s || !ore.ByProducts.Any(bp => bp.PartedBy == MethodOf(s)))
            return [new MetalUnits(ore.Metal, mainUnits * ore.Unparted)];
        bool final = whole && s is not (PartingStation.Cupel or PartingStation.LiquationPan) && FinalStep(ore) == MachineOf(s);
        double main = final ? mainUnits * WholeRestore(ore) : mainUnits;
        double p = final ? 1 : Parting(s);
        var parted = new List<MetalUnits> { new(ore.Metal, main) };
        foreach (var bp in ore.ByProducts)
            if (bp.PartedBy == MethodOf(s))
                parted.Add(new MetalUnits(bp.Metal, main * bp.Share * p));
        return parted;
    }

    /// <summary>The config key of an enum value: its name in lower case (<c>stampbattery</c>).</summary>
    public static string Key<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static bool TryParse<T>(string s, out T value) where T : struct, Enum =>
        Enum.TryParse(s, ignoreCase: true, out value) && Enum.IsDefined(value) && !int.TryParse(s, out _);

    /// <summary>What a machine counts: the reverberatory roaster concentrate, the parting machines units of
    /// metal, every other rock.</summary>
    private static string ItemOf(OreMachine m) => m switch
    {
        OreMachine.Reverberatory => "concentrate",
        OreMachine.CupellationFurnace or OreMachine.ParkesKettle or OreMachine.LiquationFurnace or OreMachine.AcidParting => "unit",
        _ => "rock",
    };

    private OreMachineSpec Spec(OreMachine m, OreProcessingConfig.MachineEntry e)
    {
        string k = $"machines.{Key(m)}";
        CheckShare($"{k}.recovery", e.Recovery);
        if (!(e.Load >= 0)) _problems.Add($"{k}.load {e.Load} is negative");
        if (!(e.EmptyLoad >= 0 && e.EmptyLoad <= e.Load)) _problems.Add($"{k}.emptyLoad {e.EmptyLoad} is not between 0 and the load");
        if (!(e.Water >= 0)) _problems.Add($"{k}.water {e.Water} is negative");
        if (!(e.ZincReturn >= 0 && e.ZincReturn <= 1)) _problems.Add($"{k}.zincReturn {e.ZincReturn} is not between 0 and 1");
        if (!(e.Sulfur >= 0 && e.Sulfur <= 1)) _problems.Add($"{k}.sulfur {e.Sulfur} is not between 0 and 1");
        _sulfur[m] = Math.Clamp(e.Sulfur, 0, 1);
        if (!(e.NativeSulfur >= 0 && e.NativeSulfur <= 1)) _problems.Add($"{k}.nativeSulfur {e.NativeSulfur} is not between 0 and 1");
        _nativeSulfur[m] = Math.Clamp(e.NativeSulfur, 0, 1);
        bool batch = e.Charge > 0;
        if (batch && !(e.ChargeHours > 0)) _problems.Add($"{k}.chargeHours {e.ChargeHours} is not positive");
        if (!batch && !(e.PerHour > 0)) _problems.Add($"{k}: no perHour or charge");
        double perHour = batch ? (e.ChargeHours > 0 ? e.Charge / e.ChargeHours : 0) : e.PerHour;
        bool follows = m is OreMachine.JawBreaker or OreMachine.Reverberatory or OreMachine.ParkesKettle;
        return new OreMachineSpec(e.Recovery, e.Load, e.EmptyLoad, follows, e.Water, perHour,
            batch ? e.Charge : 0, batch ? e.ChargeHours : 0, ItemOf(m));
    }

    private OreSpec Spec(string ore, OreProcessingConfig.OreEntry e)
    {
        var cls = OreClass.Oxide;
        if (e.Class is { } c && !TryParse(c, out cls)) _problems.Add($"ores.{ore}: unknown class '{c}'");
        if (!(e.Density > 0)) _problems.Add($"ores.{ore}: density {e.Density} is not positive");
        CheckShare($"ores.{ore}.unparted", e.Unparted);
        var byProducts = new List<ByProduct>();
        foreach (var b in e.ByProducts ?? [])
        {
            if (!TryParse(b.PartedBy, out PartingMethod method))
            {
                _problems.Add($"ores.{ore}: by-product {b.Metal} has unknown partedBy '{b.PartedBy}'");
                continue;
            }
            if (!(b.Share >= 0)) _problems.Add($"ores.{ore}: by-product {b.Metal} has a negative share");
            byProducts.Add(new ByProduct(b.Metal, b.Share, method));
        }
        if (byProducts.Select(b => b.PartedBy).Distinct().Count() > 1)
            _problems.Add($"ores.{ore}: by-products parted by more than one method");
        if (!(e.Sulfur >= 0)) _problems.Add($"ores.{ore}: sulfur {e.Sulfur} is negative");
        return new OreSpec(ore, OreMetals.MetalOf(ore) ?? ore, cls, e.Density, e.Free, e.Unparted, byProducts) { Sulfur = Math.Max(0, e.Sulfur) };
    }

    private void CheckShare(string table, Dictionary<string, double> values, string key)
    {
        if (!values.TryGetValue(key, out var v)) _problems.Add($"{table}: no figure for '{key}'");
        else CheckShare($"{table}.{key}", v);
    }

    private void CheckShare(string name, double v)
    {
        if (!(v >= 0 && v <= 1)) _problems.Add($"{name}: {v} is not between 0 and 1");
    }
}
