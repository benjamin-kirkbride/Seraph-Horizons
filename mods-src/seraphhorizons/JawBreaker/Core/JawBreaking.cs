using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.JawBreaker.Core;

/// <summary>
/// The jaw breaker's own figures (#912): <c>JawBreakerSettings</c> in ModConfig/seraphhorizons.json.
/// Its draws (0.8 working, 0.16 empty) and its rate (24 rock an hour at speed 0.6) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.jawbreaker</c>), as every ore machine's.
/// </summary>
public class JawBreakerConfig
{
    /// <summary>The rock its mouth and the grizzly's foot hold: a full charge, at which its load is the
    /// working figure. The infeed takes no more than this.</summary>
    public int ChargeRocks { get; set; } = 8;

    public static readonly JawBreakerConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (ChargeRocks is < 1 or > 64)
        {
            fixes.Add($"ChargeRocks {ChargeRocks} is out of range, using {Defaults.ChargeRocks}");
            ChargeRocks = Defaults.ChargeRocks;
        }
        return fixes;
    }
}

/// <summary>
/// What the jaw breaker does (#912): raw ore in (vanilla's graded <c>ore</c> and <c>crystalizedore</c>, what
/// spalling breaks), crushed ore of its grade out, one for one, with no loss and nothing classified (its
/// product is not screened, so the × 0.85 stays until a screen). Its load follows the rock it bites
/// (<see cref="OreRecovery.LoadAt"/>): light empty, rising with the charge in its mouth to the working
/// figure at a full charge; its flywheel rides each bite's peak, so it never jams.
/// </summary>
public static class JawBreaking
{
    /// <summary>Whether an ore item code's first part and grade make it the breaker's feed.</summary>
    public static bool Takes(string? firstPart, string? grade) => Spalling.Spalls(firstPart, grade);

    /// <summary>How full its job is, 0 to 1: the rock in its mouth of a full charge.</summary>
    public static double Fill(int rocks, int chargeRocks) =>
        chargeRocks <= 0 ? (rocks > 0 ? 1 : 0) : Math.Clamp((double)Math.Max(0, rocks) / chargeRocks, 0, 1);

    /// <summary>The stream of the crushed ore it gives for a raw ore's: the same ore, grade, units and
    /// host rock, its state as it came (breaking classifies nothing).</summary>
    public static OreStream Crush(OreStream raw) => raw;
}
