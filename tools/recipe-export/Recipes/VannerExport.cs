using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One feed of the vanner and what one item of it gives on average (fractional items).</summary>
public sealed record VannerFeed(CollectibleObject Feed, List<(CollectibleObject Output, double Items)> Outputs);

public sealed class VannerData
{
    public Block? Frame;
    public required double Recovery;
    public required double PerHour;
    public required double WaterPerItem;
    /// <summary>Shaft turns a rock, at any speed.</summary>
    public required double TurnsPerItem;
    public List<VannerFeed> Feeds = new();
}

/// <summary>
/// The vanner (seraphhorizons, Vanner/, #923): what it washes, read from the mod by reflection
/// (<c>VannerSystem.ExportYields</c>, the recovery core's own pass on each feed item, and
/// <c>VannerSystem.ExportFigures</c>: recovery, rock an hour at speed 0.6, water a rock, turns a rock), as
/// <see cref="SluiceExport"/> reads the sluice. Null when the mod is not loaded or the vanner is out of the
/// game (its switch or ore processing off).
/// </summary>
public static class VannerExport
{
    public const string SystemType = "SeraphHorizons.Mod.Vanner.VannerSystem";
    public const string FrameCode = "seraphhorizons:vanner-north";

    private static CollectibleObject? Of(ICoreServerAPI api, string code)
    {
        var loc = new AssetLocation(code);
        if (api.World.GetItem(loc) is { IsMissing: false, Code: not null } item)
            return item;
        return api.World.GetBlock(loc) is { IsMissing: false, Id: > 0, Code: not null } block ? block : null;
    }

    public static VannerData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, SystemType) is not { } system)
            return null;
        var type = system.GetType();
        var yields = type.GetMethod("ExportYields", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [api])
            as List<KeyValuePair<string, List<KeyValuePair<string, double>>>>;
        var figures = type.GetMethod("ExportFigures", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [api]) as double[];
        if (yields == null || yields.Count == 0 || figures is not { Length: 4 })
            return null;
        var data = new VannerData
        {
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Id: > 0 } frame ? frame : null,
            Recovery = figures[0],
            PerHour = figures[1],
            WaterPerItem = figures[2],
            TurnsPerItem = figures[3],
        };
        foreach (var (feedCode, outputs) in yields)
        {
            if (Of(api, feedCode) is not { } feed)
                continue;
            var resolved = outputs.Select(o => (Of(api, o.Key), o.Value)).Where(o => o.Item1 != null && o.Value > 0)
                .Select(o => (o.Item1!, o.Value)).ToList();
            if (resolved.Count > 0)
                data.Feeds.Add(new VannerFeed(feed, resolved));
        }
        return data.Feeds.Count > 0 ? data : null;
    }
}
