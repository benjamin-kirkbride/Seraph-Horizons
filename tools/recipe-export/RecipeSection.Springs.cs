using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>Springs as a source of fresh well water (seraphhorizons, Springs/). See
/// docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string SpringType = "spring";

    private static void FillSprings(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        var springs = SpringExport.Read(ctx.Api);
        if (springs.Count == 0)
            return;
        var mine = springs.Select(s => SpringRecord(ctx, s)).ToList();
        records.AddRange(mine);
        AddType(types, SpringType, "Spring", mine.Count, "generic", "SpringsSettings", GearChain.Mod);
    }

    /// <summary>
    /// One kind of spring: the spring (role <c>station</c>, never used up; any rock, its code with the
    /// rock as <c>*</c> and every rock's block among its stacks); out, a second's water from a pipe
    /// tapping it, in portions and litres. Nothing is consumed: a spring never runs dry. With a pump
    /// boost set, a requirement line and <c>extra.pumpBoostPerTurn</c> say what a mechanical pump adds.
    /// </summary>
    private static JObject SpringRecord(Context ctx, SpringSource s)
    {
        double litres = s.LitresPerSecond;
        double portions = litres * s.ItemsPerLitre;
        string station = $"{GearChain.Mod}:{SpringExport.BlockBase}-{s.Kind}-*";
        var record = new JObject
        {
            ["id"] = $"{SpringType}|{station}|0",
            ["type"] = SpringType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = new JArray(Def(station, "block", 1, "station")),
            ["outputs"] = new JArray(Def(s.Water.Code.ToString(), "item", portions, null, litres)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray { new JArray(s.Blocks.Select(b => Stack(ctx, b, 1))) },
                ["outputs"] = new JArray(Stack(ctx, s.Water, portions, litres)),
            }),
            ["requirements"] = new JArray(
                "Found where the world generator would put water running out of a cliff or a cave wall"
                + (s.Kind == "rapid" ? " in fast water (rapids)" : ""),
                $"A Pipes and Power Expanded pipe against any face taps it: {Num(litres)} litres a second into the run, at 1 atm, without end; it pours nothing while tapped",
                "Fills any liquid container, and is drunk from with an empty hand, without end"),
            ["extra"] = new JObject
            {
                ["kind"] = s.Kind,
                ["litresPerSecond"] = Num(litres),
                ["pressure"] = Num(1),
            },
        };
        if (s.PumpBoostPerTurn > 0)
        {
            ((JArray)record["requirements"]!).Add(
                $"A Pipes and Power Expanded mechanical pump drawing on its run makes it give {Num(litres * s.PumpBoostPerTurn)} litres a second more"
                + " for each turn a second of the pump's shaft, into that pump's run (the fastest pump on it only), up to what the pump moves");
            ((JObject)record["extra"]!)["pumpBoostPerTurn"] = Num(s.PumpBoostPerTurn);
        }
        return record;
    }
}
