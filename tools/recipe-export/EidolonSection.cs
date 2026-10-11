using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Writes `eidolon`: what the site's eidolon fitting sheet shows (docs/recipe-browser/schema.md,
/// "The eidolon's fittings"), all of it from the pack's mod so the page cannot drift from what ships:
/// the five layers with the materials each takes (the item each is fitted as, how many, the texture
/// it draws with) and their Jonas parts, the torso's recipe and the mind, the server's
/// EidolonSettings figures and the stats every metal build has. Textures are paths only: CI's
/// server has no textures, so the site build takes the images from the repository's texture store
/// (textures/, tools/textures.py, docs/recipe-browser/icons.md).
///
/// The rules are the mod's game-independent classes (<c>Eidolon/Core/EidolonFittings.cs</c>,
/// <c>EidolonStats.cs</c>), read by reflection as <see cref="EidolonExport"/> reads the gantry's; the
/// settings are the loaded config's. Nothing is written when the mod is not loaded, the
/// <c>Eidolon</c> switch is off or the classes are not as expected.
/// </summary>
public static class EidolonSection
{
    private const string Core = "SeraphHorizons.Mod.Eidolon.Core.";
    private const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

    /// <summary>The stages file the eidolon's generator writes: each layer's name, and its counts
    /// as the model measured them (checked against the bill, which is what the gantry takes).</summary>
    public static readonly AssetLocation StagesAsset = new(GearChain.Mod, "config/eidolon-stages.json");

    /// <summary>The eidolon's shape, when its entity type does not name one.</summary>
    public static readonly AssetLocation DefaultShape = new(GearChain.Mod, "shapes/entity/eidolon/eidolon.json");

    public const string Entity = "seraphhorizons:eidolon";

    /// <summary>Adds the section to <paramref name="root"/> and every code it names to
    /// <paramref name="referenced"/>, so the item section holds them.</summary>
    public static void Fill(ICoreServerAPI api, JObject root, ISet<string> referenced)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        if (main == null) return;
        var config = main.GetType().GetMethod("ConfigFor", S)?.Invoke(null, [api]);
        if (GearChain.Prop(config, "Eidolon") is false) return;
        try
        {
            var section = Build(api, main.GetType().Assembly, GearChain.Prop(config, "EidolonSettings"));
            if (section == null) return;
            foreach (var code in section.SelectTokens("$..code").Select(t => (string)t!))
                referenced.Add(code);
            if ((string?)section["item"] is { } creature) referenced.Add(creature);
            if ((string?)section["torso"]?["item"] is { } torso) referenced.Add(torso);
            root["eidolon"] = section;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            api.Logger.Warning("[seraphexport] the eidolon's fittings could not be read ({0}); they are not exported", e);
        }
    }

    private static JObject? Build(ICoreServerAPI api, Assembly mod, object? settings)
    {
        var fittings = mod.GetType(Core + "EidolonFittings", throwOnError: true)!;
        var layerType = mod.GetType(Core + "FittingLayer", throwOnError: true)!;
        var buildType = mod.GetType(Core + "EidolonBuild", throwOnError: true)!;
        var statsType = mod.GetType(Core + "EidolonStats", throwOnError: true)!;
        settings ??= Activator.CreateInstance(mod.GetType(Core + "EidolonConfig", throwOnError: true)!)!;

        var layers = ((IEnumerable)fittings.GetField("Layers", S)!.GetValue(null)!).Cast<object>().ToList();
        var materials = fittings.GetMethod("Materials", S, [layerType])!;
        var allows = fittings.GetMethod("Allows", S, [layerType, typeof(string)])!;
        var texturePath = fittings.GetMethod("TexturePath", S, [layerType, typeof(string)])!;
        var textureCode = fittings.GetMethod("TextureCode", S, [layerType])!;
        var bill = fittings.GetMethod("Bill", S, [layerType, typeof(string)])!;
        var jonasParts = fittings.GetMethod("JonasParts", S, [layerType])!;
        var oldBuild = buildType.GetProperty("OldBuild", S)!.GetValue(null)!;
        var indexer = buildType.GetProperty("Item")!;

        var stages = ReadStages(api);
        var outLayers = new JArray();
        foreach (var layer in layers)
        {
            var id = (string)textureCode.Invoke(null, [layer])!;
            var rawParts = ((IEnumerable)jonasParts.Invoke(null, [layer])!).Cast<object>().ToList();
            var parts = Ingredients(api, rawParts, id);
            var outMaterials = new JArray();
            foreach (var material in Offered(api, layer, id, (IEnumerable)materials.Invoke(null, [layer])!, m => (bool)allows.Invoke(null, [layer, m])!))
            {
                // A layer's bill is its material, then its Jonas parts: the material is all but the parts.
                var all = ((IEnumerable)bill.Invoke(null, [layer, material])!).Cast<object>().ToList();
                var items = all.Take(all.Count - rawParts.Count).ToList();
                if (items.Any(i => CollectibleOf(api, Code(i)) == null))
                    continue;
                var path = (string?)texturePath.Invoke(null, [layer, material]);
                outMaterials.Add(new JObject
                {
                    ["id"] = material,
                    ["texture"] = path,
                    ["items"] = new JArray(items.Select(i => Stack(Code(i), Count(i)))),
                });
            }
            var name = stages.TryGetValue(id, out var stage) ? (string?)stage["name"] : null;
            CheckCounts(api, id, stage, outMaterials);
            outLayers.Add(new JObject
            {
                ["id"] = id,
                ["name"] = name ?? char.ToUpperInvariant(id[0]) + id[1..],
                ["default"] = (string?)indexer.GetValue(oldBuild, [layer]),
                ["materials"] = outMaterials,
                ["parts"] = new JArray(parts.Select(p => Stack(p.Code, p.Count))),
            });
        }

        var shapeTextures = ShapeTextures(api);
        var torsoCode = (string)fittings.GetField("TorsoCode", S)!.GetValue(null)!;
        return new JObject
        {
            ["mod"] = GearChain.Mod,
            ["item"] = CollectibleOf(api, EidolonExport.Creature) != null ? EidolonExport.Creature : null,
            ["torso"] = new JObject
            {
                ["item"] = CollectibleOf(api, torsoCode) != null ? torsoCode : null,
                ["recipe"] = new JArray(Ingredients(api, (IEnumerable)fittings.GetField("TorsoRecipe", S)!.GetValue(null)!, "torso").Select(p => Stack(p.Code, p.Count))),
            },
            ["mind"] = new JArray(Ingredients(api, (IEnumerable)fittings.GetField("Mind", S)!.GetValue(null)!, "mind").Select(p => Stack(p.Code, p.Count))),
            ["layers"] = outLayers,
            ["settings"] = Settings(mod, fittings, settings),
            ["builds"] = Builds(fittings, buildType, statsType, settings, oldBuild, indexer, layerType),
            ["shapeTextures"] = new JObject(shapeTextures.Select(kv => new JProperty(kv.Key, kv.Value))),
        };
    }

    /// <summary>The materials a layer offers that the server has items for. Bones list the game's
    /// woods first, then any other wood a registered support beam comes in that the rules allow (a
    /// mod's), by code.</summary>
    private static IEnumerable<string> Offered(ICoreServerAPI api, object layer, string id, IEnumerable listed, System.Func<string, bool> allows)
    {
        var known = listed.Cast<string>().ToList();
        if (id != "bones")
            return known;
        var more = api.World.Blocks
            .Where(b => b?.Code != null && !b.IsMissing && b.Code.Domain == "game" && b.Code.Path.StartsWith("supportbeam-", StringComparison.Ordinal))
            .Select(b => b.Code.Path["supportbeam-".Length..])
            .Where(w => !known.Contains(w) && allows(w))
            .Distinct().OrderBy(w => w, StringComparer.Ordinal);
        return known.Concat(more);
    }

    private static List<(string Code, int Count)> Ingredients(ICoreServerAPI api, IEnumerable list, string what)
    {
        var result = new List<(string, int)>();
        foreach (var i in list)
        {
            if (CollectibleOf(api, Code(i)) == null)
                api.Logger.Warning("[seraphexport] eidolon: {0} takes {1}, which is not registered", what, Code(i));
            else
                result.Add((Code(i), Count(i)));
        }
        return result;
    }

    private static string Code(object ingredient) => (string)GearChain.Prop(ingredient, "Code")!;
    private static int Count(object ingredient) => (int)GearChain.Prop(ingredient, "Count")!;

    private static JObject Stack(string code, int count) => new() { ["code"] = code, ["count"] = count };

    /// <summary>The figures EidolonStats reads, as the server has them; every multiplier keyed by the
    /// material codes the layers use (the settings' own <c>For</c>, so <c>stainlesssteel</c>).</summary>
    private static JObject Settings(Assembly mod, Type fittings, object settings)
    {
        var plateMetals = ((IEnumerable)fittings.GetField("PlateMetals", S)!.GetValue(null)!).Cast<string>().ToList();
        var jointMetals = ((IEnumerable)fittings.GetField("JointMetals", S)!.GetValue(null)!).Cast<string>().ToList();
        var skirtMetals = ((IEnumerable)fittings.GetField("SkirtMetals", S)!.GetValue(null)!).Cast<string>().ToList();
        JObject Factors(string name, List<string> metals)
        {
            var factors = GearChain.Prop(settings, name)!;
            var forMetal = factors.GetType().GetMethod("For", [typeof(string)])!;
            return new JObject(metals.Select(m => new JProperty(m, (double)forMetal.Invoke(factors, [m])!)));
        }
        var armour = GearChain.Prop(settings, "PlateArmour")!;
        var armourFor = armour.GetType().GetMethod("For", [typeof(string)])!;
        var loss = GearChain.Prop(settings, "ArmourLoss")!;
        var stats = mod.GetType(Core + "EidolonStats", throwOnError: true)!;
        return new JObject
        {
            ["allSteelHealth"] = Convert.ToDouble(GearChain.Prop(settings, "AllSteelHealth")),
            ["baseHealth"] = (double)stats.GetMethod("BaseHealth", S)!.Invoke(null, [settings])!,
            ["plateHealth"] = Factors("PlateHealth", plateMetals),
            ["jointHealth"] = Factors("JointHealth", jointMetals),
            ["skirtHealth"] = Factors("SkirtHealth", skirtMetals),
            ["plateDamage"] = Factors("PlateDamage", plateMetals),
            ["plateMoveSpeed"] = Factors("PlateMoveSpeed", plateMetals),
            ["plateArmour"] = new JObject(plateMetals.Select(m => new JProperty(m, Armour(armourFor.Invoke(armour, [m]))))),
            ["armourLoss"] = new JObject
            {
                ["relativeLossWithinTier"] = Convert.ToDouble(GearChain.Prop(loss, "RelativeLossWithinTier")),
                ["relativeLossAboveTier"] = Convert.ToDouble(GearChain.Prop(loss, "RelativeLossAboveTier")),
                ["flatLossWithinTier"] = Convert.ToDouble(GearChain.Prop(loss, "FlatLossWithinTier")),
                ["flatLossAboveTier"] = Convert.ToDouble(GearChain.Prop(loss, "FlatLossAboveTier")),
                ["highDamageTierResistant"] = (bool)GearChain.Prop(loss, "HighDamageTierResistant")!,
            },
            ["walkSpeed"] = Round(Convert.ToDouble(GearChain.Prop(settings, "WalkSpeed"))),
            ["runSpeed"] = Round(Convert.ToDouble(GearChain.Prop(settings, "RunSpeed"))),
            ["defenceDamage"] = Round(Convert.ToDouble(GearChain.Prop(settings, "DefenceDamage"))),
            ["slamDamage"] = Round(Convert.ToDouble(GearChain.Prop(settings, "SlamDamage"))),
        };
    }

    private static JToken Armour(object? a) => a == null
        ? JValue.CreateNull()
        : new JObject
        {
            ["protectionTier"] = (int)GearChain.Prop(a, "ProtectionTier")!,
            ["relativeProtection"] = (double)GearChain.Prop(a, "RelativeProtection")!,
            ["flatDamageReduction"] = (double)GearChain.Prop(a, "FlatDamageReduction")!,
        };

    // The speeds and damage are floats in the game; a float widened to double prints 0.0219999999.
    private static double Round(double v) => Math.Round(v, 6);

    /// <summary>The stats of every build that changes them: each joints, plates and skirt metal
    /// (bones and sash change nothing; they are the old build's here), as EidolonStats.Of has them.</summary>
    private static JArray Builds(Type fittings, Type buildType, Type statsType, object settings, object oldBuild, PropertyInfo indexer, Type layerType)
    {
        var of = buildType.GetMethod("Of", S, [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)])!;
        var statsOf = statsType.GetMethod("Of", S)!;
        string Old(string layer) => (string)indexer.GetValue(oldBuild, [Enum.Parse(layerType, layer)])!;
        var metals = (string name) => ((IEnumerable)fittings.GetField(name, S)!.GetValue(null)!).Cast<string>().ToList();
        var result = new JArray();
        foreach (var joints in metals("JointMetals"))
            foreach (var plates in metals("PlateMetals"))
                foreach (var skirt in metals("SkirtMetals"))
                {
                    var build = of.Invoke(null, [Old("Bones"), joints, plates, Old("Sash"), skirt])!;
                    var stats = statsOf.Invoke(null, [build, settings])!;
                    result.Add(new JObject
                    {
                        ["joints"] = joints,
                        ["plates"] = plates,
                        ["skirt"] = skirt,
                        ["maxHealth"] = Math.Round((double)GearChain.Prop(stats, "MaxHealth")!, 6),
                        ["walkSpeed"] = Round((float)GearChain.Prop(stats, "WalkSpeed")!),
                        ["runSpeed"] = Round((float)GearChain.Prop(stats, "RunSpeed")!),
                        ["defenceDamage"] = Round((float)GearChain.Prop(stats, "DefenceDamage")!),
                        ["slamDamage"] = Round((float)GearChain.Prop(stats, "SlamDamage")!),
                        ["armour"] = Armour(GearChain.Prop(stats, "Armour")),
                    });
                }
        return result;
    }

    /// <summary>The stages file's entries by code, or none when it cannot be read.</summary>
    private static Dictionary<string, JObject> ReadStages(ICoreServerAPI api)
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var asset = api.Assets.TryGet(StagesAsset);
        if (asset == null)
        {
            api.Logger.Warning("[seraphexport] eidolon: {0} is missing", StagesAsset);
            return result;
        }
        foreach (var s in (JArray?)JObject.Parse(asset.ToText())["stages"] ?? [])
            if (s is JObject o && (string?)o["code"] is { } code)
                result[code] = o;
        return result;
    }

    /// <summary>Warns when the bill (what the gantry takes) and the model's measured counts (the
    /// stages file's <c>measure</c>) disagree: one of them is stale.</summary>
    private static void CheckCounts(ICoreServerAPI api, string id, JObject? stage, JArray materials)
    {
        if (stage?["measure"] is not JArray measure || materials.FirstOrDefault() is not JObject first)
            return;
        var billed = first["items"]!.Select(i => (int)i["count"]!).ToList();
        var measured = measure.Select(m => (int)m["count"]!).ToList();
        if (!billed.SequenceEqual(measured))
            api.Logger.Warning("[seraphexport] eidolon: the {0} layer's bill takes [{1}] but {2} measures [{3}]",
                id, string.Join(", ", billed), StagesAsset, string.Join(", ", measured));
    }

    /// <summary>The eidolon shape's own texture codes and paths (the layers' defaults among them):
    /// its entity type's shape, else the shape the mod ships.</summary>
    private static SortedDictionary<string, string> ShapeTextures(ICoreServerAPI api)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var shape = api.World.GetEntityType(new AssetLocation(Entity))?.Client?.Shape?.Base?.Clone()
            .WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json") ?? DefaultShape;
        var asset = api.Assets.TryGet(shape) ?? api.Assets.TryGet(DefaultShape);
        if (asset == null)
        {
            api.Logger.Warning("[seraphexport] eidolon: its shape {0} is missing", shape);
            return result;
        }
        if (JObject.Parse(asset.ToText())["textures"] is JObject textures)
            foreach (var (code, path) in textures)
                if ((string?)path is { } p)
                    result[code] = new AssetLocation(p).ToString();
        return result;
    }

    private static CollectibleObject? CollectibleOf(ICoreServerAPI api, string code)
    {
        var loc = new AssetLocation(code);
        if (api.World.GetItem(loc) is { IsMissing: false, Code: not null } i) return i;
        return api.World.GetBlock(loc) is { IsMissing: false, Code: not null } b ? b : null;
    }
}
