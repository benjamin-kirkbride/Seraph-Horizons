using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The riddle's process (seraphhorizons, Riddle/, #714). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string RiddleType = "riddle";

    private static void FillRiddle(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (RiddleExport.Read(ctx.Api) is not { } riddle)
            return;
        if (riddle.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the riddle's {0} is not registered; its process is exported without it", riddle.FrameCode);
        var mine = riddle.Classes.Select(c => RiddleRecord(ctx, riddle, c)).ToList();
        records.AddRange(mine);
        AddType(types, RiddleType, "Riddle", mine.Count, "machine", "RiddleSettings", riddle.Mod);
    }

    /// <summary>
    /// One crushed ore on the riddle: a charge of it (consumed), the riddle's mesh (kept: fitted, never consumed) and
    /// the station; out, the fines, the same crushed ore classified (its <c>oreState</c> attribute), and the oversize
    /// as raw ore in the default rock (granite; in play the charge's own rock, or the rock under the station), back to
    /// spalling. Worked by hand (power <c>hand</c>): its turns are the shakes, right-click held (<c>work</c> in
    /// shakes). No tool wears and there is no oil.
    /// </summary>
    private static JObject RiddleRecord(Context ctx, RiddleData s, RiddleClass k)
    {
        int fines = s.Lumps - (k.Oversize != null ? s.Oversize : 0);
        var ingredients = new JArray(
            Def(k.Crushed.Code.ToString(), "item", s.Lumps),
            Def(s.Mesh[0].Code.ToString(), "item", s.MeshCount, "kept"),
            Def(s.FrameCode, "block", 1, "station"));
        var stacks = new JArray
        {
            new JArray(Stack(ctx, k.Crushed, s.Lumps)),
            new JArray(s.Mesh.Select(i => Stack(ctx, i, s.MeshCount))),
            s.Frame != null ? new JArray(Stack(ctx, s.Frame, 1)) : new JArray(),
        };
        var classified = Stack(ctx, k.Crushed, fines);
        classified["attributes"] = new JObject { ["oreState"] = 1 };
        var outputs = new JArray(Def(k.Crushed.Code.ToString(), "item", fines));
        var outStacks = new JArray(classified);
        if (k.Oversize != null && s.Oversize > 0)
        {
            outputs.Add(Def(k.Oversize.Code.ToString(), "item", s.Oversize));
            outStacks.Add(Stack(ctx, k.Oversize, s.Oversize));
        }
        return new JObject
        {
            ["id"] = $"{RiddleType}|{k.Crushed.Code}|0",
            ["type"] = RiddleType,
            ["mod"] = s.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject { ["ingredients"] = stacks, ["outputs"] = outStacks }),
            ["machine"] = new JObject
            {
                ["power"] = "hand",
                ["turns"] = Num(s.Shakes),
                ["work"] = new JObject { ["amount"] = Num(s.Shakes), ["unit"] = "shakes" },
                ["kept"] = new JArray(1),
            },
            ["extra"] = new JObject
            {
                ["rig"] = RiddleExport.RigAsset.ToString(), ["ore"] = k.Ore, ["grade"] = k.Grade,
                ["classified"] = true, ["fines"] = fines, ["oversize"] = k.Oversize != null ? s.Oversize : 0,
            },
        };
    }
}
