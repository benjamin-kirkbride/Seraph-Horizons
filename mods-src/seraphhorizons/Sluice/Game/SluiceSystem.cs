using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Sluice;

/// <summary>
/// The sluice (#734; README "Sluice"): registers its block entity and says what it washes. Its frame is the
/// shared ore machine block (<see cref="BlockOreMachine"/>), its cells the shared ones. With the
/// <c>Sluice</c> switch off, or ore processing off (its concentrate does not exist then), the server leaves
/// its frame and recipe out of the game. With it on, bog iron joins the line (#701, the owner's ruling of
/// 2026-10-14): its block drops one stone, and the stone no longer smelts to a bloom raw
/// (<see cref="BogIronOnTheLine"/>).
/// </summary>
public class SluiceSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "sluice";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/sluice/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/sluice.json")];

    public static readonly AssetLocation BogIronBlock = new("game", "bogiron");

    /// <summary>Whether the sluice is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.Sluice && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass("seraphhorizons.Sluice", typeof(BESluice));

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && Applies(api))
            BogIronOnTheLine(api);
    }

    /// <summary>
    /// Bog iron as a placer feed (#701): Geology Additions' <c>bogiron</c> block drops exactly one
    /// <c>stone-bogiron</c> (about 1.1 before, the ruling's "one item a block", <see cref="PlacerLine.BogIronUnits"/>
    /// units), and that stone loses its <c>combustibleProps</c> (8 to an iron bloom), so it is washed, not
    /// smelted raw. On the server, before the types go to clients. Without Geology Additions there is none.
    /// </summary>
    public static void BogIronOnTheLine(ICoreAPI api)
    {
        if (api.World.GetItem(new AssetLocation(PlacerFeed.BogIronCode)) is { Id: > 0 } stone)
            stone.CombustibleProps = null;
        if (api.World.GetBlock(BogIronBlock) is { Id: > 0 } block && block.Drops != null)
            foreach (var drop in block.Drops)
                if (drop.Code?.ToString() == PlacerFeed.BogIronCode)
                    drop.Quantity = NatFloat.createUniform(1, 0);
    }

    /// <summary>The stream an item washed on the line carries: crushed or ground ore its own; rich gravel and
    /// bog iron a fresh placer stream, or the one their tailings carry; null for anything else.</summary>
    public static OreStream? StreamOf(ItemStack stack)
    {
        var item = stack.Collectible;
        if (item?.Code == null)
            return null;
        if (PlacerFeed.Parse(item.Code.ToString()) is { } placer)
        {
            if (stack.Attributes.HasAttribute(OreStreamAttributes.Ore) && OreStreamAttributes.Read(stack) is { } carried && PlacerLine.IsPlacer(carried))
                return carried;
            return placer.Kind == PlacerKind.Gravel ? PlacerLine.Gravel(placer.Rock) : PlacerLine.BogIron();
        }
        return OreStreamAttributes.FormOf(item) is OreForm.Crushed or OreForm.Ground ? OreStreamAttributes.Read(stack) : null;
    }

    /// <summary>Whether the sluice washes <paramref name="stack"/>.</summary>
    public static bool TakesFeed(ItemStack stack) => StreamOf(stack) != null;

    /// <summary>
    /// For the recipe browser (<c>tools/recipe-export</c>, by reflection): every item in the world the
    /// sluice takes fresh (crushed and ground ore, rich gravel, bog iron), by code, and what one gives on
    /// average: each product's code and its items (fractional, as the sluice carries the rest over). Empty
    /// with the sluice out of the game.
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
            if (StreamOf(new ItemStack(feed)) is not { } stream)
                continue;
            var wash = ConcentratorWash.Wash(recovery, OreMachine.Sluice, stream, () => Ore.PlacerTable.For(api, code));
            var outputs = wash.Products
                .Where(p => p.Units > 0 && p.UnitsPerItem > 0)
                .Select(p => new KeyValuePair<string, double>(p.Code, p.Units / p.UnitsPerItem))
                .ToList();
            if (outputs.Count > 0)
                result.Add(new(code, outputs));
        }
        return result;
    }

    /// <summary>For the recipe browser, by reflection: the sluice's recovery, rock an in-game hour and
    /// litres of water a rock (ore-core's <c>machines.sluice</c>).</summary>
    public static double[] ExportFigures(ICoreAPI api)
    {
        var spec = OreProcessingSystem.RecoveryFor(api).Machine(OreMachine.Sluice);
        return [spec.Recovery, spec.ItemsPerHour, spec.WaterPerItem];
    }

    /// <summary>A few of what it takes, for the interaction help.</summary>
    public static ItemStack[] FeedExamples(IWorldAccessor world)
    {
        var found = new List<CollectibleObject>();
        found.AddRange(world.SearchItems(new AssetLocation(Domain, "crushedore-*")).Take(3));
        found.AddRange(world.SearchBlocks(new AssetLocation("game", "richgravel-*")).Where(b => PlacerFeed.Parse(b.Code.ToString()) != null).Take(2));
        if (world.GetItem(new AssetLocation(PlacerFeed.BogIronCode)) is { Id: > 0 } bog)
            found.Add(bog);
        return found.Where(c => c is { Id: > 0, IsMissing: false }).Select(c => new ItemStack(c)).ToArray();
    }
}
