using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The liquation furnace's sweating (seraphhorizons, LiquationFurnace/, #929). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string LiquationFurnaceType = "liquationfurnace";

    private static void FillLiquationFurnace(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (LiquationFurnaceExport.Read(ctx.Api) is not { } furnace)
            return;
        if (furnace.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the liquation furnace's {0} is not registered; its sweating is exported without it", furnace.FrameCode);
        var mine = furnace.Bars.Select(b => LiquationFurnaceRecord(ctx, furnace, b)).ToList();
        records.AddRange(mine);
        AddType(types, LiquationFurnaceType, "Liquation furnace", mine.Count, "generic", "LiquationFurnaceSettings", furnace.Mod);
    }

    /// <summary>
    /// One tin-lead bar sweated on the furnace's plate: the bar (consumed) and the furnace's frame (role
    /// <c>station</c>); out, its tin whole as an ingot, run into the pot, and its lead left on the plate as lead bits
    /// (a fraction: the furnace carries what falls short of a whole bit over to its next charge).
    /// <c>requirements</c> says what the fire needs, <c>extra</c> has the figures.
    /// </summary>
    private static JObject LiquationFurnaceRecord(Context ctx, LiquationFurnaceData f, LiquationFurnaceBar b)
    {
        double ingots = b.MetalUnits / 100, bits = b.ByProductUnits / LiquationFurnaceExport.UnitsPerBit;
        var outputs = new JArray(Def(b.Ingot.Code.ToString(), "item", ingots));
        var outputStacks = new JArray(Stack(ctx, b.Ingot, ingots));
        if (b.Bit != null && bits > 0)
        {
            outputs.Add(Def(b.Bit.Code.ToString(), "item", bits));
            outputStacks.Add(Stack(ctx, b.Bit, bits));
        }
        double hours = f.PerHour > 0 ? b.MetalUnits / f.PerHour : 0;
        return new JObject
        {
            ["id"] = $"{LiquationFurnaceType}|{b.Bar.Code}|0",
            ["type"] = LiquationFurnaceType,
            ["mod"] = f.Mod,
            ["ingredients"] = new JArray(Def(b.Bar.Code.ToString(), "item", 1), Def(f.FrameCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, b.Bar, 1)),
                    f.Frame != null ? new JArray(Stack(ctx, f.Frame, 1)) : new JArray(),
                },
                ["outputs"] = outputStacks,
            }),
            ["requirements"] = new JArray(
                $"Up to {f.ChargeBars} bars on the plate, the fire lit (coal, coke or charcoal, {Num(f.FuelPerHour)} an hour), the fire door shut, the damper open and the row's chimney drawing: {Num(f.PerHour)} units of tin an hour"),
            ["extra"] = new JObject
            {
                ["ore"] = b.Ore, ["share"] = Num(f.Recovery), ["units"] = Num(b.MetalUnits), ["byProductUnits"] = Num(b.ByProductUnits),
                ["perHour"] = Num(f.PerHour), ["hours"] = Num(hours), ["chargeBars"] = f.ChargeBars, ["fuelPerHour"] = Num(f.FuelPerHour),
                ["carried"] = true,
            },
        };
    }
}
