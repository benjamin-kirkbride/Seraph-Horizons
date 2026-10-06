using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
    public const string SellingKeysAttr = "seraphhorizons:sellingkeys";
    public const string BuyingKeysAttr = "seraphhorizons:buyingkeys";
    private const string LastRefreshAttr = "lastRefreshTotalDays";

    private bool _imported;
    private double _walletSetFor = double.NaN;

    /// <summary>Raised on the server after every <see cref="Restock"/>, once both sides are filled
    /// and saved: the economy (#450, #451) refills the side budget and prices the shelves here.</summary>
    public static event Action<EntitySeraphTrader>? Restocked;

    /// <summary>Raised on the server after a deal through the trade dialog went through, with what
    /// the player sold (what left the selling cart): orders (#453) count their deliveries here.</summary>
    public static event Action<IServerPlayer, EntitySeraphTrader, IReadOnlyList<ItemStack>>? Dealt;

    /// <summary>Raised on the server when the trading player opens the trade dialog (the dialogue's
    /// <c>opentrade</c>, once vanilla has set <c>tradingPlayerUID</c> to them): orders and deliveries
    /// (#453, #454) say in chat what is on here, and prices and offers made for that player (#452,
    /// #455) are set here.</summary>
    public static event Action<IServerPlayer, EntitySeraphTrader>? TradeOpened;

    /// <summary>The trader type, from the entity code (<c>trader-{gender}-{type}-{climate}</c>).</summary>
    public string TraderType => Code.Path.Split('-') is { Length: >= 3 } parts ? parts[2] : "";

    /// <summary>The standing tier the shelves are stocked for (<see cref="TradeListResolver.Resolve(TradeListDef, Region, int)"/>).
    /// Shelves are shared, so a camp trader's follow the best recent customer's tier, as the wallet
    /// does (<see cref="IStandingSource.ShelfTierFor"/>); a travelling merchant (#456) stocks for its
    /// inn's owner.</summary>
    public virtual int StockTier => TradingSystem.Of(Api) is { } system ? system.Standing.ShelfTierFor(this) : 0;

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
        UpdateWallet(before);
        base.OnGameTick(dt);
        double after = WatchedAttributes.GetDouble(LastRefreshAttr, double.NaN);
        if (!before.Equals(after) && TradeProps != null) Restock(0.5f);
    }

    /// <summary>Standing (#452): before vanilla's weekly top-up, the wallet it tops up towards is the
    /// list's for the best standing tier among players who traded here recently (a trader that does
    /// well with someone keeps more gears for everyone). Asked once per due restock.</summary>
    private void UpdateWallet(double lastRefresh)
    {
        double last = double.IsNaN(lastRefresh) ? World.Calendar.TotalDays - 10 : lastRefresh;
        if (TradeProps is null || World.Calendar.TotalDays - last <= doubleRefreshIntervalDays || last.Equals(_walletSetFor)) return;
        _walletSetFor = last;
        var system = TradingSystem.Of(Api);
        if (system?.Lists?.For(TraderType) is not { } def || !system.Standing.Enabled) return;
        var wallet = def.WalletFor(system.Standing.WalletTierFor(this));
        TradeProps.Money = NatFloat.createUniform(wallet.Avg, wallet.Var);
    }

    /// <summary>Standing (#452): a deal (packet 1000, the dialog's trade button) that went through
    /// credits the player with its gear value. The game's TryBuySell is internal and reports success
    /// only to the base class, so the carts are compared: a deal that goes through empties the
    /// buying cart and takes the sold goods out of the selling cart; one that fails leaves both.</summary>
    public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
    {
        if (packetid != 1000 || Inventory is null)
        {
            base.OnReceivedClientPacket(player, packetid, data);
            return;
        }
        int paid = Inventory.GetTotalCost(), received = Inventory.GetTotalGain();
        int before = CartItems();
        // Orders (#453): what is in the selling cart before, to tell what the deal took.
        var offered = new ItemStack?[4];
        for (int i = 0; i < 4; i++) offered[i] = Inventory.GetSellingCartSlot(i).Itemstack?.Clone();
        base.OnReceivedClientPacket(player, packetid, data);
        if (CartItems() >= before) return;
        if (paid + received > 0 && TradingSystem.Of(Api) is { Standing.Enabled: true } system)
            system.Standing.OnDeal(player, this, paid, received);
        if (Dealt is null) return;
        var sold = new List<ItemStack>();
        for (int i = 0; i < 4; i++)
        {
            if (offered[i] is not { } was) continue;
            var now = Inventory.GetSellingCartSlot(i).Itemstack;
            int n = now is null || now.Collectible != was.Collectible ? was.StackSize : was.StackSize - now.StackSize;
            if (n <= 0) continue;
            var stack = was.Clone();
            stack.StackSize = n;
            sold.Add(stack);
        }
        if (sold.Count > 0) Dealt.Invoke(player, this, sold);
    }

    private int CartItems()
    {
        int n = 0;
        for (int i = 0; i < 4; i++)
            n += (Inventory.GetBuyingCartSlot(i).Itemstack?.StackSize ?? 0) + (Inventory.GetSellingCartSlot(i).Itemstack?.StackSize ?? 0);
        return n;
    }

    /// <summary>Standing (#452): opening the trade dialog shows the player's standing in chat, once
    /// a visit (the dialog itself is the game's, client side, and private).</summary>
    protected override int Dialog_DialogTriggers(EntityAgent triggeringEntity, string value, JsonObject data)
    {
        int result = base.Dialog_DialogTriggers(triggeringEntity, value, data);
        if (value == "opentrade" && World.Side == EnumAppSide.Server && WatchedAttributes.HasAttribute("tradingPlayerUID")
            && triggeringEntity is EntityPlayer { Player: { } player })
        {
            if (TradingSystem.Of(Api) is { Standing.Enabled: true } system) system.Standing.OnTradeOpened(player, this);
            if (player is IServerPlayer sp && WatchedAttributes.GetString("tradingPlayerUID") == sp.PlayerUID)
                TradeOpened?.Invoke(sp, this);
        }
        return result;
    }

    /// <summary>Fills both sides from the trader's list for its region (<see cref="RestockPlanner"/>);
    /// <paramref name="refreshChance"/> is vanilla's (above 1 redraws every rotating slot). Later waves
    /// call it when supply or standing change what a trader would stock.</summary>
    public void Restock(float refreshChance)
    {
        var system = TradingSystem.Of(Api);
        if (system?.Lists?.For(TraderType) is not { } def || Inventory is null) return;
        int tier = StockTier;
        var resolved = TradeListResolver.Resolve(def, Region, tier, system.Standing.UnlocksOfTier(tier).RareStock);
        resolved = TradeOffers.Expand(resolved, system.Offers is { } offers ? e => offers(this, e) : null);
        var context = new TraderContext(TraderType, Region, EntityId, Pos.X, Pos.Z);
        var gate = system.SupplyGate;
        Fill(system, Inventory.SellingSlots, resolved.Selling, SellingKeysAttr, refreshChance, e => gate.Stock(context, e), EnumTradeDirection.Sell);
        Fill(system, Inventory.BuyingSlots, resolved.Buying, BuyingKeysAttr, refreshChance, null, EnumTradeDirection.Buy);
        var store = WatchedAttributes.GetTreeAttribute("traderInventory") ?? new TreeAttribute();
        Inventory.ToTreeAttributes(store);
        WatchedAttributes["traderInventory"] = store;
        WatchedAttributes.MarkAllDirty();
        Restocked?.Invoke(this);
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
