using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The stall roaster's burn (seraphhorizons, StallRoaster/, #925). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string StallRoasterType = "stallroasting";

    private static void FillStallRoaster(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (StallRoasterExport.Read(ctx.Api) is not { } stall)
            return;
        if (stall.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the stall roaster's {0} is not registered; its burn is exported without it", stall.FrameCode);
        var mine = stall.Classes.Select(c => StallRoasterRecord(ctx, stall, c)).ToList();
        records.AddRange(mine);
        AddType(types, StallRoasterType, "Stall roaster", mine.Count, "generic", "StallRoasterSettings", stall.Mod);
    }

    /// <summary>
    /// One sulfide rock item kind (raw ore by grade with every host rock an alternative, or crushed ore) roasted in
    /// the stall: the rock (consumed) and the stall's frame (role <c>station</c>); out, one stall-roasted rock of its
    /// grade, and the sulfur powder a rock leaves in the crust, a fraction, since the stall carries what falls short
    /// of a whole powder over to its next burn. A charge is the stall's 32 rock on a bed of firewood, burnt through
    /// in its hours with the front walled up; <c>requirements</c> says so in words, <c>extra</c> has the figures.
    /// </summary>
    private static JObject StallRoasterRecord(Context ctx, StallRoasterData s, StallRoastClass k)
    {
        var outputs = new JArray(Def(k.Roasted.Code.ToString(), "item", 1));
        var outputStacks = new JArray(Stack(ctx, k.Roasted, 1));
        if (k.Sulfur > 0 && s.SulfurPowder != null)
        {
            outputs.Add(Def(StallRoasterExport.SulfurCode, "item", k.Sulfur));
            outputStacks.Add(Stack(ctx, s.SulfurPowder, k.Sulfur));
        }
        return new JObject
        {
            ["id"] = $"{StallRoasterType}|{k.Id}|0",
            ["type"] = StallRoasterType,
            ["mod"] = s.Mod,
            ["ingredients"] = new JArray(Def(k.Id, "item", 1), Def(s.FrameCode, "block", 1, "station")),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(k.Inputs.Select(i => Stack(ctx, i, 1))),
                    s.Frame != null ? new JArray(Stack(ctx, s.Frame, 1)) : new JArray(),
                },
                ["outputs"] = outputStacks,
            }),
            ["requirements"] = new JArray(
                $"A charge of up to {s.Charge} rock on a bed of {s.BedFirewood} firewood, lit, burns {Num(s.Hours)} in-game hours with the front walled up and the row's chimney drawing"),
            ["extra"] = new JObject
            {
                ["ore"] = k.Ore, ["grade"] = k.Grade, ["form"] = k.Form,
                ["share"] = Num(s.Recovery), ["charge"] = s.Charge, ["hours"] = Num(s.Hours), ["bedFirewood"] = s.BedFirewood,
                ["sulfur"] = Num(k.Sulfur), ["carried"] = true,
            },
        };
    }
}
