using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.AmalgamationPlates.Core;

/// <summary>
/// The amalgamation plates' own figures (#917): <c>AmalgamationPlatesSettings</c> in ModConfig/seraphhorizons.json.
/// Their catch (0.65), rate (8 rock an hour) and water (5 L a rock) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.amalgamationplates</c>); the mercury an amalgam takes is the
/// still's (<c>retort.amalgamMercury</c>), as the amalgam pan's.
/// </summary>
public class AmalgamationPlatesConfig
{
    /// <summary>Litres of mercury the dressed plates hold: what one dressing puts on them. An amalgam item caught
    /// takes <c>retort.amalgamMercury</c> portions of it (10, a tenth of a litre).</summary>
    public double DressingLitres { get; set; } = 1;

    /// <summary>Amalgam items the plates hold before they are full and catch nothing more until scraped.</summary>
    public int AmalgamItems { get; set; } = 12;

    public static readonly AmalgamationPlatesConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!double.IsFinite(DressingLitres) || DressingLitres < 0.1 || DressingLitres > 100)
        {
            fixes.Add($"DressingLitres {DressingLitres} is out of range, using {Defaults.DressingLitres}");
            DressingLitres = Defaults.DressingLitres;
        }
        if (AmalgamItems is < 1 or > 1000)
        {
            fixes.Add($"AmalgamItems {AmalgamItems} is out of range, using {Defaults.AmalgamItems}");
            AmalgamItems = Defaults.AmalgamItems;
        }
        return fixes;
    }
}

/// <summary>What the plates did with one item.</summary>
public enum PlateVerdict
{
    /// <summary>Free gold or silver caught as amalgam, the rest on amalgamated (its × 0.7 lifted).</summary>
    Caught,
    /// <summary>An ore with no free metal: on unchanged.</summary>
    NotFree,
    /// <summary>Already over plates or through the amalgam pan: on unchanged.</summary>
    AlreadyAmalgamated,
    /// <summary>Too little mercury on the plates for its catch: on unchanged, its free metal not caught.</summary>
    Undressed,
    /// <summary>No room on the plates for its catch: on unchanged until they are scraped.</summary>
    Full,
}

/// <summary>One item over the plates: the verdict, the amalgam caught (its item code and units), the mercury that
/// took off the dressing (portions) and the stream that goes on.</summary>
public sealed record PlatePass(PlateVerdict Verdict, string? AmalgamCode, double AmalgamUnits, double MercurySpent, OreStream Rest);

/// <summary>
/// The amalgamation plates' rules (#917), game-independent. Crushed or ground ore runs over the dressed plates:
/// on an ore with free gold or silver, ore processing's <see cref="OreRecovery.Amalgamate(OreMachine, OreStream)"/>
/// catches its figure (0.65) of the catchable metal as amalgam and passes the rest on amalgamated, the free
/// metal's × 0.7 lifted. The catch takes mercury off the dressing (the amalgam's own, <c>amalgamMercury</c>
/// portions an amalgam item) and builds up on the plates until scraped. Plates without the mercury for a catch,
/// or with no room for it, let the item by unchanged, as other ores go by.
/// </summary>
public static class PlateAmalgamation
{
    /// <summary>Portions of mercury to the litre (the game's liquid portions).</summary>
    public const double PortionsPerLitre = 100;

    /// <summary>Whether the plates take an item of this form carrying this stream: crushed or ground ore of a grade
    /// (fresh, or another machine's tailings). Raw ore wants crushing; roasted rock and concentrate are past them.</summary>
    public static bool Takes(OreForm? form, OreStream? stream) =>
        form is OreForm.Crushed or OreForm.Ground && stream != null && OreProducts.IsGrade(stream.Grade);

    /// <summary>The mercury, in portions, one unit of metal caught as amalgam takes.</summary>
    public static double MercuryPerUnit(OreRecovery recovery) =>
        recovery.ConcentrateUnits > 0 ? recovery.Retort.AmalgamMercury / recovery.ConcentrateUnits : 0;

    /// <summary>The dressing's mercury in portions.</summary>
    public static double DressingPortions(AmalgamationPlatesConfig config) => config.DressingLitres * PortionsPerLitre;

    /// <summary>The metal units the plates hold when full.</summary>
    public static double CapacityUnits(OreRecovery recovery, AmalgamationPlatesConfig config) => config.AmalgamItems * recovery.ConcentrateUnits;

    /// <summary>
    /// <paramref name="count"/> items of <paramref name="stream"/> over plates holding <paramref name="dressing"/>
    /// portions of mercury and <paramref name="held"/> units of amalgam, of <paramref name="capacity"/>.
    /// <see cref="PlatePass.Rest"/> is an item's stream; the amalgam and mercury are all the items'.
    /// </summary>
    public static PlatePass Pass(OreRecovery recovery, OreStream stream, int count, double dressing, double held, double capacity)
    {
        const double eps = 1e-9;
        count = Math.Max(1, count);
        if (stream.State.HasFlag(OreState.Amalgamated))
            return new PlatePass(PlateVerdict.AlreadyAmalgamated, null, 0, 0, stream);
        if (!recovery.Ore(stream.Ore).Free)
            return new PlatePass(PlateVerdict.NotFree, null, 0, 0, stream);
        var pass = recovery.Amalgamate(OreMachine.AmalgamationPlates, stream);
        double units = pass.Amalgam * count;
        double mercury = units * MercuryPerUnit(recovery);
        if (dressing + eps < mercury || dressing <= eps)
            return new PlatePass(PlateVerdict.Undressed, null, 0, 0, stream);
        if (held + units > capacity + eps)
            return new PlatePass(PlateVerdict.Full, null, 0, 0, stream);
        return new PlatePass(PlateVerdict.Caught, OreProducts.AmalgamCode(stream.Ore), units, mercury, pass.Rest);
    }

    /// <summary>The amalgam state drawn (the rig's <c>amalgam1</c> .. <c>amalgam3</c>): 0 for clean plates, then a
    /// third of the capacity each.</summary>
    public static int AmalgamState(double held, double capacity)
    {
        if (!(held > 1e-9) || !(capacity > 0))
            return 0;
        double f = held / capacity;
        return f <= 1.0 / 3 + 1e-9 ? 1 : f <= 2.0 / 3 + 1e-9 ? 2 : 3;
    }

    /// <summary>The whole amalgam items <paramref name="units"/> make, and the units left on the plates.</summary>
    public static (int Items, double Left) Scrape(double units, double unitsPerItem)
    {
        if (!(unitsPerItem > 0) || !(units > 0))
            return (0, Math.Max(0, units));
        int items = (int)Math.Floor((units + 1e-9) / unitsPerItem);
        return (items, Math.Max(0, units - items * unitsPerItem));
    }
}
