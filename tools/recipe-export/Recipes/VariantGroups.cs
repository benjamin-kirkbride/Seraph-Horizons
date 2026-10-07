using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// The top-level `variantGroups` section (docs/recipe-browser/schema.md, "Variant groups"): the
/// pack's own mod's Tidy Variants groups, the variants it collapses into one creative tile and one
/// handbook group, so the site can collapse the same items. Read by reflection from the server's
/// resolution (<c>TidyVariantsModSystem.ForSide(Server)</c>, a <c>TidyBridge</c>) and titled by
/// <c>GroupTitles.Of</c>, like <see cref="Switches"/> and <see cref="GearChain"/> reach the mod (the
/// exporter cannot reference it).
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

    /// <summary>Writes `variantGroups` when the server has a resolution. Must run inside an
    /// <see cref="EnglishLocale"/> scope (ItemSection.Fill's), so titles are the English ones.</summary>
    public static void Fill(ICoreServerAPI api, JObject root)
    {
        if (Build(api, (JObject)root["items"]!) is { } groups)
            root["variantGroups"] = groups;
    }

    /// <summary>The section, or null without a resolution.</summary>
    public static JObject? Build(ICoreServerAPI api, JObject items)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        var assembly = main?.GetType().Assembly;
        var system = assembly?.GetType(SystemType);
        var titles = assembly?.GetType(TitlesType);
        if (system == null || titles == null)
            return null;
        var forSide = system.GetMethod("ForSide", BindingFlags.Public | BindingFlags.Static, [typeof(EnumAppSide)]);
        if (forSide?.Invoke(null, [EnumAppSide.Server]) is not { } bridge)
        {
            api.Logger.Notification("[seraphexport] variant groups: none (Tidy Variants is off or did not resolve)");
            return null;
        }
        var bridgeType = bridge.GetType();
        var titleOf = titles.GetMethod("Of", BindingFlags.Public | BindingFlags.Static, [bridgeType, typeof(int), typeof(string)]);
        var collectibleOf = bridgeType.GetMethod("CollectibleOf", [typeof(int)]);
        var resolution = GearChain.Prop(bridge, "Resolution");
        var rankOf = resolution?.GetType().GetMethod("RepresentativeRank", [typeof(int)]);
        if (titleOf == null || collectibleOf == null || rankOf == null || GearChain.Prop(resolution, "Groups") is not IEnumerable all)
        {
            api.Logger.Warning("[seraphexport] {0} does not have the members the exporter reads; no variant group is exported", SystemType);
            return null;
        }

        var result = new JObject();
        var taken = new HashSet<string>(StringComparer.Ordinal);
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
        api.Logger.Notification("[seraphexport] variant groups: {0} ({1} items); {2} groups of fewer than two exported codes dropped",
            result.Count, members, dropped);
        return result.Count > 0 ? result : null;
    }
}
