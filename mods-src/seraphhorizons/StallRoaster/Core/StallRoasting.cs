using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.StallRoaster.Core;

/// <summary>The stall's front (#925, #926): open for charging, lighting and raking out; half walled, or walled
/// up with loose brick, which sets the draught through the heap.</summary>
public enum StallFront
{
    Open,
    Half,
    Walled,
}

/// <summary>Where a stall's charge stands.</summary>
public enum StallPhase
{
    /// <summary>Swept and empty.</summary>
    Empty,
    /// <summary>Some or all of the bed of firewood laid, no rock on it.</summary>
    Bed,
    /// <summary>Rock on a full bed, not lit.</summary>
    Charged,
    /// <summary>Lit: burning on its own sulfur.</summary>
    Burning,
    /// <summary>The burn done: roasted rock, and the sulfur in its crust, to take out.</summary>
    Roasted,
}

/// <summary>Why a stall refuses a click (each a lang entry <c>seraphhorizons:stallroaster-error-{name}</c>).</summary>
public enum StallRefusal
{
    None,
    /// <summary>Charging, lighting and raking out are done through the open front.</summary>
    FrontNotOpen,
    /// <summary>The bed goes in before the rock, and only into a swept stall.</summary>
    BedUnderCharge,
    /// <summary>The bed is laid.</summary>
    BedLaid,
    /// <summary>Rock goes on a full bed.</summary>
    NoBed,
    /// <summary>The charge is full.</summary>
    Full,
    /// <summary>It is burning.</summary>
    Burning,
    /// <summary>The last burn's roasted rock is still in it.</summary>
    Roasted,
    /// <summary>Nothing to light: it wants a bed and rock on it.</summary>
    NotCharged,
    /// <summary>Lighting is through the front: it must not be walled up.</summary>
    FrontWalled,
    /// <summary>Walled up already.</summary>
    AlreadyWalled,
    /// <summary>Open already.</summary>
    AlreadyOpen,
    /// <summary>Too few of the stall's own brick to wall the next course.</summary>
    TooFewBricks,
    /// <summary>A brick of another type than the stall's.</summary>
    OtherBrick,
}

/// <summary>
/// The stall roaster's own settings (#925; <c>StallRoasterSettings</c> in ModConfig/seraphhorizons.json). Its
/// recovery (92 %), its charge (32 rock) and its burn (40 hours) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.stallroaster</c>), as every ore machine's figures are; its
/// row's chimney (6 blocks from the outlet up, 6 stalls to a chimney) is the rig's (<see cref="StallRoasterRig"/>),
/// counted as the crucible furnace counts its own.
/// </summary>
public class StallRoasterConfig
{
    /// <summary>Firewood the bed under a charge takes; it kindles the charge and burns away.</summary>
    public int BedFirewood { get; set; } = 4;

    /// <summary>The share of its full draught a half-walled front gives (the burn's pace); walled up is the
    /// full draught.</summary>
    public double HalfWalledDraught { get; set; } = 0.5;

    /// <summary>The share an open front gives: the draught goes over the heap, not through it, and the burn
    /// does not advance.</summary>
    public double OpenDraught { get; set; } = 0;

    public static readonly StallRoasterConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        var d = Defaults;
        if (BedFirewood is < 1 or > 64)
        {
            fixes.Add($"BedFirewood {BedFirewood} is out of range, using {d.BedFirewood}");
            BedFirewood = d.BedFirewood;
        }
        if (!(HalfWalledDraught is >= 0 and <= 1))
        {
            fixes.Add($"HalfWalledDraught {HalfWalledDraught} is out of range, using {d.HalfWalledDraught}");
            HalfWalledDraught = d.HalfWalledDraught;
        }
        if (!(OpenDraught is >= 0 and <= 1))
        {
            fixes.Add($"OpenDraught {OpenDraught} is out of range, using {d.OpenDraught}");
            OpenDraught = d.OpenDraught;
        }
        return fixes;
    }
}

/// <summary>
/// What the stall roaster does (#925; README "Stall roaster"): a roasting stall, batch, loaded and emptied by
/// hand. Through its open front a bed of firewood is laid and a sulfide's raw or crushed rock heaped on it (up to
/// a charge, 32); lit, the bed kindles the heap, which burns on its own sulfur for the stall's hours (40) at full
/// draught: the front walled up and the row's chimney drawing (<see cref="Draught"/>). Every rock roasts one for
/// one into stall-roasted rock of its grade, which smelts at its grade's share × the stall's 92 %
/// (<see cref="OreRecovery.SmeltShare"/> with <see cref="Roaster.Stall"/>), and a third of the charge's sulfur
/// (<see cref="OreRecovery.SulfurUnits"/>) condenses in the heap's crust as sulfur powder, taken out by hand
/// once it is done, before the rock is raked out a course at a time.
/// </summary>
public static class StallRoasting
{
    /// <summary>The bed's firewood.</summary>
    public const string Firewood = "game:firewood";

    /// <summary>What the crust's sulfur comes out as.</summary>
    public const string SulfurPowder = "game:powder-sulfur";

    /// <summary>The share of the burn the bed takes to kindle the heap (the model's burning bed shows till then).</summary>
    public const double KindlingShare = 0.1;

    /// <summary>Whether the stall takes an item of this form of this ore: a sulfide's raw or crushed rock (not
    /// ground ore, too fine to burn in a heap; not concentrate, the reverberatory's).</summary>
    public static bool Takes(OreForm? form, OreSpec ore) => ore.IsSulfide && form is OreForm.Raw or OreForm.Crushed;

    /// <summary>The phase of a stall holding this much.</summary>
    public static StallPhase PhaseOf(int firewood, int rock, int bedFirewood, bool lit, bool roasted) =>
        roasted ? StallPhase.Roasted
        : lit ? StallPhase.Burning
        : rock > 0 && firewood >= bedFirewood ? StallPhase.Charged
        : firewood > 0 ? StallPhase.Bed
        : StallPhase.Empty;

    /// <summary>The courses a charge of <paramref name="rock"/> is drawn as (<c>ore1..</c>): ceil(rock / per course),
    /// at most <paramref name="courses"/>.</summary>
    public static int Courses(int rock, int perCourse, int courses) =>
        rock <= 0 || perCourse <= 0 ? 0 : Math.Min(courses, (rock + perCourse - 1) / perCourse);

    /// <summary>The share of the stall's full draught it burns at: none without a working chimney on its row,
    /// else by its front (walled up 1, half walled, open).</summary>
    public static double Draught(StallFront front, bool rowDraws, StallRoasterConfig c) =>
        !rowDraws ? 0 : front switch
        {
            StallFront.Walled => 1,
            StallFront.Half => c.HalfWalledDraught,
            _ => c.OpenDraught,
        };

    /// <summary>The burn after <paramref name="hours"/> more at <paramref name="draught"/>: its hours (full
    /// draught's) and whether it is done at <paramref name="burnHours"/>.</summary>
    public static (double Burned, bool Done) Advance(double burned, double hours, double draught, double burnHours)
    {
        double next = burned + Math.Max(0, hours) * Math.Clamp(draught, 0, 1);
        return burnHours > 0 && next >= burnHours ? (burnHours, true) : (Math.Max(0, next), burnHours <= 0);
    }

    /// <summary>Whether the bed shows burning (kindling the heap): the first part of the burn.</summary>
    public static bool Kindling(double burned, double burnHours) => burned < burnHours * KindlingShare;

    /// <summary>Laying the bed: how much firewood a click with <paramref name="held"/> puts in, or why none.</summary>
    public static (int Take, StallRefusal Refusal) LayBed(StallPhase phase, StallFront front, int firewood, int held, int bedFirewood)
    {
        if (phase is StallPhase.Burning) return (0, StallRefusal.Burning);
        if (phase is StallPhase.Roasted) return (0, StallRefusal.Roasted);
        if (phase is StallPhase.Charged) return (0, StallRefusal.BedUnderCharge);
        if (front != StallFront.Open) return (0, StallRefusal.FrontNotOpen);
        int room = bedFirewood - firewood;
        if (room <= 0) return (0, StallRefusal.BedLaid);
        return (Math.Min(room, Math.Max(0, held)), StallRefusal.None);
    }

    /// <summary>Charging: how much rock a click with <paramref name="held"/> puts on the bed, or why none.</summary>
    public static (int Take, StallRefusal Refusal) Charge(StallPhase phase, StallFront front, int firewood, int rock, int held,
        int bedFirewood, int capacity)
    {
        if (phase is StallPhase.Burning) return (0, StallRefusal.Burning);
        if (phase is StallPhase.Roasted) return (0, StallRefusal.Roasted);
        if (front != StallFront.Open) return (0, StallRefusal.FrontNotOpen);
        if (firewood < bedFirewood) return (0, StallRefusal.NoBed);
        int room = capacity - rock;
        if (room <= 0) return (0, StallRefusal.Full);
        return (Math.Min(room, Math.Max(0, held)), StallRefusal.None);
    }

    /// <summary>Why the stall cannot be lit now, or <see cref="StallRefusal.None"/>.</summary>
    public static StallRefusal CannotLight(StallPhase phase, StallFront front) => phase switch
    {
        StallPhase.Burning => StallRefusal.Burning,
        StallPhase.Roasted => StallRefusal.Roasted,
        StallPhase.Charged => front == StallFront.Walled ? StallRefusal.FrontWalled : StallRefusal.None,
        _ => StallRefusal.NotCharged,
    };

    /// <summary>The loose brick the front holds walled to <paramref name="front"/>.</summary>
    public static int BricksFor(StallFront front, int half, int walled) => front switch
    {
        StallFront.Walled => walled,
        StallFront.Half => half,
        _ => 0,
    };

    /// <summary>Walling the front up a place: the next place, and the bricks taken from the hand beyond those the
    /// stall keeps (<paramref name="kept"/>: loose brick, once given, stays the stall's), or why not.</summary>
    public static (StallFront Next, int Take, StallRefusal Refusal) WallUp(StallFront front, int kept, int held, int half, int walled)
    {
        if (front == StallFront.Walled) return (front, 0, StallRefusal.AlreadyWalled);
        var next = front == StallFront.Open ? StallFront.Half : StallFront.Walled;
        int take = Math.Max(0, BricksFor(next, half, walled) - kept);
        if (held < take) return (front, 0, StallRefusal.TooFewBricks);
        return (next, take, StallRefusal.None);
    }

    /// <summary>Taking the front down a place (the bricks stay the stall's), or why not.</summary>
    public static (StallFront Next, StallRefusal Refusal) TakeDown(StallFront front) => front switch
    {
        StallFront.Walled => (StallFront.Half, StallRefusal.None),
        StallFront.Half => (StallFront.Open, StallRefusal.None),
        _ => (front, StallRefusal.AlreadyOpen),
    };

    /// <summary>Raking out: whether the front lets it (the roasted rock, a course at a time, through the open front).</summary>
    public static StallRefusal CannotRake(StallPhase phase, StallFront front) =>
        phase != StallPhase.Roasted ? StallRefusal.NotCharged
        : front != StallFront.Open ? StallRefusal.FrontNotOpen
        : StallRefusal.None;

    /// <summary>
    /// The sulfur a burn leaves in the crust: <paramref name="sulfurUnits"/> (the charge's, <see cref="OreRecovery.SulfurUnits"/>)
    /// with <paramref name="carried"/> left from the stall's last burn, as whole powders of
    /// <paramref name="unitsPerPowder"/>, and what is carried to the next burn.
    /// </summary>
    public static (int Powders, double Carried) Sulfur(double carried, double sulfurUnits, double unitsPerPowder)
    {
        if (!(unitsPerPowder > 0)) return (0, Math.Max(0, carried));
        double total = Math.Max(0, carried) + Math.Max(0, sulfurUnits);
        int powders = (int)Math.Floor(total / unitsPerPowder + 1e-9);
        return (powders, Math.Max(0, total - powders * unitsPerPowder));
    }
}
