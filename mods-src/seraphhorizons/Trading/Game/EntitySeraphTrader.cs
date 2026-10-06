using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The pack's trader (entity class <c>SeraphHorizons.Trader</c>, every
/// <c>seraphhorizons:trader-{gender}-{type}-{climate}</c>): vanilla's EntityTrader, so the same
/// dialogue, trade dialog, personality, revive-on-death and weekly wallet top-up, stocked from the
/// pack's lists instead of vanilla's.
///
/// How it hooks in without patching the game: its TradeProps hold the wallet and two empty lists,
/// so everything vanilla does to stock (on spawn, on import, weekly in OnGameTick) runs and puts
/// nothing on the shelves; this class runs <see cref="Restock"/> right after each of those (it sees
/// the weekly one by <c>lastRefreshTotalDays</c> moving). The region it stocks for is read from
/// the world where it first spawns and kept (<see cref="RegionAttr"/>).
/// </summary>
public class EntitySeraphTrader : EntityTrader
{
    public const string ClassName = "SeraphHorizons.Trader";
    public const string RegionAttr = "seraphhorizons:region";
    private const string SellingKeysAttr = "seraphhorizons:sellingkeys";
    private const string BuyingKeysAttr = "seraphhorizons:buyingkeys";
    private const string LastRefreshAttr = "lastRefreshTotalDays";

    private bool _imported;

    /// <summary>The trader type, from the entity code (<c>trader-{gender}-{type}-{climate}</c>).</summary>
    public string TraderType => Code.Path.Split('-') is { Length: >= 3 } parts ? parts[2] : "";

    public Region Region
    {
        get => Region.TryParse(WatchedAttributes.GetString(RegionAttr, ""), out var r) ? r : new Region(Region.Temperate, Region.Sedimentary);
        set => WatchedAttributes.SetString(RegionAttr, value.ToString());
    }

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);
        if (api.Side == EnumAppSide.Server)
            TradeProps = TradingSystem.Of(api)?.TradePropsFor(TraderType) ?? TradingSystem.EmptyTradeProps(30, 5);
    }

    public override void OnEntitySpawn()
    {
        if (World.Side == EnumAppSide.Server && !WatchedAttributes.HasAttribute(RegionAttr) && TradingSystem.Of(Api) is { } system)
            Region = RegionProbe.At(World.BlockAccessor, Pos.AsBlockPos, system.Classifier);
        base.OnEntitySpawn();
        if (World.Side == EnumAppSide.Server) Restock(1.1f);
    }

    public override void DidImportOrExport(BlockPos startPos)
    {
        base.DidImportOrExport(startPos);
        _imported = true;
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        if (Api.Side == EnumAppSide.Server && _imported) Restock(1.1f);
    }

    public override void OnGameTick(float dt)
    {
        if (World.Side != EnumAppSide.Server)
        {
            base.OnGameTick(dt);
            return;
        }
        double before = WatchedAttributes.GetDouble(LastRefreshAttr, double.NaN);
        base.OnGameTick(dt);
        double after = WatchedAttributes.GetDouble(LastRefreshAttr, double.NaN);
        if (!before.Equals(after) && TradeProps != null) Restock(0.5f);
    }

    /// <summary>Fills both sides from the trader's list for its region (<see cref="RestockPlanner"/>);
    /// <paramref name="refreshChance"/> is vanilla's (above 1 redraws every rotating slot). Later waves
    /// call it when supply or standing change what a trader would stock.</summary>
    public void Restock(float refreshChance)
    {
        var system = TradingSystem.Of(Api);
        if (system?.Lists?.For(TraderType) is not { } def || Inventory is null) return;
        var resolved = TradeListResolver.Resolve(def, Region);
        var context = new TraderContext(TraderType, Region, EntityId, Pos.X, Pos.Z);
        var gate = system.SupplyGate;
        Fill(system, Inventory.SellingSlots, resolved.Selling, SellingKeysAttr, refreshChance, e => gate.Stock(context, e), EnumTradeDirection.Sell);
        Fill(system, Inventory.BuyingSlots, resolved.Buying, BuyingKeysAttr, refreshChance, null, EnumTradeDirection.Buy);
        var store = WatchedAttributes.GetTreeAttribute("traderInventory") ?? new TreeAttribute();
        Inventory.ToTreeAttributes(store);
        WatchedAttributes["traderInventory"] = store;
        WatchedAttributes.MarkAllDirty();
    }

    private void Fill(TradingSystem system, ItemSlotTrade[] slots, ResolvedSide side, string keysAttr, float refreshChance,
        System.Func<TradeEntry, int>? gate, EnumTradeDirection direction)
    {
        var keys = (WatchedAttributes[keysAttr] as StringArrayAttribute)?.value ?? [];
        var current = slots.Select((slot, i) => new SlotState(
            i < keys.Length && slot.Itemstack != null && keys[i].Length > 0 ? keys[i] : null,
            slot.TradeItem?.Stock > 0)).ToList();
        var old = slots.Select(s => s.TradeItem).ToArray();
        bool Accept(TradeEntry e)
        {
            var item = system.Lists!.ItemFor(e);
            item.Resolve(World, "seraphhorizons trade list", printWarningOnError: false);
            return item.ResolvedItemstack?.Collectible is not ITradeableCollectible tradeable
                   || tradeable.ShouldTrade(this, item, direction);
        }
        var plan = RestockPlanner.Plan(side, current, refreshChance, World.Rand, gate, Accept);
        var newKeys = new string[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var p = i < plan.Length ? plan[i] : SlotPlan.Empty;
            var slot = slots[i];
            newKeys[i] = p.Entry?.Key ?? "";
            if (p.IsEmpty)
            {
                slot.Itemstack = null;
                slot.TradeItem = null;
            }
            else if (p.KeptFrom is int k && old[k] is { } kept)
                slot.SetTradeItem(kept);
            else
            {
                var resolved = system.Lists!.ItemFor(p.Entry!).Resolve(World);
                // A core entry is always on the shelf: a stock spread that rolls 0 still leaves one.
                resolved.Stock = p.Stock ?? Math.Max(1, resolved.Stock);
                slot.SetTradeItem(resolved);
            }
            slot.MarkDirty();
        }
        WatchedAttributes[keysAttr] = new StringArrayAttribute(newKeys);
    }
}
