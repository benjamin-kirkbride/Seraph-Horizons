using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Spalling (seraphhorizons, Ore/Processing/OreSpalling.cs). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string SpallingType = "spalling";

    public const string SpallingRequirement =
        "Set down on the ground, one to a block, on any solid block; each left-click on it with a hammer is a blow";

    private static void FillSpalling(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (SpallingExport.Read(ctx.Api) is not { } spalling)
            return;
        var mine = spalling.Classes.Select(k => SpallingRecord(ctx, spalling, k)).ToList();
        records.AddRange(mine);
        AddType(types, SpallingType, "Spalling", mine.Count, "machine", "SpallingSettings", spalling.Mod);
    }

    /// <summary>
    /// One ore item kind (grade and ore, every host rock an alternative) broken by hand where it lies:
    /// the ore (consumed) and the hammer (a tool worn a fixed amount a job, its wear a blow times the
    /// blows); the crushed ore of the 5-unit rule. No station: it is worked on the ground
    /// (<c>requirements</c>). Power <c>hand</c>, its turns the blows, each a left-click.
    /// </summary>
    private static JObject SpallingRecord(Context ctx, SpallingData s, SpallClass k)
    {
        var ingredients = new JArray(Def(k.Pattern, "item", 1));
        var stacks = new JArray { new JArray(k.Inputs.Select(i => Stack(ctx, i, 1))) };
        bool hammer = s.Hammers.Count > 0 && s.HammerWearPerBlow > 0;
        if (hammer)
        {
            var tool = Def(s.Hammers.Any(h => h.Code.ToString() == SpallingExport.HammerTemplate) ? SpallingExport.HammerTemplate : s.Hammers[0].Code.ToString(), "item", 1, "tool");
            tool["isTool"] = true;
            tool["toolDurabilityCost"] = k.Blows * s.HammerWearPerBlow;
            ingredients.Add(tool);
            stacks.Add(new JArray(s.Hammers.Select(i => Stack(ctx, i, 1))));
        }
        var machine = new JObject
        {
            ["power"] = "hand",
            ["turns"] = Num(k.Blows),
            ["work"] = new JObject { ["amount"] = Num(k.Blows), ["unit"] = "strikes" },
        };
        if (hammer)
            machine["wear"] = new JObject { ["ingredient"] = 1, ["rule"] = "fixed" };

        return new JObject
        {
            ["id"] = $"{SpallingType}|{k.Pattern}|0",
            ["type"] = SpallingType,
            ["mod"] = s.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Crushed.Code.ToString(), "item", k.Count)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Crushed, k.Count)),
            }),
            ["machine"] = machine,
            ["requirements"] = new JArray(SpallingRequirement),
            ["extra"] = new JObject { ["ore"] = k.Ore, ["grade"] = k.Grade, ["form"] = SpallingExport.IsRaw(k.Grade) ? "raw" : "chunk", ["blows"] = k.Blows },
        };
    }
}
