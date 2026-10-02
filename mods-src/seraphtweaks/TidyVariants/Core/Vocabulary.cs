namespace SeraphHorizons.SeraphTweaks.TidyVariants.Core;

/// <summary>
/// Built-in name and value lists used to classify dimensions, plus the default preferred values. Every
/// list here is data; <see cref="TidySettings"/> copies them and the override file can extend or replace them.
/// Values are compared ordinally (Vintage Story codes are lower case).
/// </summary>
public static class Vocabulary
{
    /// <summary>A dimension is a material if at least this share of its distinct values is in the material's list...</summary>
    public const double MaterialRatio = 0.6;

    /// <summary>...and at least min(this, distinct values) of them match (so a two-value dimension needs both).</summary>
    public const int MaterialMinMatches = 2;

    /// <summary>Color words are generic ("red", "white"), so a color dimension needs at least this many matches.</summary>
    public const int ColorMinMatches = 3;

    /// <summary>Same ratio and minimum for process-state values.</summary>
    public const double ProcessRatio = 0.6;

    /// <summary>worldproperties files (path under <c>worldproperties/</c>, domain <c>game</c> unless given) per material kind.</summary>
    public static readonly IReadOnlyDictionary<MaterialKind, string[]> MaterialProperties = new Dictionary<MaterialKind, string[]>
    {
        [MaterialKind.Rock] = ["block/rock", "block/rockwithdeposit"],
        [MaterialKind.Wood] = ["block/wood"],
        [MaterialKind.Metal] = ["block/metal", "block/toolmetal"],
        [MaterialKind.Color] = [],
    };

    /// <summary>Values added to each material kind on top of the worldproperties lists. Vanilla has no color
    /// list, so colors are built in (dye, cloth and clay colors as used by vanilla and MoreRoads).</summary>
    public static readonly IReadOnlyDictionary<MaterialKind, string[]> MaterialValues = new Dictionary<MaterialKind, string[]>
    {
        [MaterialKind.Rock] = [],
        [MaterialKind.Wood] = ["aged", "veryaged"],
        [MaterialKind.Metal] = [],
        [MaterialKind.Color] =
        [
            "black", "blue", "brown", "cream", "cyan", "darkblue", "darkbrown", "darkgray", "darkgreen", "darkred",
            "earthyorange", "fire", "gray", "grey", "green", "lightblue", "lightgray", "lime", "magenta", "orange",
            "pink", "plain", "purple", "red", "tan", "violet", "white", "woad", "yellow", "clinker", "beige",
        ],
    };

    /// <summary>Generic material words, counted with rock, wood and metal for mixed-material dimensions (yangtransport's
    /// rails in <c>wood</c>/<c>metal</c>, mortar and pestle in <c>wood</c>/<c>stone</c>). The value maps to the kind given.</summary>
    public static readonly IReadOnlyDictionary<string, MaterialKind> GenericMaterials = new Dictionary<string, MaterialKind>
    {
        ["wood"] = MaterialKind.Wood, ["wooden"] = MaterialKind.Wood, ["stone"] = MaterialKind.Rock, ["metal"] = MaterialKind.Metal,
        ["bone"] = MaterialKind.None, ["clay"] = MaterialKind.None, ["glass"] = MaterialKind.None, ["leather"] = MaterialKind.None,
        ["cloth"] = MaterialKind.None, ["reed"] = MaterialKind.None, ["horn"] = MaterialKind.None, ["hide"] = MaterialKind.None,
    };

    /// <summary>Names that are orientation whatever their values.</summary>
    public static readonly string[] OrientationNames =
        ["rot", "rotation", "facing", "orientation", "horizontalorientation", "verticalorientation"];

    /// <summary>Names that are orientation only when every value is an orientation word (see <see cref="IsOrientationValue"/>,
    /// extended with <see cref="LooseOrientationValues"/>).</summary>
    public static readonly string[] LooseOrientationNames =
        ["side", "sides", "direction", "dir", "v", "h", "updown", "attach", "attachment", "face", "knoborientation", "hinge", "axis"];

    /// <summary>Extra orientation values accepted only under <see cref="LooseOrientationNames"/>.</summary>
    public static readonly string[] LooseOrientationValues =
        ["left", "right", "top", "bottom", "front", "back", "center", "horizontal", "vertical", "x", "y", "z", "ud"];

    /// <summary>Compass and vertical words; a dimension whose values are all of these (or letter combinations of
    /// <c>n e s w u d</c> such as <c>ns</c>, <c>nesw</c>) is orientation whatever its name.</summary>
    public static readonly string[] OrientationValues = ["north", "east", "south", "west", "up", "down"];

    /// <summary>A dimension whose values are all of these is open/closed state whatever its name.</summary>
    public static readonly string[] OpenClosedValues = ["open", "opened", "closed", "shut", "ajar"];

    /// <summary>Names that are process state only when at least one value is a process value: <c>state</c> also
    /// names shapes (slidingwoodenshutters: <c>state[left,half,right,halftop,fulltop]</c>, with open/closed in <c>status</c>).</summary>
    public static readonly string[] LooseProcessNames = ["state", "states", "condition"];

    /// <summary>Names that are process state whatever their values (unless the values say otherwise first).</summary>
    public static readonly string[] ProcessNames =
    [
        "stage", "growthstage", "growth", "ripeness", "cooked", "cookstate", "cookingstate",
        "firestate", "fired", "burnstate", "cover", "age", "wetness", "moisture", "dryness", "freshness", "lit",
    ];

    /// <summary>Process-state values: cooking, firing, burning, drying, growth, snow cover, wear.</summary>
    public static readonly string[] ProcessValues =
    [
        "raw", "cooked", "partbaked", "charred", "perfect", "baked", "burned", "burnt", "burnedout", "extinct", "lit",
        "unlit", "dough", "bread", "fired", "unfired", "dry", "dried", "wet", "cured", "curing", "fresh", "rotten",
        "ripe", "unripe", "empty", "flowering", "harvested", "sliced", "smashed", "tender", "edible", "mashed",
        "soaked", "salted", "unsalted", "oiled", "grown", "placed", "good", "damaged", "normal", "free", "snow", "ice",
        "bake", "syrup", "cut",
    ];

    /// <summary>A value ending in one of these is a process value too (<c>tenderpartbaked</c>, <c>meatballcharred</c>).</summary>
    public static readonly string[] ProcessSuffixes = ["partbaked", "charred", "cooked", "burned", "burnt"];

    /// <summary>Names that are grade/size/quality (or an amount or level: soil fertility, grass coverage) whatever their values.</summary>
    public static readonly string[] GradeNames =
        ["grade", "size", "quality", "fertility", "coverage", "grasscoverage", "density", "thickness", "fullness", "level", "amount"];

    /// <summary>Grade, size, amount and level words. A dimension with two or more values, at least 60% (and two) of them
    /// these, is grade/size/quality whatever its name: termite mounds' <c>size[medium,large]</c>, soil's
    /// <c>fertility[verylow,low,medium,compost,high]</c> and <c>grasscoverage[none,verysparse,sparse,normal]</c>.</summary>
    public static readonly string[] GradeValues =
    [
        "poor", "medium", "rich", "bountiful", "tiny", "small", "large", "huge",
        "none", "verylow", "low", "high", "veryhigh", "sparse", "verysparse", "dense", "verydense", "normal",
        "thin", "thick", "light", "heavy",
    ];

    /// <summary>Share of a dimension's values that must be <see cref="GradeValues"/> (at least two of them).</summary>
    public const double GradeRatio = 0.6;

    /// <summary>True for a value that is only digits (<c>0</c>, <c>12</c>): numbered variants (<c>coverage[0..10]</c>,
    /// <c>texture[1..10]</c>, <c>layer[1..7]</c>) are filler.</summary>
    public static bool IsNumber(string v)
    {
        if (v.Length == 0) return false;
        foreach (char c in v) if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>
    /// The value with each <c>-</c> or <c>/</c> separated token's trailing digits dropped, when the token keeps a
    /// letter: <c>collapsed3</c> to <c>collapsed</c>, <c>ruined-barred2</c> to <c>ruined-barred</c>,
    /// <c>base2-short</c> to <c>base-short</c>, <c>mk3</c> to <c>mk</c>; a size keeps its numbers (<c>round2x1</c>).
    /// Returns <paramref name="v"/> itself when nothing changes.
    /// </summary>
    public static string NumberStem(string v)
    {
        System.Text.StringBuilder? sb = null;
        int start = 0;
        for (int i = 0; i <= v.Length; i++)
        {
            if (i < v.Length && v[i] is not ('-' or '/')) continue;
            int end = i;
            while (end > start && char.IsAsciiDigit(v[end - 1])) end--;
            // A size such as 2x1 keeps its numbers.
            bool keep = end == i || (end - start >= 2 && v[end - 1] == 'x' && char.IsAsciiDigit(v[end - 2]));
            if (!keep)
            {
                keep = true;
                for (int k = start; k < end; k++) if (char.IsAsciiLetter(v[k])) { keep = false; break; }
            }
            if (!keep && sb is null) sb = new System.Text.StringBuilder(v.Length).Append(v, 0, start);
            sb?.Append(v, start, (keep ? i : end) - start);
            if (i < v.Length) sb?.Append(v[i]);
            start = i + 1;
        }
        return sb?.ToString() ?? v;
    }

    /// <summary>Default preferred values per <see cref="DimensionInfo.PreferenceKey"/>. The first value present wins.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Preferred = new Dictionary<string, string[]>
    {
        ["rock"] = ["granite", "andesite", "basalt"],
        ["wood"] = ["oak", "birch", "pine"],
        ["metal"] = ["copper", "iron", "tinbronze"],
        ["color"] = ["plain", "red", "white", "brown", "tan"],
        ["process"] =
        [
            "cooked", "perfect", "baked", "bread", "fired", "lit", "cured", "ripe", "edible", "tender", "normal",
            "good", "fresh", "grown", "free",
        ],
        ["grade"] = ["medium", "normal"],
        ["orientation"] = ["up", "north", "ns", "n", "u", "down", "east", "south", "west"],
        ["openclosed"] = ["closed", "shut"],
        ["filler"] = [],
    };

    /// <summary>
    /// The value used to compare against lists: decorations around the word are dropped (Tankards and Goblets
    /// writes its woods as <c>oak.</c>). Only leading/trailing characters that aren't letters or digits go.
    /// </summary>
    public static string Normalize(string v)
    {
        int s = 0, e = v.Length;
        while (s < e && !char.IsAsciiLetterOrDigit(v[s])) s++;
        while (e > s && !char.IsAsciiLetterOrDigit(v[e - 1])) e--;
        return s == 0 && e == v.Length ? v : v[s..e];
    }

    /// <summary>True for compass words and for letter combinations of n/e/s/w/u/d without repeats (<c>ns</c>, <c>nesw</c>).</summary>
    public static bool IsOrientationValue(string v)
    {
        if (Array.IndexOf(OrientationValues, v) >= 0) return true;
        if (v.Length is 0 or > 6) return false;
        int seen = 0;
        foreach (char c in v)
        {
            int bit = c switch { 'n' => 1, 'e' => 2, 's' => 4, 'w' => 8, 'u' => 16, 'd' => 32, _ => 0 };
            if (bit == 0 || (seen & bit) != 0) return false;
            seen |= bit;
        }
        return true;
    }

    public static bool IsProcessValue(string v)
    {
        if (Array.IndexOf(ProcessValues, v) >= 0) return true;
        foreach (var s in ProcessSuffixes) if (v.Length > s.Length && v.EndsWith(s, StringComparison.Ordinal)) return true;
        // A vocabulary word followed by digits: grown1, bake4.
        int end = v.Length;
        while (end > 0 && char.IsAsciiDigit(v[end - 1])) end--;
        return end < v.Length && end > 0 && Array.IndexOf(ProcessValues, v[..end]) >= 0;
    }
}
