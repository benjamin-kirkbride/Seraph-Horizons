namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>Where a trader is, as far as its stock cares: a climate band and a rock group, the keys
/// of a list's regional tables (<c>cold</c>, <c>temperate</c>, <c>hot</c>; <c>sedimentary</c>,
/// <c>igneous</c>, <c>metamorphic</c>).</summary>
public readonly record struct Region(string Climate, string Rock)
{
    public const string Cold = "cold";
    public const string Temperate = "temperate";
    public const string Hot = "hot";
    public const string Sedimentary = "sedimentary";
    public const string Igneous = "igneous";
    public const string Metamorphic = "metamorphic";

    public static readonly IReadOnlyList<string> Climates = [Cold, Temperate, Hot];
    public static readonly IReadOnlyList<string> Rocks = [Sedimentary, Igneous, Metamorphic];

    /// <summary>Every region, for checks that each resolves.</summary>
    public static IEnumerable<Region> All => Climates.SelectMany(c => Rocks.Select(r => new Region(c, r)));

    public override string ToString() => $"{Climate}/{Rock}";

    public static bool TryParse(string text, out Region region)
    {
        region = default;
        var parts = text.Split('/');
        if (parts.Length != 2 || !Climates.Contains(parts[0]) || !Rocks.Contains(parts[1])) return false;
        region = new Region(parts[0], parts[1]);
        return true;
    }
}

/// <summary>
/// Sorts a spot into a <see cref="Region"/>. Climate from the worldgen (yearly mean) temperature at
/// the spot: cold below <see cref="ColdBelow"/>, hot from <see cref="HotFrom"/>. Rock from the code of
/// the rock under it: the game's own <c>worldproperties/block/rock</c> gives each rock a
/// <c>RockGroup</c> (<c>igneous_extrusive</c>, <c>igneous_intrusive</c>, <c>sedimentary</c>,
/// <c>metamorphic</c>; mods that add rocks patch it, as Geology Additions does), which the game side
/// passes in; rocks without one fall back to <see cref="FallbackRockGroups"/>, then sedimentary.
/// </summary>
public sealed class RegionClassifier
{
    public const float ColdBelow = 0f;
    public const float HotFrom = 16f;

    /// <summary>Rocks the pack can generate that carry no RockGroup, and anything a mod may add.</summary>
    public static readonly IReadOnlyDictionary<string, string> FallbackRockGroups = new Dictionary<string, string>
    {
        ["andesite"] = Region.Igneous, ["basalt"] = Region.Igneous, ["granite"] = Region.Igneous,
        ["peridotite"] = Region.Igneous, ["kimberlite"] = Region.Igneous, ["obsidian"] = Region.Igneous,
        ["scoria"] = Region.Igneous, ["tuff"] = Region.Igneous, ["komatiite"] = Region.Igneous, ["bauxite"] = Region.Igneous,
        ["phyllite"] = Region.Metamorphic, ["slate"] = Region.Metamorphic, ["schist"] = Region.Metamorphic,
        ["gneiss"] = Region.Metamorphic, ["quartzite"] = Region.Metamorphic, ["suevite"] = Region.Metamorphic,
        ["whitemarble"] = Region.Metamorphic, ["redmarble"] = Region.Metamorphic, ["greenmarble"] = Region.Metamorphic,
        ["chalk"] = Region.Sedimentary, ["chert"] = Region.Sedimentary, ["conglomerate"] = Region.Sedimentary,
        ["limestone"] = Region.Sedimentary, ["claystone"] = Region.Sedimentary, ["sandstone"] = Region.Sedimentary,
        ["shale"] = Region.Sedimentary, ["halite"] = Region.Sedimentary,
    };

    private readonly Dictionary<string, string> _rocks;

    /// <param name="rockGroups">Rock code to the game's RockGroup value (any case, any suffix after
    /// the first word: <c>igneous_extrusive</c> counts as igneous).</param>
    public RegionClassifier(IReadOnlyDictionary<string, string>? rockGroups = null)
    {
        _rocks = new Dictionary<string, string>(FallbackRockGroups);
        if (rockGroups != null)
            foreach (var (rock, group) in rockGroups)
                if (NormaliseGroup(group) is { } g) _rocks[rock] = g;
    }

    public static string? NormaliseGroup(string? group)
    {
        if (string.IsNullOrEmpty(group)) return null;
        string g = group.ToLowerInvariant();
        if (g.StartsWith("igneous")) return Region.Igneous;
        if (g.StartsWith("metamorphic")) return Region.Metamorphic;
        if (g.StartsWith("sedimentary")) return Region.Sedimentary;
        return null;
    }

    public static string ClimateOf(float temperature) =>
        temperature < ColdBelow ? Region.Cold : temperature >= HotFrom ? Region.Hot : Region.Temperate;

    /// <summary>A rock code's group; null or unknown rock counts as sedimentary, the commonest
    /// surface rock.</summary>
    public string RockGroupOf(string? rock) =>
        rock != null && _rocks.TryGetValue(rock, out var g) ? g : Region.Sedimentary;

    public Region Classify(float temperature, string? rock) => new(ClimateOf(temperature), RockGroupOf(rock));
}
