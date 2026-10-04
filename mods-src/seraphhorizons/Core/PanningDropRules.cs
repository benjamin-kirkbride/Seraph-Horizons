using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Panning drops (<c>PanningDropsTrimmed</c>): which entries of the pan's
/// <c>attributes.panningDrops</c> are taken out. Game-independent, so tests/ runs it without the game.
/// </summary>
public static class PanningDropRules
{
    /// <summary>The item codes panning no longer gives, <c>domain:path</c> with <c>*</c> wildcards
    /// (<see cref="Wildcard"/>), lowercase. What each covers in the pack's panning table:
    /// <list type="bullet">
    /// <item><c>wool:*</c>: Wool's fibers (<c>wool:fibers-generic-*</c>, its
    /// <c>patches/survival-blocktypes-wood-pan.json</c>), and anything else of that mod's.</item>
    /// <item><c>tailorsdelight:awl-*</c>, <c>tailorsdelight:awlthorn-*</c>: Tailor's Delight's stitching
    /// awls and the awl head they are made from (its <c>patches/survival-blocktypes-wood-pan.json</c>).</item>
    /// <item><c>tailorsdelight:buttons-*</c>: Tailor's Delight's "Buttons &amp; Clasps" (same patch).</item>
    /// <item><c>game:nugget-uranium</c>: added by Expanded Matter (<c>em</c>,
    /// <c>patches/survival-blocktypes-wood-pan.json</c>, its <c>ore_panning</c> setting).</item>
    /// </list></summary>
    public static readonly IReadOnlyList<string> Removed =
    [
        "wool:*",
        "tailorsdelight:awl-*",
        "tailorsdelight:awlthorn-*",
        "tailorsdelight:buttons-*",
        "game:nugget-uranium",
    ];

    /// <summary>Whether a panning drop of this code is taken out. A code without a domain is in
    /// <c>game</c>, as the pan resolves it; case does not matter.</summary>
    public static bool IsRemoved(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        string normalized = Normalize(code);
        foreach (string pattern in Removed)
            if (Wildcard.IsMatch(pattern, normalized))
                return true;
        return false;
    }

    private static string Normalize(string code)
    {
        code = code.Trim().ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }
}
