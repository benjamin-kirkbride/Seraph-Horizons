using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The acid parting vessel's parting (seraphhorizons, AcidParting/, #931). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string AcidPartingType = "acidparting";

    private static void FillAcidParting(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (AcidPartingExport.Read(ctx.Api) is not { } vessel)
            return;
        if (vessel.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the acid parting vessel's {0} is not registered; its parting is exported without it", vessel.FrameCode);
        var mine = vessel.Classes.Select(c => AcidPartingRecord(ctx, vessel, c)).ToList();
        records.AddRange(mine);
        AddType(types, AcidPartingType, "Acid parting vessel", mine.Count, "generic", "AcidPartingSettings", vessel.Mod);
    }

    /// <summary>
    /// One bar of dore parted: the bar (consumed), the nitric acid it spends (its share of the charge's acid, the rest
    /// kept as weak acid) and the vessel's frame (role <c>station</c>); out, its main metal whole as metal bits and its
    /// by-product at the vessel's recovery, a fraction, carried over to the next bar. <c>requirements</c> says in words
    /// what a charge needs; <c>extra</c> has the figures.
    /// </summary>
    private static JObject AcidPartingRecord(Context ctx, AcidPartingData v, AcidPartingClass k)
    {
        double acidLitres = v.AcidPerBar * v.AcidSpent;
        var ingredients = new JArray(Def(k.Dore.Code.ToString(), "item", 1));
        var stacks = new JArray { new JArray(Stack(ctx, k.Dore, 1)) };
        if (v.Acid != null)
        {
            ingredients.Add(Def(AcidPartingExport.AcidCode, "item", acidLitres * 100, null, acidLitres));
            stacks.Add(new JArray(Stack(ctx, v.Acid, acidLitres * 100, acidLitres)));
        }
        ingredients.Add(Def(v.FrameCode, "block", 1, "station"));
        stacks.Add(v.Frame != null ? new JArray(Stack(ctx, v.Frame, 1)) : new JArray());
        var outputs = new JArray(Def(k.Gold.Code.ToString(), "item", k.GoldBits));
        var outputStacks = new JArray(Stack(ctx, k.Gold, k.GoldBits));
        if (k.Silver != null && k.SilverBits > 0)
        {
            outputs.Add(Def(k.Silver.Code.ToString(), "item", k.SilverBits));
            outputStacks.Add(Stack(ctx, k.Silver, k.SilverBits));
        }
        double hours = v.PerHour > 0 ? AcidPartingExport.UnitsPerBar / v.PerHour : 0;
        return new JObject
        {
            ["id"] = $"{AcidPartingType}|{k.Dore.Code}|0",
            ["type"] = AcidPartingType,
            ["mod"] = v.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject { ["ingredients"] = stacks, ["outputs"] = outputStacks }),
            ["requirements"] = new JArray(
                $"Up to {v.ChargeBars} bars a charge, with {Num(v.AcidPerBar)} L of nitric acid a bar in the vessel ({Num(100 * v.AcidSpent)} % of it spent) and {Num(AcidPartingExport.UnitsPerBar * v.WaterPerUnit)} L of wash water a bar from a bucket",
                $"Parts {Num(v.PerHour)} units an hour with its fire burning and its row's chimney drawing; a copper plate in the tub throws down {Num(v.CopperUses)} units of silver"),
            ["extra"] = new JObject
            {
                ["ore"] = k.Ore, ["share"] = Num(v.Recovery), ["byProductShare"] = Num(k.SilverShare),
                ["hours"] = Num(hours), ["chargeBars"] = v.ChargeBars, ["acidLitres"] = Num(acidLitres),
                ["waterLitres"] = Num(AcidPartingExport.UnitsPerBar * v.WaterPerUnit), ["copperUses"] = Num(v.CopperUses), ["carried"] = true,
            },
        };
    }
}
