using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ShakingTable.Core;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.ShakingTable;

/// <summary>
/// The shaking table (#921; README "Shaking table"): registers its block entity and says what it washes. Its
/// frame is the shared machine block (<see cref="BlockMachine"/>), its cells the shared ones; its figures
/// are ore-core's (<c>config/ore-processing.json</c>, <c>machines.table</c>). With the <c>ShakingTable</c>
/// switch off, or ore processing off (its concentrate does not exist then), the server leaves its frame and
/// recipe out of the game.
/// </summary>
public class ShakingTableSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "shakingtable";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/shakingtable/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/shakingtable.json")];

    /// <summary>Whether the shaking table is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.ShakingTable && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass("seraphhorizons.ShakingTable", typeof(BEShakingTable));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            MachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    /// <summary>Whether the table washes <paramref name="stack"/>: what the sluice and the jig wash (crushed or
    /// ground ore, rich gravel, bog iron), fresh or as the tailings before it.</summary>
    public static bool TakesFeed(ItemStack stack) => SluiceSystem.StreamOf(stack) != null;

    /// <summary>
    /// For the recipe browser (<c>tools/recipe-export</c>, by reflection): every crushed and ground ore in the
    /// world, by code, and what one fresh item gives on the table on average: its concentrate (items,
    /// fractional, as the table carries the rest over), and, when its feed has a locked share, one middlings
    /// item (the same item, carrying the locked units). Empty with the table out of the game.
    /// </summary>
    public static List<KeyValuePair<string, List<KeyValuePair<string, double>>>> ExportYields(ICoreAPI api)
    {
        var result = new List<KeyValuePair<string, List<KeyValuePair<string, double>>>>();
        if (!Applies(api))
            return result;
        var recovery = OreProcessingSystem.RecoveryFor(api);
        var feeds = api.World.Items.Where(i => i is { IsMissing: false, Code: { Domain: Domain } c }
                && (c.Path.StartsWith("crushedore-", StringComparison.Ordinal) || c.Path.StartsWith("groundore-", StringComparison.Ordinal)))
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            if (SluiceSystem.StreamOf(new ItemStack(feed)) is not { Units: > 0 } stream)
                continue;
            var outlets = ShakingTabling.Wash(recovery, stream);
            var outputs = outlets.Concentrate
                .Where(p => p.Units > 0 && p.UnitsPerItem > 0)
                .Select(p => new KeyValuePair<string, double>(p.Code, p.Units / p.UnitsPerItem))
                .ToList();
            if (outlets.Middlings != null)
                outputs.Add(new(feed.Code.ToString(), 1));
            if (outputs.Count > 0)
                result.Add(new(feed.Code.ToString(), outputs));
        }
        return result;
    }

    /// <summary>For the recipe browser, by reflection: the table's recovery, rock an in-game hour at the
    /// reference speed, litres of water a rock, its working and empty loads and the axle's turns a rock
    /// (ore-core's <c>machines.table</c>).</summary>
    public static double[] ExportFigures(ICoreAPI api)
    {
        var spec = OreProcessingSystem.RecoveryFor(api).Machine(OreMachine.Table);
        return [spec.Recovery, spec.ItemsPerHour, spec.WaterPerItem, spec.Load, spec.EmptyLoad, spec.TurnsPerItem];
    }
}
