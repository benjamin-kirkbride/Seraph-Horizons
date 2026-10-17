using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One feed item washed in the rocker: what an item gives, each output's count an item (fractional:
/// the station carries the rest over to the next item).</summary>
public sealed record RockerWash(CollectibleObject Feed, string Kind, List<(CollectibleObject Output, double PerItem)> Outputs);

public sealed class RockerData
{
    public required string Mod;
    public Block? Station;
    public required double RocksPerItem;
    public required double LitresPerItem;
    public List<RockerWash> Washes = new();
}

/// <summary>
/// The rocker (seraphhorizons, Rocker/, #716): read live from the server, as the other hand stations
/// are. The feed is every crushed and ground ore item (as it comes from spalling and grinding: not
/// riddled, not amalgamated) and every rich gravel block; what an item gives is the mod's own answer,
/// <c>RockerSystem.YieldOf(ItemStack)</c> (ore processing's hand concentration at the rocker, or the
/// gold pan's table at the rocker's rate for placer gravel), called by reflection; the pace and water
/// are RockerSettings (RocksPerItem, LitresPerItem). Null when the mod is not loaded or the rocker is
/// off (its switch or OreProcessing).
/// </summary>
public static class RockerExport
{
    public const string SystemName = "SeraphHorizons.Mod.Rocker.RockerSystem";
    public const string StationCode = "seraphhorizons:rocker-north";

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static RockerData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || GearChain.System(api, SystemName) is not { } rocker)
            return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "Rocker") is not true || GearChain.Prop(config, "OreProcessing") is not true)
            return null;
        var settings = GearChain.Prop(config, "RockerSettings");
        var yieldOf = rocker.GetType().GetMethod("YieldOf", new[] { typeof(ItemStack) });
        if (yieldOf == null)
            return null;
        var data = new RockerData
        {
            Mod = GearChain.Mod,
            Station = api.World.GetBlock(new AssetLocation(StationCode)) is { IsMissing: false, Code: not null } b ? b : null,
            RocksPerItem = Dbl(settings, "RocksPerItem", 4),
            LitresPerItem = Dbl(settings, "LitresPerItem", 2),
        };
        IEnumerable<(CollectibleObject Feed, string Kind)> feeds = api.World.Items
            .Where(i => i?.Code is { Domain: GearChain.Mod } c && !i.IsMissing
                        && (c.Path.StartsWith("crushedore-", StringComparison.Ordinal) || c.Path.StartsWith("groundore-", StringComparison.Ordinal)))
            .Select(i => ((CollectibleObject)i, i.Code.Path.StartsWith("crushed") ? "crushed" : "ground"))
            .Concat(api.World.Blocks
                .Where(bl => bl?.Code is { Domain: "game" } c && !bl.IsMissing && c.Path.StartsWith("richgravel-", StringComparison.Ordinal)
                             && c.Path.Split('-').Length == 2)
                .Select(bl => ((CollectibleObject)bl, "placer")));
        foreach (var (feed, kind) in feeds)
        {
            if (yieldOf.Invoke(rocker, new object[] { new ItemStack(feed) }) is not System.Collections.IEnumerable yields)
                continue;
            var outputs = new List<(CollectibleObject, double)>();
            foreach (var y in yields)
            {
                if (GearChain.Prop(y, "Output") is not string code) continue;
                double units = Dbl(y, "Units", 0), per = Dbl(y, "UnitsPerItem", 1);
                if (!(units > 0) || !(per > 0)) continue;
                var loc = new AssetLocation(code);
                CollectibleObject? c = api.World.GetItem(loc) ?? (CollectibleObject?)api.World.GetBlock(loc);
                if (c == null || c.IsMissing || c.Code == null) continue;
                outputs.Add((c, units / per));
            }
            if (outputs.Count > 0)
                data.Washes.Add(new RockerWash(feed, kind, outputs));
        }
        data.Washes.Sort((a, b) => string.CompareOrdinal(a.Feed.Code.ToString(), b.Feed.Code.ToString()));
        return data.Washes.Count == 0 ? null : data;
    }
}
