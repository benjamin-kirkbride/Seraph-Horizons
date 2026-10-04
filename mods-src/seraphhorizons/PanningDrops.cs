using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Panning in the pack gives no wool, stitching awls, uranium nuggets or buttons and clasps. None of
/// them is the game's: Wool (<c>wool</c>), Tailor's Delight (<c>tailorsdelight</c>) and Expanded
/// Matter (<c>em</c>) each add theirs to the pan's <c>attributes.panningDrops</c> by a JSON patch on
/// <c>game:blocktypes/wood/pan.json</c>. <see cref="TrimPanAsset"/> takes every entry
/// <see cref="PanningDropRules"/> names out of every panned material's list, on the server in
/// AssetsLoaded: after the game's patch loader (0.05) has applied every mod's patches, whatever their
/// order, and before the block types are read from the assets (0.2). So the pan block, the
/// Panning Machine (which reads the pan's table) and the handbook (clients get the block's
/// attributes from the server) all see the trimmed table.
///
/// Tailor's Delight's handbook text for the buttons says they are found while panning; that passage
/// is taken out in every language it ships (<see cref="LangEdits"/>).
/// </summary>
public static class PanningDrops
{
    public static readonly AssetLocation PanAsset = new("game", "blocktypes/wood/pan.json");

    public const string TailorsDelightId = "tailorsdelight";

    /// <summary>Tailor's Delight's buttons handbook line, without "while panning", in each language
    /// it ships.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "tailorsdelight:handbook-item-buttons", "in loot, while panning or sometimes", "in loot or sometimes"),
        new("de", "tailorsdelight:handbook-item-buttons", "in Ruinen oder beim Schürfen gefunden, oder manchmal",
            "in Ruinen gefunden oder manchmal"),
        new("fr", "tailorsdelight:handbook-item-buttons", "comme butin, lors de l'extraction ou parfois", "comme butin ou parfois"),
        new("pl", "tailorsdelight:handbook-item-buttons", "w łupach, podczas płukania lub czasami", "w łupach lub czasami"),
        new("ru", "tailorsdelight:handbook-item-buttons", "в добыче, при мытье или иногда", "в добыче или иногда"),
    ];

    /// <summary>Rewords Tailor's Delight's text, when it is installed (both sides).</summary>
    public static void RewriteText(ICoreAPI api)
    {
        if (api.ModLoader.IsModEnabled(TailorsDelightId))
            LangText.Apply(LangEdits, TailorsDelightId, api.Logger);
    }

    /// <summary>Takes the removed drops out of the pan's asset. Runs on the server in AssetsLoaded,
    /// after the patch loader and before the block types are read. If the asset is missing or has no
    /// panning table, logs a warning and changes nothing.</summary>
    public static void TrimPanAsset(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PanAsset);
        JObject? json = null;
        try
        {
            json = asset == null ? null : JObject.Parse(asset.ToText());
        }
        catch (Exception e)
        {
            api.Logger.Warning($"[seraphhorizons] Panning drops: could not read {PanAsset}: {e.Message}");
        }
        var removed = json == null ? null : TrimFrom(json);
        if (removed == null)
        {
            api.Logger.Warning($"[seraphhorizons] Panning drops: {PanAsset} is missing or has no panningDrops; "
                               + "the game changed, so panning is left as it is");
            return;
        }
        if (removed.Count == 0)
        {
            api.Logger.Notification("[seraphhorizons] Panning drops: nothing to take out");
            return;
        }
        asset!.Data = Encoding.UTF8.GetBytes(json!.ToString());
        api.Logger.Notification($"[seraphhorizons] Panning drops: took out {removed.Count} entries: "
                                + string.Join(", ", removed.Distinct()));
    }

    /// <summary>Removes every removed drop from every panned material's list, in
    /// <c>attributes.panningDrops</c> and any <c>attributesByType</c> entry's, and returns their codes;
    /// null when there is no panning table at all.</summary>
    public static List<string>? TrimFrom(JObject json)
    {
        var tables = new List<JObject>();
        if (json.GetValue("attributes", StringComparison.OrdinalIgnoreCase) is JObject attributes
            && attributes.GetValue("panningDrops", StringComparison.OrdinalIgnoreCase) is JObject table)
            tables.Add(table);
        if (json.GetValue("attributesByType", StringComparison.OrdinalIgnoreCase) is JObject byType)
            foreach (var entry in byType.Properties())
                if (entry.Value is JObject typeAttributes
                    && typeAttributes.GetValue("panningDrops", StringComparison.OrdinalIgnoreCase) is JObject typeTable)
                    tables.Add(typeTable);
        if (tables.Count == 0)
            return null;

        var removed = new List<string>();
        foreach (var drops in tables.SelectMany(t => t.Properties()).Select(p => p.Value).OfType<JArray>())
        {
            var gone = drops.OfType<JObject>()
                .Where(d => d.GetValue("code", StringComparison.OrdinalIgnoreCase) is { Type: JTokenType.String } code
                            && PanningDropRules.IsRemoved((string)code!))
                .ToList();
            foreach (var drop in gone)
            {
                removed.Add((string)drop.GetValue("code", StringComparison.OrdinalIgnoreCase)!);
                drop.Remove();
            }
        }
        return removed;
    }
}
