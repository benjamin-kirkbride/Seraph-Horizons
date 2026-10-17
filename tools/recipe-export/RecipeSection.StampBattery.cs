using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The stamp battery's process (seraphhorizons, StampBattery/, #728). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string StampBatteryType = "stampbattery";

    public const string StampBatteryRequirement =
        "Fed by chute from above or by hand at the feed box's mouth; the crushed ore leaves through the screen by the output chute, classified";

    private static void FillStampBattery(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (StampBatteryExport.Read(ctx.Api) is not { } battery)
            return;
        if (battery.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the stamp battery's {0} is not registered; its process is exported without it", battery.FrameCode);
        var mine = battery.Classes.Select(k => StampBatteryRecord(ctx, battery, k)).ToList();
        records.AddRange(mine);
        AddType(types, StampBatteryType, "Stamp battery", mine.Count, "machine", "config/ore-processing.json machines.stampbattery", battery.Mod);
    }

    /// <summary>
    /// One kind of rock through the stamp battery: the rock (consumed; raw ore every host rock an
    /// alternative), the water it takes from the battery's reservoir (consumed, in litres), a rock's share
    /// of a set of shoes (the steel pounder caps, worn by use) and the battery (station); the crushed ore
    /// of its grade, classified (<c>extra.classified</c>: the stack's <c>oreState</c>), one for one, two
    /// from crystallised ore. Power mechanical, its turns the shaft's a rock.
    /// </summary>
    private static JObject StampBatteryRecord(Context ctx, StampBatteryData b, StampClass k)
    {
        var ingredients = new JArray(Def(k.Pattern.Contains('*') ? k.Pattern : k.Inputs[0].Code.ToString(), "item", 1));
        var stacks = new JArray { new JArray(k.Inputs.Select(i => Stack(ctx, i, 1))) };
        if (b.Water != null && b.WaterPerRock > 0)
        {
            ingredients.Add(Def(b.Water.Code.ToString(), "item", b.WaterPerRock * b.WaterItemsPerLitre, null, b.WaterPerRock));
            stacks.Add(new JArray(Stack(ctx, b.Water, b.WaterPerRock * b.WaterItemsPerLitre, b.WaterPerRock)));
        }
        if (b.Shoe != null && b.ShoesPerSet > 0 && b.RocksPerSet > 0)
        {
            double share = b.ShoesPerSet / b.RocksPerSet;
            ingredients.Add(Def(b.Shoe.Code.ToString(), "item", share));
            stacks.Add(new JArray(Stack(ctx, b.Shoe, share)));
        }
        ingredients.Add(Def(b.FrameCode, "block", 1, "station"));
        stacks.Add(b.Frame != null ? new JArray(Stack(ctx, b.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{StampBatteryType}|{k.Pattern}|0",
            ["type"] = StampBatteryType,
            ["mod"] = b.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Crushed.Code.ToString(), "item", k.Count)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Crushed, k.Count)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(b.TurnsPerRock),
                ["work"] = new JObject { ["amount"] = 1, ["unit"] = "rocks", ["turnsPerUnit"] = Num(b.TurnsPerRock) },
            },
            ["requirements"] = new JArray(StampBatteryRequirement),
            ["extra"] = new JObject
            {
                ["ore"] = k.Ore, ["grade"] = k.Grade, ["form"] = k.Form, ["classified"] = true,
                ["shoesPerSet"] = b.ShoesPerSet, ["rocksPerSet"] = Num(b.RocksPerSet),
            },
        };
    }
}
