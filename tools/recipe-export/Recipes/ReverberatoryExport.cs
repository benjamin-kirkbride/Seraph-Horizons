using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One item through the reverberatory roaster: a sulfide's concentrate and its roasted concentrate, or native
/// sulfur rock and its sulfur powder; how much of the output an item gives, and its weight (concentrates of work,
/// water and coal it takes).</summary>
public sealed record ReverberatoryClass(string Ore, Item Input, Item Output, double Amount, double Weight, string Form);

public sealed class ReverberatoryData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>Shaft turns a concentrate: turns an hour at the reference speed over concentrate an hour.</summary>
    public required double TurnsPerConcentrate;
    public required double WaterPerConcentrate;
    public required double Recovery;
    public required double ConcentratePerCoal;
    public required int HearthCharge;
    public List<ReverberatoryClass> Classes = new();
}

/// <summary>
/// The reverberatory roaster (seraphhorizons, ReverberatoryRoaster/, #736): its process, one record per sulfide's
/// concentrate (<c>seraphhorizons:concentrate-{ore}</c>) and the roasted concentrate it gives one for one, and one for
/// native sulfur rock (<c>game:ore-sulfur</c>) and the sulfur powder it melts into, with its rate, water and melt from
/// <c>config/ore-processing.json</c>'s <c>machines.reverberatory</c> (perHour, water, recovery, nativeSulfur) and
/// <c>nativeSulfurUnits</c>, <c>concentrateUnits</c>, <c>sulfurUnitsPerPowder</c>, and its coal from the mod's
/// <c>ReverberatorySettings</c>. Null when the mod is not loaded, its switch or OreProcessing is off, or nothing it
/// names is registered.
/// </summary>
public static class ReverberatoryExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/reverberatory-rig.json");
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:reverberatory-north";
    public const string NativeSulfurCode = "game:ore-sulfur";
    public const string SulfurCode = "game:powder-sulfur";

    /// <summary>OreRecovery.TurnsPerHourAtSpeed1 × ReferenceSpeed: 57.3 turns an hour.</summary>
    public const double TurnsPerHourAtReference = 95.5 * 0.6;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static ReverberatoryData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "ReverberatoryRoaster") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject recovery;
        try
        {
            recovery = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the reverberatory roaster: cannot read {0} ({1})", RecoveryAsset, e.Message);
            return null;
        }
        if (recovery["machines"]?["reverberatory"] is not JObject entry || !((double?)entry["perHour"] is > 0 and var perHour)) return null;
        var settings = GearChain.Prop(config, "ReverberatorySettings");
        double share = (double?)entry["recovery"] ?? 1;
        double concUnits = (double?)recovery["concentrateUnits"] ?? 5, perPowder = (double?)recovery["sulfurUnitsPerPowder"] ?? 5;
        double nativeUnits = (double?)recovery["nativeSulfurUnits"] ?? 0, melt = (double?)entry["nativeSulfur"] ?? 0;
        var data = new ReverberatoryData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            TurnsPerConcentrate = TurnsPerHourAtReference / perHour,
            WaterPerConcentrate = (double?)entry["water"] ?? 0,
            Recovery = share,
            ConcentratePerCoal = GearChain.Prop(settings, "ConcentratePerCoal") is { } c ? Convert.ToDouble(c) : 6,
            HearthCharge = GearChain.Prop(settings, "HearthCharge") is { } h ? Convert.ToInt32(h) : 32,
        };
        var ores = recovery["ores"] as JObject ?? new JObject();
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || item.IsMissing || !code.Path.StartsWith("concentrate-", StringComparison.Ordinal)) continue;
            string ore = code.Path["concentrate-".Length..];
            if ((string?)ores[ore]?["class"] != "sulfide") continue;
            if (ItemOf(api, $"{GearChain.Mod}:roastedconcentrate-{ore}") is { } roasted)
                data.Classes.Add(new ReverberatoryClass(ore, item, roasted, share, 1, "concentrate"));
        }
        if (nativeUnits > 0 && melt > 0 && perPowder > 0 && concUnits > 0
            && ItemOf(api, NativeSulfurCode) is { } native && ItemOf(api, SulfurCode) is { } powder)
            data.Classes.Add(new ReverberatoryClass("sulfur", native, powder, nativeUnits * melt / perPowder, nativeUnits / concUnits, "nativesulfur"));
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Input.Code.ToString(), b.Input.Code.ToString()));
        return data.Classes.Count == 0 ? null : data;
    }
}
