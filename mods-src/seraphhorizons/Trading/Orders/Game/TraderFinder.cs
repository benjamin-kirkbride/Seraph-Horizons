using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Orders;

/// <summary>Finding the pack's traders and paying gears, for orders and deliveries (#453, #454).
/// A trader is known by its standing id (<see cref="TradingSystem.Standing"/>'s <c>TraderIdOf</c>:
/// <c>camp:x,z</c> or <c>entity:n</c>), so it need not be loaded for the books.</summary>
public static class TraderFinder
{
    /// <summary>How near a player must stand to a trader to deal with it by command.</summary>
    public const double NearRange = 8;

    public static IEnumerable<EntitySeraphTrader> Loaded(ICoreServerAPI api) =>
        api.World.LoadedEntities.Values.OfType<EntitySeraphTrader>().Where(t => t.State != EnumEntityState.Despawned && t.Alive).ToList();

    public static string IdOf(ICoreServerAPI api, EntitySeraphTrader trader) =>
        TradingSystem.Of(api)?.Standing.TraderIdOf(trader) ?? TraderIds.Entity(trader.EntityId);

    public static EntitySeraphTrader? ById(ICoreServerAPI api, string id) => Loaded(api).FirstOrDefault(t => IdOf(api, t) == id);

    public static EntitySeraphTrader? Nearest(ICoreServerAPI api, Vec3d pos, double range = NearRange) =>
        Loaded(api).Select(t => (T: t, D: t.Pos.DistanceTo(pos))).Where(x => x.D <= range).OrderBy(x => x.D).Select(x => x.T).FirstOrDefault();

    /// <summary>A trader an admin command names: <c>near</c> (the nearest to the caller within 16
    /// blocks) or its id; loaded traders only.</summary>
    public static EntitySeraphTrader? Named(ICoreServerAPI api, string word, Entity? caller) =>
        word == "near" ? caller is null ? null : Nearest(api, caller.Pos.XYZ, 16) : ById(api, word);

    public static void GiveGears(ICoreAPI api, EntityAgent to, int gears)
    {
        if (gears <= 0) return;
        InventoryTrader.GiveOrDrop(to, new ItemStack(api.World.GetItem(new AssetLocation("game:gear-rusty")), 1), gears, null);
    }

    /// <summary>Takes up to <paramref name="gears"/> from the trader's wallet; returns what it took.</summary>
    public static int TakeFromWallet(EntitySeraphTrader trader, int gears)
    {
        int take = Math.Min(Math.Max(0, gears), trader.Inventory.GetTraderAssets());
        if (take > 0) trader.Inventory.DeductFromTrader(take);
        return take;
    }

    public static void ReturnToWallet(EntitySeraphTrader trader, int gears)
    {
        if (gears > 0) trader.Inventory.GiveToTrader(gears);
    }

    public static CollectibleObject? Collectible(IWorldAccessor world, string code)
    {
        var loc = new AssetLocation(code);
        return (CollectibleObject?)world.GetItem(loc) ?? world.GetBlock(loc);
    }

    public static ItemStack? StackOf(IWorldAccessor world, string code, int size = 1) => Collectible(world, code) switch
    {
        Block b => new ItemStack(b, size),
        Item i => new ItemStack(i, size),
        _ => null,
    };

    public static string ItemName(IWorldAccessor world, string code) => StackOf(world, code)?.GetName() ?? code;

    public static string TypeName(string type) => Vintagestory.API.Config.Lang.Get("seraphhorizons:trading-type-" + type);
}
