using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The cupellation furnace's parting (seraphhorizons, CupellationFurnace/, #740). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string CupellationFurnaceType = "cupellationfurnace";

    private static void FillCupellationFurnace(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (CupellationFurnaceExport.Read(ctx.Api) is not { } furnace)
            return;
        if (furnace.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the cupellation furnace's {0} is not registered; its parting is exported without it", furnace.FrameCode);
        var mine = furnace.Classes.Select(c => CupellationFurnaceRecord(ctx, furnace, c)).ToList();
        records.AddRange(mine);
        AddType(types, CupellationFurnaceType, "Cupellation furnace", mine.Count, "generic", "CupellationFurnaceSettings", furnace.Mod);
    }

    /// <summary>
    /// One bar cupelled in the furnace: the bar (consumed), a lead ingot for a bar whose metal is not lead, and the
    /// furnace's frame (role <c>station</c>); out, the litharge its lead burns to, and its silver and copper as metal
    /// bits, fractions, since the furnace draws the last item of each at its chance. A charge is up to 200 units on
    /// the bone-ash test, worked while the fire burns, the chimney draws and the blast blows; <c>requirements</c> says
    /// so in words, <c>extra</c> has the figures.
    /// </summary>
    private static JObject CupellationFurnaceRecord(Context ctx, CupellationFurnaceData f, CupellationFurnaceClass k)
    {
        var ingredients = new JArray(Def(k.Bar.Code.ToString(), "item", 1));
        var ingredientStacks = new JArray { new JArray(Stack(ctx, k.Bar, 1)) };
        if (k.WantsLead && f.Lead != null)
        {
            ingredients.Add(Def(CupellationFurnaceExport.LeadCode, "item", 1));
            ingredientStacks.Add(new JArray(Stack(ctx, f.Lead, 1)));
        }
        ingredients.Add(Def(f.FrameCode, "block", 1, "station"));
        ingredientStacks.Add(f.Frame != null ? new JArray(Stack(ctx, f.Frame, 1)) : new JArray());
        var outputs = new JArray(Def(CupellationFurnaceExport.LithargeCode, "item", k.Litharge));
        var outputStacks = new JArray(Stack(ctx, f.Litharge!, k.Litharge));
        if (k.Silver > 0 && f.SilverBit != null)
        {
            outputs.Add(Def(CupellationFurnaceExport.SilverBitCode, "item", k.Silver));
            outputStacks.Add(Stack(ctx, f.SilverBit, k.Silver));
        }
        if (k.Copper > 0 && f.CopperBit != null)
        {
            outputs.Add(Def(CupellationFurnaceExport.CopperBitCode, "item", k.Copper));
            outputStacks.Add(Stack(ctx, f.CopperBit, k.Copper));
        }
        return new JObject
        {
            ["id"] = $"{CupellationFurnaceType}|{k.Bar.Code}|0",
            ["type"] = CupellationFurnaceType,
            ["mod"] = f.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject { ["ingredients"] = ingredientStacks, ["outputs"] = outputStacks }),
            ["requirements"] = new JArray(
                $"A charge of up to {Num(f.ChargeUnits)} units on the bone-ash test, worked at {Num(f.PerHour)} units of lead an in-game hour while the coal fire burns ({Num(f.CoalPerHour)} an hour), the row's chimney draws and an engine air blower blows 10 L/s of air at 1.5 atm into the blast inlet"),
            ["extra"] = new JObject
            {
                ["ore"] = k.Ore, ["richLead"] = k.Ore == null, ["share"] = Num(k.Ore == null ? 1 : f.Recovery),
                ["leadAdded"] = k.WantsLead, ["chargeUnits"] = Num(f.ChargeUnits), ["perHour"] = Num(f.PerHour),
                ["coalPerHour"] = Num(f.CoalPerHour), ["blastLitresPerSecond"] = 10, ["blastPressure"] = Num(1.5),
                ["testCharges"] = 15,
            },
        };
    }
}
