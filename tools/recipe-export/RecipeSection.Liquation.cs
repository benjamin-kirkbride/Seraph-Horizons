using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Liquation in the clay pan (seraphhorizons, OreProcessing). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string LiquationType = "liquation";

    private static void FillLiquation(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (LiquationExport.Read(ctx.Api) is not { } l)
            return;
        var mine = l.Charges.Select(f => LiquationRecord(ctx, l, f)).ToList();
        records.AddRange(mine);
        AddType(types, LiquationType, "Liquation", mine.Count, "generic", "BlockLiquationPan", l.Mod);
    }

    /// <summary>
    /// A full pan of one ore, kept under the lead point: the fired pan (role <c>kept</c>, reused), its
    /// roasted concentrate, and the firepit (role <c>station</c>; crucibulum's forge works too). The
    /// outputs are the metal poured, in ingots of 100 units (the tin, molten in the pan, poured into a
    /// mold), and the residue knocked out of the pan in the grid as metal bits of 5 units, on average
    /// (whole bits are drawn when the pan is done).
    /// </summary>
    private static JObject LiquationRecord(Context ctx, LiquationData l, LiquationFill f)
    {
        var ingredients = new JArray(
            Def(l.Pan.Code.ToString(), "block", 1, "kept"),
            Def(f.Concentrate.Code.ToString(), "item", f.Items),
            Def(LiquationExport.FirepitCode, "block", 1, "station"));
        var stacks = new JArray
        {
            new JArray(Stack(ctx, l.Pan, 1)),
            new JArray(Stack(ctx, f.Concentrate, f.Items)),
            l.Firepit != null ? new JArray(Stack(ctx, l.Firepit, 1)) : new JArray(),
        };
        var outputs = new JArray(Def(f.Ingot.Code.ToString(), "item", f.Units / 100));
        var outStacks = new JArray(Stack(ctx, f.Ingot, f.Units / 100));
        var residue = new JObject();
        foreach (var (metal, units) in f.Residue)
        {
            residue[metal] = Num(units);
            string code = "game:metalbit-" + metal;
            if (ctx.Api.World.GetItem(new Vintagestory.API.Common.AssetLocation(code)) is not { IsMissing: false } bit) continue;
            outputs.Add(Def(code, "item", units / LiquationExport.UnitsPerItem));
            outStacks.Add(Stack(ctx, bit, units / LiquationExport.UnitsPerItem));
        }
        double seconds = l.SecondsPerIngot * f.Items * LiquationExport.UnitsPerItem / 100;
        return new JObject
        {
            ["id"] = $"{LiquationType}|{f.Concentrate.Code}|0",
            ["type"] = LiquationType,
            ["mod"] = l.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject { ["ingredients"] = stacks, ["outputs"] = outStacks }),
            ["requirements"] = new JArray(
                $"The pan in a firepit or crucibulum's forge, at {Num(l.TinPoint)} °C: {Num(seconds)} s for a full pan",
                $"Over {Num(l.LeadPoint)} °C when it is done, the lead runs with the tin and the charge gives no lead",
                "Done, the pan holds the molten metal, poured into a mold; the residue is knocked out with a hammer in "
                + "the crafting grid, and the pan is kept. The residue's bits are averages (whole bits, the fraction a chance)"),
            ["extra"] = new JObject
            {
                ["ore"] = f.Ore,
                ["units"] = Num(f.Units),
                ["residueUnits"] = residue,
                ["capacityUnits"] = Num(l.CapacityUnits),
                ["tinPoint"] = Num(l.TinPoint),
                ["leadPoint"] = Num(l.LeadPoint),
                ["seconds"] = Num(seconds),
            },
        };
    }
}
