using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The slitting mill's process (seraphhorizons, SlittingMill/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string SlittingMillType = "slittingmill";

    private static void FillSlittingMill(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (SlittingMillExport.Read(ctx.Api) is not { } mill)
            return;
        if (mill.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the slitting mill's {0} is not registered; its process is exported without it", mill.FrameCode);
        var mine = mill.Classes.Select(c => SlittingMillRecord(ctx, mill, c)).ToList();
        records.AddRange(mine);
        AddType(types, SlittingMillType, "Slitting mill", mine.Count, "machine", "SlittingMillSettings", mill.Mod);
    }

    /// <summary>
    /// One metal on the slitting mill: the plate (consumed), what the slitter rolls, the pinions and great wheel, the
    /// drive, the shear and the push bar are built of (kept: fitted, never consumed, summed by item), the knife (a
    /// cleaver: a tool that wears a fixed amount a plate, by the plate's metal), the oil (drained from the machine's
    /// tank per plate) and the machine; the nails and strips of the metal, NailsPerPlate of them.
    /// </summary>
    private static JObject SlittingMillRecord(Context ctx, SlittingMillData m, SlitClass k)
    {
        double points = m.DrainPerPlate;
        double litres = points / GearChain.PointsPerLitre;
        var firstOil = m.Oils.FirstOrDefault();
        var knife = Def(m.Knives[0].Code.ToString(), "item", 1, "tool");
        knife["isTool"] = true;
        knife["toolDurabilityCost"] = k.KnifeWear;
        var ingredients = new JArray(Def(k.Plate.Code.ToString(), "item", 1));
        foreach (var (kind, template, _, quantity) in m.Kept)
            ingredients.Add(Def(template, kind, quantity, "kept"));
        int knifeAt = ingredients.Count;
        ingredients.Add(knife);
        int oilAt = ingredients.Count;
        ingredients.Add(Def(firstOil?.Item.Code.ToString() ?? "game:oilportion-*", "item",
            firstOil != null ? litres / firstOil.LitresPerItem : points, "oil", litres));
        ingredients.Add(Def(m.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray { new JArray(Stack(ctx, k.Plate, 1)) };
        foreach (var (_, _, alternatives, quantity) in m.Kept)
            stacks.Add(new JArray(alternatives.Select(a => Stack(ctx, a, quantity))));
        stacks.Add(new JArray(m.Knives.Select(i => Stack(ctx, i, 1))));
        stacks.Add(new JArray(m.Oils.Select(o => Stack(ctx, o.Item, litres / o.LitresPerItem, litres))));
        stacks.Add(m.Frame != null ? new JArray(Stack(ctx, m.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{SlittingMillType}|{k.Plate.Code}|0",
            ["type"] = SlittingMillType,
            ["mod"] = m.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Output.Code.ToString(), "item", m.NailsPerPlate)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Output, m.NailsPerPlate)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(m.Strokes * m.TurnsPerStroke),
                ["work"] = new JObject { ["amount"] = Num(m.Strokes), ["unit"] = "strokes", ["turnsPerUnit"] = Num(m.TurnsPerStroke) },
                ["kept"] = new JArray(Enumerable.Range(1, m.Kept.Count)),
                ["wear"] = new JObject { ["ingredient"] = knifeAt, ["rule"] = "fixed" },
                ["oil"] = new JObject { ["ingredient"] = oilAt, ["points"] = Num(points), ["tank"] = Num(m.Tank) },
            },
            ["extra"] = new JObject { ["rig"] = SlittingMillExport.RigAsset.ToString(), ["metal"] = k.Metal },
        };
    }
}
