using System.Collections;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One stage of a build: the codes each of its items may be (alternatives, the first the
/// definition's) and how many of each it takes. <paramref name="Material"/>: a piece of a layer's
/// material, its codes the piece in each material the layer takes (<see cref="EidolonLayer.Materials"/>'
/// order).</summary>
public sealed record EidolonStageItem(List<string> Codes, int Count, bool Material = false);

/// <summary>A body layer's choice: the materials it takes (the default build's first) as the mod writes
/// them (EidolonFittings' short codes), all of a layer's pieces one of them.</summary>
public sealed record EidolonLayer(string Name, List<string> Materials, string Default);

public sealed record EidolonStage(string Name, List<EidolonStageItem> Items, EidolonLayer? Layer = null);

public sealed class EidolonData
{
    public required string Mod;
    /// <summary>The gantry's woods, as the mod lists them (GantryBuild.Woods).</summary>
    public List<string> Woods = new();
    /// <summary>The nine winch stages, in order, on a gantry of each wood (the drum's planks and the
    /// spine's beams are the gantry's wood: a code holds <c>{wood}</c>).</summary>
    public List<EidolonStage> Winch = new();
    /// <summary>The seven body stages, in order (BodyBill): the torso, the five layers (each with its
    /// choice of material), the mind.</summary>
    public List<EidolonStage> Body = new();
}

/// <summary>
/// The eidolon (seraphhorizons, EidolonGantry/ and Eidolon/): the gantry is placed as a frame (its
/// grid recipe) and then fitted with nine winch stages by right-click, one stage's whole count a
/// click, in order; then the eidolon's body is built on the gantry's spine in seven stages: the crafted
/// torso, five layers each in a material of the builder's choice, and the mind (a temporal gear), after
/// which the winch's crank wakes it. Neither is a registry: what each stage takes is the mod's own: the
/// winch's its build file (<c>config/builds/eidolongantry.json</c>, the shared machine base's, its codes
/// with <c>{wood}</c> for the gantry's wood), the woods and the body's read by reflection from its
/// game-independent classes (<c>EidolonGantry/Core/GantryBuild.cs</c>'s <c>GantryBuild.Woods</c>,
/// <c>BodyParts.cs</c>'s <c>BodyBill</c>, <c>Eidolon/Core/EidolonFittings.cs</c>'s materials, codes, counts
/// and Jonas parts), so the export cannot drift from them. Null when the
/// mod is not loaded, the <c>Eidolon</c> switch is off or the classes are not as expected.
/// </summary>
public static class EidolonExport
{
    public const string GantryPrefix = "seraphhorizons:eidolongantry-";
    public const string Creature = "seraphhorizons:creature-eidolon";
    private const string Core = "SeraphHorizons.Mod.EidolonGantry.Core.";
    private const string EidolonCore = "SeraphHorizons.Mod.Eidolon.Core.";

    public static string GantryCode(string wood) => $"{GantryPrefix}{wood}-north";

    /// <summary>The winch's build file, the shared machine base's.</summary>
    public static readonly AssetLocation WinchBuild = new("seraphhorizons", "config/builds/eidolongantry.json");

    public static EidolonData? Read(ICoreServerAPI api)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        if (main == null) return null;
        var config = main.GetType().GetMethod("ConfigFor", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "Eidolon") is false) return null;
        try
        {
            if (api.Assets.TryGet(WinchBuild) is not { } build)
                throw new FileNotFoundException($"{WinchBuild} is missing");
            return ReadFrom(main.GetType().Assembly, build.ToText());
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            api.Logger.Warning("[seraphexport] the eidolon's stages could not be read ({0}); its builds are not exported", e.Message);
            return null;
        }
    }

    private static EidolonData ReadFrom(Assembly mod, string winchBuild)
    {
        var gantry = mod.GetType(Core + "GantryBuild", throwOnError: true)!;
        var bill = mod.GetType(Core + "BodyBill", throwOnError: true)!;
        const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

        var data = new EidolonData { Mod = GearChain.Mod };
        data.Woods = ((IEnumerable)gantry.GetField("Woods", S)!.GetValue(null)!).Cast<string>().ToList();

        // Each stage's one material: its codes (a code with {wood} is of the gantry's own wood) and count.
        using (var doc = JsonDocument.Parse(winchBuild, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
            foreach (var stage in doc.RootElement.GetProperty("stages").EnumerateArray())
                data.Winch.Add(new EidolonStage(stage.GetProperty("id").GetString()!,
                    stage.GetProperty("materials").EnumerateArray().Select(m => new EidolonStageItem(
                        m.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList(),
                        m.GetProperty("quantity").GetInt32())).ToList()));

        var fittings = mod.GetType(EidolonCore + "EidolonFittings", throwOnError: true)!;
        var counts = mod.GetType(EidolonCore + "FittingCounts", throwOnError: true)!;
        var layerType = mod.GetType(EidolonCore + "FittingLayer", throwOnError: true)!;
        var layerOf = bill.GetMethod("LayerOf", S, [typeof(string)])!;
        var of = bill.GetMethod("Of", S, [typeof(string), typeof(string)])!;
        var defaultMaterial = bill.GetMethod("DefaultMaterial", S, [layerType])!;
        var materials = fittings.GetMethod("Materials", S, [layerType])!;
        var materialCodes = fittings.GetMethod("MaterialCodes", S, [layerType, typeof(string)])!;
        var jonas = fittings.GetMethod("JonasParts", S, [layerType])!;
        var countsOf = counts.GetMethod("Of", S, [layerType])!;
        static List<string> Strings(object list) => ((IEnumerable)list).Cast<string>().ToList();

        var order = ((IEnumerable)bill.GetProperty("Order", S)!.GetValue(null)!).Cast<string>();
        foreach (var stage in order)
        {
            var items = new List<EidolonStageItem>();
            if (layerOf.Invoke(null, [stage]) is not { } layer)
            {
                foreach (var ing in (IEnumerable)of.Invoke(null, [stage, null])!)
                    items.Add(new EidolonStageItem([(string)GearChain.Prop(ing, "Code")!], (int)GearChain.Prop(ing, "Count")!));
                data.Body.Add(new EidolonStage(stage, items));
                continue;
            }
            // a layer: each piece of its material in every material it takes, the default first, then
            // its Jonas parts, which it takes whatever the material
            string fallback = (string)defaultMaterial.Invoke(null, [layer])!;
            var all = Strings(materials.Invoke(null, [layer])!);
            var choice = all.Where(m => m == fallback).Concat(all.Where(m => m != fallback)).ToList();
            var codes = choice.Select(m => Strings(materialCodes.Invoke(null, [layer, m])!)).ToList();
            var n = ((IEnumerable)countsOf.Invoke(null, [layer])!).Cast<int>().ToList();
            for (int part = 0; part < n.Count; part++)
                items.Add(new EidolonStageItem(codes.Select(c => c[part]).ToList(), n[part], Material: true));
            foreach (var ing in (IEnumerable)jonas.Invoke(null, [layer])!)
                items.Add(new EidolonStageItem([(string)GearChain.Prop(ing, "Code")!], (int)GearChain.Prop(ing, "Count")!));
            data.Body.Add(new EidolonStage(stage, items, new EidolonLayer(stage, choice, fallback)));
        }
        return data;
    }
}
