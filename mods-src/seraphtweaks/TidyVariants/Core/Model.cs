namespace SeraphHorizons.SeraphTweaks.TidyVariants.Core;

/// <summary>Whether a creative entry is a block or an item.</summary>
public enum EntryKind { Block, Item }

/// <summary>
/// One entry of the creative inventory, as the game layer reads it: one per collectible, or one per
/// attribute stack for collectibles that put several stacks in creative (clutter, shields, Food Shelves).
/// The engine never looks at anything but these fields.
/// </summary>
public sealed class CreativeEntry
{
    /// <param name="code">Full asset code, <c>domain:path</c> (a missing domain means <c>game</c>).</param>
    /// <param name="kind">Block or item.</param>
    /// <param name="variant">The collectible's resolved variant map in variantgroups order (the game's
    /// <c>CollectibleObject.Variant</c>). The code path is expected to be <c>&lt;base&gt;-&lt;v1&gt;-&lt;v2&gt;...</c>.</param>
    /// <param name="tabs">Creative tab codes this entry is listed in.</param>
    /// <param name="stack">Set only for attribute-stack entries; null for plain collectibles.</param>
    /// <param name="propertySources">Optional hint: variant dimension name to the worldproperties
    /// <c>loadFromProperties</c> it came from (<c>block/rock</c>, <c>game:block/wood</c>, ...). Usually unavailable at runtime.</param>
    /// <param name="shippedGroupBy">The collectible's own <c>attributes.handbook.groupBy</c> patterns as shipped by
    /// vanilla or its mod (code-path wildcards, no domain). <c>{dim}</c> placeholders are filled from the variant.</param>
    public CreativeEntry(
        string code,
        EntryKind kind,
        IReadOnlyList<KeyValuePair<string, string>>? variant = null,
        IReadOnlyList<string>? tabs = null,
        AttributeStack? stack = null,
        IReadOnlyDictionary<string, string>? propertySources = null,
        IReadOnlyList<string>? shippedGroupBy = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        int colon = code.IndexOf(':');
        Domain = colon < 0 ? "game" : code[..colon];
        Path = colon < 0 ? code : code[(colon + 1)..];
        Code = Domain + ":" + Path;
        Kind = kind;
        Variant = variant ?? [];
        Tabs = tabs ?? [];
        Stack = stack;
        PropertySources = propertySources;
        ShippedGroupBy = shippedGroupBy ?? [];
    }

    /// <summary>Normalised <c>domain:path</c>.</summary>
    public string Code { get; }
    public string Domain { get; }
    public string Path { get; }
    public EntryKind Kind { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Variant { get; }
    public IReadOnlyList<string> Tabs { get; }
    public AttributeStack? Stack { get; }
    public IReadOnlyDictionary<string, string>? PropertySources { get; }
    public IReadOnlyList<string> ShippedGroupBy { get; }

    public override string ToString() => Stack is null ? Code : Code + "{" + Stack.Key + "}";
}

/// <summary>
/// Identifies one attribute stack of a collectible. <see cref="Key"/> must be stable and unique among
/// the stacks of one code (e.g. the stack's attribute JSON). <see cref="Values"/> holds the attributes
/// the game layer chose to expose (top-level string attributes such as <c>type</c>, <c>wood</c>,
/// <c>metal</c>); each one is classified like a variant dimension named <c>attr:&lt;key&gt;</c>.
/// </summary>
public sealed class AttributeStack(string key, IReadOnlyList<KeyValuePair<string, string>>? values = null)
{
    public string Key { get; } = key ?? throw new ArgumentNullException(nameof(key));
    public IReadOnlyList<KeyValuePair<string, string>> Values { get; } = values ?? [];

    public string? Get(string name)
    {
        foreach (var kv in Values) if (kv.Key == name) return kv.Value;
        return null;
    }
}

/// <summary>How a dimension (variant group or exposed attribute) is treated.</summary>
public enum DimensionClass
{
    /// <summary>Stays a separate group per value (generic <c>type</c>, ore, style, ...).</summary>
    Meaningful,
    /// <summary>Rock, wood, metal or color (see <see cref="MaterialKind"/>). Filler.</summary>
    Material,
    /// <summary>Cooking state, raw/fired, growth stage, lit/extinct, snow cover, ... Filler.</summary>
    ProcessState,
    /// <summary>Grade, size or quality. Filler.</summary>
    GradeSizeQuality,
    /// <summary>Facing / rotation. Hidden down to one canonical value; filler if unhidden.</summary>
    Orientation,
    /// <summary>Open / closed. Hidden down to one canonical value; filler if unhidden.</summary>
    OpenClosedState,
    /// <summary>Explicit filler from the override file, or a dimension matching one of the override file's
    /// named value <c>lists</c> (see <see cref="DimensionInfo.List"/>).</summary>
    Filler,
}

public enum MaterialKind { None, Rock, Wood, Metal, Color }

/// <summary>The classification of one dimension, with a human-readable reason (for reports and tests).
/// <paramref name="List"/> names the override file's value list a <see cref="DimensionClass.Filler"/> dimension matched.
/// <paramref name="ByStem"/> (meaningful dimensions only): values that differ only in their numbers
/// (<see cref="Vocabulary.NumberStem"/>: <c>collapsed1</c>..<c>collapsed4</c>) share a group.</summary>
public sealed record DimensionInfo(string Name, DimensionClass Class, MaterialKind Material, string Reason, string? List = null, bool ByStem = false)
{
    /// <summary>The value that splits groups: the value itself, or its number stem when <see cref="ByStem"/>.</summary>
    public string GroupValue(string value) => ByStem ? Vocabulary.NumberStem(value) : value;

    /// <summary>Filler dimensions collapse into one group; meaningful ones split groups.</summary>
    public bool IsFiller => Class != DimensionClass.Meaningful;

    /// <summary>Dimensions the hide rule reduces to one canonical value.</summary>
    public bool IsHideable => Class is DimensionClass.Orientation or DimensionClass.OpenClosedState;

    /// <summary>Key into <see cref="Preferences"/> for this dimension's preferred values.</summary>
    public string PreferenceKey => Class switch
    {
        DimensionClass.Material => Material.ToString().ToLowerInvariant(),
        DimensionClass.ProcessState => "process",
        DimensionClass.GradeSizeQuality => "grade",
        DimensionClass.Orientation => "orientation",
        DimensionClass.OpenClosedState => "openclosed",
        DimensionClass.Filler => List ?? "filler",
        _ => "",
    };
}
