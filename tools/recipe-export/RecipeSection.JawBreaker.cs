using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The jaw breaker's process (seraphhorizons, JawBreaker/, #912). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string JawBreakerType = "jawbreaker";
    public const string JawBreakerFrame = "seraphhorizons:jawbreaker-north";

    /// <summary>The ore processing figures' turns an in-game hour at speed 1 and the reference speed
    /// (Ore/Core/OreRecovery.cs, TurnsPerHourAtSpeed1 and ReferenceSpeed).</summary>
    private const double OreTurnsPerHourAtSpeed1 = 95.5, OreReferenceSpeed = 0.6;

    private static void FillJawBreaker(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (BlockOf(ctx, JawBreakerFrame) is not { } frame || SpallingExport.Read(ctx.Api) is not { } ores)
            return;
        var asset = ctx.Api.Assets.TryGet(new AssetLocation("seraphhorizons", "config/ore-processing.json"));
        var figures = asset == null ? null
            : JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore })["machines"]?["jawbreaker"] as JObject;
        double perHour = (double?)figures?["perHour"] ?? 0;
        if (figures == null || !(perHour > 0))
        {
            ctx.Api.Logger.Warning("[seraphexport] the jaw breaker's figures (ore-processing.json machines.jawbreaker) are missing; its process is not exported");
            return;
        }
        double turns = OreTurnsPerHourAtSpeed1 * OreReferenceSpeed / perHour;
        var mine = ores.Classes.Select(k => JawBreakerRecord(ctx, frame, k, turns, figures)).ToList();
        records.AddRange(mine);
        AddType(types, JawBreakerType, "Jaw breaker", mine.Count, "machine", "JawBreakerSettings", ores.Mod);
    }

    /// <summary>
    /// One ore item kind (grade and ore, every host rock an alternative) broken in the jaw breaker: the ore
    /// (consumed) and the breaker (the station); one crushed ore of its grade, as spalling gives, with no
    /// loss. Power <c>mechanical</c>, <c>turns</c> the axle's turns a rock at any speed.
    /// </summary>
    private static JObject JawBreakerRecord(Context ctx, Block frame, SpallClass k, double turns, JObject figures) => new()
    {
        ["id"] = $"{JawBreakerType}|{k.Pattern}|0",
        ["type"] = JawBreakerType,
        ["mod"] = GearChain.Mod,
        ["ingredients"] = new JArray(Def(k.Pattern, "item", 1), Def(JawBreakerFrame, "block", 1, "station")),
        ["outputs"] = new JArray(Def(k.Crushed.Code.ToString(), "item", k.Count)),
        ["variants"] = new JArray(new JObject
        {
            ["ingredients"] = new JArray { new JArray(k.Inputs.Select(i => Stack(ctx, i, 1))), new JArray(Stack(ctx, frame, 1)) },
            ["outputs"] = new JArray(Stack(ctx, k.Crushed, k.Count)),
        }),
        ["machine"] = new JObject
        {
            ["power"] = "mechanical",
            ["turns"] = Num(turns),
            ["work"] = new JObject { ["amount"] = 1, ["unit"] = "rocks", ["turnsPerUnit"] = Num(turns) },
        },
        ["requirements"] = new JArray("Fed by a chute onto its grizzly's head or by hand; the crushed ore leaves by the chute low on its front face"),
        ["extra"] = new JObject
        {
            ["ore"] = k.Ore, ["grade"] = k.Grade, ["form"] = k.FirstPart == "crystalizedore" ? "crystallised" : "raw",
            ["perHour"] = figures["perHour"], ["load"] = figures["load"], ["emptyLoad"] = figures["emptyLoad"],
        },
    };
}
