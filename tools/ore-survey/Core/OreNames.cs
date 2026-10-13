namespace SeraphHorizons.OreSurvey.Core;

/// <summary>
/// The metal of an ore block, for the census's flood: the pack's <c>OreMetals</c> table
/// (mods-src/seraphhorizons/Ore/Core/OreMetals.cs), copied so this local mod needs nothing of the
/// pack's at build time, with argentiferous galena as lead, as ore_survey.py counts it (#690).
/// Interesting Ore Gen's saltpeter (<c>saltpeterore-*</c>, not an <c>ore-</c> block) is saltpeter.
/// </summary>
public static class OreNames
{
    private static readonly Dictionary<string, string> ByOre = Build(new()
    {
        ["copper"] = ["nativecopper", "malachite", "azurite", "chalcocite", "chalcopyrite", "tetrahedrite"],
        ["iron"] = ["hematite", "magnetite", "limonite", "pyrite"],
        ["tin"] = ["cassiterite", "teallite", "franckeite"],
        ["zinc"] = ["sphalerite", "smithsonite", "hemimorphite"],
        ["lead"] = ["galena", "galena_nativesilver", "cerussite", "vanadinite", "wulfenite"],
        ["bismuth"] = ["bismuthinite"],
        ["nickel"] = ["pentlandite"],
        ["silver"] = ["quartz_nativesilver", "freibergite"],
        ["gold"] = ["quartz_nativegold"],
        ["platinum"] = ["nativeplatinum", "sperrylite"],
        ["titanium"] = ["ilmenite"],
        ["chromium"] = ["chromite"],
        ["uranium"] = ["uranium"],
        ["coal"] = ["anthracite", "bituminouscoal", "lignite"],
        ["alum"] = ["alum"],
        ["borax"] = ["borax"],
        ["saltpeter"] = ["saltpeter"],
        ["cinnabar"] = ["cinnabar"],
        ["sulfur"] = ["sulfur"],
        ["rhodochrosite"] = ["rhodochrosite"],
    });

    private static readonly HashSet<string> Grades = ["poor", "medium", "rich", "bountiful"];

    /// <summary>The metal of an ore (<c>malachite</c>), or null.</summary>
    public static string? MetalOfOre(string? ore) => ore != null && ByOre.TryGetValue(ore, out var m) ? m : null;

    /// <summary>The ore of a block path: <c>ore-{grade}-{ore}-{rock}</c>, <c>ore-{ore}-{rock}</c>,
    /// IOG's <c>saltpeterore-{rock}</c>; also an IOG district code with <c>*</c> for the grade.
    /// Null for anything else.</summary>
    public static string? OreOfPath(string path)
    {
        int colon = path.IndexOf(':');
        if (colon >= 0) path = path[(colon + 1)..];
        if (path.StartsWith("saltpeterore", StringComparison.Ordinal)) return "saltpeter";
        if (!path.StartsWith("ore-", StringComparison.Ordinal)) return null;
        var parts = path.Split('-');
        if (parts.Length < 3) return null;
        return parts.Length >= 4 && (parts[1] == "*" || Grades.Contains(parts[1])) ? parts[2] : parts[1];
    }

    /// <summary>The metal of a block path, or null.</summary>
    public static string? MetalOfPath(string path) => MetalOfOre(OreOfPath(path));

    private static Dictionary<string, string> Build(Dictionary<string, string[]> groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (metal, ores) in groups)
            foreach (var ore in ores)
                map.Add(ore, metal);
        return map;
    }
}
