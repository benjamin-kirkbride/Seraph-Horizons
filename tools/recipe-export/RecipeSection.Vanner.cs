using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The vanner's washing (seraphhorizons, Vanner/; #923). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string VannerType = "vanner";

    private static void FillVanner(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (VannerExport.Read(ctx.Api) is not { } vanner)
            return;
        if (vanner.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the vanner's {0} is not registered; its washing is exported without it", VannerExport.FrameCode);
        var mine = vanner.Feeds.Select(f => VannerRecord(ctx, vanner, f)).ToList();
        records.AddRange(mine);
        AddType(types, VannerType, "Vanner", mine.Count, "machine", "Vanner", GearChain.Mod);
    }

    /// <summary>
    /// One feed item washed on the vanner (the frame role <c>station</c>, not consumed): one item in, what its
    /// belt keeps out, fractional since the vanner carries what falls short of a whole item over to the next.
    /// Mechanical, its turns the shaft's a rock at any speed. The rock goes on as tailings, the same item
    /// carrying the rest (<c>extra.tailings</c>), not an output here. At the end of an ore's whole route
    /// (after the sluice, jig and table) it catches all that is left, which a record per item cannot show.
    /// </summary>
    private static JObject VannerRecord(Context ctx, VannerData v, VannerFeed f)
    {
        string Kind(CollectibleObject c) => c is Block ? "block" : "item";
        return new JObject
        {
            ["id"] = $"{VannerType}|{f.Feed.Code}|0",
            ["type"] = VannerType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(
                Def(f.Feed.Code.ToString(), Kind(f.Feed), 1),
                Def(VannerExport.FrameCode, "block", 1, "station")),
            ["outputs"] = new JArray(f.Outputs.Select(o => Def(o.Output.Code.ToString(), Kind(o.Output), o.Items))),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, f.Feed, 1)),
                    v.Frame != null ? new JArray(Stack(ctx, v.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(f.Outputs.Select(o => Stack(ctx, o.Output, o.Items))),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(v.TurnsPerItem),
            },
            ["extra"] = new JObject
            {
                ["rig"] = "seraphhorizons:config/vanner-rig.json",
                ["recovery"] = Num(v.Recovery),
                ["perHour"] = Num(v.PerHour),
                ["water"] = Num(v.WaterPerItem),
                ["carried"] = true,
                ["tailings"] = f.Feed.Code.ToString(),
            },
        };
    }
}
