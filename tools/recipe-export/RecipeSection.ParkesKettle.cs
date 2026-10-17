using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The Parkes kettle's charge (seraphhorizons, ParkesKettle/, #927). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string ParkesKettleType = "parkeskettle";

    private static void FillParkesKettle(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (ParkesKettleExport.Read(ctx.Api) is not { } kettle)
            return;
        if (kettle.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the Parkes kettle's {0} is not registered; its charge is exported without it", kettle.FrameCode);
        var mine = kettle.Bullion.Select(b => ParkesKettleRecord(ctx, kettle, b)).ToList();
        records.AddRange(mine);
        AddType(types, ParkesKettleType, "Parkes kettle", mine.Count, "machine", "ParkesKettleSettings", kettle.Mod);
    }

    /// <summary>
    /// One silver-lead bar desilverized in the kettle: the bar and the zinc it takes (a fraction of a bit), consumed,
    /// and the kettle's frame (role <c>station</c>); out, per bar, the rich lead its silver makes (a fraction: the kettle
    /// carries silver short of a whole bar on to its next charge), the rest of its lead desilverized, and the zinc back.
    /// A charge is up to 24 bars, stirred in its hours at the reference speed over the kettle's own fire;
    /// <c>requirements</c> says so in words, <c>extra</c> has the figures.
    /// </summary>
    private static JObject ParkesKettleRecord(Context ctx, ParkesKettleData k, KettleBullion b)
    {
        double rich = b.SilverPerBar / (ParkesKettleExport.UnitsPerBar * k.RichLeadSilver);
        double lead = 1 - rich;
        double zincIn = ParkesKettleExport.UnitsPerBar * k.ZincPerLead / ParkesKettleExport.ZincUnitsPerBit;
        double zincBack = zincIn * k.ZincReturn;
        var ingredients = new JArray(Def(b.Bar.Code.ToString(), "item", 1));
        var ingredientStacks = new JArray(new JArray(Stack(ctx, b.Bar, 1)));
        if (k.Zinc != null)
        {
            ingredients.Add(Def(ParkesKettleExport.ZincCode, "item", zincIn));
            ingredientStacks.Add(new JArray(Stack(ctx, k.Zinc, zincIn)));
        }
        ingredients.Add(Def(k.FrameCode, "block", 1, "station"));
        ingredientStacks.Add(k.Frame != null ? new JArray(Stack(ctx, k.Frame, 1)) : new JArray());
        var outputs = new JArray(Def(ParkesKettleExport.RichLeadCode, "item", rich), Def(ParkesKettleExport.LeadCode, "item", lead));
        var outputStacks = new JArray(Stack(ctx, k.RichLead!, rich), Stack(ctx, k.Lead!, lead));
        if (k.Zinc != null && zincBack > 0)
        {
            outputs.Add(Def(ParkesKettleExport.ZincCode, "item", zincBack));
            outputStacks.Add(Stack(ctx, k.Zinc, zincBack));
        }
        int bars = (int)(k.ChargeUnits / ParkesKettleExport.UnitsPerBar);
        // the shaft's turns a bar at any speed: a charge's hours at 0.6, shared among its bars
        double turnsPerBar = OreTurnsPerHourAtSpeed1 * OreReferenceSpeed * k.Hours / Math.Max(1, bars);
        return new JObject
        {
            ["id"] = $"{ParkesKettleType}|{b.Bar.Code}|0",
            ["type"] = ParkesKettleType,
            ["mod"] = k.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = ingredientStacks,
                ["outputs"] = outputStacks,
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(turnsPerBar),
                ["work"] = new JObject { ["amount"] = 1, ["unit"] = "bars", ["turnsPerUnit"] = Num(turnsPerBar) },
            },
            ["requirements"] = new JArray(
                $"A charge of up to {bars} bars with their zinc, stirred {Num(k.Hours)} in-game hours at shaft speed 0.6 over its own fire ({k.CoalPerCharge} coal a charge) with the row's chimney drawing"),
            ["extra"] = new JObject
            {
                ["ore"] = b.Ore,
                ["share"] = Num(k.Recovery), ["charge"] = k.ChargeUnits, ["hours"] = Num(k.Hours),
                ["silver"] = Num(b.SilverPerBar), ["richLeadSilver"] = Num(k.RichLeadSilver),
                ["zincReturn"] = Num(k.ZincReturn), ["coalPerCharge"] = k.CoalPerCharge,
                ["load"] = Num(k.Load), ["emptyLoad"] = Num(k.EmptyLoad),
                ["carried"] = true,
            },
        };
    }
}
