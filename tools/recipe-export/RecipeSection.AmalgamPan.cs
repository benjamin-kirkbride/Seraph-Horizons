using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The amalgam pan's process (seraphhorizons, AmalgamPan/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string AmalgamPanType = "amalgampan";

    private static void FillAmalgamPan(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (AmalgamPanExport.Read(ctx.Api) is not { } pan)
            return;
        var mine = pan.Charges.Select(c => AmalgamPanRecord(ctx, pan, c)).ToList();
        records.AddRange(mine);
        AddType(types, AmalgamPanType, "Amalgam pan", mine.Count, "machine", "AmalgamPanSettings", pan.Mod);
    }

    /// <summary>
    /// A full charge of one kind of crushed gold or silver quartz in the amalgam pan: the quartz, the
    /// mercury its amalgam takes and the charge's water (consumed), the muller and the shoes (kept) and
    /// the pan (role <c>station</c>); out, the amalgam (on average: the fraction of an item carries to the
    /// next charge) and the same quartz back, amalgamated, for the rocker. Worked by hand (power
    /// <c>hand</c>; an axle below may turn it instead), its turns the muller's.
    /// </summary>
    private static JObject AmalgamPanRecord(Context ctx, AmalgamPanData p, AmalgamCharge c)
    {
        int n = p.ChargeItems;
        double mercury = c.AmalgamItems * p.AmalgamMercury;
        string input = c.Input.Code.ToString();
        var ingredients = new JArray(
            Def(input, "item", n),
            Def(p.Mercury.Code.ToString(), "item", mercury, null, mercury / 100));
        var stacks = new JArray
        {
            new JArray(Stack(ctx, c.Input, n)),
            new JArray(Stack(ctx, p.Mercury, mercury, mercury / 100)),
        };
        if (p.Water != null && p.WaterLitres > 0)
        {
            ingredients.Add(Def(p.Water.Code.ToString(), "item", p.WaterLitres * 100, null, p.WaterLitres));
            stacks.Add(new JArray(Stack(ctx, p.Water, p.WaterLitres * 100, p.WaterLitres)));
        }
        int kept0 = ingredients.Count;
        foreach (var (template, alternatives) in p.Kept)
        {
            ingredients.Add(Def(template, "item", 1, "kept"));
            stacks.Add(new JArray(alternatives.Select(a => Stack(ctx, a, 1))));
        }
        ingredients.Add(Def(p.FrameCode, "block", 1, "station"));
        stacks.Add(p.Frame != null ? new JArray(Stack(ctx, p.Frame, 1)) : new JArray());

        double turns = n * p.TurnsPerItem;
        return new JObject
        {
            ["id"] = $"{AmalgamPanType}|{input}|0",
            ["type"] = AmalgamPanType,
            ["mod"] = p.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(c.Amalgam.Code.ToString(), "item", c.AmalgamItems), Def(input, "item", n)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, c.Amalgam, c.AmalgamItems), Stack(ctx, c.Input, n)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "hand",
                ["turns"] = Num(turns),
                ["work"] = new JObject { ["amount"] = Num(turns), ["unit"] = "muller turns" },
                ["kept"] = new JArray(Enumerable.Range(kept0, p.Kept.Count)),
            },
            ["requirements"] = new JArray(
                "Hold right mouse on the pan to turn the muller by hand, or turn it from a mechanical power axle in the block below",
                $"By hand, {Num(turns / p.HandTurnsPerSecond)} seconds a charge ({Num(p.HandTurnsPerSecond * 60)} turns a minute)",
                "The quartz comes back amalgamated: the rocker then catches its free gold or silver without the × 0.7 loss"),
            ["extra"] = new JObject
            {
                ["ore"] = c.Ore,
                ["grade"] = c.Grade,
                ["chargeItems"] = n,
                ["amalgamMercury"] = Num(p.AmalgamMercury),
                ["waterLitres"] = Num(p.WaterLitres),
            },
        };
    }
}
