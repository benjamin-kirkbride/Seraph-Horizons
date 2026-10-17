using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The rocker (seraphhorizons, Rocker/, #716). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string RockerType = "rocker";

    private static void FillRocker(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (RockerExport.Read(ctx.Api) is not { } rocker)
            return;
        if (rocker.Station == null)
            ctx.Api.Logger.Warning("[seraphexport] the rocker's {0} is not registered; its washes are exported without it", RockerExport.StationCode);
        var mine = rocker.Washes.Select(w => RockerRecord(ctx, rocker, w)).ToList();
        records.AddRange(mine);
        AddType(types, RockerType, "Rocker", mine.Count, "machine", "RockerSettings", rocker.Mod);
    }

    /// <summary>
    /// One feed item washed in the rocker (the station): its outputs an item, fractional (concentrate
    /// carried from item to item; placer gravel's nuggets and stones as expected counts). Worked by hand
    /// (power <c>hand</c>): its turns are the rocks an item takes, a rock a second while the player holds
    /// right-click with a bucket of water, whose litres an item are a requirement.
    /// </summary>
    private static JObject RockerRecord(Context ctx, RockerData r, RockerWash w)
    {
        string kind = w.Feed is Vintagestory.API.Common.Block ? "block" : "item";
        var ingredients = new JArray(Def(w.Feed.Code.ToString(), kind, 1), Def(RockerExport.StationCode, "block", 1, "station"));
        var stacks = new JArray
        {
            new JArray(Stack(ctx, w.Feed, 1)),
            r.Station != null ? new JArray(Stack(ctx, r.Station, 1)) : new JArray(),
        };
        string OutKind(Vintagestory.API.Common.CollectibleObject c) => c is Vintagestory.API.Common.Block ? "block" : "item";
        return new JObject
        {
            ["id"] = $"{RockerType}|{w.Feed.Code}|0",
            ["type"] = RockerType,
            ["mod"] = r.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(w.Outputs.Select(o => Def(o.Output.Code.ToString(), OutKind(o.Output), o.PerItem))),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(w.Outputs.Select(o => Stack(ctx, o.Output, o.PerItem))),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "hand",
                ["turns"] = Num(r.RocksPerItem),
                ["work"] = new JObject { ["amount"] = Num(r.RocksPerItem), ["unit"] = "rocks" },
            },
            ["requirements"] = new JArray(
                $"Hold right-click on it with a bucket of fresh water: {Num(r.LitresPerItem)} L an item, poured as it rocks; the rest carries over to the next item"),
            ["extra"] = new JObject { ["feed"] = w.Kind, ["litres"] = Num(r.LitresPerItem), ["carried"] = true },
        };
    }
}
