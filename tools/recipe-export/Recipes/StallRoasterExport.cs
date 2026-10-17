using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One sulfide rock item kind roasted in the stall: its items (every host rock an alternative for raw ore),
/// the stall-roasted rock it becomes, one for one, and the sulfur powder a rock leaves in the crust.</summary>
public sealed record StallRoastClass(string Id, string Ore, string Grade, string Form, List<Item> Inputs, Item Roasted, double Sulfur);

public sealed class StallRoasterData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public Item? SulfurPowder;
    public Item? Firewood;
    public required double Recovery;
    public required int Charge;
    public required double Hours;
    public required int BedFirewood;
    public List<StallRoastClass> Classes = new();
}

/// <summary>
/// The stall roaster (seraphhorizons, StallRoaster/, #925): its burn, one record per sulfide rock item kind it takes
/// (vanilla's raw ore by grade and ore, every host rock an alternative, and the pack's crushed ore), and the
/// stall-roasted rock (<c>seraphhorizons:stallroastedore-{ore}-{grade}</c>) it gives one for one, with the sulfur
/// powder a rock leaves: its units × the ore's sulfur a unit × the stall's share / the units in a powder, from
/// <c>config/ore-processing.json</c> (<c>machines.stallroaster</c>, <c>ores</c>, <c>sulfurUnitsPerPowder</c>). Null
/// when the mod is not loaded, its switch or OreProcessing is off, or nothing it names is registered.
/// </summary>
public static class StallRoasterExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/stallroaster-rig.json");
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:stallroaster-north";
    public const string SulfurCode = "game:powder-sulfur";
    public const string FirewoodCode = "game:firewood";

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Units(Item item) => item.Attributes?["metalUnits"].AsDouble(0) ?? 0;

    public static StallRoasterData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "StallRoaster") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject recovery;
        try
        {
            recovery = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the stall roaster: cannot read {0} ({1})", RecoveryAsset, e.Message);
            return null;
        }
        if (recovery["machines"]?["stallroaster"] is not JObject entry || !((double?)entry["chargeHours"] is > 0 and var hours)) return null;
        double share = (double?)entry["sulfur"] ?? 0, perPowder = (double?)recovery["sulfurUnitsPerPowder"] ?? 5;
        var settings = GearChain.Prop(config, "StallRoasterSettings");
        var data = new StallRoasterData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            SulfurPowder = ItemOf(api, SulfurCode),
            Firewood = ItemOf(api, FirewoodCode),
            Recovery = (double?)entry["recovery"] ?? 0,
            Charge = (int?)entry["charge"] ?? 0,
            Hours = hours,
            BedFirewood = GearChain.Prop(settings, "BedFirewood") is { } bed ? Convert.ToInt32(bed) : 4,
        };
        var ores = recovery["ores"] as JObject ?? new JObject();
        bool Sulfide(string ore) => (string?)ores[ore]?["class"] == "sulfide";
        double SulfurOf(string ore, double units) =>
            perPowder > 0 ? units * ((double?)ores[ore]?["sulfur"] ?? 0) * share / perPowder : 0;

        // raw ore, by grade and ore, every host rock an alternative (the jaw breaker's and spalling's classes)
        if (SpallingExport.Read(api) is { } spall)
            foreach (var k in spall.Classes.Where(k => k.FirstPart == "ore" && Sulfide(k.Ore)))
                if (ItemOf(api, $"{GearChain.Mod}:stallroastedore-{k.Ore}-{k.Grade}") is { } roasted)
                    data.Classes.Add(new StallRoastClass(k.Pattern, k.Ore, k.Grade, "raw", k.Inputs, roasted, SulfurOf(k.Ore, Units(roasted))));
        // crushed ore
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || item.IsMissing || !code.Path.StartsWith("crushedore-", StringComparison.Ordinal)) continue;
            var rest = code.Path["crushedore-".Length..];
            int dash = rest.LastIndexOf('-');
            if (dash <= 0) continue;
            string ore = rest[..dash], grade = rest[(dash + 1)..];
            if (Sulfide(ore) && ItemOf(api, $"{GearChain.Mod}:stallroastedore-{ore}-{grade}") is { } roasted)
                data.Classes.Add(new StallRoastClass(code.ToString(), ore, grade, "crushed", [item], roasted, SulfurOf(ore, Units(roasted))));
        }
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return data.Classes.Count == 0 ? null : data;
    }
}
