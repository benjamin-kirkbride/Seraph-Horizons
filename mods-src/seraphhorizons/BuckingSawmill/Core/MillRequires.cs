namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>The mill's <c>requires</c> vocabulary: which fitted parts draw which rig parts.</summary>
public static class MillRequires
{
    /// <summary>The <c>requires</c> values the gameplay knows.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { "crankshaft", "levers", "sash1", "sash2", "blade" };

    /// <summary>Whether a part needing <paramref name="requires"/> is drawn with these parts fitted.
    /// <c>blade</c> is the one blade kit, which puts a blade in both saws.</summary>
    public static bool Fitted(string? requires, int sashes, bool crankshaft, bool bladeKit, bool levers = false) => requires switch
    {
        null => true,
        "crankshaft" => crankshaft,
        "levers" => levers,
        "sash1" => sashes >= 1,
        "sash2" => sashes >= 2,
        "blade" => bladeKit,
        _ => false,
    };
}
