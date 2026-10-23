using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The arrastra's grinding (seraphhorizons, Arrastra/; #732). See docs/recipe-browser/schema.md.
/// Its build is the shared ore machines' <c>construction</c> record (<see cref="FillOreMachines"/>).</summary>
public static partial class RecipeSection
{
    public const string ArrastraType = "arrastra";
    public const string ArrastraCode = "seraphhorizons:arrastra-north";
    public static readonly AssetLocation OreProcessingAsset = new(GearChain.Mod, "config/ore-processing.json");

    // Ore/Core/OreRecovery.cs: shaft turns an in-game hour at speed 1, and the reference speed.
    private const double TurnsPerHourAtSpeed1 = 95.5, ReferenceSpeed = 0.6;

    private static void FillArrastra(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (BlockOf(ctx, ArrastraCode) is not { } arrastra)
            return;
        if (ctx.Api.Assets.TryGet(OreProcessingAsset) is not { } asset)
            return;
        JObject spec;
        try
        {
            spec = JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore })["machines"]?["arrastra"] as JObject ?? new JObject();
        }
        catch (Newtonsoft.Json.JsonException e)
        {
            ctx.Api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the arrastra is not exported", OreProcessingAsset, e.Message);
            return;
        }
        int charge = (int?)spec["charge"] ?? 0;
        double hours = (double?)spec["chargeHours"] ?? 0, water = (double?)spec["water"] ?? 0;
        if (charge <= 0 || hours <= 0)
            return;
        double turnsPerRock = TurnsPerHourAtSpeed1 * ReferenceSpeed * hours / charge;
        var waterItem = ctx.Api.World.GetItem(new AssetLocation("game:waterportion")) is { IsMissing: false, Code: not null } w ? w : null;
        var mine = new List<JObject>();
        foreach (var crushed in ctx.Api.World.Items
                     .Where(i => i is { IsMissing: false, Code: { Domain: GearChain.Mod } } && i.Code.Path.StartsWith("crushedore-", StringComparison.Ordinal))
                     .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal))
        {
            var groundCode = new AssetLocation(GearChain.Mod, "groundore-" + crushed.Code.Path["crushedore-".Length..]);
            if (ctx.Api.World.GetItem(groundCode) is not { IsMissing: false, Code: not null } ground)
                continue;
            mine.Add(ArrastraRecord(ctx, crushed, ground, arrastra, waterItem, water, turnsPerRock, charge, hours));
        }
        records.AddRange(mine);
        AddType(types, ArrastraType, "Arrastra", mine.Count, "machine", "OreMachinesSettings", GearChain.Mod);
    }

    /// <summary>
    /// One crushed ore ground in the arrastra: the crushed rock (consumed), its water (role liquid, its
    /// share of a charge's, used up) and the arrastra (role station); one ground ore of the same ore and grade,
    /// nothing lost. A rock's share of a charge's turns (power mechanical); a batch machine, loaded,
    /// watered and drawn by hand (<c>requirements</c>).
    /// </summary>
    private static JObject ArrastraRecord(Context ctx, Item crushed, Item ground, Block arrastra, Item? waterItem, double litres,
        double turnsPerRock, int charge, double hours)
    {
        var ingredients = new JArray(Def(crushed.Code.ToString(), "item", 1));
        var stacks = new JArray { new JArray(Stack(ctx, crushed, 1)) };
        if (litres > 0 && waterItem != null)
        {
            ingredients.Add(Def(waterItem.Code.ToString(), "item", litres * 100, "liquid", litres));
            stacks.Add(new JArray(Stack(ctx, waterItem, litres * 100, litres)));
        }
        ingredients.Add(Def(ArrastraCode, "block", 1, "station"));
        stacks.Add(new JArray(Stack(ctx, arrastra, 1)));
        return new JObject
        {
            ["id"] = $"{ArrastraType}|{crushed.Code}|0",
            ["type"] = ArrastraType,
            ["mod"] = GearChain.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(ground.Code.ToString(), "item", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, ground, 1)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = Num(turnsPerRock),
            },
            ["requirements"] = new JArray(
                $"Batch, by hand: a charge of up to {charge} rock tipped into the pit, its water ({Num(litres * charge)} L a full charge) poured after it by bucket, "
                + $"ground in {Num(hours)} hours at speed 0.6 (a full charge) and drawn out by the gate"),
            ["extra"] = new JObject
            {
                ["charge"] = charge,
                ["chargeHours"] = Num(hours),
                ["lifts"] = "poor ore's × 0.4",
            },
        };
    }
}
