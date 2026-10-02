using System.Globalization;
using System.Text;
using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.SeraphTweaks.TidyVariants;

/// <summary>Where an engine entry came from in the loaded game.</summary>
/// <param name="Collectible">The collectible the entry's stack is of (its code is the entry's code).</param>
/// <param name="Stack">The entry's own stack: <c>new ItemStack(collectible)</c> for a plain entry, a resolved
/// clone of the <c>CreativeInventoryStacks</c> stack otherwise. Shared: clone it before changing it.</param>
/// <param name="Owner">The collectible whose <c>CreativeInventoryTabs</c> / <c>CreativeInventoryStacks</c> listed
/// it first (usually <see cref="Collectible"/>; a stack list can name another collectible's stack).</param>
/// <param name="ListIndex">Index into <c>Owner.CreativeInventoryStacks</c>, or -1 for a plain entry.</param>
/// <param name="StackIndex">Index into that list's <c>Stacks</c>, or -1 for a plain entry.</param>
/// <param name="AttributeKey">Canonical JSON of the stack's attributes (key-sorted), "" for a plain entry.</param>
public sealed record CreativeSource(
    CollectibleObject Collectible,
    ItemStack Stack,
    CollectibleObject Owner,
    int ListIndex,
    int StackIndex,
    string AttributeKey)
{
    public bool IsPlain => ListIndex < 0;
}

/// <summary>
/// Reads the creative inventory out of the loaded collectibles, in the game's creative order, as engine
/// entries. Mirrors <c>InventoryPlayerCreative.UpdateFromWorld</c> / <c>GatherTabStacks</c> (VintagestoryLib):
/// blocks stable-sorted by <c>BlockMaterial</c>, then items stable-sorted by <c>Tool</c> (null first);
/// per collectible, its plain stack (once per <c>CreativeInventoryTabs</c> tab) then each
/// <c>CreativeInventoryStacks</c> list's stacks (once per tab of that list).
///
/// The game lists a stack once per tab; here a stack is one entry carrying every tab it is in, so each
/// creative tab's slot list is (up to the merging of duplicates below) the subsequence of the entries in
/// that tab. Duplicates (the same collectible with the same attributes listed again, by its own lists or
/// another collectible's) merge into the first entry, adding their tabs.
/// </summary>
public static class CreativeCollector
{
    public sealed class Result
    {
        public required List<CreativeEntry> Entries { get; init; }
        public required List<CreativeSource> Sources { get; init; }
        /// <summary>Unresolvable creative stacks skipped (the game skips them too, with a warning).</summary>
        public int SkippedStacks { get; init; }
    }

    /// <summary>The collectibles in the creative inventory's order (nulls and code-less ones dropped).</summary>
    public static List<CollectibleObject> CreativeOrder(IWorldAccessor world)
    {
        // Exactly the game's sort: LINQ OrderBy is stable, and a nullable key sorts null first.
        var blocks = world.Blocks.OrderBy(b => b?.BlockMaterial).ToList();
        var items = world.Items.OrderBy(i => i?.Tool).ToList();
        var all = new List<CollectibleObject>(blocks.Count + items.Count);
        foreach (var b in blocks) if (b?.Code is not null) all.Add(b);
        foreach (var i in items) if (i?.Code is not null) all.Add(i);
        return all;
    }

    public static Result Collect(IWorldAccessor world)
    {
        var raws = new List<Raw>();
        var byKey = new Dictionary<(EnumItemClass, int, string), int>();
        int skipped = 0;

        foreach (var coll in CreativeOrder(world))
        {
            if (coll.CreativeInventoryTabs is { Length: > 0 } tabs)
            {
                var stack = new ItemStack(coll);
                Add(new Raw(coll, stack, coll, -1, -1, ""), tabs);
            }
            var lists = coll.CreativeInventoryStacks;
            if (lists is null) continue;
            for (int k = 0; k < lists.Length; k++)
            {
                var list = lists[k];
                if (list?.Stacks is null || list.Tabs is null || list.Tabs.Length == 0) continue;
                for (int m = 0; m < list.Stacks.Length; m++)
                {
                    var resolved = list.Stacks[m]?.ResolvedItemstack;
                    if (resolved is null) { skipped++; continue; }
                    var stack = resolved.Clone();
                    stack.ResolveBlockOrItem(world);
                    if (stack.Collectible?.Code is null) { skipped++; continue; }
                    string key = AttributeKey(stack.Attributes);
                    Add(new Raw(stack.Collectible, stack, coll, k, m, key), list.Tabs);
                }
            }
        }

        // Exposed attributes: per collectible, the flattened attributes whose values differ among its stacks.
        var exposed = new Dictionary<CollectibleObject, List<string>>(ReferenceEqualityComparer.Instance);
        var byColl = new Dictionary<CollectibleObject, List<Raw>>(ReferenceEqualityComparer.Instance);
        foreach (var r in raws)
        {
            if (r.AttrKey.Length == 0) continue;
            r.Flat = Flatten(r.Stack.Attributes);
            if (!byColl.TryGetValue(r.Coll, out var l)) byColl[r.Coll] = l = [];
            l.Add(r);
        }
        foreach (var (coll, list) in byColl)
        {
            var order = new List<string>();
            var first = new Dictionary<string, string?>(StringComparer.Ordinal);
            var varies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in list)
                foreach (var (k, _) in r.Flat!)
                    if (!first.ContainsKey(k)) { first[k] = null; order.Add(k); }
            foreach (var k in order)
            {
                string? seen = null; bool init = false;
                foreach (var r in list)
                {
                    string v = Get(r.Flat!, k);
                    if (!init) { seen = v; init = true; }
                    else if (v != seen) { varies.Add(k); break; }
                }
            }
            exposed[coll] = order.Where(varies.Contains).ToList();
        }

        var entries = new List<CreativeEntry>(raws.Count);
        var sources = new List<CreativeSource>(raws.Count);
        foreach (var r in raws)
        {
            AttributeStack? attr = null;
            if (r.AttrKey.Length > 0)
            {
                var keys = exposed[r.Coll];
                var values = new List<KeyValuePair<string, string>>(keys.Count);
                foreach (var k in keys) values.Add(new(k, Get(r.Flat!, k)));
                attr = new AttributeStack(r.AttrKey, values);
            }
            entries.Add(new CreativeEntry(
                r.Coll.Code.ToString(),
                r.Coll is Block ? EntryKind.Block : EntryKind.Item,
                r.Coll.VariantStrict?.ToList(),
                r.Tabs,
                attr,
                propertySources: null,
                shippedGroupBy: ShippedGroupBy(r.Coll)));
            sources.Add(new CreativeSource(r.Coll, r.Stack, r.Owner, r.List, r.StackIdx, r.AttrKey));
        }
        return new Result { Entries = entries, Sources = sources, SkippedStacks = skipped };

        void Add(Raw raw, string[] tabs)
        {
            var key = (raw.Stack.Class, raw.Stack.Id, raw.AttrKey);
            if (!byKey.TryGetValue(key, out int at))
            {
                byKey[key] = raws.Count;
                raws.Add(raw);
                at = raws.Count - 1;
            }
            var t = raws[at].Tabs;
            foreach (var tab in tabs)
                if (tab is not null && !t.Contains(tab)) t.Add(tab);
        }
    }

    /// <summary>The collectible's own <c>attributes.handbook.groupBy</c> (a string or an array of strings).</summary>
    static IReadOnlyList<string>? ShippedGroupBy(CollectibleObject coll)
    {
        var gb = coll.Attributes?["handbook"]?["groupBy"];
        if (gb is null || !gb.Exists) return null;
        if (gb.IsArray()) return gb.AsArray<string>()?.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
        var s = gb.AsString();
        return string.IsNullOrEmpty(s) ? null : [s];
    }

    /// <summary>
    /// A stable identity for a stack's attributes: JSON with the keys of every tree sorted (ordinal), so it
    /// does not depend on insertion order. "" for no attributes.
    /// </summary>
    public static string AttributeKey(ITreeAttribute? attrs)
    {
        if (attrs is null || attrs.Count == 0) return "";
        var sb = new StringBuilder();
        AppendCanonical(sb, attrs);
        return sb.ToString();
    }

    static void AppendCanonical(StringBuilder sb, ITreeAttribute tree)
    {
        sb.Append('{');
        bool firstKey = true;
        foreach (var kv in tree.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!firstKey) sb.Append(',');
            firstKey = false;
            sb.Append('"').Append(kv.Key).Append("\":");
            if (kv.Value is ITreeAttribute sub) AppendCanonical(sb, sub);
            else sb.Append(kv.Value?.ToJsonToken() ?? "null");
        }
        sb.Append('}');
    }

    /// <summary>Leaf attributes as dotted path to string, in insertion order.</summary>
    static List<KeyValuePair<string, string>> Flatten(ITreeAttribute attrs)
    {
        var list = new List<KeyValuePair<string, string>>();
        Walk(attrs, "");
        return list;

        void Walk(ITreeAttribute tree, string prefix)
        {
            foreach (var kv in tree)
            {
                string path = prefix + kv.Key;
                if (kv.Value is ITreeAttribute sub) { Walk(sub, path + "."); continue; }
                list.Add(new(path, Scalar(kv.Value)));
            }
        }
    }

    static string Scalar(IAttribute? a)
    {
        object? v;
        try { v = a?.GetValue(); } catch { v = null; }
        return v switch
        {
            null => a?.ToJsonToken() ?? "",
            string s => s,
            bool b => b ? "true" : "false",
            IFormattable f when v is int or long or float or double or short or byte or decimal => f.ToString(null, CultureInfo.InvariantCulture),
            _ => a!.ToJsonToken(),
        };
    }

    static string Get(List<KeyValuePair<string, string>> flat, string key)
    {
        foreach (var kv in flat) if (kv.Key == key) return kv.Value;
        return "";
    }

    sealed class Raw(CollectibleObject coll, ItemStack stack, CollectibleObject owner, int list, int stackIdx, string attrKey)
    {
        public CollectibleObject Coll = coll;
        public ItemStack Stack = stack;
        public CollectibleObject Owner = owner;
        public int List = list;
        public int StackIdx = stackIdx;
        public string AttrKey = attrKey;
        public List<string> Tabs = [];
        public List<KeyValuePair<string, string>>? Flat;
    }
}
