using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Roasting sulfide concentrate in the firepit (seraphhorizons, ore processing). See
/// docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string OreRoastingType = "oreroasting";

    private static void FillOreRoasting(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        var roasts = OreRoastingExport.Read(ctx.Api);
        if (roasts.Count == 0)
            return;
        var firepit = BlockOf(ctx, OreRoastingExport.FirepitCode);
        if (firepit == null)
            ctx.Api.Logger.Warning("[seraphexport] {0} is not registered; roasting is exported without it", OreRoastingExport.FirepitCode);
        var mine = roasts.Select(r => OreRoastingRecord(ctx, r, firepit)).ToList();
        records.AddRange(mine);
        AddType(types, OreRoastingType, "Roasting", mine.Count, "generic", "FirepitRoastSeconds", GearChain.Mod);
    }

    /// <summary>
    /// One sulfide's concentrate roasted in a firepit (role <c>station</c>, not consumed): one item in,
    /// its share of a roasted item out (0.85), the fraction carried over to the next item in the same
    /// firepit. <c>extra</c> has the share, the temperature and the seconds one item takes at it.
    /// </summary>
    private static JObject OreRoastingRecord(Context ctx, OreRoast r, Vintagestory.API.Common.Block? firepit) => new()
    {
        ["id"] = $"{OreRoastingType}|{r.Concentrate.Code}|0",
        ["type"] = OreRoastingType,
        ["mod"] = GearChain.Mod,
        ["ingredients"] = new JArray(
            Def(r.Concentrate.Code.ToString(), "item", 1),
            Def(OreRoastingExport.FirepitCode, "block", 1, "station")),
        ["outputs"] = new JArray(Def(r.Roasted.Code.ToString(), "item", r.Share)),
        ["variants"] = new JArray(new JObject
        {
            ["ingredients"] = new JArray
            {
                new JArray(Stack(ctx, r.Concentrate, 1)),
                firepit != null ? new JArray(Stack(ctx, firepit, 1)) : new JArray(),
            },
            ["outputs"] = new JArray(Stack(ctx, r.Roasted, r.Share)),
        }),
        ["requirements"] = new JArray($"A lit firepit, any fuel, at {r.MeltingPoint} °C: one item every {Num(r.Seconds)} s"),
        ["extra"] = new JObject
        {
            ["share"] = Num(r.Share),
            ["meltingPoint"] = r.MeltingPoint,
            ["seconds"] = Num(r.Seconds),
            ["carried"] = true,
        },
    };
}
