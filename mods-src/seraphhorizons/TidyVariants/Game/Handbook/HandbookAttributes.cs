using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.TidyVariants.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.TidyVariants;

/// <summary>
/// Writes the layout's <c>attributes.handbook.groupBy</c> (one pattern, replacing what the mods ship) and
/// <c>exclude</c> onto this side's collectibles (docs/variant-grouping/handbook.md). Client only: these attributes
/// only steer the client's handbook, and nothing written here is sent anywhere.
/// </summary>
internal static class HandbookAttributes
{
    internal readonly record struct Result(int GroupBy, int ReplacedShipped, int Exclude, int Failed);

    public static Result Apply(TidyBridge bridge, HandbookPlan plan, ILogger log)
    {
        // (kind, code) -> collectible, from the bridge's own entries.
        var byCode = new Dictionary<(EntryKind, string), CollectibleObject>();
        var entries = bridge.Resolution.Entries;
        for (int i = 0; i < entries.Count; i++)
            byCode.TryAdd((entries[i].Kind, entries[i].Code), bridge.CollectibleOf(i));

        int groupBy = 0, replaced = 0, exclude = 0, failed = 0;
        foreach (var e in plan.Entries)
        {
            if (!byCode.TryGetValue((e.Kind, e.Code), out var coll)) { failed++; continue; }
            try
            {
                // Copy-on-write: a mod may share one attribute tree between collectibles.
                var token = coll.Attributes?.Token;
                if (token is not null and not JObject) { failed++; continue; }
                var root = token is JObject o ? (JObject)o.DeepClone() : new JObject();
                if (root["handbook"] is not JObject hb) root["handbook"] = hb = new JObject();
                if (e.GroupBy is not null)
                {
                    if (hb["groupBy"] is not null) replaced++;
                    hb["groupBy"] = new JArray(e.GroupBy);
                    groupBy++;
                }
                if (e.Exclude) { hb["exclude"] = true; exclude++; }
                coll.Attributes = new JsonObject(root);
            }
            catch (Exception ex)
            {
                failed++;
                log.VerboseDebug("[seraphhorizons] Tidy Variants handbook: could not write attributes on {0}: {1}", e.Code, ex.Message);
            }
        }
        return new Result(groupBy, replaced, exclude, failed);
    }
}
