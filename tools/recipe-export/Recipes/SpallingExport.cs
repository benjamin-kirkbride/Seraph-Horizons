using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One ore item kind spalled: its rock variants (the alternatives), the crushed ore and how
/// many it breaks into, and the blows it takes.</summary>
public sealed record SpallClass(string Pattern, string FirstPart, string Grade, string Ore, List<Item> Inputs, Item Crushed, int Count, int Blows);

public sealed class SpallingData
{
    public required string Mod;
    /// <summary>The hammers that strike, any of the game's.</summary>
    public List<Item> Hammers = new();
    public required int HammerWearPerBlow;
    public List<SpallClass> Classes = new();
}

/// <summary>
/// Spalling (seraphhorizons, Ore/Processing/OreSpalling.cs, #747): raw ore and chunks set down on the
/// ground and broken with a hammer into crushed ore. Read from the loaded items (vanilla's graded ore
/// and crystallised ore with the pack's class, <c>ItemGradedOre</c>, and their crushing, set with ore
/// processing on) and the gameplay's SpallingSettings (BlowsRawOre, BlowsChunk, HammerWearPerBlow),
/// read live from the server's config. Null when the mod is not loaded or OreProcessing is off.
/// </summary>
public static class SpallingExport
{
    public const string HammerTemplate = "game:hammer-copper";
    public const string ItemClass = "ItemGradedOre";

    private static int Int(object? o, string name, int fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToInt32(v) : fallback;

    /// <summary>The form of a grade: raw ore (poor, medium) or chunk (rich, bountiful), as
    /// OreProducts.FormOfGrade.</summary>
    public static bool IsRaw(string grade) => grade is "poor" or "medium";

    public static SpallingData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "OreProcessing") is not true) return null;
        var settings = GearChain.Prop(config, "SpallingSettings");
        int raw = Int(settings, "BlowsRawOre", 6), chunk = Int(settings, "BlowsChunk", 3);

        var data = new SpallingData { Mod = GearChain.Mod, HammerWearPerBlow = Math.Max(0, Int(settings, "HammerWearPerBlow", 1)) };
        data.Hammers = api.World.Items
            .Where(i => i?.Code != null && !i.IsMissing && i.Code.Domain == "game" && i.Code.Path.StartsWith("hammer-", StringComparison.Ordinal))
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal).ToList();

        var classes = new SortedDictionary<string, SpallClass>(StringComparer.Ordinal);
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: "game" } code || item.IsMissing || item.GetType().Name != ItemClass) continue;
            var parts = code.Path.Split('-');
            if (parts.Length != 4 || parts[0] is not ("ore" or "crystalizedore") || parts[1] is not ("poor" or "medium" or "rich" or "bountiful")) continue;
            if (item.CrushingProps?.CrushedStack?.ResolvedItemstack is not { Collectible: Item crushed } stack) continue;
            string pattern = $"game:{parts[0]}-{parts[1]}-{parts[2]}-*";
            if (!classes.TryGetValue(pattern, out var k))
            {
                int blows = IsRaw(parts[1]) ? raw : chunk;
                if (blows < 1) continue;
                classes[pattern] = k = new SpallClass(pattern, parts[0], parts[1], parts[2], new List<Item>(), crushed, stack.StackSize, blows);
            }
            k.Inputs.Add(item);
        }
        foreach (var k in classes.Values)
            k.Inputs.Sort((a, b) => string.CompareOrdinal(a.Code.ToString(), b.Code.ToString()));
        data.Classes = classes.Values.ToList();
        return data.Classes.Count == 0 ? null : data;
    }
}
