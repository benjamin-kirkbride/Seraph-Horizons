using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>Writes `mods`, `items` and `guides`. docs/recipe-browser/item-data.md describes the data.</summary>
public static class ItemSection
{
    /// <param name="referenced">Codes recipes reference; exported even when hidden from the handbook.</param>
    public static void Fill(ICoreServerAPI api, JObject root, ISet<string> referenced)
    {
        using var english = new EnglishLocale();
        var mods = new ModIndex(api);

        root["mods"] = BuildMods(mods);
        root["items"] = BuildItems(api, mods, referenced);
        Switches.AnnotateItems(api, (JObject)root["items"]!);
        root["guides"] = Guides.Build(api, mods);
    }

    private static JObject BuildMods(ModIndex index)
    {
        var mods = new JObject();
        foreach (var mod in index.Mods)
        {
            var info = mod.Info;
            var o = new JObject { ["name"] = info.Name ?? info.ModID, ["version"] = info.Version ?? "" };
            var authors = info.Authors?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (authors is { Count: > 0 }) o["authors"] = new JArray(authors);
            if (!string.IsNullOrWhiteSpace(info.Website)) o["website"] = info.Website;
            if (!string.IsNullOrWhiteSpace(info.Description)) o["description"] = info.Description;
            o["side"] = Json.Lower(info.Side);
            var domains = index.ExtraDomains(info.ModID).ToList();
            if (domains.Count > 0) o["domains"] = new JArray(domains);
            o["extra"] = new JObject { ["type"] = Json.Lower(info.Type) };
            mods[info.ModID] = o;
        }
        return mods;
    }

    private static JObject BuildItems(ICoreServerAPI api, ModIndex mods, ISet<string> referenced)
    {
        var scope = new SortedDictionary<string, (CollectibleObject C, bool Visible)>(StringComparer.Ordinal);
        // Pages of stacks with attributes (lantern materials, clutter types, ...), by code.
        var variants = new Dictionary<string, List<ItemStack>>();
        int invalid = 0, overriding = 0;
        foreach (var c in api.World.Collectibles)
        {
            if (c?.Code == null || c.IsMissing) continue;
            if (HandbookRule.OverridesRule(c)) overriding++;
            foreach (var page in HandbookRule.PagesFor(c))
            {
                var code = page.Collectible.Code.ToString();
                if (!Json.IsValidCode(code)) { invalid++; continue; }
                if (!scope.TryAdd(code, (page.Collectible, true)) && scope[code].C != page.Collectible) continue;
                if (page.Attributes is { Count: > 0 })
                {
                    if (!variants.TryGetValue(code, out var list)) variants[code] = list = new();
                    if (!list.Any(s => s.Equals(api.World, page))) list.Add(page);
                }
            }
        }

        var unregistered = new List<string>();
        foreach (var code in referenced.OrderBy(c => c, StringComparer.Ordinal))
        {
            var loc = new AssetLocation(code);
            var key = loc.ToString();
            if (scope.ContainsKey(key)) continue;
            CollectibleObject? c = api.World.GetItem(loc);
            if (c?.Code == null || c.IsMissing) c = api.World.GetBlock(loc);
            if (c?.Code == null || c.IsMissing || c.Code.ToString() != key)
            {
                unregistered.Add(code);
                continue;
            }
            if (!Json.IsValidCode(key)) { invalid++; continue; }
            scope[key] = (c, false);
        }
        if (unregistered.Count > 0)
        {
            api.Logger.Warning("[seraphexport] {0} referenced code(s) are not registered and were skipped: {1}",
                unregistered.Count, string.Join(", ", unregistered));
        }

        var sources = new SourceIndex(api);
        var items = new JObject();
        int untranslated = 0;
        foreach (var (code, (c, visible)) in scope)
        {
            var extra = new JObject();
            var name = ItemRecords.Name(c);
            if (variants.TryGetValue(code, out var stacks))
            {
                var named = stacks.Select(s => (Stack: s, Name: ItemRecords.Name(s))).ToList();
                // The bare code of an attribute-driven block often has no name of its own;
                // the handbook only ever shows the variants. Borrow theirs only when they
                // agree: "Abacus" is not a name for every clutter block.
                var distinct = named.Select(n => n.Name).Distinct().ToList();
                if (ItemRecords.IsLangKey(name, c) && distinct.Count == 1 && !ItemRecords.IsLangKey(distinct[0], c))
                    name = distinct[0];
                extra["handbookStacks"] = new JArray(named.Select(n => new JObject
                {
                    ["code"] = code,
                    ["kind"] = Json.Kind(c),
                    ["quantity"] = 1,
                    ["attributes"] = JObject.Parse(n.Stack.Attributes.ToJsonToken()),
                    ["name"] = n.Name,
                }));
            }
            if (ItemRecords.IsLangKey(name, c))
            {
                extra["untranslated"] = true;
                untranslated++;
            }

            var o = new JObject
            {
                ["kind"] = Json.Kind(c),
                ["name"] = name,
                ["mod"] = mods.ModForCollectible(c),
                ["handbookVisible"] = visible,
            };
            var description = ItemRecords.Description(c);
            if (description != null) o["description"] = description;
            o["attributes"] = ItemRecords.Attributes(c);
            var from = sources.For(code);
            if (from != null) o["sources"] = from;
            if (extra.Count > 0) o["extra"] = extra;
            items[code] = o;
        }

        api.Logger.Notification(
            "[seraphexport] items: {0} ({1} handbook-visible, {2} referenced only); {3} codes not valid in the schema skipped; " +
            "{4} collectibles use a class that overrides GetHandBookStacks; {5} names have no English translation",
            items.Count, scope.Count(s => s.Value.Visible), scope.Count(s => !s.Value.Visible), invalid, overriding, untranslated);
        return items;
    }
}
