using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The shaking table's washing (seraphhorizons, ShakingTable/; #921). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string ShakingTableType = "shakingtable";

    private static void FillShakingTable(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (ShakingTableExport.Read(ctx.Api) is not { } table)
            return;
        if (table.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the shaking table's {0} is not registered; its washing is exported without it", ShakingTableExport.FrameCode);
        var mine = table.Feeds.Select(f => ShakingTableRecord(ctx, table, f)).ToList();
        records.AddRange(mine);
        AddType(types, ShakingTableType, "Shaking table", mine.Count, "machine", "ShakingTable", GearChain.Mod);
    }

    /// <summary>
    /// One crushed or ground ore washed on the shaking table (the machine role <c>station</c>): one item in (fresh, or
    /// the jig's tailings); out what its deck catches, fractional since the table carries what falls short of a whole
    /// item over to the next, and, for a feed with a locked share (not screened, or poor and not ground), one
    /// middlings item: the same item carrying only its locked units, for the ball mill. The rock goes on as tailings,
    /// the same item, to the vanner (not an output here). Mechanical, its turns the axle's a rock at any speed.
    /// </summary>
    private static JObject ShakingTableRecord(Context ctx, ShakingTableData t, ShakingTableFeed f)
    {
        string Kind(CollectibleObject c) => c is Block ? "block" : "item";
        var outputs = new JArray(f.Concentrate.Select(o => Def(o.Output.Code.ToString(), Kind(o.Output), o.Items)));
        var stacks = new JArray(f.Concentrate.Select(o => Stack(ctx, o.Output, o.Items)));
        if (f.Middlings != null)
        {
            outputs.Add(Def(f.Middlings.Code.ToString(), Kind(f.Middlings), 1));
            stacks.Add(Stack(ctx, f.Middlings, 1));
        }
        var extra = new JObject
        {
            ["rig"] = ShakingTableExport.RigAsset.ToString(),
            ["recovery"] = Num(t.Recovery),
            ["perHour"] = Num(t.PerHour),
            ["water"] = Num(t.WaterPerItem),
            ["carried"] = true,
            ["tailings"] = f.Feed.Code.ToString(),
        };
        if (f.Middlings != null)
            extra["middlings"] = f.Middlings.Code.ToString();
        return new JObject
        {
            ["id"] = $"{ShakingTableType}|{f.Feed.Code}|0",
            ["type"] = ShakingTableType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(
                Def(f.Feed.Code.ToString(), Kind(f.Feed), 1),
                Def(ShakingTableExport.FrameCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, f.Feed, 1)),
                    t.Frame != null ? new JArray(Stack(ctx, t.Frame, 1)) : new JArray(),
                },
                ["outputs"] = stacks,
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(t.TurnsPerItem),
            },
            ["requirements"] = new JArray(
                $"Built, on a turning axle (load {Num(t.Load)}, steady), with {Num(t.WaterPerItem)} L of water a rock from a pipe: {Num(t.PerHour)} rock an in-game hour at shaft speed 0.6"),
            ["extra"] = extra,
        };
    }
}
