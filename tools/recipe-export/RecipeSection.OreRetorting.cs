using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Retorting cinnabar and amalgam in the still (seraphhorizons, ore processing). See
/// docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string OreRetortingType = "oreretorting";

    private static void FillOreRetorting(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        var retorts = OreRetortingExport.Read(ctx.Api);
        if (retorts.Count == 0)
            return;
        var boiler = BlockOf(ctx, OreRetortingExport.BoilerCode);
        var condenser = BlockOf(ctx, OreRetortingExport.CondenserCode);
        if (boiler == null || condenser == null)
            ctx.Api.Logger.Warning("[seraphexport] {0} or {1} is not registered; retorting is exported without it",
                OreRetortingExport.BoilerCode, OreRetortingExport.CondenserCode);
        var mine = retorts.Select(r => OreRetortingRecord(ctx, r, boiler, condenser)).ToList();
        records.AddRange(mine);
        AddType(types, OreRetortingType, "Retorting", mine.Count, "generic", "OreProcessing", GearChain.Mod);
    }

    /// <summary>
    /// One item retorted in the still: the item (one, consumed), the boiler and the condenser (role
    /// <c>station</c>); out, its mercury in portions (and litres) and, for an amalgam, its sponge.
    /// </summary>
    private static JObject OreRetortingRecord(Context ctx, OreRetort r, Vintagestory.API.Common.Block? boiler,
        Vintagestory.API.Common.Block? condenser)
    {
        double litres = r.Portions / 100;
        var outputs = new JArray(Def(r.Mercury.Code.ToString(), "item", r.Portions, null, litres));
        var stacks = new JArray(Stack(ctx, r.Mercury, r.Portions, litres));
        if (r.Residue != null)
        {
            outputs.Add(Def(r.Residue.Code.ToString(), "item", 1));
            stacks.Add(Stack(ctx, r.Residue, 1));
        }
        return new JObject
        {
            ["id"] = $"{OreRetortingType}|{r.Input.Code}|0",
            ["type"] = OreRetortingType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(
                Def(r.Input.Code.ToString(), "item", 1),
                Def(OreRetortingExport.BoilerCode, "block", 1, "station"),
                Def(OreRetortingExport.CondenserCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, r.Input, 1)),
                    boiler != null ? new JArray(Stack(ctx, boiler, 1)) : new JArray(),
                    condenser != null ? new JArray(Stack(ctx, condenser, 1)) : new JArray(),
                },
                ["outputs"] = stacks,
            }),
            ["requirements"] = new JArray(
                "A boiler on a lit firepit, a condenser beside it with water and a bucket under its spout",
                $"{Num(r.PortionsPerSecond)} portions of mercury a second once the boiler is at 75 °C"),
            ["extra"] = new JObject
            {
                ["portions"] = Num(r.Portions),
                ["portionsPerSecond"] = Num(r.PortionsPerSecond),
            },
        };
    }
}
