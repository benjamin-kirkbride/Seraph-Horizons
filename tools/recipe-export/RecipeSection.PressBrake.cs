using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The press brake's process (seraphhorizons, PressBrake/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string PressBrakeType = "pressbrake";

    private static void FillPressBrake(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (PressBrakeExport.Read(ctx.Api) is not { } brake)
            return;
        if (brake.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the press brake's {0} is not registered; its process is exported without it", brake.FrameCode);
        var mine = brake.Classes.Select(c => PressBrakeRecord(ctx, brake, c)).ToList();
        records.AddRange(mine);
        AddType(types, PressBrakeType, "Press brake", mine.Count, "machine", "PressBrakeSettings", brake.Mod);
    }

    /// <summary>
    /// One metal on the press brake: the half plate (consumed), the screws and the edges (kept: fitted,
    /// never consumed) and the machine; one angle of the metal. Worked by hand (power
    /// <c>hand</c>): its turns are the lever's, a turn a second while the player holds right-click.
    /// No tool wears and there is no oil.
    /// </summary>
    private static JObject PressBrakeRecord(Context ctx, PressBrakeData b, FoldClass k)
    {
        var ingredients = new JArray(Def(k.Plate.Code.ToString(), "item", 1));
        foreach (var (template, _) in b.Kept)
            ingredients.Add(Def(template, "item", 1, "kept"));
        ingredients.Add(Def(b.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray { new JArray(Stack(ctx, k.Plate, 1)) };
        foreach (var (_, items) in b.Kept)
            stacks.Add(new JArray(items.Select(i => Stack(ctx, i, 1))));
        stacks.Add(b.Frame != null ? new JArray(Stack(ctx, b.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{PressBrakeType}|{k.Plate.Code}|0",
            ["type"] = PressBrakeType,
            ["mod"] = b.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Angle.Code.ToString(), "item", b.AnglesPerPlate)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Angle, b.AnglesPerPlate)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "hand",
                ["turns"] = Num(k.LeverTurnsPerPlate),
                ["kept"] = new JArray(Enumerable.Range(1, b.Kept.Count)),
            },
            ["extra"] = new JObject { ["rig"] = PressBrakeExport.RigAsset.ToString(), ["class"] = k.Name, ["metal"] = k.Metal },
        };
    }
}
