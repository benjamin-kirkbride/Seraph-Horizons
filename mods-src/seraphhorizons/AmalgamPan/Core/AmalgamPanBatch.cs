namespace SeraphHorizons.Mod.AmalgamPan.Core;

/// <summary>The model's <c>requires</c> values the pan sets as its contents stand (the stages',
/// <c>muller</c> and <c>shoes</c>, are the staged build's).</summary>
public static class AmalgamPanRequires
{
    public const string Muller = "muller";
    public const string Shoes = "shoes";
    public const string Mercury = "mercury";
    public const string Slurry = "slurry";
    public const string Water = "water";
    public const string Amalgam = "amalgam";

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string> { Muller, Shoes, Mercury, Slurry, Water, Amalgam };

    /// <summary>The contents' values, the batch's to say (<see cref="AmalgamPanBatch.Shows"/>).</summary>
    public static bool IsContents(string? requires) => requires is Mercury or Slurry or Water or Amalgam;
}

/// <summary>Why something cannot go into the pan, or why it will not work.</summary>
public enum PanRefusal
{
    None,
    /// <summary>The muller and shoes are not both fitted.</summary>
    Incomplete,
    /// <summary>A worked charge waits to be taken out (the amalgam and the quartz).</summary>
    Worked,
    /// <summary>Not gold or silver quartz (an ore with no free metal), or not crushed or ground.</summary>
    NotFreeQuartz,
    /// <summary>The quartz has been amalgamated already.</summary>
    AlreadyAmalgamated,
    /// <summary>The pan holds a charge of other quartz (another ore, grade or state).</summary>
    OtherCharge,
    /// <summary>The charge is full.</summary>
    ChargeFull,
    /// <summary>The mercury is at the pan's capacity.</summary>
    MercuryFull,
    /// <summary>The water is at the charge's need.</summary>
    WaterFull,
    /// <summary>Nothing to work: no quartz in the pan.</summary>
    NoCharge,
    /// <summary>The charge wants its water.</summary>
    NoWater,
    /// <summary>Not enough mercury for the amalgam the charge will make.</summary>
    NoMercury,
    /// <summary>The charge is being worked: it stays until it is done.</summary>
    Busy,
}

/// <summary>What a worked charge gives: the amalgam items (made by the carry) and the mercury they took.</summary>
public readonly record struct PanYield(int AmalgamItems, int MercuryUsed);

/// <summary>
/// One amalgam pan's batch (#718): the mercury in it, the charge of crushed gold or silver quartz (its
/// item count; the stack itself is the game side's), the water poured over it and the muller's turns
/// on it. Game-independent; the amalgam's units come from ore processing's recovery, given here as a
/// number.
/// <list type="bullet">
/// <item>Mercury is poured in (up to <see cref="AmalgamPanConfig.MercuryCapacityPortions"/>) and stays
/// between charges; only an amalgam made takes any, <c>amalgamMercury</c> portions an item.</item>
/// <item>A charge is one kind of quartz, up to <see cref="AmalgamPanConfig.ChargeItems"/>; it wants
/// <see cref="AmalgamPanConfig.WaterLitresPerCharge"/> of water and the mercury its amalgam will take,
/// and then <see cref="AmalgamPanConfig.TurnsPerItem"/> turns of the muller an item.</item>
/// <item>Worked, the charge's amalgam is made and the pan shows it until the player takes it out with
/// the worked quartz, which goes on to the rocker; the water is spent.</item>
/// </list>
/// </summary>
public sealed class AmalgamPanBatch
{
    private const double Epsilon = 1e-9;

    public int Mercury { get; private set; }
    public int Items { get; private set; }
    public double Water { get; private set; }
    public double Turns { get; private set; }
    /// <summary>The charge has been worked: its amalgam and quartz wait to be taken out.</summary>
    public bool Worked { get; private set; }

    public bool Charged => Items > 0 && !Worked;
    /// <summary>A charge loaded and not yet turned: it may still be taken back.</summary>
    public bool Untouched => Turns <= Epsilon;

    public static AmalgamPanBatch Restore(int mercury, int items, double water, double turns, bool worked) => new()
    {
        Mercury = Math.Max(0, mercury),
        Items = Math.Max(0, items),
        Water = double.IsFinite(water) ? Math.Max(0, water) : 0,
        Turns = double.IsFinite(turns) ? Math.Max(0, turns) : 0,
        Worked = worked && items > 0,
    };

    /// <summary>Whether a rig content part needing <paramref name="requires"/> is drawn: the amalgam
    /// once worked; the slurry while charged, with the water film over it once it has water; the mercury
    /// pool on its own with no charge.</summary>
    public bool Shows(string? requires) => requires switch
    {
        AmalgamPanRequires.Amalgam => Worked,
        AmalgamPanRequires.Slurry => Charged,
        AmalgamPanRequires.Water => Charged && Water > Epsilon,
        AmalgamPanRequires.Mercury => Items == 0 && Mercury > 0,
        _ => false,
    };

    /// <summary>Portions of mercury that fit.</summary>
    public int MercuryRoom(AmalgamPanConfig config) => Math.Max(0, config.MercuryCapacityPortions - Mercury);

    public PanRefusal CanPourMercury(bool complete, AmalgamPanConfig config) =>
        !complete ? PanRefusal.Incomplete : MercuryRoom(config) <= 0 ? PanRefusal.MercuryFull : PanRefusal.None;

    public void AddMercury(int portions) => Mercury += Math.Max(0, portions);

    /// <summary>Takes up to <paramref name="portions"/> of mercury back out (into a container), only
    /// with no charge in the pan; returns what came out.</summary>
    public int TakeMercury(int portions)
    {
        if (Items > 0)
            return 0;
        int taken = Math.Clamp(portions, 0, Mercury);
        Mercury -= taken;
        return taken;
    }

    /// <summary>Litres of water that fit over the charge.</summary>
    public double WaterRoom(AmalgamPanConfig config) => Math.Max(0, config.WaterLitresPerCharge - Water);

    public PanRefusal CanPourWater(bool complete, AmalgamPanConfig config) =>
        !complete ? PanRefusal.Incomplete
        : Worked ? PanRefusal.Worked
        : WaterRoom(config) <= Epsilon ? PanRefusal.WaterFull
        : PanRefusal.None;

    public void AddWater(double litres) => Water += Math.Max(0, litres);

    /// <summary>Whether quartz can go in: <paramref name="free"/> an ore with free metal in a rock form
    /// the stream reads, <paramref name="amalgamated"/> already through the pan or the plates,
    /// <paramref name="sameAsCharge"/> whether it stacks with the charge already in (true when none).</summary>
    public PanRefusal CanLoad(bool complete, bool free, bool amalgamated, bool sameAsCharge, AmalgamPanConfig config)
    {
        if (!complete) return PanRefusal.Incomplete;
        if (Worked) return PanRefusal.Worked;
        if (!free) return PanRefusal.NotFreeQuartz;
        if (amalgamated) return PanRefusal.AlreadyAmalgamated;
        if (Items > 0 && !Untouched) return PanRefusal.Busy;
        if (Items > 0 && !sameAsCharge) return PanRefusal.OtherCharge;
        if (Items >= config.ChargeItems) return PanRefusal.ChargeFull;
        return PanRefusal.None;
    }

    /// <summary>How many of <paramref name="held"/> go in.</summary>
    public int LoadCount(int held, AmalgamPanConfig config) => Math.Clamp(config.ChargeItems - Items, 0, Math.Max(0, held));

    public void Load(int items) => Items += Math.Max(0, items);

    /// <summary>Takes an untouched charge back out; returns the items.</summary>
    public int Unload()
    {
        if (Items == 0 || Worked || !Untouched)
            return 0;
        int items = Items;
        Items = 0;
        return items;
    }

    /// <summary>Amalgam items a charge whose amalgam holds <paramref name="amalgamUnits"/> makes, with
    /// <paramref name="carried"/> units carried from the charges before, at
    /// <paramref name="unitsPerItem"/> units an item (<c>UnitCarry</c>'s rule).</summary>
    public static int AmalgamItems(double amalgamUnits, double carried, double unitsPerItem) =>
        unitsPerItem > 0 ? (int)Math.Floor((Math.Max(0, amalgamUnits) + Math.Max(0, carried)) / unitsPerItem + Epsilon) : 0;

    /// <summary>The mercury a charge needs: <c>amalgamMercury</c> portions for each amalgam item it will
    /// make.</summary>
    public static int MercuryNeeded(int amalgamItems, double amalgamMercury) =>
        (int)Math.Ceiling(Math.Max(0, amalgamItems) * Math.Max(0, amalgamMercury) - Epsilon);

    /// <summary>Whether the charge can be worked now, and if not why: the build complete, a charge
    /// in, its water, and <paramref name="mercuryNeeded"/> portions of mercury.</summary>
    public PanRefusal CanWork(bool complete, int mercuryNeeded, AmalgamPanConfig config)
    {
        if (!complete) return PanRefusal.Incomplete;
        if (Worked) return PanRefusal.Worked;
        if (Items == 0) return PanRefusal.NoCharge;
        if (Water + Epsilon < config.WaterLitresPerCharge) return PanRefusal.NoWater;
        if (Mercury < mercuryNeeded) return PanRefusal.NoMercury;
        return PanRefusal.None;
    }

    /// <summary>The share of the charge worked, 0..1.</summary>
    public double Progress(AmalgamPanConfig config) =>
        config.TurnsFor(Items) is var need and > 0 ? Math.Clamp(Turns / need, 0, 1) : 0;

    /// <summary>Works the charge by <paramref name="turns"/> of the muller (the caller has checked
    /// <see cref="CanWork"/>). Returns true when this step finished it: the caller then makes the
    /// amalgam and calls <see cref="Finish"/>.</summary>
    public bool Advance(double turns, AmalgamPanConfig config)
    {
        if (!Charged || !(turns > 0) || !double.IsFinite(turns))
            return false;
        Turns += turns;
        return Turns + Epsilon >= config.TurnsFor(Items);
    }

    /// <summary>Ends a worked charge: the mercury its amalgam took is spent, and so is the water; the
    /// pan shows the amalgam until <see cref="Empty"/>.</summary>
    public PanYield Finish(int amalgamItems, double amalgamMercury)
    {
        int used = Math.Min(Mercury, MercuryNeeded(amalgamItems, amalgamMercury));
        Mercury -= used;
        Water = 0;
        Turns = 0;
        Worked = true;
        return new PanYield(amalgamItems, used);
    }

    /// <summary>The worked charge taken out: the pan holds only its mercury again.</summary>
    public void Empty()
    {
        Items = 0;
        Worked = false;
        Turns = 0;
    }
}
