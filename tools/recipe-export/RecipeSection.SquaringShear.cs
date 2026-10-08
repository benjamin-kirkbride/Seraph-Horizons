using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The squaring shear's process (seraphhorizons, SquaringShear/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string SquaringShearType = "squaringshear";

    private static void FillSquaringShear(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (SquaringShearExport.Read(ctx.Api) is not { } shear)
            return;
        if (shear.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the squaring shear's {0} is not registered; its process is exported without it", shear.FrameCode);
        var mine = shear.Classes.Select(c => SquaringShearRecord(ctx, shear, c)).ToList();
        records.AddRange(mine);
        AddType(types, SquaringShearType, "Squaring shear", mine.Count, "machine", "SquaringShearSettings", shear.Mod);
    }

    /// <summary>
    /// One metal on the squaring shear: the plate (consumed), the blades and the gauge (kept: fitted,
    /// never consumed) and the machine; two half plates of the metal. Worked by hand (power
    /// <c>hand</c>): its turns are the treadle's strokes, a stroke a second while the player holds
    /// right-click (<c>work</c> in strokes). No tool wears and there is no oil.
    /// </summary>
    private static JObject SquaringShearRecord(Context ctx, SquaringShearData s, CutClass k)
    {
        var ingredients = new JArray(Def(k.Plate.Code.ToString(), "item", 1));
        foreach (var (template, _) in s.Kept)
            ingredients.Add(Def(template, "item", 1, "kept"));
        ingredients.Add(Def(s.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray { new JArray(Stack(ctx, k.Plate, 1)) };
        foreach (var (_, items) in s.Kept)
            stacks.Add(new JArray(items.Select(i => Stack(ctx, i, 1))));
        stacks.Add(s.Frame != null ? new JArray(Stack(ctx, s.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{SquaringShearType}|{k.Plate.Code}|0",
            ["type"] = SquaringShearType,
            ["mod"] = s.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.HalfPlate.Code.ToString(), "item", s.HalfPlatesPerPlate)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.HalfPlate, s.HalfPlatesPerPlate)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "hand",
                ["turns"] = Num(k.StrokesPerPlate),
                ["work"] = new JObject { ["amount"] = Num(k.StrokesPerPlate), ["unit"] = "strokes" },
                ["kept"] = new JArray(Enumerable.Range(1, s.Kept.Count)),
            },
            ["extra"] = new JObject { ["rig"] = SquaringShearExport.RigAsset.ToString(), ["class"] = k.Name, ["metal"] = k.Metal },
        };
    }
}
