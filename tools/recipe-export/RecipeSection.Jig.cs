using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The jig's jigging (seraphhorizons, Jig/; #919). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string JigType = "jig";

    private static void FillJig(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (JigExport.Read(ctx.Api) is not { } jig)
            return;
        if (jig.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the jig's {0} is not registered; its jigging is exported without it", JigExport.FrameCode);
        var mine = jig.Feeds.Select(f => JigRecord(ctx, jig, f)).ToList();
        records.AddRange(mine);
        AddType(types, JigType, "Jig", mine.Count, "generic", "Jig", GearChain.Mod);
    }

    /// <summary>
    /// One feed item jigged (the jig its station, not consumed): one item in, what sinks through its sieve
    /// out, fractional since the jig carries what falls short of a whole item over to the next (crushed and
    /// ground ore their concentrate, rich gravel the pan's nuggets and by-products, bog iron limonite
    /// concentrate), on a fresh item: on the sluice's tailings it catches its share of what the sluice left.
    /// The rock goes on as tailings, the same item, to the shaking table (not an output here). Power
    /// <c>mechanical</c>, <c>turns</c> the axle's turns a rock at any speed.
    /// </summary>
    private static JObject JigRecord(Context ctx, JigData j, SluiceFeed f)
    {
        string Kind(CollectibleObject c) => c is Block ? "block" : "item";
        var outputs = new JArray(f.Outputs.Select(o => Def(o.Output.Code.ToString(), Kind(o.Output), o.Items)));
        return new JObject
        {
            ["id"] = $"{JigType}|{f.Feed.Code}|0",
            ["type"] = JigType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(
                Def(f.Feed.Code.ToString(), Kind(f.Feed), 1),
                Def(JigExport.FrameCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, f.Feed, 1)),
                    j.Frame != null ? new JArray(Stack(ctx, j.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(f.Outputs.Select(o => Stack(ctx, o.Output, o.Items))),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(j.TurnsPerItem),
            },
            ["requirements"] = new JArray(
                $"Built and turned by an axle, with {Num(j.WaterPerItem)} L of water a rock from a pipe: {Num(j.PerHour)} rock an in-game hour at speed 0.6"),
            ["extra"] = new JObject
            {
                ["recovery"] = Num(j.Recovery),
                ["perHour"] = Num(j.PerHour),
                ["waterPerItem"] = Num(j.WaterPerItem),
                ["load"] = Num(j.Load),
                ["carried"] = true,
                ["tailings"] = f.Feed.Code.ToString(),
            },
        };
    }
}
