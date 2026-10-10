using System.Collections;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One stage of a build: the codes each of its items may be (alternatives, the first the
/// definition's) and how many of each it takes.</summary>
public sealed record EidolonStageItem(List<string> Codes, int Count);

public sealed record EidolonStage(string Name, List<EidolonStageItem> Items);

public sealed class EidolonData
{
    public required string Mod;
    /// <summary>The gantry's woods, as the mod lists them (GantryParts.Woods).</summary>
    public List<string> Woods = new();
    /// <summary>The nine winch stages, in order, on a gantry of each wood (the drum's planks and the
    /// spine's beams are the gantry's wood: a code holds <c>{wood}</c>).</summary>
    public List<EidolonStage> Winch = new();
    /// <summary>The six body stages, in order (BodyBill).</summary>
    public List<EidolonStage> Body = new();
}

/// <summary>
/// The eidolon (seraphhorizons, EidolonGantry/ and Eidolon/): the gantry is placed as a frame (its
/// grid recipe) and then fitted with nine winch stages by right-click, one stage's whole count a
/// click, in order; then the eidolon's body is built on the gantry's spine in six stages, the last
/// (the mind, a temporal gear) waking it. Neither is a registry: what each stage takes is the mod's
/// own rules, read here by reflection from its game-independent classes
/// (<c>EidolonGantry/Core/GantryParts.cs</c>'s <c>GantryParts.CodesFor</c> and <c>Needed</c>,
/// <c>BodyParts.cs</c>'s <c>BodyBill.Stages</c>), so the export cannot drift from them. Null when the
/// mod is not loaded, the <c>Eidolon</c> switch is off or the classes are not as expected.
/// </summary>
public static class EidolonExport
{
    public const string GantryPrefix = "seraphhorizons:eidolongantry-";
    public const string Creature = "seraphhorizons:creature-eidolon";
    private const string Core = "SeraphHorizons.Mod.EidolonGantry.Core.";

    public static string GantryCode(string wood) => $"{GantryPrefix}{wood}-north";

    public static EidolonData? Read(ICoreServerAPI api)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        if (main == null) return null;
        var config = main.GetType().GetMethod("ConfigFor", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "Eidolon") is false) return null;
        try
        {
            return ReadFrom(main.GetType().Assembly);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            api.Logger.Warning("[seraphexport] the eidolon's stages could not be read ({0}); its builds are not exported", e.Message);
            return null;
        }
    }

    private static EidolonData ReadFrom(Assembly mod)
    {
        var parts = mod.GetType(Core + "GantryParts", throwOnError: true)!;
        var requires = mod.GetType(Core + "GantryRequires", throwOnError: true)!;
        var stageType = mod.GetType(Core + "GantryStage", throwOnError: true)!;
        var bill = mod.GetType(Core + "BodyBill", throwOnError: true)!;
        const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

        var data = new EidolonData { Mod = GearChain.Mod };
        data.Woods = ((IEnumerable)parts.GetField("Woods", S)!.GetValue(null)!).Cast<string>().ToList();

        var codesFor = parts.GetMethod("CodesFor", S, [stageType, typeof(string)])!;
        var needed = parts.GetMethod("Needed", S, [stageType])!;
        var name = requires.GetMethod("Name", S, [stageType])!;
        foreach (var stage in Enum.GetValues(stageType))
        {
            // The wood stands in as a placeholder: a code that holds it is of the gantry's own wood.
            var codes = ((IEnumerable)codesFor.Invoke(null, [stage, "{wood}"])!).Cast<string>().ToList();
            data.Winch.Add(new EidolonStage((string)name.Invoke(null, [stage])!,
                [new EidolonStageItem(codes, (int)needed.Invoke(null, [stage])!)]));
        }

        var stages = (IDictionary)ToDictionary(bill.GetField("Stages", S)!.GetValue(null)!);
        var order = ((IEnumerable)bill.GetProperty("Order", S)!.GetValue(null)!).Cast<string>();
        foreach (var stage in order)
        {
            var items = new List<EidolonStageItem>();
            foreach (var ing in (IEnumerable)stages[stage]!)
                items.Add(new EidolonStageItem([(string)GearChain.Prop(ing, "Code")!], (int)GearChain.Prop(ing, "Count")!));
            data.Body.Add(new EidolonStage(stage, items));
        }
        return data;
    }

    // An IReadOnlyDictionary<string, IReadOnlyList<BodyIngredient>> as a plain dictionary.
    private static Dictionary<string, object> ToDictionary(object readOnly)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in (IEnumerable)readOnly)
            d[(string)kv.GetType().GetProperty("Key")!.GetValue(kv)!] = kv.GetType().GetProperty("Value")!.GetValue(kv)!;
        return d;
    }
}
