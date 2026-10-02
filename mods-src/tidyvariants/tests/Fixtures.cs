using SeraphHorizons.TidyVariants.Core;

namespace SeraphHorizons.TidyVariants.Tests;

/// <summary>
/// Trimmed-down real data: worldproperties lists from vanilla 1.22.7 (assets/survival/worldproperties)
/// and variantgroups from vanilla and the pack's mods (DoorVariants, MoreRoads, ExpandedFoods, Food Shelves).
/// </summary>
static class Fx
{
    public static readonly string[] Rock =
        ["andesite", "chalk", "chert", "conglomerate", "limestone", "claystone", "granite", "sandstone", "shale", "basalt", "peridotite", "phyllite", "slate", "bauxite"];

    public static readonly string[] RockWithDeposit =
    [
        .. Rock, "obsidian", "kimberlite", "scoria", "tuff", "halite", "suevite", "whitemarble", "redmarble", "greenmarble", "travertine",
    ];

    public static readonly string[] Wood =
        ["birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony", "walnut", "purpleheart"];

    public static readonly string[] Metal =
    [
        "bismuth", "bismuthbronze", "blackbronze", "brass", "chromium", "copper", "cupronickel", "electrum", "gold", "iron",
        "meteoriciron", "lead", "molybdochalkos", "platinum", "nickel", "silver", "stainlesssteel", "steel", "tin", "tinbronze",
        "titanium", "uranium", "zinc",
    ];

    public static readonly string[] OreGraded =
        ["nativecopper", "limonite", "quartz_nativegold", "galena", "cassiterite", "chromite", "ilmenite", "sphalerite", "malachite"];

    public static readonly string[] Grades = ["poor", "medium", "rich", "bountiful"];
    public static readonly string[] Horizontal = ["north", "east", "south", "west"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> WorldProperties => new Dictionary<string, IReadOnlyList<string>>
    {
        ["game:block/rock"] = Rock,
        ["game:block/rockwithdeposit"] = RockWithDeposit,
        ["game:block/wood"] = Wood,
        ["game:block/metal"] = Metal,
        ["game:block/toolmetal"] = ["bismuth", "bismuthbronze", "blackbronze", "brass", "copper", "gold", "iron", "meteoriciron", "lead", "molybdochalkos", "silver", "steel", "tinbronze"],
        ["game:block/ore-graded"] = OreGraded,
        ["game:abstract/horizontalorientation"] = Horizontal,
    };

    public static TidySettings Settings(OverrideFile? o = null) => new(WorldProperties, o);

    /// <summary>One entry; the code is <c>domain:base-v1-v2...</c> built from the variant.</summary>
    public static CreativeEntry E(string domainBase, EntryKind kind, params (string Name, string Value)[] variant) =>
        E(domainBase, kind, variant, null);

    public static CreativeEntry E(string domainBase, EntryKind kind, (string Name, string Value)[] variant, AttributeStack? stack, params string[] tabs)
    {
        var code = domainBase + string.Concat(variant.Select(v => "-" + v.Value));
        return new CreativeEntry(code, kind, variant.Select(v => KeyValuePair.Create(v.Name, v.Value)).ToList(),
            tabs.Length == 0 ? ["general"] : tabs, stack);
    }

    public static AttributeStack Stack(string key, params (string Name, string Value)[] values) =>
        new(key, values.Select(v => KeyValuePair.Create(v.Name, v.Value)).ToList());

    /// <summary>The cartesian product in variantgroups order (first group slowest), as the game lists it.</summary>
    public static IEnumerable<CreativeEntry> Product(string domainBase, EntryKind kind, params (string Name, string[] Values)[] groups)
    {
        IEnumerable<(string, string)[]> combos = [[]];
        foreach (var (name, vals) in groups)
            combos = combos.SelectMany(c => vals.Select(v => c.Append((name, v)).ToArray())).ToList();
        foreach (var c in combos) yield return E(domainBase, kind, c);
    }

    public static TidyResolution Resolve(IEnumerable<CreativeEntry> entries, string? overrides = null) =>
        TidyEngine.Resolve(entries.ToList(), WorldProperties, overrides is null ? null : OverrideFile.Parse(overrides));

    public static int Index(this TidyResolution r, string code)
    {
        for (int i = 0; i < r.Entries.Count; i++) if (r.Entries[i].Code == code) return i;
        throw new KeyNotFoundException(code);
    }

    public static TidyGroup GroupOf(this TidyResolution r, string code)
    {
        int g = r.GroupOf(r.Index(code));
        Assert.True(g >= 0, $"{code} is not grouped");
        return r.Groups[g];
    }

    public static string[] Codes(this TidyResolution r, IEnumerable<int> entries) => entries.Select(i => r.Entries[i].Code).ToArray();

    public static DimensionInfo Classify(string name, params string[] values) =>
        Classifier.Classify(name, values.Distinct().ToArray(), null, Settings());
}
