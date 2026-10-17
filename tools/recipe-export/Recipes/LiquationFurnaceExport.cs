using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One tin-lead bar the furnace parts: its ore, the bar, the metal it runs off (tin, a whole ingot a bar) and
/// the by-product it leaves on the plate (lead), in units and as the game's metal bits.</summary>
public sealed record LiquationFurnaceBar(string Ore, Item Bar, Item Ingot, double MetalUnits, Item? Bit, double ByProductUnits);

public sealed class LiquationFurnaceData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    public required double Recovery;
    public required double PerHour;
    public required int ChargeBars;
    public required double FuelPerHour;
    public List<LiquationFurnaceBar> Bars = new();
}

/// <summary>
/// The liquation furnace (seraphhorizons, LiquationFurnace/, #929): one record per tin-lead bullion bar it parts
/// (<c>seraphhorizons:bullion-{ore}-tin</c>, teallite's and franckeite's): the bar's tin whole, an ingot, and its lead
/// at the ore's share × the furnace's recovery, as the game's lead bits. From <c>config/ore-processing.json</c>
/// (<c>machines.liquationfurnace</c>, <c>ores</c>) and the mod's config (<c>LiquationFurnaceSettings</c>). Null when
/// the mod is not loaded, its switch or OreProcessing is off, or no bar is registered.
/// </summary>
public static class LiquationFurnaceExport
{
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:liquationfurnace-north";

    /// <summary>Units of the main metal a bar holds (the mod's <c>Bullion.UnitsPerItem</c>), and of metal in a bit.</summary>
    public const double UnitsPerBar = 100, UnitsPerBit = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static LiquationFurnaceData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "LiquationFurnace") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject recovery;
        try
        {
            recovery = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the liquation furnace: cannot read {0} ({1})", RecoveryAsset, e.Message);
            return null;
        }
        if (recovery["machines"]?["liquationfurnace"] is not JObject entry) return null;
        var settings = GearChain.Prop(config, "LiquationFurnaceSettings");
        var data = new LiquationFurnaceData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            Recovery = (double?)entry["recovery"] ?? 0,
            PerHour = (double?)entry["perHour"] ?? 0,
            ChargeBars = GearChain.Prop(settings, "ChargeBars") is { } bars ? Convert.ToInt32(bars) : 4,
            FuelPerHour = GearChain.Prop(settings, "FuelPerHour") is { } fuel ? Convert.ToDouble(fuel) : 0.36,
        };
        if (recovery["ores"] is not JObject ores) return null;
        foreach (var (ore, spec) in ores)
        {
            if (spec?["byProducts"] is not JArray byProducts) continue;
            foreach (var bp in byProducts)
            {
                if ((string?)bp["partedBy"] != "liquation") continue;
                string byMetal = (string?)bp["metal"] ?? "";
                double share = (double?)bp["share"] ?? 0;
                // the bar's main metal is the last part of its code, as the mod's Bullion.Code writes it
                var bar = api.World.Items.FirstOrDefault(i => i?.Code is { Domain: GearChain.Mod } c && !i.IsMissing
                    && c.Path.StartsWith($"bullion-{ore}-", StringComparison.Ordinal) && c.Path.Count(ch => ch == '-') == 2);
                if (bar == null) continue;
                string metal = bar.Code.Path[(bar.Code.Path.LastIndexOf('-') + 1)..];
                if (ItemOf(api, "game:ingot-" + metal) is not { } ingot) continue;
                data.Bars.Add(new LiquationFurnaceBar(ore, bar, ingot, UnitsPerBar, ItemOf(api, "game:metalbit-" + byMetal),
                    UnitsPerBar * share * data.Recovery));
            }
        }
        data.Bars.Sort((a, b) => string.CompareOrdinal(a.Ore, b.Ore));
        return data.Bars.Count == 0 ? null : data;
    }
}
