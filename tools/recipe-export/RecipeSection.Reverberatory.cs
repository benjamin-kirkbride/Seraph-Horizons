using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The reverberatory roaster's process (seraphhorizons, ReverberatoryRoaster/, #736). See
/// docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string ReverberatoryType = "reverberatory";

    private static void FillReverberatory(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (ReverberatoryExport.Read(ctx.Api) is not { } furnace)
            return;
        if (furnace.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the reverberatory roaster's {0} is not registered; its process is exported without it", furnace.FrameCode);
        var mine = furnace.Classes.Select(c => ReverberatoryRecord(ctx, furnace, c)).ToList();
        records.AddRange(mine);
        AddType(types, ReverberatoryType, "Reverberatory roaster", mine.Count, "machine", "ReverberatoryRoaster", furnace.Mod);
    }

    /// <summary>
    /// One item through the reverberatory roaster: a sulfide's concentrate (consumed) and the furnace (role
    /// <c>station</c>); out, its roasted concentrate one for one (100 %), or for native sulfur rock its sulfur powder (a
    /// fraction, carried over to the next rock). Mechanical, its turns the shaft's an item at any speed (a native sulfur
    /// rock as long as the concentrate holding as much); <c>extra</c> has its water, its coal a concentrate and the
    /// hearth's charge; <c>requirements</c> says the fire and draught in words.
    /// </summary>
    private static JObject ReverberatoryRecord(Context ctx, ReverberatoryData m, ReverberatoryClass k) => new()
    {
        ["id"] = $"{ReverberatoryType}|{k.Input.Code}|0",
        ["type"] = ReverberatoryType,
        ["mod"] = m.Mod,
        ["ingredients"] = new JArray(Def(k.Input.Code.ToString(), "item", 1), Def(m.FrameCode, "block", 1, "station")),
        ["outputs"] = new JArray(Def(k.Output.Code.ToString(), "item", k.Amount)),
        ["variants"] = new JArray(new JObject
        {
            ["ingredients"] = new JArray
            {
                new JArray(Stack(ctx, k.Input, 1)),
                m.Frame != null ? new JArray(Stack(ctx, m.Frame, 1)) : new JArray(),
            },
            ["outputs"] = new JArray(Stack(ctx, k.Output, k.Amount)),
        }),
        ["machine"] = new JObject
        {
            ["power"] = "mechanical",
            ["turns"] = Num(m.TurnsPerConcentrate * k.Weight),
        },
        ["requirements"] = new JArray(
            $"Lit, a coal every {Num(m.ConcentratePerCoal)} concentrate in its bunker, its chimney drawing and its damper open"),
        ["extra"] = new JObject
        {
            ["rig"] = ReverberatoryExport.RigAsset.ToString(), ["form"] = k.Form, ["ore"] = k.Ore,
            ["share"] = Num(k.Amount), ["water"] = Num(m.WaterPerConcentrate * k.Weight),
            ["coal"] = Num(k.Weight / m.ConcentratePerCoal), ["hearth"] = m.HearthCharge, ["carried"] = k.Form == "nativesulfur",
        },
    };
}
