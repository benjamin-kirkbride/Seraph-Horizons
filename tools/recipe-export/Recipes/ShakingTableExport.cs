using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One feed of the shaking table and what one item of it gives on average: its concentrate (fractional
/// items) and, when its feed has a locked share, its middlings (the feed item, one).</summary>
public sealed record ShakingTableFeed(CollectibleObject Feed, List<(CollectibleObject Output, double Items)> Concentrate,
    CollectibleObject? Middlings);

public sealed class ShakingTableData
{
    public Block? Frame;
    public required double Recovery;
    public required double PerHour;
    public required double WaterPerItem;
    public required double Load;
    public required double EmptyLoad;
    public required double TurnsPerItem;
    public List<ShakingTableFeed> Feeds = new();
}

/// <summary>
/// The shaking table (seraphhorizons, ShakingTable/, #921): what it washes, read from the mod by reflection
/// (<c>ShakingTableSystem.ExportYields</c>, the recovery core's own pass on each crushed and ground ore, and
/// <c>ShakingTableSystem.ExportFigures</c>: recovery, rock an hour, water a rock, loads, turns a rock), as
/// <see cref="SluiceExport"/> reads the sluice. Null when the mod is not loaded or the table is out of the game
/// (its switch or ore processing off).
/// </summary>
public static class ShakingTableExport
{
    public const string SystemType = "SeraphHorizons.Mod.ShakingTable.ShakingTableSystem";
    public const string FrameCode = "seraphhorizons:shakingtable-north";
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/shakingtable-rig.json");

    private static CollectibleObject? Of(ICoreServerAPI api, string code)
    {
        var loc = new AssetLocation(code);
        if (api.World.GetItem(loc) is { IsMissing: false, Code: not null } item)
            return item;
        return api.World.GetBlock(loc) is { IsMissing: false, Id: > 0, Code: not null } block ? block : null;
    }

    public static ShakingTableData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, SystemType) is not { } system)
            return null;
        var type = system.GetType();
        var yields = type.GetMethod("ExportYields", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [api])
            as List<KeyValuePair<string, List<KeyValuePair<string, double>>>>;
        var figures = type.GetMethod("ExportFigures", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [api]) as double[];
        if (yields == null || yields.Count == 0 || figures is not { Length: 6 })
            return null;
        var data = new ShakingTableData
        {
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Id: > 0 } frame ? frame : null,
            Recovery = figures[0],
            PerHour = figures[1],
            WaterPerItem = figures[2],
            Load = figures[3],
            EmptyLoad = figures[4],
            TurnsPerItem = figures[5],
        };
        foreach (var (feedCode, outputs) in yields)
        {
            if (Of(api, feedCode) is not { } feed)
                continue;
            // the feed item among the outputs is its middlings
            var concentrate = outputs.Where(o => o.Key != feedCode).Select(o => (Of(api, o.Key), o.Value))
                .Where(o => o.Item1 != null && o.Value > 0).Select(o => (o.Item1!, o.Value)).ToList();
            var middlings = outputs.Any(o => o.Key == feedCode) ? feed : null;
            if (concentrate.Count > 0 || middlings != null)
                data.Feeds.Add(new ShakingTableFeed(feed, concentrate, middlings));
        }
        return data.Feeds.Count > 0 ? data : null;
    }
}
