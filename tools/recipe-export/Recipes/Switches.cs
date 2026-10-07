using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// The pack's own mod's switch ownership and item value table (#506, #523; see
/// docs/recipe-browser/schema.md): `recipes[i].switch`, `items[code].switch`, `items[code].value`,
/// `items[code].floorZero` and `items[code].valueSwitches`. The ownership is the mod's
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

    /// <summary>Adds the fields to the built document's `recipes` and `items`.</summary>
    public static void Annotate(ICoreServerAPI api, JObject root)
    {
        var owners = Find(api);
        var table = Table(api);
        int recipes = 0, owned = 0, valued = 0;
        if (owners != null && root["recipes"] is JArray list)
            foreach (var r in list.OfType<JObject>())
                if (owners.ForRecipe((string)r["id"]!) is { } name)
                {
                    r["switch"] = name;
                    recipes++;
                }
        if (root["items"] is not JObject items)
            return;
        var values = table?["values"] as JObject;
        var floorZero = new HashSet<string>((table?["floorZero"] as JArray)?.Values<string>().OfType<string>() ?? [], StringComparer.Ordinal);
        var switches = table?["switches"] as JObject;
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
                if (switches?[code] is JArray { Count: > 0 } depends)
                    item["valueSwitches"] = depends.DeepClone();
            }
        }
        api.Logger.Notification("[seraphexport] switches: {0} recipes and {1} items owned by a switch{2}; item values: {3}{4}",
            recipes, owned, owners == null ? " (no seraphhorizons)" : "", valued, table == null ? " (no table)" : "");
    }
}
