using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// The ore machines' builds (seraphhorizons, Machines/; #711): every build file
/// (<c>assets/seraphhorizons/config/builds/{machine}.json</c>) whose machine's frame block is in the
/// game becomes a <c>construction</c> record: the frame as placed, then each stage's materials, one
/// right-click a material, its wearing parts' first sets with the stage they are fitted with. The
/// shared shape every ore machine's build reuses; a machine's processing records are its own. See
/// docs/recipe-browser/schema.md.
/// </summary>
public static partial class RecipeSection
{
    public const string OreMachineBuildsFolder = "config/builds/";

    private static void FillOreMachines(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        int added = 0;
        using (new EnglishLocale())
            foreach (var asset in ctx.Api.Assets.GetMany(OreMachineBuildsFolder, GearChain.Mod, loadAsset: true)
                         .OrderBy(a => a.Location.Path, StringComparer.Ordinal))
            {
                JObject build;
                try
                {
                    build = JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
                }
                catch (Newtonsoft.Json.JsonException e)
                {
                    ctx.Api.Logger.Warning("[seraphexport] {0} is not valid JSON ({1}); its machine's build is not exported", asset.Location, e.Message);
                    continue;
                }
                if ((string?)build["block"] is not { } blockCode || BlockOf(ctx, blockCode) is not { } block)
                    continue;
                records.Add(OreMachineBuildRecord(ctx, build, block));
                added++;
            }
        if (added > 0 && types.TryGetValue(InPlaceType, out var type))
            type["count"] = records.Count(r => (string?)r["type"] == InPlaceType);
    }

    /// <summary>The materials of each stage, a wearing part fitted with a stage its last, with the
    /// codes each may be (patterns expanded to what is in the game).</summary>
    private static List<(string Stage, List<(List<string> Codes, int Quantity)> Materials)> OreMachineStages(JObject build)
    {
        var stages = new List<(string, List<(List<string>, int)>)>();
        foreach (var s in build["stages"] as JArray ?? [])
        {
            var materials = (s["materials"] as JArray ?? []).Select(m =>
                ((m["codes"] as JArray ?? []).Select(c => (string)c!).ToList(), (int?)m["quantity"] ?? 0)).ToList();
            stages.Add(((string?)s["id"] ?? "", materials));
        }
        foreach (var w in build["wearing"] as JArray ?? [])
            if ((string?)w["stage"] is { } at && stages.FindIndex(x => x.Item1 == at) is >= 0 and var i)
                stages[i].Item2.Add(((w["codes"] as JArray ?? []).Select(c => (string)c!).ToList(), (int?)w["count"] ?? 0));
        return stages;
    }

    /// <summary>Every item in the game a code or pattern names.</summary>
    private static List<CollectibleObject> Matching(Context ctx, IEnumerable<string> codes)
    {
        var found = new List<CollectibleObject>();
        foreach (var code in codes)
        {
            if (!code.Contains('*'))
            {
                if (CollectibleOf(ctx, code) is { } c)
                    found.Add(c);
                continue;
            }
            var loc = new AssetLocation(code);
            found.AddRange(ctx.Api.World.SearchItems(loc).Where(i => i is { IsMissing: false, Code: not null }));
            found.AddRange(ctx.Api.World.SearchBlocks(loc).Where(b => b is { IsMissing: false, Code: not null }));
        }
        return found.DistinctBy(c => c.Code.ToString()).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList();
    }

    private static JObject OreMachineBuildRecord(Context ctx, JObject build, Block block)
    {
        string machine = (string?)build["machine"] ?? block.Code.Path;
        var action = Lang.Get("seraphhorizons:machine-help-fit");
        var ingredients = new JArray();
        var stages = new JArray { new JObject { ["ingredients"] = new JArray() } };
        var stacks = new JArray();
        var names = new JArray();
        foreach (var (id, materials) in OreMachineStages(build))
        {
            var slots = new JArray();
            foreach (var (codes, quantity) in materials)
            {
                var items = Matching(ctx, codes);
                if (items.Count == 0)
                {
                    ctx.Api.Logger.Warning("[seraphexport] {0}'s stage {1} takes {2}, which is not in the game", machine, id, string.Join(", ", codes));
                    continue;
                }
                var codeList = items.Select(i => i.Code.ToString()).ToList();
                var def = Def(codeList.Count == 1 ? codeList[0] : CommonFamily(codeList), Kind(items[0].ItemClass), quantity);
                if (codeList.Count > 1)
                    def["allowedVariants"] = new JArray(codeList.Select(c => c[FamilyPrefix(codeList).Length..]));
                slots.Add(ingredients.Count);
                ingredients.Add(def);
                stacks.Add(new JArray(items.Select(i => Stack(ctx, i, quantity))));
            }
            stages.Add(new JObject { ["ingredients"] = slots, ["action"] = action });
            string key = $"seraphhorizons:{machine}-stage-{id}";
            names.Add(Lang.HasTranslation(key) ? Lang.Get(key) : id);
        }
        return new JObject
        {
            ["id"] = $"{InPlaceType}|{block.Code}|0",
            ["type"] = InPlaceType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(block.Code.ToString(), "block", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, block, 1)),
            }),
            ["construction"] = new JObject { ["stages"] = stages },
            ["extra"] = new JObject
            {
                ["behavior"] = "seraphhorizons.OreMachine",
                ["machine"] = machine,
                ["feed"] = (string?)build["feed"] ?? "continuous",
                ["stages"] = names,
                ["wearing"] = new JArray((build["wearing"] as JArray ?? []).Select(w => new JObject
                {
                    ["id"] = (string?)w["id"],
                    ["count"] = (int?)w["count"] ?? 0,
                    ["uses"] = (double?)w["uses"] ?? 0,
                })),
            },
        };
    }
}
