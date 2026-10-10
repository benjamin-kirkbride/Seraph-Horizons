using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Cupellation in the bone-ash cupel (seraphhorizons, OreProcessing). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string CupellationType = "cupellation";

    private static void FillCupellation(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (CupellationExport.Read(ctx.Api) is not { } c)
            return;
        var mine = c.Charges.Select(f => CupellationRecord(ctx, c, f)).ToList();
        records.AddRange(mine);
        AddType(types, CupellationType, "Cupellation", mine.Count, "generic", "BlockCupel", c.Mod);
    }

    /// <summary>
    /// A full cupel of one ore: the fired cupel (used up), its roasted concentrate and, for an ore
    /// whose main metal is not lead, lead bits; the forge (role station). The outputs are what the
    /// cupel with its silver bead breaks into in the grid, on average (whole items are drawn when it
    /// is done): litharge, the lead, and the bead's metal bits, 5 units each.
    /// </summary>
    private static JObject CupellationRecord(Context ctx, CupellationData c, CupelFill f)
    {
        var ingredients = new JArray(Def(c.Cupel.Code.ToString(), "block", 1), Def(f.Concentrate.Code.ToString(), "item", f.Items));
        var stacks = new JArray { new JArray(Stack(ctx, c.Cupel, 1)), new JArray(Stack(ctx, f.Concentrate, f.Items)) };
        if (f.LeadBits > 0 && c.LeadBit != null)
        {
            ingredients.Add(Def(c.LeadBit.Code.ToString(), "item", f.LeadBits));
            stacks.Add(new JArray(Stack(ctx, c.LeadBit, f.LeadBits)));
        }
        ingredients.Add(Def("game:forge", "block", 1, "station"));
        stacks.Add(c.Forge != null ? new JArray(Stack(ctx, c.Forge, 1)) : new JArray());

        var outputs = new JArray(Def(c.Litharge.Code.ToString(), "item", f.LeadUnits / CupellationExport.UnitsPerItem));
        var outStacks = new JArray(Stack(ctx, c.Litharge, f.LeadUnits / CupellationExport.UnitsPerItem));
        var metals = new JObject();
        foreach (var (metal, units) in f.Metals)
        {
            metals[metal] = Num(units);
            string code = "game:metalbit-" + metal;
            if (ctx.Api.World.GetItem(new Vintagestory.API.Common.AssetLocation(code)) is not { IsMissing: false } bit) continue;
            outputs.Add(Def(code, "item", units / CupellationExport.UnitsPerItem));
            outStacks.Add(Stack(ctx, bit, units / CupellationExport.UnitsPerItem));
        }
        double seconds = c.SecondsPerIngot * (f.Items * CupellationExport.UnitsPerItem + f.LeadBits * CupellationExport.UnitsPerItem) / 100;
        return new JObject
        {
            ["id"] = $"{CupellationType}|{f.Concentrate.Code}|0",
            ["type"] = CupellationType,
            ["mod"] = c.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject { ["ingredients"] = stacks, ["outputs"] = outStacks }),
            ["requirements"] = new JArray(
                $"The cupel in crucibulum's forge, held at {Num(c.MeltingPoint)} °C: {Num(seconds)} s with the blast gate open, "
                + "longer half or a quarter open (air 0.85, 0.7), nothing with it shut; a firepit will not do",
                "Done, it is a cupel with silver bead, broken with a hammer in the crafting grid into these; "
                + "the outputs are averages (whole items, the fraction a chance)"),
            ["extra"] = new JObject
            {
                ["ore"] = f.Ore,
                ["leadUnits"] = Num(f.LeadUnits),
                ["metalUnits"] = metals,
                ["capacityUnits"] = Num(c.CapacityUnits),
                ["seconds"] = Num(seconds),
            },
        };
    }
}
