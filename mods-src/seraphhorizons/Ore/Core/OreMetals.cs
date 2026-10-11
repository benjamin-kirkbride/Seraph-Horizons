namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Which metal (or coal, or industrial mineral) an ore belongs to. Ore cells are kept per metal,
/// not per ore: copper is one deposit per cell whether the rock there makes it malachite or native
/// copper. Coal and each industrial mineral count as their own "metal". Gems, quartz and olivine
/// are not managed (<see cref="MetalOf"/> gives null), so Interesting Ore Gen places them as it
/// always did. Argentiferous galena (<c>galena_nativesilver</c>) is a lead ore whose silver is won
/// by cupellation (#690): lead deposits and their maps count it, silver deposits are silver quartz and
/// freibergite.
/// </summary>
public static class OreMetals
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

    /// <summary>Every metal group, sorted.</summary>
    public static readonly IReadOnlyList<string> All = ByOre.Values.Distinct().Order(StringComparer.Ordinal).ToArray();

    /// <summary>Coal and the industrial minerals: they get a much smaller size cut than metals.</summary>
    public static readonly IReadOnlySet<string> NonMetals =
        new HashSet<string> { "coal", "alum", "borax", "saltpeter", "cinnabar", "sulfur", "rhodochrosite" };

    /// <summary>The metal group of an ore (a deposit variant's code, or the ore part of an ore
    /// block's code), or null if it is not managed.</summary>
    public static string? MetalOf(string? ore) =>
        ore != null && ByOre.TryGetValue(ore, out var metal) ? metal : null;

    /// <summary>
    /// The metal group worldgen sizes and places an ore by: <see cref="MetalOf"/>, except argentiferous
    /// galena, which stays silver here as it was when the ore cells and the district veins were set
    /// up. #690 made it a lead ore without changing worldgen: its felsic-district lenses are kept as
    /// silver's are (lead's are not placed), and an ore cell's anchor check counts it as before.
    /// </summary>
    public static string? WorldgenMetalOf(string? ore) =>
        ore == WorldgenSilver ? "silver" : MetalOf(ore);

    /// <summary>The ore <see cref="WorldgenMetalOf"/> keeps in its old group.</summary>
    public const string WorldgenSilver = "galena_nativesilver";

    /// <summary>The ores of a metal group.</summary>
    public static IEnumerable<string> OresOf(string metal) =>
        ByOre.Where(kv => kv.Value == metal).Select(kv => kv.Key).Order(StringComparer.Ordinal);

    /// <summary>
    /// The ore named by an ore block's path: <c>ore-{grade}-{ore}-{rock}</c> for graded ores,
    /// <c>ore-{ore}-{rock}</c> for the rest (coal, minerals). Null for anything else.
    /// </summary>
    public static string? OreOfBlockPath(string path)
    {
        if (!path.StartsWith("ore-", StringComparison.Ordinal)) return null;
        var parts = path.Split('-');
        if (parts.Length < 3) return null;
        return parts.Length >= 4 && Grades.Contains(parts[1]) ? parts[2] : parts[1];
    }

    private static readonly HashSet<string> Grades = ["poor", "medium", "rich", "bountiful"];

    private static Dictionary<string, string> Build(Dictionary<string, string[]> groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (metal, ores) in groups)
            foreach (var ore in ores)
                map.Add(ore, metal);
        return map;
    }
}
