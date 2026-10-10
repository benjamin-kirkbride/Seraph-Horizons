using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.RecipeExport;

/// <summary>The eidolon's two builds (seraphhorizons, EidolonGantry/): the gantry's winch and the
/// body on its spine, as blocks built in place. See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    private static void FillEidolon(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (EidolonExport.Read(ctx.Api) is not { } e)
            return;
        var gantries = e.Woods.Select(w => (Wood: w, Block: BlockOf(ctx, EidolonExport.GantryCode(w))))
            .Where(g => g.Block != null)
            // as built-in-place blocks are: in code order, the record named after the first
            .OrderBy(g => g.Block!.Code.ToString(), StringComparer.Ordinal).ToList();
        if (gantries.Count == 0)
        {
            ctx.Api.Logger.Warning("[seraphexport] no eidolon gantry is registered; the eidolon's builds are not exported");
            return;
        }
        using (new EnglishLocale())
        {
            records.Add(GantryRecord(ctx, e, gantries!));
            if (CollectibleOf(ctx, EidolonExport.Creature) is { } creature)
                records.Add(BodyRecord(ctx, e, gantries!, creature));
            else
                ctx.Api.Logger.Warning("[seraphexport] {0} is not registered; the eidolon's body is not exported", EidolonExport.Creature);
        }
        if (types.TryGetValue(InPlaceType, out var type))
            type["count"] = records.Count(r => (string?)r["type"] == InPlaceType);
    }

    /// <summary>
    /// The gantry's winch: the frame as placed (its grid recipe makes it), then the nine stages, each
    /// one right-click taking its whole count; one variant per wood, the drum's planks and the spine's
    /// beams of the gantry's wood.
    /// </summary>
    private static JObject GantryRecord(Context ctx, EidolonData e, List<(string Wood, Block Block)> gantries)
    {
        var action = Lang.Get("seraphhorizons:blockhelp-eidolongantry-fitpart");
        var ingredients = new JArray();
        var stages = new JArray { new JObject { ["ingredients"] = new JArray() } };
        foreach (var stage in e.Winch)
            stages.Add(StageOf(ctx, stage, ingredients, action, gantries[0].Wood));

        var variants = new JArray();
        foreach (var (wood, block) in gantries)
        {
            var stacks = new JArray();
            foreach (var stage in e.Winch)
                foreach (var item in stage.Items)
                    stacks.Add(new JArray(item.Codes.Select(c => CollectibleOf(ctx, c.Replace("{wood}", wood)))
                        .OfType<CollectibleObject>().Select(c => Stack(ctx, c, item.Count))));
            variants.Add(new JObject
            {
                ["bindings"] = new JObject { ["wood"] = wood },
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, block, 1)),
            });
        }

        return new JObject
        {
            ["id"] = $"{InPlaceType}|{gantries[0].Block.Code}|0",
            ["type"] = InPlaceType,
            ["mod"] = e.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(EidolonExport.GantryPrefix + "{wood}-north", "block", 1)),
            ["variants"] = variants,
            ["construction"] = new JObject { ["stages"] = stages },
            ["extra"] = new JObject
            {
                ["behavior"] = "seraphhorizons.EidolonGantry",
                ["members"] = new JArray(gantries.Select(g => g.Block.Code.ToString())),
                ["stages"] = new JArray(e.Winch.Select(s => s.Name)),
            },
        };
    }

    /// <summary>
    /// The eidolon's body: a gantry with its spine hung (the first stage, not consumed: it stays as
    /// the eidolon's dock, role station), then the six body stages, any order within a stage; the
    /// mind's temporal gear wakes it. The output is the eidolon, as its creature item.
    /// </summary>
    private static JObject BodyRecord(Context ctx, EidolonData e, List<(string Wood, Block Block)> gantries,
        CollectibleObject creature)
    {
        var ingredients = new JArray(Def(EidolonExport.GantryPrefix + "*-north", "block", 1, "station"));
        var stages = new JArray { new JObject { ["ingredients"] = new JArray(0) } };
        foreach (var stage in e.Body)
        {
            var action = Lang.Get(stage == e.Body[^1]
                ? "seraphhorizons:blockhelp-eidolongantry-wake"
                : "seraphhorizons:blockhelp-eidolongantry-fitbody");
            stages.Add(StageOf(ctx, stage, ingredients, action, null));
        }

        var stacks = new JArray { new JArray(gantries.Select(g => Stack(ctx, g.Block, 1))) };
        foreach (var stage in e.Body)
            foreach (var item in stage.Items)
                stacks.Add(new JArray(item.Codes.Select(c => CollectibleOf(ctx, c)).OfType<CollectibleObject>()
                    .Select(c => Stack(ctx, c, item.Count))));

        return new JObject
        {
            ["id"] = $"{InPlaceType}|{creature.Code}|0",
            ["type"] = InPlaceType,
            ["mod"] = e.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(creature.Code.ToString(), Kind(creature.ItemClass), 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, creature, 1)),
            }),
            ["construction"] = new JObject { ["stages"] = stages },
            ["extra"] = new JObject
            {
                ["behavior"] = "seraphhorizons.EidolonBody",
                ["stages"] = new JArray(e.Body.Select(s => s.Name)),
            },
        };
    }

    /// <summary>Adds a stage's items to the ingredients (a code with alternatives as a wildcard with
    /// its allowed variants) and returns the stage.</summary>
    private static JObject StageOf(Context ctx, EidolonStage stage, JArray ingredients, string action, string? wood)
    {
        var slots = new JArray();
        foreach (var item in stage.Items)
        {
            var first = CollectibleOf(ctx, item.Codes[0].Replace("{wood}", wood ?? ""));
            var def = Def(item.Codes.Count == 1 ? item.Codes[0] : CommonFamily(item.Codes), first != null ? Kind(first.ItemClass) : "item", item.Count);
            if (item.Codes.Count > 1)
                def["allowedVariants"] = new JArray(item.Codes.Select(c => c[(c.LastIndexOf('-') + 1)..]));
            slots.Add(ingredients.Count);
            ingredients.Add(def);
        }
        return new JObject { ["ingredients"] = slots, ["action"] = action };
    }

    /// <summary><c>game:rod-*</c> for <c>game:rod-iron</c>, <c>game:rod-steel</c>, ...: the codes
    /// share all but their last part.</summary>
    private static string CommonFamily(List<string> codes) => codes[0][..(codes[0].LastIndexOf('-') + 1)] + "*";

    private static Block? BlockOf(Context ctx, string code) =>
        ctx.Api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Code: not null } b ? b : null;

    private static CollectibleObject? CollectibleOf(Context ctx, string code)
    {
        var loc = new AssetLocation(code);
        if (ctx.Api.World.GetItem(loc) is { IsMissing: false, Code: not null } i) return i;
        return BlockOf(ctx, code);
    }
}
