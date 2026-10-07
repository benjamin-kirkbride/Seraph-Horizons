using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// The pack's own mod's switch ownership and item value table (#506, #523; see
/// docs/recipe-browser/schema.md): `recipes[i].switch`, `items[code].switch`, `items[code].value`,
/// `items[code].floorZero`, `items[code].valuePerLitre` and `items[code].valueSwitches`. The ownership is the mod's
/// <c>SwitchRegistry.For(api)</c>, called by reflection like <see cref="GearChain"/> reads the
/// mod's settings (the exporter cannot reference the mod); the table is the loaded asset
/// <c>seraphhorizons:config/item-values.json</c>. Without the mod neither exists, and nothing is
/// written.
/// </summary>
public static class Switches
{
    public const string RegistryType = "SeraphHorizons.Mod.SwitchRegistry";
    public static readonly AssetLocation ValuesAsset = new(GearChain.Mod, "config/item-values.json");

    /// <summary>The mod's ownership, as two lookups, or null without the mod.</summary>
    public sealed record Ownership(System.Func<string, string?> ForRecipe, System.Func<string, string?> ForCode);

    public static Ownership? Find(ICoreServerAPI api)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        var type = main?.GetType().Assembly.GetType(RegistryType);
        var build = type?.GetMethod("For", BindingFlags.Public | BindingFlags.Static, [typeof(ICoreAPI)]);
        if (build?.Invoke(null, [api]) is not { } registry)
            return null;
        var forRecipe = registry.GetType().GetMethod("SwitchForRecipe", [typeof(string)]);
        var forCode = registry.GetType().GetMethod("SwitchForCode", [typeof(string)]);
        if (forRecipe == null || forCode == null)
        {
            api.Logger.Warning("[seraphexport] {0} has no SwitchForRecipe or SwitchForCode; no switch is exported", RegistryType);
            return null;
        }
        return new Ownership(id => forRecipe.Invoke(registry, [id]) as string, code => forCode.Invoke(registry, [code]) as string);
    }

    /// <summary>The item value table, or null when the asset is missing or does not parse.</summary>
    public static JObject? Table(ICoreServerAPI api)
    {
        if (api.Assets.TryGet(ValuesAsset) is not { } asset)
            return null;
        try
        {
            return JObject.Parse(asset.ToText());
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] {0} does not parse ({1}); no item value is exported", ValuesAsset, e.Message);
            return null;
        }
    }

    /// <summary>`switch` on each record of `recipes` (RecipeSection.Fill's last step).</summary>
    public static void AnnotateRecipes(ICoreServerAPI api, JArray recipes)
    {
        if (Find(api) is not { } owners)
            return;
        int n = 0;
        foreach (var r in recipes.OfType<JObject>())
            if (owners.ForRecipe((string)r["id"]!) is { } name)
            {
                r["switch"] = name;
                n++;
            }
        api.Logger.Notification("[seraphexport] switches: {0} recipes owned by a switch", n);
    }

    /// <summary>`switch`, `value`, `floorZero`, `valuePerLitre` and `valueSwitches` on each item (ItemSection.Fill's
    /// last step).</summary>
    public static void AnnotateItems(ICoreServerAPI api, JObject items)
    {
        var owners = Find(api);
        var table = Table(api);
        int owned = 0, valued = 0;
        var values = table?["values"] as JObject;
        var floorZero = new HashSet<string>((table?["floorZero"] as JArray)?.Values<string>().OfType<string>() ?? [], StringComparer.Ordinal);
        var switches = table?["switches"] as JObject;
        // The table's perLitre: {code: itemsPerLitre} for liquids, whose values[code] is gears per
        // litre. The value is copied as it is; valuePerLitre says what unit it is in.
        var perLitre = table?["perLitre"] as JObject;
        foreach (var (code, token) in items)
        {
            if (token is not JObject item)
                continue;
            if (owners?.ForCode(code) is { } name)
            {
                item["switch"] = name;
                owned++;
            }
            if (values?[code] is { Type: JTokenType.Float or JTokenType.Integer } value)
            {
                item["value"] = value.DeepClone();
                valued++;
                if (floorZero.Contains(code))
                    item["floorZero"] = true;
                if (perLitre?[code] != null)
                    item["valuePerLitre"] = true;
                if (switches?[code] is JArray { Count: > 0 } depends)
                    item["valueSwitches"] = depends.DeepClone();
            }
        }
        api.Logger.Notification("[seraphexport] switches: {0} items owned by a switch{1}; item values: {2}{3}",
            owned, owners == null ? " (no seraphhorizons)" : "", valued, table == null ? " (no table)" : "");
    }
}
