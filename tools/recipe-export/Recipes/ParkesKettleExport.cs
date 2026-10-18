using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One silver-lead bullion the kettle takes: its bar, its ore, and the silver a bar of it carries.</summary>
public sealed record KettleBullion(string Ore, Item Bar, double SilverPerBar);

public sealed class ParkesKettleData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public Item? RichLead;
    public Item? Lead;
    public Item? Zinc;
    public Item? Coal;
    public required double Recovery;
    public required int ChargeUnits;
    public required double Hours;
    public required double ZincReturn;
    public required double ZincPerLead;
    public required double RichLeadSilver;
    public required int CoalPerCharge;
    public required double Load;
    public required double EmptyLoad;
    public List<KettleBullion> Bullion = new();
}

/// <summary>
/// The Parkes kettle (seraphhorizons, ParkesKettle/, #927): its charge, one record per silver-lead bullion it takes
/// (<c>seraphhorizons:bullion-{ore}-lead</c> of an ore whose silver cupellation parts at a share under rich lead's,
/// plain galena's), with what a bar gives: its silver whole (<c>machines.parkeskettle.recovery</c>) as rich lead at
/// <c>bullion.richLeadSilver</c> silver a unit of lead, the rest of its lead desilverized, the zinc it takes (1 unit to
/// 100 of lead) and 80 % of it back (<c>zincReturn</c>), from <c>config/ore-processing.json</c>; the coal a charge
/// burns is the rig's. Null when the mod is not loaded, its switch or OreProcessing is off, or nothing it names is
/// registered.
/// </summary>
public static class ParkesKettleExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/parkeskettle-rig.json");
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:parkeskettle-north";
    public const string RichLeadCode = "seraphhorizons:richlead";
    public const string LeadCode = "game:ingot-lead";
    public const string ZincCode = "game:metalbit-zinc";
    public const string CoalCode = "game:ore-bituminouscoal";
    public const double UnitsPerBar = 100, ZincUnitsPerBit = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static JObject? Json(ICoreServerAPI api, AssetLocation loc)
    {
        try
        {
            return JObject.Parse(api.Assets.Get(loc).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the Parkes kettle: cannot read {0} ({1})", loc, e.Message);
            return null;
        }
    }

    public static ParkesKettleData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "ParkesKettle") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        if (Json(api, RecoveryAsset) is not { } recovery || Json(api, RigAsset) is not { } rig) return null;
        if (recovery["machines"]?["parkeskettle"] is not JObject entry || !((double?)entry["chargeHours"] is > 0 and var hours)) return null;
        var settings = GearChain.Prop(config, "ParkesKettleSettings");
        var data = new ParkesKettleData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            RichLead = ItemOf(api, RichLeadCode),
            Lead = ItemOf(api, LeadCode),
            Zinc = ItemOf(api, ZincCode),
            Coal = ItemOf(api, CoalCode),
            Recovery = (double?)entry["recovery"] ?? 0,
            ChargeUnits = (int?)entry["charge"] ?? 0,
            Hours = hours,
            ZincReturn = (double?)entry["zincReturn"] ?? 0,
            ZincPerLead = GearChain.Prop(settings, "ZincPerLead") is { } z ? Convert.ToDouble(z) : 0.01,
            RichLeadSilver = (double?)recovery["bullion"]?["richLeadSilver"] ?? 0.1,
            CoalPerCharge = (int?)rig["furnace"]?["coalPerCharge"] ?? 0,
            Load = (double?)entry["load"] ?? 0,
            EmptyLoad = (double?)entry["emptyLoad"] ?? 0,
        };
        if (data.RichLead == null || data.Lead == null || data.RichLeadSilver <= 0) return null;
        var ores = recovery["ores"] as JObject ?? new JObject();
        foreach (var (ore, spec) in ores)
        {
            var silver = (spec?["byProducts"] as JArray ?? new JArray()).OfType<JObject>()
                .FirstOrDefault(b => (string?)b["metal"] == "silver" && (string?)b["partedBy"] == "cupellation");
            double share = (double?)silver?["share"] ?? 0;
            // silver-lead whose silver share is under rich lead's (plain galena); richer bullion is cupelled directly
            if (share <= 0 || share >= data.RichLeadSilver) continue;
            if (ItemOf(api, $"{GearChain.Mod}:bullion-{ore}-lead") is { } bar)
                data.Bullion.Add(new KettleBullion(ore, bar, UnitsPerBar * share * data.Recovery));
        }
        data.Bullion.Sort((a, b) => string.CompareOrdinal(a.Ore, b.Ore));
        return data.Bullion.Count == 0 ? null : data;
    }
}
