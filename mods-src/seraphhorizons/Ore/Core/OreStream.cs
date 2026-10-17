namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>The ore line's machines (#765, epic #684): one real piece of equipment each, no tiers.
/// Their figures, draws and rates are <c>machines</c> in the config (<see cref="OreRecovery.Machine"/>).</summary>
public enum OreMachine
{
    JawBreaker, StampBattery, Arrastra, BallMill, AmalgamationPlates, Sluice, Jig, Table, Vanner,
    StallRoaster, Reverberatory, CupellationFurnace, ParkesKettle, LiquationFurnace, AcidParting,
}

/// <summary>The hand stations with a recovery figure: the gold pan and the rocker concentrate, the amalgam pan
/// catches free gold and silver (<c>handStations</c> in the config). The riddle only classifies (a lift, no
/// figure); the firepit's roast is <see cref="Roaster.Firepit"/>, the cupel's and the liquation pan's parting
/// <see cref="PartingStation"/>.</summary>
public enum HandStation { Pan, Rocker, AmalgamPan }

/// <summary>Where a by-product is parted from the main metal: by hand (the bone-ash cupel, the clay liquation
/// pan) or by a machine. <see cref="ParkesKettle"/> is the kettle's rich lead through the cupellation
/// furnace.</summary>
public enum PartingStation { Cupel, LiquationPan, CupellationFurnace, ParkesKettle, LiquationFurnace, AcidParting }

/// <summary>What has been done to rock on its way to the concentrators (#765), the stack attribute
/// <c>oreState</c>. Each flag lifts one locked share (<see cref="OreRecovery.LockFactor"/>). The values are
/// stable: other code writes them.</summary>
[Flags]
public enum OreState
{
    None = 0,
    /// <summary>Screened: the stamp battery's or ball mill's screen, or the riddle. Lifts × 0.85.</summary>
    Classified = 1,
    /// <summary>Ground: the arrastra or ball mill (ground ore's own item). Lifts poor ore's × 0.4.</summary>
    Ground = 2,
    /// <summary>Over the amalgamation plates or through the amalgam pan. Lifts free metal's × 0.7.</summary>
    Amalgamated = 4,
}

/// <summary>The concentrator kinds that have worked a stream (its tailings, #765): a machine recovers nothing
/// from its own kind's tailings. Stable values (the stack attribute <c>oreWorked</c>).</summary>
[Flags]
public enum WorkedBy { None = 0, Sluice = 1, Jig = 2, Table = 4, Vanner = 8 }

/// <summary>
/// One item's ore on the line (#765): a rock form, a concentrator's tailings or the table's middlings.
/// </summary>
/// <param name="Ore">The ore part of its code (<c>galena</c>).</param>
/// <param name="Grade">Its grade (<c>poor</c> .. <c>bountiful</c>), or <c>-</c> for ore with none (placer).</param>
/// <param name="Units">Metal units the item holds, catchable and locked together.</param>
/// <param name="Locked">Units gravity cannot catch; null until the first concentrator splits the stream, which
/// applies the locked share once (<see cref="OreRecovery.LockFactor"/>).</param>
/// <param name="Rock">The host rock, carried for the tailings' look (#694); null if unknown.</param>
public sealed record OreStream(string Ore, string Grade, double Units, double? Locked = null,
    OreState State = OreState.None, WorkedBy Worked = WorkedBy.None, string? Rock = null)
{
    /// <summary>A rock item's stream: <paramref name="units"/> (the grade's, <see cref="OreProducts.RawUnits"/>,
    /// when null), not yet split. Ground ore is <see cref="OreState.Ground"/> by its form.</summary>
    public static OreStream FromRock(string ore, string grade, OreForm form, OreState state = OreState.None,
        double? units = null, string? rock = null) =>
        new(ore, grade, units ?? OreProducts.RawUnits(ore, grade) ?? 0, null,
            form == OreForm.Ground ? state | OreState.Ground : state, WorkedBy.None, rock);
}

/// <summary>What a concentrator's pass gives (<see cref="OreRecovery.Concentrate"/>).</summary>
/// <param name="Concentrate">Units into concentrate (a <see cref="UnitCarry"/> makes items of them).</param>
/// <param name="Tailings">What goes on, carrying the rest.</param>
/// <param name="Middlings">The table's: the locked units, out to the ball mill; null from any other machine
/// or with nothing locked.</param>
/// <param name="Whole">The concentrate came off the whole ideal route of an ore whose final step is later
/// (a sulfide's reverberatory roaster, a by-product ore's parting): mark it (<c>oreWhole</c>) so that step
/// restores it whole (<see cref="OreRecovery.WholeRestore"/>).</param>
public sealed record ConcentratorPass(double Concentrate, OreStream Tailings, OreStream? Middlings, bool Whole);

/// <summary>What amalgamation gives (<see cref="OreRecovery.Amalgamate(OreMachine, OreStream)"/>).</summary>
/// <param name="Amalgam">Units caught as amalgam.</param>
/// <param name="Rest">The rest, amalgamated (its × 0.7 lifted), on to the concentrators.</param>
public sealed record AmalgamPass(double Amalgam, OreStream Rest);

/// <summary>A machine's figures (<c>machines</c> in the config, #765, #940).</summary>
/// <param name="Recovery">Its recovery: a concentrator's r, a roaster's or parting machine's share, the plates'
/// catch; 1 for the machines that lose nothing (breaker, battery, arrastra, ball mill).</param>
/// <param name="Load">Its working resistance (0.1 a period horsepower).</param>
/// <param name="EmptyLoad">Its resistance empty (a fifth of the working one).</param>
/// <param name="LoadFollowsJob">The jaw breaker, reverberatory roaster and Parkes kettle: the load rises with the
/// job (<see cref="OreRecovery.LoadAt"/>); every other machine's is constant.</param>
/// <param name="WaterPerItem">Litres an item (<see cref="Item"/>), from its own reservoir; 0 for a dry machine.</param>
/// <param name="ItemsPerHour">Items an in-game hour at the reference speed.</param>
/// <param name="ChargeItems">A batch machine's charge (items, or units for the Parkes kettle); 0 if continuous.</param>
/// <param name="ChargeHours">A batch machine's hours a charge at the reference speed; 0 if continuous.</param>
/// <param name="Item">What it counts: <c>rock</c>, <c>concentrate</c> or <c>unit</c> (of metal).</param>
public sealed record OreMachineSpec(double Recovery, double Load, double EmptyLoad, bool LoadFollowsJob,
    double WaterPerItem, double ItemsPerHour, int ChargeItems, double ChargeHours, string Item)
{
    public bool Batch => ChargeItems > 0;

    /// <summary>Shaft turns an item at any speed: the rate is in proportion to shaft speed (#711).</summary>
    public double TurnsPerItem => ItemsPerHour > 0 ? OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed / ItemsPerHour : 0;

    /// <summary>Shaft turns a charge, for a batch machine.</summary>
    public double TurnsPerCharge => OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed * ChargeHours;
}
