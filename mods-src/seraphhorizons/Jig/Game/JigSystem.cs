using SeraphHorizons.Mod.Jig.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Jig;

/// <summary>
/// The jig (#919; README "Jig"): registers its block entity and says what it gives. Its frame is the shared
/// ore machine block (<see cref="BlockOreMachine"/>), its cells the shared ones; its figures are ore-core's
/// (<c>config/ore-processing.json</c>, <c>machines.jig</c>). With the <c>Jig</c> switch off, or ore
/// processing off (its concentrate does not exist then), the server leaves its frame and recipe out of the
/// game.
/// </summary>
public class JigSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "jig";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/jig/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/jig.json")];

    /// <summary>Whether the jig is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.Jig && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass("seraphhorizons.Jig", typeof(BEJig));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    /// <summary>
    /// For the recipe browser (<c>tools/recipe-export</c>, by reflection): every item in the world the jig
    /// takes fresh (crushed and ground ore, rich gravel, bog iron), by code, and what one gives on average:
    /// each product's code and its items (fractional, as the jig carries the rest over). A rock the sluice
    /// worked first gives less (its catch is the sluice's); this is the jig's alone. Empty with the jig out
    /// of the game.
    /// </summary>
    public static List<KeyValuePair<string, List<KeyValuePair<string, double>>>> ExportYields(ICoreAPI api)
    {
        var result = new List<KeyValuePair<string, List<KeyValuePair<string, double>>>>();
        if (!Applies(api))
            return result;
        var recovery = OreProcessingSystem.RecoveryFor(api);
        var feeds = new List<CollectibleObject>();
        feeds.AddRange(api.World.Items.Where(i => i?.Code is { Domain: Domain } c
            && (c.Path.StartsWith("crushedore-", StringComparison.Ordinal) || c.Path.StartsWith("groundore-", StringComparison.Ordinal))));
        feeds.AddRange(api.World.Blocks.Where(b => b?.Code != null && PlacerFeed.Parse(b.Code.ToString()) is { Kind: PlacerKind.Gravel }));
        if (api.World.GetItem(new AssetLocation(PlacerFeed.BogIronCode)) is { Id: > 0 } bog)
            feeds.Add(bog);
        foreach (var feed in feeds.Where(f => f is { IsMissing: false }).OrderBy(f => f.Code.ToString(), StringComparer.Ordinal))
        {
            string code = feed.Code.ToString();
            if (SluiceSystem.StreamOf(new ItemStack(feed)) is not { } stream)
                continue;
            var wash = Jigging.Wash(recovery, stream, () => Ore.PlacerTable.For(api, code));
            var outputs = wash.Products
                .Where(p => p.Units > 0 && p.UnitsPerItem > 0)
                .Select(p => new KeyValuePair<string, double>(p.Code, p.Units / p.UnitsPerItem))
                .ToList();
            if (outputs.Count > 0)
                result.Add(new(code, outputs));
        }
        return result;
    }

    /// <summary>For the recipe browser, by reflection: the jig's recovery, rock an in-game hour at speed
    /// 0.6, litres of water a rock, its working load and its axle turns a rock (ore-core's
    /// <c>machines.jig</c>).</summary>
    public static double[] ExportFigures(ICoreAPI api)
    {
        var spec = OreProcessingSystem.RecoveryFor(api).Machine(OreMachine.Jig);
        return [spec.Recovery, spec.ItemsPerHour, spec.WaterPerItem, spec.Load, spec.TurnsPerItem];
    }
}
