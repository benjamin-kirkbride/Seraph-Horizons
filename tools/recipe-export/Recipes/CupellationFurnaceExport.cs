using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One bar the cupellation furnace parts: the bar (base bullion of an ore, or rich lead), whether it wants a lead
/// ingot added, and what one bar gives, in items (fractions: the furnace draws the last item at that chance).</summary>
public sealed record CupellationFurnaceClass(Item Bar, string? Ore, bool WantsLead, double Litharge, double Silver, double Copper);

public sealed class CupellationFurnaceData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public Item? Lead;
    public Item? Litharge;
    public Item? SilverBit;
    public Item? CopperBit;
    public required double Recovery;
    public required double PerHour;
    public required double ChargeUnits;
    public required double CoalPerHour;
    public List<CupellationFurnaceClass> Classes = new();
}

/// <summary>
/// The cupellation furnace (seraphhorizons, CupellationFurnace/, #740): one record per bar it parts, the base bullion
/// of every ore cupellation parts (<c>seraphhorizons:bullion-{ore}-{metal}</c>: silver-lead, argentiferous copper with a
/// lead ingot added, cupriferous silver with one) and the Parkes kettle's rich lead (<c>seraphhorizons:richlead</c>).
/// A bar is 100 units of its ore's main metal and carries its by-product at the ore's share; the lead (the bar's and
/// the ingot's) goes to litharge, 5 units an item; the by-product at the furnace's recovery (rich lead's silver at its
/// <c>bullion.richLeadSilver</c>, whole), silver and copper as metal bits. From <c>config/ore-processing.json</c>
/// (<c>machines.cupellationfurnace</c>, <c>ores</c>, <c>bullion</c>). Null when the mod is not loaded, its switch or
/// OreProcessing is off, or nothing it names is registered.
/// </summary>
public static class CupellationFurnaceExport
{
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:cupellationfurnace-north";
    public const string LeadCode = "game:ingot-lead";
    public const string LithargeCode = "seraphhorizons:litharge";
    public const string RichLeadCode = "seraphhorizons:richlead";
    public const string SilverBitCode = "game:metalbit-silver";
    public const string CopperBitCode = "game:metalbit-copper";
    public const double BarUnits = 100, ItemUnits = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static CupellationFurnaceData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "CupellationFurnace") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject recovery;
        try
        {
            recovery = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the cupellation furnace: cannot read {0} ({1})", RecoveryAsset, e.Message);
            return null;
        }
        if (recovery["machines"]?["cupellationfurnace"] is not JObject entry) return null;
        var settings = GearChain.Prop(config, "CupellationFurnaceSettings");
        double Setting(string name, double fallback) => GearChain.Prop(settings, name) is { } v ? Convert.ToDouble(v) : fallback;
        double share = (double?)entry["recovery"] ?? 0;
        var data = new CupellationFurnaceData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            Lead = ItemOf(api, LeadCode),
            Litharge = ItemOf(api, LithargeCode),
            SilverBit = ItemOf(api, SilverBitCode),
            CopperBit = ItemOf(api, CopperBitCode),
            Recovery = share,
            PerHour = (double?)entry["perHour"] ?? 0,
            ChargeUnits = Setting("ChargeUnits", 200),
            CoalPerHour = Setting("CoalPerHour", 0.7),
        };
        double leadPerOther = Setting("LeadPerOtherUnit", 1);
        var ores = recovery["ores"] as JObject ?? new JObject();
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || item.IsMissing || !code.Path.StartsWith("bullion-", StringComparison.Ordinal)) continue;
            var rest = code.Path["bullion-".Length..];
            int dash = rest.LastIndexOf('-');
            if (dash <= 0) continue;
            string ore = rest[..dash], metal = rest[(dash + 1)..];
            if (ores[ore]?["byProducts"] is not JArray byProducts) continue;
            var cupelled = byProducts.OfType<JObject>().Where(b => (string?)b["partedBy"] == "cupellation").ToList();
            if (cupelled.Count == 0) continue;
            bool lead = metal == "lead";
            double silver = metal == "silver" ? BarUnits : 0, copper = metal == "copper" ? BarUnits : 0;
            foreach (var b in cupelled)
            {
                double units = BarUnits * ((double?)b["share"] ?? 0) * share;
                if ((string?)b["metal"] == "silver") silver += units;
                else if ((string?)b["metal"] == "copper") copper += units;
            }
            double leadUnits = BarUnits + (lead ? 0 : BarUnits * leadPerOther);
            data.Classes.Add(new CupellationFurnaceClass(item, ore, !lead, leadUnits / ItemUnits, silver / ItemUnits, copper / ItemUnits));
        }
        if (ItemOf(api, RichLeadCode) is { } rich)
        {
            double richSilver = (double?)recovery["bullion"]?["richLeadSilver"] ?? 0.1;
            data.Classes.Add(new CupellationFurnaceClass(rich, null, false, BarUnits / ItemUnits, BarUnits * richSilver / ItemUnits, 0));
        }
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Bar.Code.ToString(), b.Bar.Code.ToString()));
        return data.Classes.Count == 0 || data.Litharge == null ? null : data;
    }
}
