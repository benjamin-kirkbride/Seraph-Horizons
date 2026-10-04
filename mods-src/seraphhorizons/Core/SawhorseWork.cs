namespace SeraphHorizons.Mod.Core;

/// <summary>A Logging Expanded sawhorse's tier (<c>UnifiedWoodworking</c>): its blocks
/// <c>sawhorse</c>, <c>sawhorsestandard</c> and <c>sawhorseadvanced</c>.</summary>
public enum SawhorseTier
{
    Primitive,
    Standard,
    Advanced,
}

/// <summary>What the player works a loaded sawhorse with, as far as the tweak tells tools apart.</summary>
public enum SawhorseTool
{
    /// <summary>Anything else, including no tool: Logging Expanded decides.</summary>
    Other,
    Axe,
    Saw,
    /// <summary>Immersive Woodworking's bark spud (<c>immersivewoodworking:barkspud-*</c>), which has
    /// no tool type of its own.</summary>
    BarkSpud,
}

/// <summary>What a completed hold on a loaded sawhorse does.</summary>
public enum SawhorseWork
{
    /// <summary>Logging Expanded's own work, untouched: an axe extracts a log, a saw makes boards.</summary>
    LoggingExpanded,
    /// <summary>Shift + saw: support beams (<see cref="SawhorseWorks.BeamsPerLog"/>) from one log.</summary>
    Beams,
    /// <summary>Axe with a hammer in the offhand: Logging Expanded's debarking, which also drops
    /// Immersive Woodworking's bark now.</summary>
    AxeAndHammerDebark,
    /// <summary>Bark spud: debarks as the axe and hammer do at that tier, and drops bark.</summary>
    SpudDebark,
}

/// <summary>
/// The sawhorse part of the unified woodworking tweak (<c>Woodworking/Sawhorses</c>): which tool
/// set does what on Logging Expanded's sawhorses, and how many beams and bark rolls it gives.
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class SawhorseWorks
{
    /// <summary>Support beams per log by tier (Shift + saw). Logging Expanded's splitting logs gave
    /// 2 and 3 (its <c>Primitive</c>/<c>AdvancedSplittingLogBeamYield</c>); the standard tier
    /// keeps the primitive's, as Logging Expanded's debark and log yields do.</summary>
    private static readonly int[] BeamsPerLogByTier = [2, 2, 3];

    public static int BeamsPerLog(this SawhorseTier tier) => BeamsPerLogByTier[(int)tier];

    /// <summary>The interaction help line for sawing beams: Logging Expanded names its lines the
    /// same way, one for the primitive and standard tiers and one for the advanced.</summary>
    public static string BeamsHelpKey(this SawhorseTier tier) =>
        tier == SawhorseTier.Advanced ? "seraphhorizons:wi-sawhorseadvanced-beams" : "seraphhorizons:wi-sawhorse-beams";

    /// <summary>The interaction help line for debarking with the bark spud, worded like the axe
    /// and hammer's at that tier.</summary>
    public static string SpudHelpKey(this SawhorseTier tier) =>
        tier == SawhorseTier.Advanced ? "seraphhorizons:wi-sawhorseadvanced-debark-spud" : "seraphhorizons:wi-sawhorse-debark-spud";

    /// <summary>What a completed hold does with <paramref name="tool"/> in the main hand. A bark
    /// spud always debarks (no offhand needed); an axe debarks only with a hammer in the offhand;
    /// a saw makes beams only while sneaking (Shift), read when the hold completes.</summary>
    public static SawhorseWork Classify(SawhorseTool tool, bool hammerInOffhand, bool sneaking) => tool switch
    {
        SawhorseTool.BarkSpud => SawhorseWork.SpudDebark,
        SawhorseTool.Axe when hammerInOffhand => SawhorseWork.AxeAndHammerDebark,
        SawhorseTool.Saw when sneaking => SawhorseWork.Beams,
        _ => SawhorseWork.LoggingExpanded,
    };

    public static bool IsDebark(this SawhorseWork work) =>
        work is SawhorseWork.AxeAndHammerDebark or SawhorseWork.SpudDebark;

    /// <summary>How many logs one debark took off the sawhorse, from its log count before and
    /// after: the logs whose bark comes off, so one bark roll each. The advanced sawhorse takes 2
    /// stored logs of a trunk and gives 3 debarked ones: that is 2 rolls, as the third is
    /// Logging Expanded's yield bonus, not a log that had bark. Never negative.</summary>
    public static int LogsTaken(int before, int after) => Math.Max(0, before - Math.Max(0, after));

    /// <summary>Logging Expanded's wood type of the loaded logs (its <c>TreeTrunkInventory.WoodType</c>,
    /// e.g. <c>oak</c> or, from a felled trunk, <c>grown-oak</c>) as a bare species, lower case:
    /// the support beam's wood and Immersive Woodworking's bark table key. Null for none.</summary>
    public static string? Species(string? woodType)
    {
        if (string.IsNullOrEmpty(woodType))
            return null;
        string wood = woodType.ToLowerInvariant();
        foreach (string prefix in (string[])["grown-", "placed-", "resin-", "resinharvested-"])
            if (wood.StartsWith(prefix, StringComparison.Ordinal))
                wood = wood[prefix.Length..];
        return wood.Length == 0 ? null : wood;
    }
}
