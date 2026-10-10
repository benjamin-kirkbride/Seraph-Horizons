using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// What one switch of <c>ModConfig/seraphhorizons.json</c> adds to the game when it is on, and so
/// takes out when it is off.
/// </summary>
/// <param name="Switch">The <c>SeraphHorizonsConfig</c> property, as named in the config file.</param>
/// <param name="RecipeAssets">Recipe files (<c>domain:recipes/...json</c>) whose every recipe the
/// switch leaves out when off.</param>
/// <param name="CodePatterns">Item and block codes (<c>domain:path</c>, <c>*</c> wildcards) that do
/// not exist with the switch off.</param>
/// <param name="RecipeTypes">Recipe types of the recipe export (its <c>recipeTypes</c> keys) that
/// only the switch's feature has: the pickling tub's rules, the oiled gear's roll, the gear
/// cutter's process.</param>
public sealed record OwnedBySwitch(
    string Switch,
    IReadOnlyList<string> RecipeAssets,
    IReadOnlyList<string> CodePatterns,
    IReadOnlyList<string> RecipeTypes);

/// <summary>
/// Which switch owns a recipe or an item (README "Switch ownership"): the switch that adds it when
/// on. The game side (<c>SwitchRegistry</c>) builds it from what each feature already disables
/// (its systems' recipe and type asset lists) plus the short <see cref="HandListed"/>; the recipe
/// exporter asks it, by reflection, for each recipe id and item code it writes. Game-independent.
///
/// A recipe id is the exporter's <c>&lt;type&gt;|&lt;source&gt;|&lt;index&gt;</c>. It is owned by a
/// switch when its type is one of the switch's <see cref="OwnedBySwitch.RecipeTypes"/>, when its
/// source is one of the switch's recipe files, or when its source is a code the switch owns (the
/// exporter keys transitions, casting and in-place builds by the code they start from: a transition
/// of an item a switch adds exists only with the item).
/// </summary>
public sealed class SwitchOwnership
{
    /// <summary>What no feature's asset list says: kept here, next to the registry, and checked
    /// against the game by the pack tests (<c>SwitchOwnershipScenarios</c>).</summary>
    public static readonly OwnedBySwitch[] HandListed =
    [
        // The rosser's output is Logging Expanded's own trunk with a third branches state, added by
        // a JSON patch (patches/rosser-debarkedtrunk.json), not a type file of the mod's.
        new("Rosser", [], ["loggingmod:treetrunk-*-debarked-*"], []),
        // The exporter's own recipe types for the gear chain (tools/recipe-export, RecipeSection.Gears.cs).
        new("GearReclamation", [], [], ["picklingtub", "lottery"]),
        new("GearCutter", [], [], ["gearcutter"]),
        // The exporter's own recipe type for the draw bench's process (tools/recipe-export, Recipes/DrawBenchExport.cs).
        new("DrawBench", [], [], ["drawbench"]),
        // The exporter's own recipe type for the press brake's process (tools/recipe-export, Recipes/PressBrakeExport.cs).
        new("PressBrake", [], [], ["pressbrake"]),
        // The exporter's own recipe type for the squaring shear's process (tools/recipe-export, Recipes/SquaringShearExport.cs).
        new("SquaringShear", [], [], ["squaringshear"]),
        // The exporter's own recipe type for the mandrel station's process (tools/recipe-export, Recipes/MandrelStationExport.cs).
        new("MandrelStation", [], [], ["mandrelstation"]),
        // The pipe mold is Steelmaking Expanded's own tool mold with a fourth tool type, added by a
        // JSON patch (patches/castpipes-smexmold.json), not a type file of the mod's.
        new("CastPipes", [], ["smex:toolmold-*-pipe"], []),
        // Copper and lead pipes and bronze valves are Pipes and Power Expanded's own blocks with more
        // material states, added by a JSON patch (patches/unifiedpipes-ppex.json); the lead chute
        // section is the game's chute section with another state (patches/unifiedpipes-chutesection.json).
        new("UnifiedPipes", [], ["ppex:pipe-*-copper", "ppex:pipe-*-lead", "ppex:pipe-*-tinbronze",
                                 "ppex:pipe-*-bismuthbronze", "ppex:pipe-*-blackbronze",
                                 "game:chutesection-lead"], []),
        // Crushed ore is an item type in the game's domain whose code, "crushed", is vanilla's
        // crushed item's too (game:crushed-{material}), so its codes are listed by their grain.
        new("OreProcessing", [], ["game:crushed-*-coarse", "game:crushed-*-fine"], ["cupellation"]),
    ];

    private readonly List<OwnedBySwitch> _owned;
    private readonly Dictionary<string, string> _byRecipeAsset = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byRecipeType = new(StringComparer.Ordinal);
    private readonly List<(Regex Pattern, string Switch)> _byCode = [];

    public SwitchOwnership(IEnumerable<OwnedBySwitch> owned)
    {
        _owned = owned.ToList();
        foreach (var o in _owned)
        {
            foreach (var asset in o.RecipeAssets)
                _byRecipeAsset.TryAdd(asset, o.Switch);
            foreach (var type in o.RecipeTypes)
                _byRecipeType.TryAdd(type, o.Switch);
            foreach (var pattern in o.CodePatterns)
                _byCode.Add((Wildcard(pattern), o.Switch));
        }
    }

    /// <summary>Everything the registry was built from, merged by switch.</summary>
    public IReadOnlyList<OwnedBySwitch> Owned => _owned;

    /// <summary>The switches that own anything, in order.</summary>
    public IEnumerable<string> Switches => _owned.Select(o => o.Switch).Distinct();

    /// <summary>The switch that owns the item or block <paramref name="code"/>, or null.</summary>
    public string? SwitchForCode(string code)
    {
        code = Normalize(code);
        foreach (var (pattern, name) in _byCode)
            if (pattern.IsMatch(code))
                return name;
        return null;
    }

    /// <summary>The switch that owns the export's recipe <paramref name="recipeId"/>, or null.</summary>
    public string? SwitchForRecipe(string recipeId)
    {
        var parts = recipeId.Split('|');
        if (_byRecipeType.TryGetValue(parts[0], out var byType))
            return byType;
        if (parts.Length < 2)
            return null;
        return _byRecipeAsset.TryGetValue(parts[1], out var byAsset) ? byAsset : SwitchForCode(parts[1]);
    }

    /// <summary>The codes a type file defines, from its <c>code</c>: <c>domain:code</c> itself and
    /// every variant of it, <c>domain:code-*</c>.</summary>
    public static string[] TypeCodePatterns(string domain, string code) =>
        [$"{domain}:{code}", $"{domain}:{code}-*"];

    /// <summary>The switches that are off, as the server tells its clients (one world config
    /// string, names joined by commas, sorted).</summary>
    public static string EncodeOff(IEnumerable<string> off) =>
        string.Join(",", off.Where(s => s.Length > 0).Distinct().OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>The names in <see cref="EncodeOff"/>'s string; empty for null or "".</summary>
    public static HashSet<string> DecodeOff(string? encoded) =>
        new((encoded ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);

    private static string Normalize(string code)
    {
        code = code.ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }

    private static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(Normalize(pattern)).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
}
