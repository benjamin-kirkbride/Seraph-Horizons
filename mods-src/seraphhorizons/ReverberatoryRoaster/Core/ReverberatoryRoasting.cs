using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.ReverberatoryRoaster.Core;

/// <summary>
/// The reverberatory roaster's own figures (#736): <c>ReverberatorySettings</c> in ModConfig/seraphhorizons.json. Its
/// recovery, draws, water and rate are ore processing's (<c>config/ore-processing.json</c>, <c>machines.reverberatory</c>),
/// as every ore machine's.
/// </summary>
public class ReverberatoryConfig
{
    /// <summary>Concentrate the hearth's bed holds: the infeed takes no more, and the rabbles' load is the working
    /// figure with it full (native sulfur rock counts as the concentrate holding as much).</summary>
    public int HearthCharge { get; set; } = 32;

    /// <summary>Concentrate roasted a coal: the period's coal was about a sixth of the charge's mass (316 lb a ton,
    /// Hofman 1904, p. 182).</summary>
    public double ConcentratePerCoal { get; set; } = 6;

    /// <summary>The share of its pace the furnace roasts at with the ash-mouth damper half over the mouth (open: all
    /// of it; shut: the fire banked, none).</summary>
    public double HalfDamperPace { get; set; } = 0.5;

    public static readonly ReverberatoryConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (HearthCharge is < 1 or > 128)
        {
            fixes.Add($"HearthCharge {HearthCharge} is out of range, using {Defaults.HearthCharge}");
            HearthCharge = Defaults.HearthCharge;
        }
        if (!(ConcentratePerCoal >= 1 && ConcentratePerCoal <= 1000))
        {
            fixes.Add($"ConcentratePerCoal {ConcentratePerCoal} is out of range, using {Defaults.ConcentratePerCoal}");
            ConcentratePerCoal = Defaults.ConcentratePerCoal;
        }
        if (!(HalfDamperPace >= 0 && HalfDamperPace <= 1))
        {
            fixes.Add($"HalfDamperPace {HalfDamperPace} is out of range, using {Defaults.HalfDamperPace}");
            HalfDamperPace = Defaults.HalfDamperPace;
        }
        return fixes;
    }
}

/// <summary>What goes on the hearth.</summary>
public enum ReverberatoryFeed
{
    /// <summary>Not the furnace's.</summary>
    None,
    /// <summary>A sulfide's concentrate (<c>seraphhorizons:concentrate-{ore}</c>), roasted.</summary>
    Concentrate,
    /// <summary>Native sulfur rock (<c>game:ore-sulfur</c>, #938), its sulfur melted out.</summary>
    NativeSulfur,
}

/// <summary>
/// What the reverberatory roaster does (#736; README "Ore processing: the reverberatory roaster"), game-independent:
/// a sulfide's concentrate roasted into its roasted concentrate at the furnace's 100 % (whole-route concentrate made
/// whole when the furnace is the ore's last step, <see cref="OreRecovery.Roasting"/>), native sulfur rock melted into
/// sulfur powder at its 0.65 (#938). The flue's sulfur is lost (the owner's ruling, 2026-10-14). Work, water and coal
/// go by the concentrate: a native sulfur rock counts as the concentrate holding as many units
/// (<see cref="Weight"/>). The rabbles' load follows the bed (<see cref="Fill"/>); the ash-mouth damper and the
/// row's chimney set the pace (<see cref="Pace"/>).
/// </summary>
public static class ReverberatoryRoasting
{
    public const string NativeSulfurCode = "game:ore-sulfur";
    public const string SulfurPowderCode = "game:powder-sulfur";

    /// <summary>What the coal bunker takes: the game's coals, charcoal and coke.</summary>
    public static readonly IReadOnlyList<string> Fuels =
        ["game:ore-bituminouscoal", "game:ore-anthracite", "game:ore-lignite", "game:charcoal", "game:coke"];

    public static bool IsFuel(string? code) => code != null && Fuels.Contains(code);

    /// <summary>What an item is to the furnace: a sulfide's concentrate (its <c>oreForm</c> <c>concentrate</c>, its
    /// ore a sulfide), native sulfur rock, or nothing.</summary>
    public static ReverberatoryFeed FeedOf(string? code, string? oreForm, OreSpec? ore)
    {
        if (code == NativeSulfurCode)
            return ReverberatoryFeed.NativeSulfur;
        return oreForm == "concentrate" && ore is { IsSulfide: true } ? ReverberatoryFeed.Concentrate : ReverberatoryFeed.None;
    }

    /// <summary>How many concentrates' work, water and coal one item takes: 1 for concentrate, a native sulfur rock's
    /// units over a concentrate's (13 / 5).</summary>
    public static double Weight(ReverberatoryFeed feed, double concentrateUnits, double nativeSulfurUnits) => feed switch
    {
        ReverberatoryFeed.NativeSulfur when concentrateUnits > 0 && nativeSulfurUnits > 0 => nativeSulfurUnits / concentrateUnits,
        _ => 1,
    };

    /// <summary>How many of an item of <paramref name="weight"/> the bed takes: the hearth's charge in concentrate.</summary>
    public static int Capacity(int hearthCharge, double weight) =>
        Math.Max(1, (int)Math.Floor(Math.Max(1, hearthCharge) / Math.Max(1e-9, weight) + 1e-9));

    /// <summary>How full the bed is, 0 to 1: its concentrate (by weight) over the hearth's charge.</summary>
    public static double Fill(int items, double weight, int hearthCharge) =>
        hearthCharge <= 0 ? (items > 0 ? 1 : 0) : Math.Clamp(Math.Max(0, items) * weight / hearthCharge, 0, 1);

    /// <summary>The share of its pace the furnace roasts at: the damper's (open 1, half the setting, shut 0), and
    /// nothing without the row's working chimney or its fire.</summary>
    public static double Pace(Damper damper, bool rowWorks, bool lit, double halfPace) =>
        !rowWorks || !lit ? 0 : damper switch { Damper.Open => 1, Damper.Half => Math.Clamp(halfPace, 0, 1), _ => 0 };

    /// <summary>The roasted units one concentrate of <paramref name="units"/> gives (the furnace's 100 %, made whole
    /// off the whole route when it is the ore's last step), and whether the roasted concentrate carries the whole
    /// mark on to the ore's later final step (a by-product ore's parting).</summary>
    public static (double Units, bool Whole) Roast(OreRecovery recovery, OreSpec ore, double units, bool whole)
    {
        double share = recovery.Roasting(ore, Roaster.Reverberatory, whole);
        bool carry = whole && recovery.FinalStep(ore) != OreMachine.Reverberatory;
        return (units * share, carry);
    }

    /// <summary>The sulfur units one native sulfur rock gives as powder: its units × the furnace's melt (0.65).</summary>
    public static double Melt(OreRecovery recovery) =>
        recovery.NativeSulfurUnits * recovery.NativeSulfurMelt(OreMachine.Reverberatory);

    /// <summary>
    /// The coal an item of <paramref name="weight"/> burns: a coal on the grate roasts <paramref name="perCoal"/>
    /// concentrate, so the item first spends what is left of the coal already burning (<paramref name="left"/>, in
    /// concentrate), and the whole coals still needed come from the bunker now; what is left of them after the item.
    /// </summary>
    public static (int Take, double Left) Coal(double left, double weight, double perCoal)
    {
        double per = Math.Max(1e-9, perCoal), have = Math.Max(0, left), need = Math.Max(0, weight) - have;
        int take = need > 1e-9 ? (int)Math.Ceiling(need / per - 1e-9) : 0;
        return (take, Math.Max(0, have + take * per - Math.Max(0, weight)));
    }
}
