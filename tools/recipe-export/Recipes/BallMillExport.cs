using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One crushed ore through the ball mill: the crushed ore and the ground ore it becomes.</summary>
public sealed record BallMillClass(string Ore, string Grade, Item Crushed, Item Ground);

public sealed class BallMillData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>Shaft turns a rock: turns an hour at the reference speed over rock an hour.</summary>
    public required double TurnsPerRock;
    public required double WaterPerRock;
    public List<BallMillClass> Classes = new();
}

/// <summary>
/// The ball mill (seraphhorizons, BallMill/, #915): its process, one record per crushed ore
/// (<c>seraphhorizons:crushedore-{ore}-{grade}</c>) and the ground ore it grinds to
/// (<c>groundore-{ore}-{grade}</c>), with its rate and water from <c>config/ore-processing.json</c>'s
/// <c>machines.ballmill</c> (perHour, water). Null when the mod is not loaded, its switch or
/// OreProcessing is off, or nothing it names is registered.
/// </summary>
public static class BallMillExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/ballmill-rig.json");
    public static readonly AssetLocation RecoveryAsset = new(GearChain.Mod, "config/ore-processing.json");
    public const string FrameCode = "seraphhorizons:ballmill-north";
    public const string CrushedPrefix = "crushedore-";

    /// <summary>OreRecovery.TurnsPerHourAtSpeed1 × ReferenceSpeed: 57.3 turns an hour.</summary>
    public const double TurnsPerHourAtReference = 95.5 * 0.6;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    public static BallMillData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "BallMill") is not true || GearChain.Prop(config, "OreProcessing") is not true) return null;
        JObject? entry;
        try
        {
            entry = JObject.Parse(api.Assets.Get(RecoveryAsset).ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore })["machines"]?["ballmill"] as JObject;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] the ball mill: cannot read {0} ({1})", RecoveryAsset, e.Message);
            return null;
        }
        double perHour = (double?)entry?["perHour"] ?? 0;
        if (!(perHour > 0)) return null;
        var data = new BallMillData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            TurnsPerRock = TurnsPerHourAtReference / perHour,
            WaterPerRock = (double?)entry?["water"] ?? 0,
        };
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { Domain: GearChain.Mod } code || item.IsMissing || !code.Path.StartsWith(CrushedPrefix, StringComparison.Ordinal)) continue;
            var rest = code.Path[CrushedPrefix.Length..];
            int dash = rest.LastIndexOf('-');
            if (dash <= 0) continue;
            string ore = rest[..dash], grade = rest[(dash + 1)..];
            if (ItemOf(api, $"{GearChain.Mod}:groundore-{ore}-{grade}") is { } ground)
                data.Classes.Add(new BallMillClass(ore, grade, item, ground));
        }
        data.Classes.Sort((a, b) => string.CompareOrdinal(a.Crushed.Code.ToString(), b.Crushed.Code.ToString()));
        return data.Classes.Count == 0 ? null : data;
    }
}
