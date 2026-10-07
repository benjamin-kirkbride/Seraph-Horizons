using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// The top-level `variantGroups` section (docs/recipe-browser/schema.md, "Variant groups"): the
/// variants the game shows as one, so the site can collapse the same items. Two sources, in order.
/// First the pack's own mod's Tidy Variants groups, the variants it collapses into one creative tile
/// and one handbook group, read by reflection from the server's resolution
/// (<c>TidyVariantsModSystem.ForSide(Server)</c>, a <c>TidyBridge</c>) and titled by
/// <c>GroupTitles.Of</c>, like <see cref="Switches"/> and <see cref="GearChain"/> reach the mod (the
/// exporter cannot reference it). Then, for the items left, the handbook's own grouping: a
/// collectible's shipped <c>attributes.handbook.groupBy</c> pattern, matched with the game's matcher
/// over the other items left, as the handbook makes one page of them (juices are only in creative
/// inside buckets, so Tidy Variants never sees the juice items, but <c>juiceportion-*</c> groups
/// their pages).
/// </summary>
/// <remarks>
/// The server resolves at the WorldReady run phase, before RunGame, when ExportModSystem builds the
/// export, and before an Atlas scenario can call <see cref="Exporter.Build(ICoreServerAPI, PackInfo)"/>.
/// Without the mod, with Tidy Variants switched off, or when its resolution failed, the bridge is null
/// and the section is not written: absent is valid and every reader copes.
/// </remarks>
public static class VariantGroups
{
    public const string SystemType = "SeraphHorizons.Mod.TidyVariants.TidyVariantsModSystem";
    public const string TitlesType = "SeraphHorizons.Mod.TidyVariants.GroupTitles";
    public const string DeriverType = "SeraphHorizons.Mod.TidyVariants.Core.TitleDeriver";

    /// <summary>Writes `variantGroups` when either source gives a group. Must run inside an
    /// <see cref="EnglishLocale"/> scope (ItemSection.Fill's), so titles are the English ones.</summary>
    public static void Fill(ICoreServerAPI api, JObject root)
    {
        if (Build(api, (JObject)root["items"]!) is { } groups)
            root["variantGroups"] = groups;
    }

    /// <summary>The section, or null when neither source gives a group.</summary>
    public static JObject? Build(ICoreServerAPI api, JObject items)
    {
        var result = new JObject();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var assembly = GearChain.System(api, GearChain.MainSystem)?.GetType().Assembly;
        TidyGroups(api, assembly, items, result, taken);
        HandbookGroups(api, assembly, items, result, taken);
        return result.Count > 0 ? result : null;
    }

    /// <summary>Tidy Variants' groups, from the server's resolution; none without it.</summary>
    static void TidyGroups(ICoreServerAPI api, Assembly? assembly, JObject items, JObject result, HashSet<string> taken)
    {
        var system = assembly?.GetType(SystemType);
        var titles = assembly?.GetType(TitlesType);
        if (system == null || titles == null)
            return;
        var forSide = system.GetMethod("ForSide", BindingFlags.Public | BindingFlags.Static, [typeof(EnumAppSide)]);
        if (forSide?.Invoke(null, [EnumAppSide.Server]) is not { } bridge)
        {
            api.Logger.Notification("[seraphexport] variant groups: none from Tidy Variants (off or did not resolve)");
            return;
        }
        var bridgeType = bridge.GetType();
        var titleOf = titles.GetMethod("Of", BindingFlags.Public | BindingFlags.Static, [bridgeType, typeof(int), typeof(string)]);
        var collectibleOf = bridgeType.GetMethod("CollectibleOf", [typeof(int)]);
        var resolution = GearChain.Prop(bridge, "Resolution");
        var rankOf = resolution?.GetType().GetMethod("RepresentativeRank", [typeof(int)]);
        if (titleOf == null || collectibleOf == null || rankOf == null || GearChain.Prop(resolution, "Groups") is not IEnumerable all)
        {
            api.Logger.Warning("[seraphexport] {0} does not have the members the exporter reads; no Tidy Variants group is exported", SystemType);
            return;
        }

        int index = -1, dropped = 0, members = 0;
        foreach (var group in all)
        {
            index++;
            if (GearChain.Prop(group, "Id") is not string id || GearChain.Prop(group, "Members") is not IEnumerable<int> entries)
                continue;
            // Best representative first: the engine ranks every member of its group, 0 the best. Entries
            // of one collectible's attribute stacks share a code, and the first (best) one stands for it.
            var codes = new List<string>();
            foreach (var entry in entries.OrderBy(e => (int)rankOf.Invoke(resolution, [e])!))
            {
                var c = (CollectibleObject)collectibleOf.Invoke(bridge, [entry])!;
                var code = c.Code?.ToString();
                // Only codes the export has, as the same kind (a block and an item can share a code, and
                // `items` holds one of them), and not already in an earlier group.
                if (code == null || codes.Contains(code) || items[code] is not JObject item
                    || (string?)item["kind"] != Json.Kind(c) || taken.Contains(code))
                    continue;
                codes.Add(code);
            }
            if (codes.Count < 2)
            {
                dropped++;
                continue;
            }
            var title = titleOf.Invoke(null, [bridge, index, null]) as string;
            if (string.IsNullOrWhiteSpace(title))
                title = (string?)items[codes[0]]!["name"];
            if (string.IsNullOrWhiteSpace(title))
                title = id;
            taken.UnionWith(codes);
            members += codes.Count;
            result[id] = new JObject { ["title"] = title, ["members"] = new JArray(codes) };
        }
        api.Logger.Notification("[seraphexport] variant groups: {0} from Tidy Variants ({1} items); {2} groups of fewer than two exported codes dropped",
            result.Count, members, dropped);
    }

    /// <summary>
    /// The handbook's own groups among the items no Tidy Variants group took: each such item's
    /// shipped <c>handbook.groupBy</c> patterns (the server's collectibles carry what the mods ship;
    /// Tidy Variants rewrites them on the client only), each matched with the game's matcher
    /// (<see cref="WildcardUtil"/>, which the handbook's slideshow uses) against the code of every
    /// other item left, blocks and items alike, as the handbook does. A pattern without a domain is
    /// read in the item's; one with a <c>*</c> domain matches paths in any. Patterns with a
    /// placeholder are the game's attribute-grouping collectibles (clutter, shields), expanded per
    /// stack; those collectibles are in Tidy Variants' groups, so they are skipped here. Items are
    /// visited in code order, so the first item that declares a pattern leads its group and the
    /// rest follow in code order. The id is <c>handbook:&lt;domain&gt;:&lt;pattern&gt;</c>; the title
    /// is derived from the members' names as Tidy Variants derives its own (<c>TitleDeriver.Derive</c>,
    /// by reflection), else the leader's name.
    /// </summary>
    static void HandbookGroups(ICoreServerAPI api, Assembly? assembly, JObject items, JObject result, HashSet<string> taken)
    {
        var derive = assembly?.GetType(DeriverType)?.GetMethod("Derive", BindingFlags.Public | BindingFlags.Static,
            [typeof(IEnumerable<string?>), typeof(string), typeof(bool)]);
        // The items left, with their codes, in code order (`items` is a SortedDictionary's output).
        var free = new List<(string Code, AssetLocation Location, CollectibleObject Collectible)>();
        foreach (var (code, token) in items)
        {
            if (taken.Contains(code) || token is not JObject item)
                continue;
            var location = new AssetLocation(code);
            CollectibleObject? c = (string?)item["kind"] == "block" ? api.World.GetBlock(location) : api.World.GetItem(location);
            if (c != null)
                free.Add((code, location, c));
        }
        int groups = 0, members = 0, placeholders = 0;
        foreach (var (code, location, collectible) in free)
        {
            if (taken.Contains(code))
                continue;
            var patterns = collectible.Attributes?["handbook"]?["groupBy"]?.AsArray<string>();
            if (patterns is not { Length: > 0 })
                continue;
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                if (pattern.Contains('{'))
                {
                    placeholders++;
                    continue;
                }
                var id = $"handbook:{location.Domain}:{pattern}";
                if (result.ContainsKey(id))
                    continue;
                var (patternDomain, path) = Pattern(location.Domain, pattern);
                var codes = new List<string> { code };
                foreach (var other in free)
                    if (other.Code != code && !taken.Contains(other.Code) && (patternDomain == "*" || patternDomain == other.Location.Domain)
                        && WildcardUtil.Match(new AssetLocation(other.Location.Domain, path), other.Location))
                        codes.Add(other.Code);
                if (codes.Count < 2)
                    continue;
                var names = codes.Select(c => (string?)items[c]!["name"]).ToList();
                var title = derive?.Invoke(null, [names, names[0], true]) as string;
                if (string.IsNullOrWhiteSpace(title))
                    title = names[0];
                if (string.IsNullOrWhiteSpace(title))
                    title = id;
                taken.UnionWith(codes);
                members += codes.Count;
                groups++;
                result[id] = new JObject { ["title"] = title, ["members"] = new JArray(codes) };
                break; // the item is in this group; its other patterns would only overlap
            }
        }
        api.Logger.Notification("[seraphexport] variant groups: {0} from handbook groupBy ({1} items){2}",
            groups, members, placeholders > 0 ? $"; {placeholders} placeholder patterns skipped" : "");
    }

    /// <summary>
    /// A `groupBy` pattern as the handbook reads it (docs/variant-grouping/handbook.md, "Patterns"):
    /// without a `:` the path is kept as is, in the head's domain; with one the whole string is
    /// lowercased and split at the first `:`. The domain must equal a stack's unless it is `*`.
    /// </summary>
    static (string Domain, string Path) Pattern(string domain, string pattern)
    {
        int colon = pattern.IndexOf(':');
        if (colon < 0)
            return (domain, pattern);
        var lower = pattern.ToLowerInvariant();
        return (lower[..colon], lower[(colon + 1)..]);
    }
}
