using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The ball mill's process (seraphhorizons, BallMill/, #915). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string BallMillType = "ballmill";

    private static void FillBallMill(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (BallMillExport.Read(ctx.Api) is not { } mill)
            return;
        if (mill.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the ball mill's {0} is not registered; its process is exported without it", mill.FrameCode);
        var mine = mill.Classes.Select(c => BallMillRecord(ctx, mill, c)).ToList();
        records.AddRange(mine);
        AddType(types, BallMillType, "Ball mill", mine.Count, "machine", "BallMill", mill.Mod);
    }

    /// <summary>
    /// One crushed ore through the ball mill: the crushed ore (consumed) and the machine; out, one ground ore of its
    /// grade, ground and screened (its <c>oreState</c> attribute: classified; ground by its item), no metal lost.
    /// Mechanical, its turns the shaft's a rock at any speed; its water the litres a rock (<c>extra.water</c>). The
    /// shaking table's middlings take the same way in, as ground ore.
    /// </summary>
    private static JObject BallMillRecord(Context ctx, BallMillData m, BallMillClass k)
    {
        var ground = Stack(ctx, k.Ground, 1);
        ground["attributes"] = new JObject { ["oreState"] = 1 };
        return new JObject
        {
            ["id"] = $"{BallMillType}|{k.Crushed.Code}|0",
            ["type"] = BallMillType,
            ["mod"] = m.Mod,
            ["ingredients"] = new JArray(Def(k.Crushed.Code.ToString(), "item", 1), Def(m.FrameCode, "block", 1, "station")),
            ["outputs"] = new JArray(Def(k.Ground.Code.ToString(), "item", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, k.Crushed, 1)),
                    m.Frame != null ? new JArray(Stack(ctx, m.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(ground),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(m.TurnsPerRock),
            },
            ["extra"] = new JObject
            {
                ["rig"] = BallMillExport.RigAsset.ToString(), ["ore"] = k.Ore, ["grade"] = k.Grade,
                ["ground"] = true, ["classified"] = true, ["water"] = Num(m.WaterPerRock),
            },
        };
    }
}
