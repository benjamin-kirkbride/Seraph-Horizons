using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.TidyVariants.Core;

/// <summary>What an override rule does. See <c>Core/README.md</c> for the file format.</summary>
public enum RuleAction
{
    /// <summary>Matching entries form one group (or one per <see cref="GroupSpec.By"/> value tuple).</summary>
    Group,
    /// <summary>Matching entries are plain entries, never grouped.</summary>
    Ungroup,
    /// <summary>Overrides the class of named dimensions for matching entries (<c>split</c> is sugar for "meaningful").</summary>
    Dimensions,
    /// <summary>Matching entries are hidden.</summary>
    Hide,
    /// <summary>Matching entries are never hidden by the automatic hide rule.</summary>
    Unhide,
    /// <summary>Matching entries are preferred as their group's representative.</summary>
    Prefer,
}

/// <summary>Selects entries by asset domain plus a code wildcard (never modid), optionally kind and attribute values.</summary>
public sealed class EntryMatch
{
    public EntryMatch(string domain, string code, EntryKind? kind = null, IReadOnlyList<KeyValuePair<string, string>>? attributes = null)
        : this(domain, [code], kind, attributes) { }

    public EntryMatch(string domain, IReadOnlyList<string> codes, EntryKind? kind = null, IReadOnlyList<KeyValuePair<string, string>>? attributes = null)
    {
        Domain = domain;
        Codes = codes;
        Kind = kind;
        Attributes = attributes ?? [];
        prefixes = codes.Select(Wildcard.LiteralPrefix).ToArray();
    }

    /// <summary>Asset domain, exact or a <c>*</c> wildcard.</summary>
    public string Domain { get; }
    /// <summary><c>*</c> wildcards over the code path (no domain); any one matching is enough. Several make cross-type groups.</summary>
    public IReadOnlyList<string> Codes { get; }
    public EntryKind? Kind { get; }
    /// <summary>Attribute name to value wildcard; every one must match, so entries without a stack never match.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; }
    readonly string[] prefixes;

    public bool Matches(CreativeEntry e)
    {
        if (Kind is { } k && e.Kind != k) return false;
        bool code = false;
        for (int i = 0; i < prefixes.Length && !code; i++)
            code = e.Path.StartsWith(prefixes[i], StringComparison.Ordinal) && Wildcard.IsMatch(Codes[i], e.Path);
        if (!code || !Wildcard.IsMatch(Domain, e.Domain)) return false;
        foreach (var (name, pattern) in Attributes)
        {
            string? v = e.Stack?.Get(name);
            if (v is null || !Wildcard.IsMatch(pattern, v)) return false;
        }
        return true;
    }

    public override string ToString() => Domain + ":" + string.Join("|", Codes) + (Kind is { } k ? " (" + k.ToString().ToLowerInvariant() + ")" : "");
}

/// <summary>The <c>group</c> part of a rule.</summary>
public sealed class GroupSpec(string id, IReadOnlyList<string> by, string? title, EntryMatch? representative)
{
    /// <summary>Group id; may hold <c>{dim}</c> placeholders, each of which must be listed in <see cref="By"/>, and
    /// <c>{base}</c> (the family's base code path), which by itself splits the matches into one group per base.</summary>
    public string Id { get; } = id;
    /// <summary>Dimensions (variant names, or <c>attr:name</c>) that split the matches into one group per value tuple.</summary>
    public IReadOnlyList<string> By { get; } = by;
    /// <summary>Lang key for the group title; may hold <c>{dim}</c> placeholders from <see cref="By"/>.</summary>
    public string? Title { get; } = title;
    /// <summary>The first member (creative order) this matches becomes the representative.</summary>
    public EntryMatch? Representative { get; } = representative;

    /// <summary>True when the id or title uses <c>{base}</c>: one group per family base code.</summary>
    public bool UsesBase { get; } = id.Contains("{base}") || (title?.Contains("{base}") ?? false);
}

public sealed class OverrideRule
{
    internal OverrideRule(int index, RuleAction action, EntryMatch match, GroupSpec? group,
        IReadOnlyDictionary<string, DimensionOverride>? dimensions, string? note)
    {
        Index = index; Action = action; Match = match; Group = group; Dimensions = dimensions; Note = note;
    }

    /// <summary>Position in the file's <c>rules</c> array.</summary>
    public int Index { get; }
    public RuleAction Action { get; }
    public EntryMatch Match { get; }
    public GroupSpec? Group { get; }
    public IReadOnlyDictionary<string, DimensionOverride>? Dimensions { get; }
    public string? Note { get; }
}

/// <summary>An explicit dimension class from the override file (<see cref="List"/> set for a named value list).</summary>
public readonly record struct DimensionOverride(DimensionClass Class, MaterialKind Material = MaterialKind.None, string? List = null);

/// <summary>A named value list from the override file (e.g. <c>claycolor</c>): a dimension whose values are in it
/// (same thresholds as materials) is filler, and <see cref="Preferred"/> picks its representative value.</summary>
public sealed record ValueList(IReadOnlyList<string> Values, IReadOnlyList<string> Properties, IReadOnlyList<string> Preferred);

/// <summary>Extra worldproperties files and values for a material kind.</summary>
public sealed record MaterialExtension(IReadOnlyList<string> Properties, IReadOnlyList<string> Values);

/// <summary>The parsed pack override file. See <c>Core/README.md</c> for the format.</summary>
public sealed class OverrideFile
{
    public static readonly OverrideFile Empty = new([], new Dictionary<string, string[]>(), new Dictionary<MaterialKind, MaterialExtension>());

    public OverrideFile(IReadOnlyList<OverrideRule> rules, IReadOnlyDictionary<string, string[]> preferred,
        IReadOnlyDictionary<MaterialKind, MaterialExtension> materials,
        IReadOnlyDictionary<string, ValueList>? lists = null, bool? honorShippedGroupBy = null)
    {
        Rules = rules; Preferred = preferred; Materials = materials;
        Lists = lists ?? new Dictionary<string, ValueList>();
        HonorShippedGroupBy = honorShippedGroupBy;
    }

    /// <summary>Named value lists, in file order (a <see cref="Dictionary{TKey,TValue}"/> keeps insertion order here).</summary>
    public IReadOnlyDictionary<string, ValueList> Lists { get; }

    /// <summary><c>honorShippedGroupBy</c>: null when the file doesn't say (the default, true, applies).</summary>
    public bool? HonorShippedGroupBy { get; }

    public IReadOnlyList<OverrideRule> Rules { get; }
    /// <summary>Replaces the default preferred list for each key given.</summary>
    public IReadOnlyDictionary<string, string[]> Preferred { get; }
    /// <summary>Extends the material lists.</summary>
    public IReadOnlyDictionary<MaterialKind, MaterialExtension> Materials { get; }

    public const int FormatVersion = 1;

    static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    static readonly Regex Placeholder = new(@"\{([^{}]+)\}", RegexOptions.CultureInvariant);

    /// <summary>Parses the file; throws <see cref="OverrideFormatException"/> listing every problem found.</summary>
    public static OverrideFile Parse(string json)
    {
        if (TryParse(json, out var file, out var errors)) return file!;
        throw new OverrideFormatException(errors);
    }

    public static bool TryParse(string json, out OverrideFile? file, out IReadOnlyList<string> errors)
    {
        var errs = new List<string>();
        file = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, DocOptions); }
        catch (JsonException ex) { errors = [$"invalid JSON: {ex.Message}"]; return false; }

        using (doc)
        {
            var root = doc.RootElement;
            var rules = new List<OverrideRule>();
            var preferred = new Dictionary<string, string[]>();
            var materials = new Dictionary<MaterialKind, MaterialExtension>();
            var lists = new Dictionary<string, ValueList>(StringComparer.Ordinal);
            bool? honor = null;
            if (root.ValueKind != JsonValueKind.Object) { errors = ["the file must be a JSON object"]; return false; }

            foreach (var prop in root.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "version":
                        if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out int v) || v != FormatVersion)
                            errs.Add($"version: must be {FormatVersion}");
                        break;
                    case "note":
                        if (prop.Value.ValueKind != JsonValueKind.String) errs.Add("note: must be a string");
                        break;
                    case "preferred":
                        ParsePreferred(prop.Value, preferred, errs);
                        break;
                    case "materials":
                        ParseMaterials(prop.Value, materials, errs);
                        break;
                    case "lists":
                        ParseLists(prop.Value, lists, errs);
                        break;
                    case "honorShippedGroupBy":
                        if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) honor = prop.Value.GetBoolean();
                        else errs.Add("honorShippedGroupBy: must be true or false");
                        break;
                    case "rules":
                        if (prop.Value.ValueKind != JsonValueKind.Array) { errs.Add("rules: must be an array"); break; }
                        int i = 0;
                        foreach (var r in prop.Value.EnumerateArray())
                        {
                            var rule = ParseRule(r, i, errs);
                            if (rule is not null) rules.Add(rule);
                            i++;
                        }
                        break;
                    default:
                        errs.Add($"unknown field '{prop.Name}'");
                        break;
                }
            }

            // Group ids must be unique across rules (templates compared as written).
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in rules)
            {
                if (r.Group is null) continue;
                if (seen.TryGetValue(r.Group.Id, out int first))
                    errs.Add($"rules[{r.Index}]: group id '{r.Group.Id}' is already used by rules[{first}]");
                else seen[r.Group.Id] = r.Index;
            }

            // Dimension classes naming a list must name one the file defines.
            foreach (var r in rules)
                if (r.Dimensions is not null)
                    foreach (var (dim, o) in r.Dimensions)
                        if (o.List is { } l && !lists.ContainsKey(l))
                            errs.Add($"rules[{r.Index}].dimensions.{dim}: unknown class '{l}' (expected meaningful, filler, rock, wood, metal, color, process, grade, orientation, openclosed or a name from 'lists')");

            errors = errs;
            if (errs.Count > 0) return false;
            file = new OverrideFile(rules, preferred, materials, lists, honor);
            return true;
        }
    }

    static readonly string[] PreferenceKeys = ["rock", "wood", "metal", "color", "process", "grade", "orientation", "openclosed", "filler"];

    static void ParsePreferred(JsonElement e, Dictionary<string, string[]> into, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Object) { errs.Add("preferred: must be an object"); return; }
        foreach (var p in e.EnumerateObject())
        {
            if (Array.IndexOf(PreferenceKeys, p.Name) < 0)
            {
                errs.Add($"preferred: unknown key '{p.Name}' (expected one of {string.Join(", ", PreferenceKeys)})");
                continue;
            }
            var list = StringArray(p.Value, $"preferred.{p.Name}", errs);
            if (list is not null) into[p.Name] = list;
        }
    }

    static void ParseMaterials(JsonElement e, Dictionary<MaterialKind, MaterialExtension> into, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Object) { errs.Add("materials: must be an object"); return; }
        foreach (var p in e.EnumerateObject())
        {
            var kind = ParseMaterialKind(p.Name);
            if (kind is null) { errs.Add($"materials: unknown material '{p.Name}' (expected rock, wood, metal or color)"); continue; }
            if (p.Value.ValueKind != JsonValueKind.Object) { errs.Add($"materials.{p.Name}: must be an object"); continue; }
            string[] props = [], values = [];
            foreach (var f in p.Value.EnumerateObject())
            {
                switch (f.Name)
                {
                    case "properties": props = StringArray(f.Value, $"materials.{p.Name}.properties", errs) ?? []; break;
                    case "values": values = StringArray(f.Value, $"materials.{p.Name}.values", errs) ?? []; break;
                    default: errs.Add($"materials.{p.Name}: unknown field '{f.Name}'"); break;
                }
            }
            into[kind.Value] = new MaterialExtension(props, values);
        }
    }

    static void ParseLists(JsonElement e, Dictionary<string, ValueList> into, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Object) { errs.Add("lists: must be an object"); return; }
        foreach (var p in e.EnumerateObject())
        {
            string where = $"lists.{p.Name}";
            if (Array.IndexOf(PreferenceKeys, p.Name) >= 0 || BuiltInClasses.Contains(p.Name) || p.Name.Length == 0)
            {
                errs.Add($"{where}: '{p.Name}' is a built-in name; pick another");
                continue;
            }
            if (p.Value.ValueKind != JsonValueKind.Object) { errs.Add($"{where}: must be an object"); continue; }
            string[] values = [], props = [], preferred = [];
            foreach (var f in p.Value.EnumerateObject())
            {
                switch (f.Name)
                {
                    case "values": values = StringArray(f.Value, $"{where}.values", errs) ?? []; break;
                    case "properties": props = StringArray(f.Value, $"{where}.properties", errs) ?? []; break;
                    case "preferred": preferred = StringArray(f.Value, $"{where}.preferred", errs) ?? []; break;
                    default: errs.Add($"{where}: unknown field '{f.Name}'"); break;
                }
            }
            if (values.Length == 0 && props.Length == 0) errs.Add($"{where}: needs 'values' or 'properties'");
            into[p.Name] = new ValueList(values, props, preferred);
        }
    }

    static readonly string[] BuiltInClasses = ["meaningful", "filler", "rock", "wood", "metal", "color", "process", "grade", "orientation", "openclosed"];

    static MaterialKind? ParseMaterialKind(string s) => s switch
    {
        "rock" => MaterialKind.Rock,
        "wood" => MaterialKind.Wood,
        "metal" => MaterialKind.Metal,
        "color" => MaterialKind.Color,
        _ => null,
    };

    static readonly string[] ActionKeys = ["group", "ungroup", "dimensions", "split", "hide", "prefer"];

    static OverrideRule? ParseRule(JsonElement e, int index, List<string> errs)
    {
        string where = $"rules[{index}]";
        int before = errs.Count;
        if (e.ValueKind != JsonValueKind.Object) { errs.Add($"{where}: must be an object"); return null; }

        EntryMatch? match = null;
        string? note = null;
        var actions = new List<string>();
        RuleAction action = RuleAction.Group;
        GroupSpec? group = null;
        Dictionary<string, DimensionOverride>? dims = null;

        foreach (var p in e.EnumerateObject())
        {
            switch (p.Name)
            {
                case "match":
                    match = ParseMatch(p.Value, $"{where}.match", errs, requireDomain: true);
                    break;
                case "note":
                    if (p.Value.ValueKind != JsonValueKind.String) errs.Add($"{where}.note: must be a string");
                    else note = p.Value.GetString();
                    break;
                case "group":
                    actions.Add(p.Name); action = RuleAction.Group;
                    group = ParseGroup(p.Value, $"{where}.group", errs);
                    break;
                case "ungroup":
                    actions.Add(p.Name); action = RuleAction.Ungroup;
                    if (p.Value.ValueKind != JsonValueKind.True) errs.Add($"{where}.ungroup: must be true");
                    break;
                case "hide":
                    actions.Add(p.Name);
                    if (p.Value.ValueKind == JsonValueKind.True) action = RuleAction.Hide;
                    else if (p.Value.ValueKind == JsonValueKind.False) action = RuleAction.Unhide;
                    else errs.Add($"{where}.hide: must be true (hide) or false (never hide)");
                    break;
                case "prefer":
                    actions.Add(p.Name); action = RuleAction.Prefer;
                    if (p.Value.ValueKind != JsonValueKind.True) errs.Add($"{where}.prefer: must be true");
                    break;
                case "dimensions":
                    actions.Add(p.Name); action = RuleAction.Dimensions;
                    dims = ParseDimensions(p.Value, $"{where}.dimensions", errs);
                    break;
                case "split":
                    actions.Add(p.Name); action = RuleAction.Dimensions;
                    var names = StringArray(p.Value, $"{where}.split", errs);
                    if (names is { Length: 0 }) errs.Add($"{where}.split: must list at least one dimension");
                    if (names is not null)
                    {
                        dims = new Dictionary<string, DimensionOverride>(StringComparer.Ordinal);
                        foreach (var n in names) dims[n] = new DimensionOverride(DimensionClass.Meaningful);
                    }
                    break;
                default:
                    errs.Add($"{where}: unknown field '{p.Name}'");
                    break;
            }
        }

        if (match is null && errs.Count == before) errs.Add($"{where}: 'match' is required");
        if (actions.Count == 0) errs.Add($"{where}: needs exactly one of {string.Join(", ", ActionKeys)}");
        else if (actions.Count > 1) errs.Add($"{where}: has {string.Join(" and ", actions)}; a rule does exactly one thing");

        if (errs.Count > before || match is null) return null;
        return new OverrideRule(index, action, match, group, dims, note);
    }

    static EntryMatch? ParseMatch(JsonElement e, string where, List<string> errs, bool requireDomain)
    {
        // Shorthand for a representative: a bare code wildcard.
        if (!requireDomain && e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString()!;
            if (s.Length == 0) { errs.Add($"{where}: must not be empty"); return null; }
            return new EntryMatch("*", s);
        }
        if (e.ValueKind != JsonValueKind.Object) { errs.Add($"{where}: must be an object"); return null; }
        string? domain = null;
        string[]? codes = null;
        EntryKind? kind = null;
        List<KeyValuePair<string, string>>? attrs = null;
        int before = errs.Count;
        foreach (var p in e.EnumerateObject())
        {
            switch (p.Name)
            {
                case "domain":
                    domain = NonEmptyString(p.Value, $"{where}.domain", errs);
                    if (domain is not null && domain.Contains(':')) errs.Add($"{where}.domain: a domain has no ':' ('{domain}')");
                    break;
                case "code":
                    codes = p.Value.ValueKind == JsonValueKind.Array
                        ? StringArray(p.Value, $"{where}.code", errs)
                        : NonEmptyString(p.Value, $"{where}.code", errs) is { } one ? [one] : null;
                    if (codes is { Length: 0 }) errs.Add($"{where}.code: must list at least one pattern");
                    foreach (var c in codes ?? [])
                        if (c.Contains(':')) errs.Add($"{where}.code: give the domain in 'domain' and only the path here ('{c}')");
                    break;
                case "kind":
                    var k = NonEmptyString(p.Value, $"{where}.kind", errs);
                    if (k == "block") kind = EntryKind.Block;
                    else if (k == "item") kind = EntryKind.Item;
                    else if (k is not null) errs.Add($"{where}.kind: must be 'block' or 'item'");
                    break;
                case "attributes":
                    if (p.Value.ValueKind != JsonValueKind.Object) { errs.Add($"{where}.attributes: must be an object"); break; }
                    attrs = [];
                    foreach (var a in p.Value.EnumerateObject())
                    {
                        var v = NonEmptyString(a.Value, $"{where}.attributes.{a.Name}", errs);
                        if (v is not null) attrs.Add(new(a.Name, v));
                    }
                    break;
                default:
                    errs.Add($"{where}: unknown field '{p.Name}'");
                    break;
            }
        }
        if (requireDomain && domain is null && errs.Count == before) errs.Add($"{where}: 'domain' is required (rules are keyed on asset domain, not modid)");
        if (codes is null && errs.Count == before) errs.Add($"{where}: 'code' is required");
        if (errs.Count > before) return null;
        return new EntryMatch(domain ?? "*", codes!, kind, attrs);
    }

    static GroupSpec? ParseGroup(JsonElement e, string where, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Object) { errs.Add($"{where}: must be an object"); return null; }
        int before = errs.Count;
        string? id = null, title = null;
        string[] by = [];
        EntryMatch? rep = null;
        foreach (var p in e.EnumerateObject())
        {
            switch (p.Name)
            {
                case "id": id = NonEmptyString(p.Value, $"{where}.id", errs); break;
                case "title": title = NonEmptyString(p.Value, $"{where}.title", errs); break;
                case "by": by = StringArray(p.Value, $"{where}.by", errs) ?? []; break;
                case "representative": rep = ParseMatch(p.Value, $"{where}.representative", errs, requireDomain: false); break;
                default: errs.Add($"{where}: unknown field '{p.Name}'"); break;
            }
        }
        if (id is null && errs.Count == before) errs.Add($"{where}: 'id' is required");
        if (errs.Count > before) return null;

        foreach (var (field, text) in new[] { ("id", id!), ("title", title) })
        {
            if (text is null) continue;
            foreach (Match m in Placeholder.Matches(text))
                if (m.Groups[1].Value != "base" && !by.Contains(m.Groups[1].Value))
                    errs.Add($"{where}.{field}: placeholder '{{{m.Groups[1].Value}}}' must be listed in 'by'");
        }
        foreach (var b in by)
            if (!id!.Contains("{" + b + "}"))
                errs.Add($"{where}.id: must contain '{{{b}}}' since 'by' lists it (one group per value)");
        if (errs.Count > before) return null;
        return new GroupSpec(id!, by, title, rep);
    }

    static Dictionary<string, DimensionOverride>? ParseDimensions(JsonElement e, string where, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Object) { errs.Add($"{where}: must be an object"); return null; }
        var d = new Dictionary<string, DimensionOverride>(StringComparer.Ordinal);
        int before = errs.Count;
        foreach (var p in e.EnumerateObject())
        {
            var v = NonEmptyString(p.Value, $"{where}.{p.Name}", errs);
            if (v is null) continue;
            DimensionOverride? o = v switch
            {
                "meaningful" => new(DimensionClass.Meaningful),
                "filler" => new(DimensionClass.Filler),
                "rock" => new(DimensionClass.Material, MaterialKind.Rock),
                "wood" => new(DimensionClass.Material, MaterialKind.Wood),
                "metal" => new(DimensionClass.Material, MaterialKind.Metal),
                "color" => new(DimensionClass.Material, MaterialKind.Color),
                "process" => new(DimensionClass.ProcessState),
                "grade" => new(DimensionClass.GradeSizeQuality),
                "orientation" => new(DimensionClass.Orientation),
                "openclosed" => new(DimensionClass.OpenClosedState),
                _ => new(DimensionClass.Filler, MaterialKind.None, v), // a named list; checked once 'lists' is read
            };
            d[p.Name] = o.Value;
        }
        if (d.Count == 0 && errs.Count == before) errs.Add($"{where}: must name at least one dimension");
        return d;
    }

    static string? NonEmptyString(JsonElement e, string where, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.String || e.GetString() is not { Length: > 0 } s)
        {
            errs.Add($"{where}: must be a non-empty string");
            return null;
        }
        return s;
    }

    static string[]? StringArray(JsonElement e, string where, List<string> errs)
    {
        if (e.ValueKind != JsonValueKind.Array) { errs.Add($"{where}: must be an array of strings"); return null; }
        var list = new List<string>();
        int i = 0;
        foreach (var x in e.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.String || x.GetString() is not { Length: > 0 } s)
                errs.Add($"{where}[{i}]: must be a non-empty string");
            else list.Add(s);
            i++;
        }
        return list.ToArray();
    }
}

/// <summary>Thrown by <see cref="OverrideFile.Parse"/>; <see cref="Errors"/> holds one line per problem, prefixed with its location (<c>rules[3].match: ...</c>).</summary>
public sealed class OverrideFormatException(IReadOnlyList<string> errors)
    : Exception("Invalid Tidy Variants override file:\n  " + string.Join("\n  ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
