using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The sluice's washing (seraphhorizons, Sluice/; #734, #701). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string SluiceType = "sluice";

    private static void FillSluice(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (SluiceExport.Read(ctx.Api) is not { } sluice)
            return;
        if (sluice.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the sluice's {0} is not registered; its washing is exported without it", SluiceExport.FrameCode);
        var mine = sluice.Feeds.Select(f => SluiceRecord(ctx, sluice, f)).ToList();
        records.AddRange(mine);
        AddType(types, SluiceType, "Sluice", mine.Count, "generic", "OreMachinesSettings", GearChain.Mod);
    }

    /// <summary>
    /// One feed item washed on the sluice (role <c>station</c>, not consumed): one item in, what its riffles
    /// catch out, fractional since the sluice carries what falls short of a whole item over to the next
    /// (crushed and ground ore their concentrate, rich gravel the pan's nuggets and by-products, bog iron
    /// limonite concentrate). The rock goes on as tailings, the same item, to the jig (not an output here).
    /// </summary>
    private static JObject SluiceRecord(Context ctx, SluiceData s, SluiceFeed f)
    {
        string Kind(CollectibleObject c) => c is Block ? "block" : "item";
        var outputs = new JArray(f.Outputs.Select(o => Def(o.Output.Code.ToString(), Kind(o.Output), o.Items)));
        return new JObject
        {
            ["id"] = $"{SluiceType}|{f.Feed.Code}|0",
            ["type"] = SluiceType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(
                Def(f.Feed.Code.ToString(), Kind(f.Feed), 1),
                Def(SluiceExport.FrameCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, f.Feed, 1)),
                    s.Frame != null ? new JArray(Stack(ctx, s.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(f.Outputs.Select(o => Stack(ctx, o.Output, o.Items))),
            }),
            ["requirements"] = new JArray(
                $"Built, with {Num(s.WaterPerItem)} L of water a rock from a pipe: {Num(s.PerHour)} rock an in-game hour, no power"),
            ["extra"] = new JObject
            {
                ["recovery"] = Num(s.Recovery),
                ["perHour"] = Num(s.PerHour),
                ["waterPerItem"] = Num(s.WaterPerItem),
                ["carried"] = true,
                ["tailings"] = f.Feed.Code.ToString(),
            },
        };
    }
}
