using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One bullion the acid parting vessel parts (dore): the bar, and the gold and silver bits a bar gives.</summary>
public sealed record AcidPartingClass(string Ore, Item Dore, Item Gold, double GoldBits, Item? Silver, double SilverBits, double SilverShare);

public sealed class AcidPartingData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public Item? Acid;
    public Item? CopperPlate;
    public required double Recovery;
    public required double PerHour;
    public required double WaterPerUnit;
    public required int ChargeBars;
    public required double AcidPerBar;
    public required double AcidSpent;
    public required double CopperUses;
    public List<AcidPartingClass> Classes = new();
}

/// <summary>
/// The acid parting vessel (seraphhorizons, AcidParting/, #931): one record per bullion it parts (gold quartz's dore,
/// <c>seraphhorizons:bullion-{ore}-{metal}</c>, the cupellation agent's bullion of #740), a bar holding 100 units of
/// its main metal and the ore's by-product at its share: out, the main metal whole as metal bits (5 units each) and
/// the by-product at the vessel's recovery, from <c>config/ore-processing.json</c> (<c>machines.acidparting</c>,
/// <c>ores</c>). The acid a bar spends and the copper plate's wear come from <c>AcidPartingSettings</c> and the build
/// file. Null when the mod is not loaded, its switch or OreProcessing is off, or nothing it names is registered.
/// </summary>
public static class AcidPartingExport
{
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public static readonly AssetLocation BuildAsset = new(GearChain.Mod, "config/builds/acidparting.json");
    public const string FrameCode = "seraphhorizons:acidparting-north";
    public const string AcidCode = "game:acid-full-nitric";
    public const string CopperCode = "game:metalplate-copper";
    public const double UnitsPerBar = 100, UnitsPerBit = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static AcidPartingData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "AcidParting") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject recovery, build;
        try
        {
            var settingsJson = new JsonLoadSettings { CommentHandling = CommentHandling.Ignore };
            recovery = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), settingsJson);
            build = JObject.Parse(api.Assets.Get(BuildAsset).ToText(), settingsJson);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the acid parting vessel: cannot read its config ({0})", e.Message);
            return null;
        }
        if (recovery["machines"]?["acidparting"] is not JObject entry) return null;
        var settings = GearChain.Prop(config, "AcidPartingSettings");
        double Setting(string name, double fallback) => GearChain.Prop(settings, name) is { } v ? Convert.ToDouble(v) : fallback;
        double share = (double?)entry["recovery"] ?? 0;
        var data = new AcidPartingData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            Acid = ItemOf(api, AcidCode),
            CopperPlate = ItemOf(api, CopperCode),
            Recovery = share,
            PerHour = (double?)entry["perHour"] ?? 0,
            WaterPerUnit = (double?)entry["water"] ?? 0,
            ChargeBars = (int)Setting("ChargeBars", 2),
            AcidPerBar = Setting("AcidLitresPerBar", 2),
            AcidSpent = Setting("AcidSpentShare", 0.4),
            CopperUses = (double?)(build["wearing"] as JArray)?.FirstOrDefault()?["uses"] ?? 0,
        };
        var ores = recovery["ores"] as JObject ?? new JObject();
        foreach (var (ore, spec) in ores)
        {
            if (spec?["byProducts"] is not JArray byProducts || byProducts.Count == 0
                || byProducts.Any(b => (string?)b["partedBy"] != "acidparting")) continue;
            var dore = api.World.Items.FirstOrDefault(i => i?.Code is { Domain: GearChain.Mod } c && !i.IsMissing
                                                           && c.Path.StartsWith($"bullion-{ore}-", StringComparison.Ordinal));
            if (dore == null) continue;
            string metal = dore.Code.Path[$"bullion-{ore}-".Length..];
            string byMetal = (string?)byProducts[0]["metal"] ?? "silver";
            double byShare = byProducts.Sum(b => (double?)b["share"] ?? 0);
            if (ItemOf(api, "game:metalbit-" + metal) is not { } gold) continue;
            data.Classes.Add(new AcidPartingClass(ore, dore, gold, UnitsPerBar / UnitsPerBit, ItemOf(api, "game:metalbit-" + byMetal),
                UnitsPerBar * byShare * share / UnitsPerBit, byShare));
        }
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Ore, b.Ore));
        return data.Classes.Count == 0 ? null : data;
    }
}
