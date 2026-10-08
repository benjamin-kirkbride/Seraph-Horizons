using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The draw bench's process (seraphhorizons, DrawBench/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string DrawBenchType = "drawbench";

    private static void FillDrawBench(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (DrawBenchExport.Read(ctx.Api) is not { } bench)
            return;
        if (bench.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the draw bench's {0} is not registered; its process is exported without it", bench.FrameCode);
        var mine = bench.Classes.Select(c => DrawBenchRecord(ctx, bench, c)).ToList();
        records.AddRange(mine);
        AddType(types, DrawBenchType, "Draw bench", mine.Count, "machine", "DrawBenchSettings", bench.Mod);
    }

    /// <summary>
    /// One metal on the draw bench: the hollow section (the game's chute section, consumed), the
    /// gearbox, chain, dog and mandrel (kept: fitted, never consumed), the die (a tool that wears a
    /// fixed amount per hollow), the oil (drained from the machine's tank per pipe section) and the
    /// machine; four pipe sections of the metal.
    /// </summary>
    private static JObject DrawBenchRecord(Context ctx, DrawBenchData b, DrawClass k)
    {
        double points = b.DrainPerSection * b.SectionsPerHollow;
        double litres = points / GearChain.PointsPerLitre;
        var firstOil = b.Oils.FirstOrDefault();
        var die = Def(k.Dies[0].Code.ToString(), "item", 1, "tool");
        die["isTool"] = true;
        die["toolDurabilityCost"] = b.DieWear;
        var ingredients = new JArray(Def(k.Hollow.Code.ToString(), "item", 1));
        foreach (var (template, _, count) in b.Kept)
            ingredients.Add(Def(template, "item", count, "kept"));
        int dieAt = ingredients.Count;
        ingredients.Add(die);
        int oilAt = ingredients.Count;
        ingredients.Add(Def(firstOil?.Item.Code.ToString() ?? "game:oilportion-*", "item",
            firstOil != null ? litres / firstOil.LitresPerItem : points, "oil", litres));
        ingredients.Add(Def(b.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray { new JArray(Stack(ctx, k.Hollow, 1)) };
        foreach (var (_, items, count) in b.Kept)
            stacks.Add(new JArray(items.Select(i => Stack(ctx, i, count))));
        stacks.Add(new JArray(k.Dies.Select(d => Stack(ctx, d, 1))));
        stacks.Add(new JArray(b.Oils.Select(o => Stack(ctx, o.Item, litres / o.LitresPerItem, litres))));
        stacks.Add(b.Frame != null ? new JArray(Stack(ctx, b.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{DrawBenchType}|{k.Hollow.Code}|0",
            ["type"] = DrawBenchType,
            ["mod"] = b.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Section.Code.ToString(), "item", b.SectionsPerHollow)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Section, b.SectionsPerHollow)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(k.TurnsPerSection * b.SectionsPerHollow),
                ["work"] = new JObject { ["amount"] = b.SectionsPerHollow, ["unit"] = "sections", ["turnsPerUnit"] = Num(k.TurnsPerSection) },
                ["kept"] = new JArray(Enumerable.Range(1, b.Kept.Count)),
                ["wear"] = new JObject { ["ingredient"] = dieAt, ["rule"] = "fixed" },
                ["oil"] = new JObject { ["ingredient"] = oilAt, ["points"] = Num(points), ["tank"] = Num(b.Tank) },
            },
            ["extra"] = new JObject { ["rig"] = DrawBenchExport.RigAsset.ToString(), ["class"] = k.Name, ["metal"] = k.Metal },
        };
    }
}
