using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The cylinder borer's process (seraphhorizons, CylinderBorer/; #639). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string CylinderBorerType = "cylinderborer";

    private static void FillCylinderBorer(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (CylinderBorerExport.Read(ctx.Api) is not { } borer)
            return;
        if (borer.Head == null || borer.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the cylinder borer's {0} or {1} is not registered; its process is exported without them",
                borer.HeadCode, borer.FrameCode);
        var mine = borer.Classes.Select(c => CylinderBorerRecord(ctx, borer, c)).ToList();
        records.AddRange(mine);
        AddType(types, CylinderBorerType, "Cylinder borer", mine.Count, "machine", "CylinderBorerSettings", borer.Mod);
    }

    /// <summary>
    /// One metal on the cylinder borer: the cast cylinder (consumed), the bushings, bar and feed (kept:
    /// fitted, never consumed), the cutter head (a tool that wears per cylinder, more as the oil runs
    /// down), the oil (drained from the machine's tank per cylinder) and the machine; the bored cylinder.
    /// </summary>
    private static JObject CylinderBorerRecord(Context ctx, CylinderBorerData b, BoreClass k)
    {
        double points = b.DrainPerCylinder;
        double litres = points / GearChain.PointsPerLitre;
        var firstOil = b.Oils.FirstOrDefault();
        var head = Def(b.HeadCode, "item", 1, "tool");
        head["isTool"] = true;
        head["toolDurabilityCost"] = b.HeadWear;
        var ingredients = new JArray(Def(k.Blank.Code.ToString(), "item", 1));
        foreach (var part in b.Kept)
            ingredients.Add(Def(part.Code.ToString(), "item", 1, "kept"));
        int headAt = ingredients.Count;
        ingredients.Add(head);
        int oilAt = ingredients.Count;
        ingredients.Add(Def(firstOil?.Item.Code.ToString() ?? "game:oilportion-*", "item",
            firstOil != null ? litres / firstOil.LitresPerItem : points, "oil", litres));
        ingredients.Add(Def(b.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray { new JArray(Stack(ctx, k.Blank, 1)) };
        foreach (var part in b.Kept)
            stacks.Add(new JArray(Stack(ctx, part, 1)));
        stacks.Add(b.Head != null ? new JArray(Stack(ctx, b.Head, 1)) : new JArray());
        stacks.Add(new JArray(b.Oils.Select(o => Stack(ctx, o.Item, litres / o.LitresPerItem, litres))));
        stacks.Add(b.Frame != null ? new JArray(Stack(ctx, b.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{CylinderBorerType}|{k.Blank.Code}|0",
            ["type"] = CylinderBorerType,
            ["mod"] = b.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Cylinder.Code.ToString(), "item", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Cylinder, 1)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(b.Travel * k.TurnsPerBlock),
                ["work"] = new JObject { ["amount"] = Num(b.Travel), ["unit"] = "blocks", ["turnsPerUnit"] = Num(k.TurnsPerBlock) },
                ["kept"] = new JArray(Enumerable.Range(1, b.Kept.Count)),
                ["wear"] = new JObject { ["ingredient"] = headAt, ["rule"] = "dividedByOilFill" },
                ["oil"] = new JObject { ["ingredient"] = oilAt, ["points"] = Num(points), ["tank"] = Num(b.Tank) },
            },
            ["extra"] = new JObject { ["rig"] = CylinderBorerExport.RigAsset.ToString(), ["class"] = k.Name, ["metal"] = k.Metal },
        };
    }
}
