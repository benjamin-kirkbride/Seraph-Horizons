using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The amalgamation plates' process (seraphhorizons, AmalgamationPlates/, #917). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string AmalgamationPlatesType = "amalgamationplates";

    private static void FillAmalgamationPlates(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (AmalgamationPlatesExport.Read(ctx.Api) is not { } plates)
            return;
        var mine = plates.Rocks.Select(r => AmalgamationPlatesRecord(ctx, plates, r)).ToList();
        records.AddRange(mine);
        AddType(types, AmalgamationPlatesType, "Amalgamation plates", mine.Count, "generic", "AmalgamationPlates", plates.Mod);
    }

    /// <summary>
    /// One rock of crushed or ground gold or silver quartz over the amalgamation plates (role <c>station</c>, not
    /// consumed): the rock and the mercury its catch takes off the dressing (consumed); out, the amalgam (on
    /// average, fractional: what falls short of an item stays on the plates for the next) and the same rock
    /// amalgamated (its <c>oreState</c>), on to the concentrators. No power: water and gravity.
    /// </summary>
    private static JObject AmalgamationPlatesRecord(Context ctx, AmalgamationPlatesData p, PlatesRock r)
    {
        string input = r.Input.Code.ToString();
        var rest = Stack(ctx, r.Input, 1);
        rest["attributes"] = new JObject { ["oreState"] = r.OreState };
        return new JObject
        {
            ["id"] = $"{AmalgamationPlatesType}|{input}|0",
            ["type"] = AmalgamationPlatesType,
            ["mod"] = p.Mod,
            ["ingredients"] = new JArray(
                Def(input, "item", 1),
                Def(p.Mercury.Code.ToString(), "item", r.Mercury, null, r.Mercury / 100),
                Def(p.FrameCode, "block", 1, "station")),
            ["outputs"] = new JArray(Def(r.Amalgam.Code.ToString(), "item", r.AmalgamItems), Def(input, "item", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, r.Input, 1)),
                    new JArray(Stack(ctx, p.Mercury, r.Mercury, r.Mercury / 100)),
                    p.Frame != null ? new JArray(Stack(ctx, p.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(Stack(ctx, r.Amalgam, r.AmalgamItems), rest),
            }),
            ["requirements"] = new JArray(
                $"Built, with {Num(p.WaterPerItem)} L of water a rock from a pipe: {Num(p.PerHour)} rock an in-game hour, no power",
                $"The plates dressed with mercury (they hold {Num(p.DressingLitres)} L) and room on them for the amalgam ({p.AmalgamItems} pieces), scraped off by hand",
                "The rock goes on amalgamated: the concentrators then catch its free gold or silver without the × 0.7 loss"),
            ["extra"] = new JObject
            {
                ["ore"] = r.Ore,
                ["grade"] = r.Grade,
                ["recovery"] = Num(p.Recovery),
                ["perHour"] = Num(p.PerHour),
                ["waterPerItem"] = Num(p.WaterPerItem),
                ["carried"] = true,
            },
        };
    }
}
