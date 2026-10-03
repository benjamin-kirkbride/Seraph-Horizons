namespace SeraphHorizons.Mod.TidyVariants.Core;

/// <summary>A non-fatal finding for logs and the Atlas report. <see cref="Kind"/> is a stable slug.</summary>
public sealed record TidyIssue(string Kind, string Message)
{
    public override string ToString() => $"[{Kind}] {Message}";
}

/// <summary>
/// The effective vocabulary for one resolution: material value sets (from the worldproperties lists the
/// game layer read, plus built-in and override extras) and the preferred-value ranks.
/// </summary>
public sealed class TidySettings
{
    readonly Dictionary<MaterialKind, HashSet<string>> materials = [];
    readonly Dictionary<string, Dictionary<string, int>> preferredRank = new(StringComparer.Ordinal);
    readonly Dictionary<string, MaterialKind> propertyKinds = new(StringComparer.Ordinal);
    readonly List<(string Name, HashSet<string> Values)> lists = [];

    /// <param name="worldProperties">worldproperties lists keyed by <c>domain:path</c> under <c>worldproperties/</c>
    /// without <c>.json</c> (<c>game:block/rock</c>; a key without domain means <c>game</c>), each the list of variant codes.</param>
    public TidySettings(IReadOnlyDictionary<string, IReadOnlyList<string>>? worldProperties, OverrideFile? overrides, List<TidyIssue>? issues = null)
    {
        overrides ??= OverrideFile.Empty;
        var props = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (worldProperties is not null)
            foreach (var (k, v) in worldProperties) props[NormalizePropertyKey(k)] = v;

        foreach (MaterialKind kind in new[] { MaterialKind.Rock, MaterialKind.Wood, MaterialKind.Metal, MaterialKind.Color })
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var sources = Vocabulary.MaterialProperties[kind].AsEnumerable();
            var extraValues = Vocabulary.MaterialValues[kind].AsEnumerable();
            if (overrides.Materials.TryGetValue(kind, out var ext))
            {
                sources = sources.Concat(ext.Properties);
                extraValues = extraValues.Concat(ext.Values);
            }
            foreach (var src in sources)
            {
                var key = NormalizePropertyKey(src);
                propertyKinds[key] = kind;
                if (!props.TryGetValue(key, out var values))
                    issues?.Add(new("property-missing", $"worldproperties '{key}' ({kind.ToString().ToLowerInvariant()}) was not provided"));
                else if (values.Count == 0)
                    issues?.Add(new("property-empty", $"worldproperties '{key}' ({kind.ToString().ToLowerInvariant()}) has no variant codes"));
                else set.UnionWith(values.Select(Vocabulary.Normalize));
            }
            set.UnionWith(extraValues);
            materials[kind] = set;
        }

        foreach (var (name, vl) in overrides.Lists)
        {
            var set = new HashSet<string>(vl.Values.Select(Vocabulary.Normalize), StringComparer.Ordinal);
            foreach (var src in vl.Properties)
            {
                var key = NormalizePropertyKey(src);
                if (props.TryGetValue(key, out var values)) set.UnionWith(values.Select(Vocabulary.Normalize));
                else issues?.Add(new("property-missing", $"worldproperties '{key}' (list {name}) was not provided"));
            }
            lists.Add((name, set));
            preferredRank[name] = Rank(vl.Preferred.ToArray());
        }

        foreach (var (key, list) in Vocabulary.Preferred)
            preferredRank[key] = Rank(overrides.Preferred.TryGetValue(key, out var o) ? o : list);
        foreach (var (key, list) in overrides.Preferred)
            preferredRank[key] = Rank(list);
    }

    /// <summary>The override file's named value lists, in file order.</summary>
    public IReadOnlyList<(string Name, HashSet<string> Values)> Lists => lists;

    static Dictionary<string, int> Rank(string[] list)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < list.Length; i++) d.TryAdd(list[i], i);
        return d;
    }

    /// <summary><c>block/rock</c>, <c>worldproperties/block/rock.json</c> and <c>game:block/rock</c> all become <c>game:block/rock</c>.</summary>
    public static string NormalizePropertyKey(string key)
    {
        int colon = key.IndexOf(':');
        string domain = colon < 0 ? "game" : key[..colon];
        string path = colon < 0 ? key : key[(colon + 1)..];
        if (path.StartsWith("worldproperties/", StringComparison.Ordinal)) path = path["worldproperties/".Length..];
        if (path.EndsWith(".json", StringComparison.Ordinal)) path = path[..^5];
        return domain + ":" + path;
    }

    public IReadOnlySet<string> MaterialValues(MaterialKind kind) => materials[kind];

    /// <summary>The material kind a <c>loadFromProperties</c> source maps to, if any.</summary>
    public MaterialKind PropertyKind(string property) =>
        propertyKinds.TryGetValue(NormalizePropertyKey(property), out var k) ? k : MaterialKind.None;

    /// <summary>Index of <paramref name="value"/> (or its <see cref="Vocabulary.Normalize"/>d form) in the preferred
    /// list for <paramref name="key"/>, or int.MaxValue.</summary>
    public int PreferenceRank(string key, string value)
    {
        if (!preferredRank.TryGetValue(key, out var d)) return int.MaxValue;
        if (d.TryGetValue(value, out int r)) return r;
        var n = Vocabulary.Normalize(value);
        return !ReferenceEquals(n, value) && d.TryGetValue(n, out r) ? r : int.MaxValue;
    }
}

/// <summary>Classifies one dimension from its distinct values (first) and its name (second).</summary>
public static class Classifier
{
    /// <param name="name">Variant group code, or <c>attr:&lt;key&gt;</c> for an attribute.</param>
    /// <param name="values">Distinct values the dimension takes within its family (all of the family's entries).</param>
    /// <param name="propertySource">The <c>loadFromProperties</c> hint, when the game layer has one.</param>
    public static DimensionInfo Classify(string name, IReadOnlyCollection<string> values, string? propertySource, TidySettings settings)
    {
        if (propertySource is not null)
        {
            var kind = settings.PropertyKind(propertySource);
            if (kind != MaterialKind.None)
                return new(name, DimensionClass.Material, kind, $"loadFromProperties {TidySettings.NormalizePropertyKey(propertySource)}");
            if (propertySource.EndsWith("horizontalorientation", StringComparison.Ordinal) || propertySource.EndsWith("verticalorientation", StringComparison.Ordinal))
                return new(name, DimensionClass.Orientation, MaterialKind.None, $"loadFromProperties {propertySource}");
        }

        string bare = name.StartsWith("attr:", StringComparison.Ordinal) ? name[5..] : name;
        int dot = bare.LastIndexOf('.'); // FSAttributes.wood
        string lname = (dot >= 0 ? bare[(dot + 1)..] : bare).ToLowerInvariant();
        values = values.Select(Vocabulary.Normalize).Distinct(StringComparer.Ordinal).ToArray();
        int n = values.Count;

        if (n > 0 && values.All(v => Array.IndexOf(Vocabulary.OpenClosedValues, v) >= 0))
            return new(name, DimensionClass.OpenClosedState, MaterialKind.None, "values are open/closed");

        if (n > 0 && values.All(Vocabulary.IsOrientationValue))
            return new(name, DimensionClass.Orientation, MaterialKind.None, "values are orientations");

        // Material: best ratio wins; ties go to rock, wood, metal, color in that order.
        MaterialKind best = MaterialKind.None;
        double bestRatio = 0;
        int bestMatches = 0;
        if (n > 0)
        {
            foreach (MaterialKind kind in new[] { MaterialKind.Rock, MaterialKind.Wood, MaterialKind.Metal, MaterialKind.Color })
            {
                var set = settings.MaterialValues(kind);
                int m = 0;
                foreach (var v in values) if (set.Contains(v)) m++;
                double ratio = (double)m / n;
                int min = kind == MaterialKind.Color ? Vocabulary.ColorMinMatches : Math.Min(n, Vocabulary.MaterialMinMatches);
                if (ratio >= Vocabulary.MaterialRatio && m >= min && ratio > bestRatio)
                {
                    best = kind; bestRatio = ratio; bestMatches = m;
                }
            }
        }
        if (best != MaterialKind.None)
            return new(name, DimensionClass.Material, best, $"{bestMatches}/{n} values are {best.ToString().ToLowerInvariant()}");

        // Mixed materials (tool heads in stone and metal, beams in wood, clay and metal, rails in wood and metal): the
        // same thresholds over rock + wood + metal + generic material words together; the kind with most matches names
        // it (ties rock, wood, metal).
        if (n > 0)
        {
            int m = 0;
            var counts = new int[3];
            var kinds = new[] { MaterialKind.Rock, MaterialKind.Wood, MaterialKind.Metal };
            foreach (var v in values)
            {
                bool hit = false;
                for (int k = 0; k < kinds.Length; k++)
                    if (settings.MaterialValues(kinds[k]).Contains(v)) { counts[k]++; hit = true; }
                if (!hit && Vocabulary.GenericMaterials.TryGetValue(v, out var generic))
                {
                    hit = true;
                    int k = Array.IndexOf(kinds, generic);
                    if (k >= 0) counts[k]++;
                }
                if (hit) m++;
            }
            if ((double)m / n >= Vocabulary.MaterialRatio && m >= Math.Min(n, Vocabulary.MaterialMinMatches))
            {
                int top = 0;
                for (int k = 1; k < kinds.Length; k++) if (counts[k] > counts[top]) top = k;
                return new(name, DimensionClass.Material, kinds[top], $"{m}/{n} values are rock, wood or metal");
            }
        }

        // The override file's named lists (claycolor, ...), same thresholds as materials, first best ratio wins.
        if (n > 0)
        {
            string? bestList = null;
            double listRatio = 0;
            int listMatches = 0;
            foreach (var (listName, set) in settings.Lists)
            {
                int m = values.Count(set.Contains);
                double ratio = (double)m / n;
                if (ratio >= Vocabulary.MaterialRatio && m >= Math.Min(n, Vocabulary.MaterialMinMatches) && ratio > listRatio)
                {
                    bestList = listName; listRatio = ratio; listMatches = m;
                }
            }
            if (bestList is not null)
                return new(name, DimensionClass.Filler, MaterialKind.None, $"{listMatches}/{n} values are in list {bestList}", bestList);
        }

        if (n > 0)
        {
            int m = values.Count(Vocabulary.IsProcessValue);
            if ((double)m / n >= Vocabulary.ProcessRatio && m >= Math.Min(n, 2))
                return new(name, DimensionClass.ProcessState, MaterialKind.None, $"{m}/{n} values are process states");
            int g = values.Count(v => Array.IndexOf(Vocabulary.GradeValues, v) >= 0);
            if (n >= 2 && g >= 2 && (double)g / n >= Vocabulary.GradeRatio)
                return new(name, DimensionClass.GradeSizeQuality, MaterialKind.None, $"{g}/{n} values are grades/sizes/levels");
        }

        if (Array.IndexOf(Vocabulary.OrientationNames, lname) >= 0)
            return new(name, DimensionClass.Orientation, MaterialKind.None, $"name '{bare}'");
        if (Array.IndexOf(Vocabulary.LooseOrientationNames, lname) >= 0 && n > 0
            && values.All(v => Vocabulary.IsOrientationValue(v) || Array.IndexOf(Vocabulary.LooseOrientationValues, v) >= 0))
            return new(name, DimensionClass.Orientation, MaterialKind.None, $"name '{bare}' with orientation values");
        if (Array.IndexOf(Vocabulary.ProcessNames, lname) >= 0)
            return new(name, DimensionClass.ProcessState, MaterialKind.None, $"name '{bare}'");
        if (Array.IndexOf(Vocabulary.LooseProcessNames, lname) >= 0 && values.Any(Vocabulary.IsProcessValue))
            return new(name, DimensionClass.ProcessState, MaterialKind.None, $"name '{bare}' with a process value");
        if (Array.IndexOf(Vocabulary.GradeNames, lname) >= 0)
            return new(name, DimensionClass.GradeSizeQuality, MaterialKind.None, $"name '{bare}'");

        if (n > 0 && values.All(Vocabulary.IsNumber))
            return new(name, DimensionClass.Filler, MaterialKind.None, "values are numbers");

        // One collectible's creative stacks (bookshelf shapes, bucket contents, fruit tree types): the handbook can
        // only group whole collectibles, so their attributes don't split tiles unless an override says so.
        if (name.StartsWith("attr:", StringComparison.Ordinal))
            return new(name, DimensionClass.Filler, MaterialKind.None, "attribute of one collectible's stacks");

        // Numbered variants of one thing (collapsed1..4, ruined-barred1..3, mk1..3) share a group.
        if (values.Select(Vocabulary.NumberStem).Distinct(StringComparer.Ordinal).Count() < n)
            return new(name, DimensionClass.Meaningful, MaterialKind.None, "default; numbered values share a group", ByStem: true);

        return new(name, DimensionClass.Meaningful, MaterialKind.None, "default");
    }

    /// <summary>The class an override assigns, as a <see cref="DimensionInfo"/>.</summary>
    public static DimensionInfo FromOverride(string name, DimensionOverride o, int ruleIndex) =>
        new(name, o.Class, o.Material, $"override rules[{ruleIndex}]", o.List);
}
