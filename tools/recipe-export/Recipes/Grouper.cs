using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>A registered recipe and the wildcard values it was generated with.</summary>
public sealed record Variant(RecipeForm Form, SortedDictionary<string, string> Bindings);

/// <summary>A future `recipes` record: a definition with the registered recipes made from it.</summary>
public sealed class Group
{
    /// <summary>Null for recipes registered by code, with no definition asset.</summary>
    public Definition? Definition;

    /// <summary>The reading of the definition the variants matched (see <see cref="Definition.Forms"/>).</summary>
    public RecipeForm? Form;

    /// <summary>For recipes without a definition: the recipe `Name`, if any, and its ordinal among those.</summary>
    public string? CodeSource;
    public int CodeIndex;

    public List<Variant> Variants = new();
}

/// <summary>
/// Puts the engine's expanded recipes back together per definition. The loaders clone a
/// wildcard definition once per combination and register the clones in definition order,
/// all under the definition's `Name` (the asset location unless it sets its own). A
/// registered recipe belongs to the definition that its Name points at and that it matches:
/// code patterns, quantities, attributes, tags, and the grid pattern or voxels. Ties go to
/// the first such definition at or after the last one matched, following registration order.
/// </summary>
public static class Grouper
{
    public static List<Group> Group(IReadOnlyList<RecipeForm> registered, IReadOnlyList<DefinitionFile> files)
    {
        var groups = new List<Group>();
        var byDefinition = new Dictionary<Definition, Group>();
        var all = files.SelectMany(f => f.Definitions).ToList();
        for (int i = 0; i < all.Count; i++)
        {
            all[i].Ordinal = i;
            var g = new Group { Definition = all[i], Form = all[i].Form };
            byDefinition[all[i]] = g;
            groups.Add(g);
        }
        var byFile = files.ToDictionary(f => f.Location.ToString(), f => f.Definitions);
        var byName = all.Where(d => d.Form?.Name != null)
            .GroupBy(d => d.Form!.Name!.ToString())
            .ToDictionary(g => g.Key, g => g.ToList());

        var unmatched = new List<RecipeForm>();
        int pointer = -1;
        foreach (var form in registered)
        {
            IReadOnlyList<Definition> candidates;
            var name = form.Name?.ToString();
            if (name == null) candidates = all;
            else if (byFile.TryGetValue(name, out var inFile)) candidates = inFile;
            else if (byName.TryGetValue(name, out var named)) candidates = named;
            else candidates = Array.Empty<Definition>();

            (Definition Def, RecipeForm Form, SortedDictionary<string, string> Bindings)? chosen = null;
            foreach (var d in candidates)
            {
                var match = d.Forms.Select(f => (Ok: Matches(f, form, out var b), Form: f, Bindings: b)).FirstOrDefault(m => m.Ok);
                if (!match.Ok) continue;
                if (chosen == null || (chosen.Value.Def.Ordinal < pointer && d.Ordinal >= pointer))
                {
                    chosen = (d, match.Form, match.Bindings);
                    if (d.Ordinal >= pointer) break;
                }
            }

            if (chosen is var (def, defForm, bindings))
            {
                pointer = Math.Max(pointer, def.Ordinal);
                var g = byDefinition[def];
                // The first match decides which reading of the definition the record shows.
                if (g.Variants.Count == 0) g.Form = defForm;
                g.Variants.Add(new Variant(form, bindings));
            }
            else unmatched.Add(form);
        }

        groups.AddRange(CodeGroups(unmatched));
        return groups;
    }

    /// <summary>
    /// Recipes with no definition asset (made by code, e.g. hydrateordiedrate's copies of
    /// recipes for each kind of water) are grouped by Name and shape: one record for each
    /// run of recipes with the same Name and the same slots.
    /// </summary>
    private static IEnumerable<Group> CodeGroups(List<RecipeForm> forms)
    {
        var groups = new Dictionary<string, Group>();
        var perName = new Dictionary<string, int>();
        foreach (var form in forms)
        {
            var name = form.Name?.ToString() ?? "code";
            var shape = name + "\n" + string.Join(",", form.Slots.Select(s => s.Key + ":" + s.Accepts.Count)) +
                        "\n" + form.Outputs.Count + "\n" + form.IdentityCode;
            if (!groups.TryGetValue(shape, out var g))
            {
                var index = perName.GetValueOrDefault(name);
                perName[name] = index + 1;
                g = groups[shape] = new Group { CodeSource = form.Name?.ToString(), CodeIndex = index };
            }
            g.Variants.Add(new Variant(form, new SortedDictionary<string, string>(StringComparer.Ordinal)));
        }
        return groups.Values;
    }

    /// <summary>
    /// Whether a registered recipe can have been generated from a definition, and with which
    /// wildcard values. Slots line up by pattern key where there are keys (grid), else by position.
    /// </summary>
    public static bool Matches(RecipeForm definition, RecipeForm registered, out SortedDictionary<string, string> bindings)
    {
        bindings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (definition.Outputs.Count != registered.Outputs.Count) return false;
        if (definition.Block != null && registered.Block != null && !JToken.DeepEquals(definition.Block, registered.Block)) return false;
        if (definition.Voxels != null && registered.Voxels != null && !JToken.DeepEquals(definition.Voxels, registered.Voxels)) return false;
        // Entries that differ only in attributes (which liquid a bucket must hold) are common.
        if (!SameUnlessPlaceholders(definition.Attributes, registered.Attributes)) return false;

        var aligned = Align(definition.Slots, registered.Slots);
        if (aligned == null) return false;

        // Recipes without a Name (cooking, alloys) that carry a code of their own are known by
        // it; code mods add valid stacks to them (hydrateordiedrate's water kinds).
        bool byIdentity = registered.Name == null && definition.IdentityCode != null &&
                          string.Equals(definition.IdentityCode, registered.IdentityCode, StringComparison.OrdinalIgnoreCase);
        if (!byIdentity)
            foreach (var (d, r) in aligned)
            {
                if (r == null) continue;
                // Code mods append alternatives to existing slots (hydrateordiedrate's water kinds).
                if (d.Accepts.Count > r.Accepts.Count) return false;
                if (!SameUnlessPlaceholders(d.Extra["recipeAttributes"], r.Extra["recipeAttributes"])) return false;
                for (int i = 0; i < d.Accepts.Count; i++)
                    if (!StackMatches(d.Accepts[i], r.Accepts[i], bindings, binds: true)) return false;
            }
        // Returned stacks tell apart entries that differ only in what they give back.
        foreach (var (d, r) in aligned)
            if (d.Returned?.Code != null && r?.Returned?.Code != null &&
                !TextMatches(d.Returned.Code.ToString(), r.Returned.Code.ToString(), null, bindings, binds: false))
                return false;
        for (int i = 0; i < definition.Outputs.Count; i++)
            if (!StackMatches(definition.Outputs[i].Stack, registered.Outputs[i].Stack, bindings, binds: false)) return false;
        // Smithing codes carry placeholders too ("chisel-{metal}").
        if (definition.IdentityCode != null && registered.IdentityCode != null &&
            !TextMatches(definition.IdentityCode, registered.IdentityCode, null, bindings, binds: false))
            return false;
        return true;
    }

    /// <summary>
    /// Pairs each definition slot with the registered recipe's slot, or null where the
    /// registered recipe has none (a grid key that no cell uses). Null if they cannot pair.
    /// </summary>
    public static List<(SlotForm Definition, SlotForm? Registered)>? Align(List<SlotForm> definition, List<SlotForm> registered)
    {
        if (definition.Count > 0 && definition.All(s => s.Key != null))
        {
            if (registered.Any(r => r.Key == null || definition.All(d => d.Key != r.Key))) return null;
            return definition.Select(d => (d, registered.FirstOrDefault(r => r.Key == d.Key))).ToList();
        }
        if (definition.Count != registered.Count) return null;
        return definition.Select((d, i) => (d, (SlotForm?)registered[i])).ToList();
    }

    private static bool StackMatches(StackSpec definition, StackSpec registered, SortedDictionary<string, string> bindings, bool binds)
    {
        if (!CodeMatches(definition, registered, bindings, binds)) return false;
        // Liquids have their quantity recomputed from litres when resolved.
        if (definition.Litres == null && registered.Litres == null && definition.Quantity != registered.Quantity) return false;
        return SameUnlessPlaceholders(definition.Attributes, registered.Attributes);
    }

    /// <summary>Attributes without placeholders survive expansion unchanged; with them, they are not compared.</summary>
    private static bool SameUnlessPlaceholders(JToken? definition, JToken? registered)
    {
        if (definition == null || definition.Type == JTokenType.Null) return registered == null || registered.Type == JTokenType.Null || !registered.HasValues;
        return HasPlaceholder(definition) || JToken.DeepEquals(definition, registered);
    }

    private static bool HasPlaceholder(JToken token) =>
        Regex.IsMatch(token.ToString(Newtonsoft.Json.Formatting.None), "\\{[a-z0-9_-]+\\}", RegexOptions.IgnoreCase);

    /// <summary>
    /// A definition code against a registered one. `*` in a named wildcard binds the
    /// ingredient's name; `{x}` binds x. An unnamed `*` stays a wildcard in the registered
    /// recipe too, so equal strings match first. A tags-only slot matches a registered slot
    /// with the same tag condition, or one the engine expanded to a stack carrying those tags.
    /// </summary>
    private static bool CodeMatches(StackSpec definition, StackSpec registered, SortedDictionary<string, string> bindings, bool binds)
    {
        if (definition.Code == null)
        {
            if (definition.Tags.Equals(registered.Tags)) return true;
            var tags = registered.Resolved?.Collectible?.Tags;
            return tags != null && !definition.Tags.IsEmpty && definition.Tags.Matches(tags.Value);
        }
        if (registered.Code == null) return false;
        // A code with tags (hide-raw-* of size-large) keeps its tag condition on every clone.
        if (!definition.Tags.IsEmpty && !definition.Tags.Equals(registered.Tags)) return false;
        if (!Readers.IsPattern(definition.Code))
            return string.Equals(definition.Code.ToString(), registered.Code.ToString(), StringComparison.OrdinalIgnoreCase);
        return TextMatches(definition.Code.ToString(), registered.Code.ToString(), binds ? definition.WildcardName : null, bindings, binds);
    }

    private static bool TextMatches(string d, string r, string? wildcardName, SortedDictionary<string, string> bindings, bool binds)
    {
        if (string.Equals(d, r, StringComparison.OrdinalIgnoreCase)) return true;
        var (regex, groups) = Pattern(d, wildcardName);
        var m = regex.Match(r);
        if (!m.Success) return false;
        for (int i = 0; i < groups.Count; i++)
        {
            var name = groups[i];
            if (name == null) continue;
            var value = m.Groups[i + 1].Value;
            if (bindings.TryGetValue(name, out var had))
            {
                if (had != value) return false;
            }
            else if (binds) bindings[name] = value;
            else return false;   // an output placeholder no ingredient bound
        }
        return true;
    }

    private static readonly Dictionary<string, (Regex Regex, List<string?> Groups)> PatternCache = new();

    private static (Regex, List<string?>) Pattern(string code, string? wildcardName)
    {
        var key = code + "\n" + wildcardName;
        lock (PatternCache)
        {
            if (PatternCache.TryGetValue(key, out var hit)) return hit;
            var sb = new StringBuilder("^");
            var groups = new List<string?>();
            bool firstStar = true;
            if (code.StartsWith("*:")) { sb.Append("[^:]+:"); code = code[2..]; }
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '*')
                {
                    sb.Append("(.*)");
                    groups.Add(firstStar ? wildcardName : null);
                    firstStar = false;
                }
                else if (c == '{' && code.IndexOf('}', i) is var end and > 0)
                {
                    sb.Append("(.+?)");
                    groups.Add(code[(i + 1)..end]);
                    i = end;
                }
                else sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            var result = (new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), groups);
            PatternCache[key] = result;
            return result;
        }
    }
}
