using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Window;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The pack's trader (entity class <c>SeraphHorizons.Trader</c>, every
/// <c>seraphhorizons:trader-{gender}-{type}-{climate}</c>): vanilla's EntityTrader, so the same
/// personality, revive-on-death and weekly wallet top-up, stocked from the pack's lists instead of
/// vanilla's, talking from the pack's own dialogue file, and trading through the pack's own window
/// (<see cref="GuiDialogSeraphTrade"/>) instead of vanilla's dialog: the dialogue's <c>opentrade</c>
/// opens it (<see cref="Dialog_DialogTriggers"/>), and every trade in it is one unit, made at once
/// on the server (<see cref="BuyUnit"/>, <see cref="SellUnit"/>, through vanilla's own deal).
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

    /// <summary>Raised on the server after a deal went through (the trade window's buy or sell, or
    /// vanilla's deal packet), with what the player sold (what left the selling cart): orders (#453)
    /// count their deliveries here.</summary>
    public static event Action<IServerPlayer, EntitySeraphTrader, IReadOnlyList<ItemStack>>? Dealt;

    /// <summary>Raised on the server when the trading player opens the trade window (the dialogue's
    /// <c>opentrade</c>, once vanilla has set <c>tradingPlayerUID</c> to them): prices and offers made
    /// for that player (#452, #455) are set here, and the window's state is sent.</summary>
    public static event Action<IServerPlayer, EntitySeraphTrader>? TradeOpened;

    // Vanilla's deal (internal): the pack's window makes every trade through it, so the economy's
    // patches on it, the trade hooks of maps and leads and vanilla's own checks all apply.
    private static readonly System.Reflection.MethodInfo? TryBuySellMethod = AccessTools.Method(typeof(InventoryTrader), "TryBuySell");

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
        // The window's inventory, before vanilla makes its own (it only makes one when there is none).
        Inventory ??= new SeraphTraderInventory("traderInv", EntityId.ToString(), api);
        base.Initialize(properties, api, InChunkIndex3d);
        if (api.Side == EnumAppSide.Server)
        {
            TradeProps = TradingSystem.Of(api)?.TradePropsFor(TraderType) ?? TradingSystem.EmptyTradeProps(30, 5);
            TradeWindowSystem.MarkStanding(this, TradingSystem.Of(api)?.Standing.Enabled == true);
            // The dialogue's standing reply is written on the client from what the server sends when
            // a conversation starts (TradeWindowSystem.SendStanding).
            if (GetBehavior<EntityBehaviorConversable>() is { } talk)
                talk.OnControllerCreated += controller =>
                {
                    if (controller.PlayerEntity?.Player is IServerPlayer player) TradeWindowSystem.Of(Api)?.SendState(player, this, forDialogue: true);
                };
        }
    }

    public override void FromBytes(BinaryReader reader, bool forClient)
    {
        // Vanilla makes its inventory here when an entity is read before it is initialised.
        Inventory ??= new SeraphTraderInventory("traderInv", EntityId.ToString(), null);
        base.FromBytes(reader, forClient);
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

    /// <summary>Vanilla's deal packet (1000, its dialog's Buy / Sell button), which the pack's window
    /// does not send but a client may: the game's deal runs on the carts as they are, and a deal that
    /// went through is credited as the window's are (<see cref="AfterDeal"/>). The game's TryBuySell
    /// reports success only to the base class, so the carts are compared: a deal that goes through
    /// empties the buying cart and takes the sold goods out of the selling cart; one that fails
    /// leaves both.</summary>
    public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data)
    {
        // The selling cart (the window's sell slot) is its owner's alone: another player with the
        // inventory open (vanilla's packet 1001 opens it to anyone) may neither move its stacks nor
        // deal on it.
        if (Inventory is SeraphTraderInventory own && packetid <= 1000 && player.PlayerUID != own.OwnerUid
            || packetid == 1000 && WatchedAttributes.GetString("tradingPlayerUID") != player.PlayerUID)
        {
            if (packetid < 1000) Inventory.InvNetworkUtil.SendInventoryRollback(player, packetid, data);
            return;
        }
        if (packetid != 1000 || Inventory is null)
        {
            base.OnReceivedClientPacket(player, packetid, data);
            return;
        }
        int paid = Inventory.GetTotalCost(), received = Inventory.GetTotalGain();
        int before = CartItems();
        var offered = SellingCart();
        base.OnReceivedClientPacket(player, packetid, data);
        if (CartItems() >= before) return;
        AfterDeal(player, paid, received, offered);
    }

    private int CartItems()
    {
        int n = 0;
        for (int i = 0; i < 4; i++)
            n += (Inventory.GetBuyingCartSlot(i).Itemstack?.StackSize ?? 0) + (Inventory.GetSellingCartSlot(i).Itemstack?.StackSize ?? 0);
        return n;
    }

    private ItemStack?[] SellingCart()
    {
        var offered = new ItemStack?[4];
        for (int i = 0; i < 4; i++) offered[i] = Inventory.GetSellingCartSlot(i).Itemstack?.Clone();
        return offered;
    }

    /// <summary>Standing (#452) and orders (#453) for a deal that went through: the player is
    /// credited with its gear value, and what left the selling cart is raised as
    /// <see cref="Dealt"/>.</summary>
    private void AfterDeal(IServerPlayer player, int paid, int received, ItemStack?[] offered)
    {
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

    // ---- The trade window's deals (server) ----

    /// <summary>The outcome of one of the window's trades: vanilla's result, and when it failed, the
    /// lang key (mod domain) saying why.</summary>
    public readonly record struct UnitDeal(EnumTransactionResult Result, string? Key = null, object[]? Args = null)
    {
        public bool Ok => Result == EnumTransactionResult.Success;
    }

    /// <summary>The player buys one trade unit of selling slot <paramref name="slot"/> (0–15), goods or
    /// a map or lead offer: put in the buying cart alone and dealt through vanilla's deal, as its
    /// dialog's Buy button would, the window's sell slot kept out of it.</summary>
    public UnitDeal BuyUnit(IServerPlayer player, int slot, string? expectCode = null, int? expectPrice = null)
    {
        if (Inventory is null || slot < 0 || slot > 15) return new UnitDeal(EnumTransactionResult.Failure, "trading-window-noslot");
        var shelf = Inventory.GetSellingSlot(slot);
        if (shelf?.Itemstack is null || shelf.TradeItem?.Stack is not { } unit) return new UnitDeal(EnumTransactionResult.Failure, "trading-window-noslot");
        // What the player held on may have been repriced or restocked meanwhile: no surprises.
        if (expectCode != null && expectCode != shelf.Itemstack.Collectible.Code.ToString()
            || expectPrice is int price && price != shelf.TradeItem.Price)
            return new UnitDeal(EnumTransactionResult.Failure, "trading-window-changed");
        if (shelf.TradeItem.Stock <= 0) return new UnitDeal(EnumTransactionResult.TraderNotEnoughSupplyOrDemand, "trading-window-soldout");
        var stash = Stash();
        var cart = Inventory.GetBuyingCartSlot(0);
        EnumTransactionResult result;
        try
        {
            var stack = unit.Clone();
            stack.ResolveBlockOrItem(World);
            cart.Itemstack = stack;
            cart.TradeItem = shelf.TradeItem;
            result = Deal(player);
        }
        finally
        {
            cart.Itemstack = null;
            cart.TradeItem = null;
            Restore(stash);
            Sync();
        }
        return Outcome(result, player, buying: true);
    }

    /// <summary>The player sells one trade unit of the stack in the window's sell slot
    /// (<see cref="SeraphTraderInventory.SellSlot"/>): the unit alone in the selling cart, dealt
    /// through vanilla's deal; the rest stays in the slot.</summary>
    public UnitDeal SellUnit(IServerPlayer player)
    {
        if (Inventory is null) return new UnitDeal(EnumTransactionResult.Failure, "trading-window-sell-empty");
        if (Inventory is SeraphTraderInventory own && own.OwnerUid != player.PlayerUID)
            return new UnitDeal(EnumTransactionResult.Failure, "trading-window-sell-notyours");
        var sellSlot = Inventory[SeraphTraderInventory.SellSlot];
        if (sellSlot?.Itemstack is not { } offered) return new UnitDeal(EnumTransactionResult.Failure, "trading-window-sell-empty");
        var condition = Inventory.GetBuyingConditionsSlot(offered);
        if (condition?.TradeItem?.Stack is not { } unit)
            return new UnitDeal(EnumTransactionResult.TraderNotEnoughSupplyOrDemand, "trading-window-sell-refused", [offered.GetName()]);
        int size = Math.Max(1, unit.StackSize);
        if (offered.StackSize < size) return new UnitDeal(EnumTransactionResult.Failure, "trading-window-sell-short", [size]);
        if (condition.TradeItem.Stock <= 0) return new UnitDeal(EnumTransactionResult.TraderNotEnoughSupplyOrDemand, "trading-window-sell-nodemand");
        // The rest of the stack waits while one unit is dealt.
        var rest = offered.Clone();
        rest.StackSize = offered.StackSize - size;
        offered.StackSize = size;
        var stash = Stash(keepSellSlot: true);
        var result = EnumTransactionResult.Failure;
        try
        {
            result = Deal(player);
        }
        finally
        {
            // Whatever happened in the deal, the rest of the stack goes back.
            var left = sellSlot.Itemstack;
            if (rest.StackSize > 0)
            {
                if (left is null) sellSlot.Itemstack = rest;
                else if (left.Equals(World, rest, GlobalConstants.IgnoredStackAttributes)) left.StackSize += rest.StackSize;
                else World.SpawnItemEntity(rest, player.Entity?.Pos.XYZ ?? Pos.XYZ);
            }
            sellSlot.MarkDirty();
            Restore(stash);
            Sync();
        }
        return Outcome(result, player, buying: false);
    }

    private UnitDeal Outcome(EnumTransactionResult result, IServerPlayer player, bool buying) => result switch
    {
        EnumTransactionResult.Success => new UnitDeal(result),
        EnumTransactionResult.PlayerNotEnoughAssets => new UnitDeal(result, "trading-window-player-broke"),
        EnumTransactionResult.TraderNotEnoughAssets => new UnitDeal(result, "trading-window-trader-broke"),
        EnumTransactionResult.TraderNotEnoughSupplyOrDemand => new UnitDeal(result, buying ? "trading-window-soldout" : "trading-window-sell-nodemand"),
        _ => new UnitDeal(result, "trading-window-failed"),
    };

    /// <summary>Vanilla's deal on the carts as they are now, then what follows a deal that went
    /// through (standing, orders, the nod).</summary>
    private EnumTransactionResult Deal(IServerPlayer player)
    {
        if (TryBuySellMethod is null) return EnumTransactionResult.Failure;
        var perms = new CachedAccessPerms(this, player);
        if (!perms.IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.None, false, "trader")) return EnumTransactionResult.Failure;
        int paid = Inventory.GetTotalCost(), received = Inventory.GetTotalGain();
        var offered = SellingCart();
        EnumTransactionResult result;
        try
        {
            result = (EnumTransactionResult)TryBuySellMethod.Invoke(Inventory, [player])!;
        }
        catch (System.Reflection.TargetInvocationException e)
        {
            Api.Logger.Error("[seraphhorizons] Trade window: a deal at {0} threw; nothing was traded: {1}", Pos.AsBlockPos, e.InnerException ?? e);
            return EnumTransactionResult.Failure;
        }
        if (result != EnumTransactionResult.Success) return result;
        (Api as ICoreServerAPI)?.WorldManager.GetChunk(Pos.AsBlockPos)?.MarkModified();
        AnimManager.StopAnimation("idle");
        AnimManager.StartAnimation(new AnimationMetaData { Animation = "nod", Code = "nod", Weight = 10, EaseOutSpeed = 10000, EaseInSpeed = 10000 });
        AfterDeal(player, paid, received, offered);
        return result;
    }

    /// <summary>Takes both carts out of the inventory for one of the window's deals (the sell slot
    /// stays when <paramref name="keepSellSlot"/>: it is the deal).</summary>
    private (ItemStack? Stack, ResolvedTradeItem? Item)[] Stash(bool keepSellSlot = false)
    {
        var stash = new (ItemStack?, ResolvedTradeItem?)[8];
        for (int i = 0; i < 4; i++)
        {
            var buy = Inventory.GetBuyingCartSlot(i);
            stash[i] = (buy.Itemstack, buy.TradeItem);
            buy.Itemstack = null;
            buy.TradeItem = null;
            if (keepSellSlot && i == 0) continue;
            var sell = Inventory.GetSellingCartSlot(i);
            stash[4 + i] = (sell.Itemstack, null);
            sell.Itemstack = null;
        }
        return stash;
    }

    private void Restore((ItemStack? Stack, ResolvedTradeItem? Item)[] stash)
    {
        for (int i = 0; i < 4; i++)
        {
            if (stash[i].Stack is { } b)
            {
                var buy = Inventory.GetBuyingCartSlot(i);
                buy.Itemstack = b;
                buy.TradeItem = stash[i].Item;
            }
            if (stash[4 + i].Stack is { } s)
            {
                var sell = Inventory.GetSellingCartSlot(i);
                if (sell.Itemstack is null) sell.Itemstack = s;
                else World.SpawnItemEntity(s, Pos.XYZ);
            }
        }
    }

    /// <summary>Saves the inventory into the watched attributes and sends it to every client, as
    /// vanilla does after a deal (packet 1234).</summary>
    public void Sync()
    {
        var store = new TreeAttribute();
        Inventory.ToTreeAttributes(store);
        WatchedAttributes["traderInventory"] = store;
        WatchedAttributes.MarkPathDirty("traderInventory");
        (Api as ICoreServerAPI)?.Network.BroadcastEntityPacket(EntityId, 1234, store.ToBytes());
    }

    /// <summary>A trader leaving (a visitor's visit over, killed, unloaded) hands what is in the
    /// window's sell slot back to its owner if they are online, else drops it by the trader.</summary>
    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (World.Side == EnumAppSide.Server && Inventory is SeraphTraderInventory own && own.CartHoldsGoods)
        {
            if (own.OwnerUid != null && World.PlayerByUid(own.OwnerUid) is { Entity: not null } owner) own.GiveBack(owner);
            for (int i = 0; i < 4; i++)
                if (own.GetSellingCartSlot(i) is { Itemstack: { } stack } slot)
                {
                    World.SpawnItemEntity(stack, Pos.XYZ);
                    slot.Itemstack = null;
                }
            own.OwnerUid = null;
        }
        base.OnEntityDespawn(despawn);
    }

    // ---- Opening the window ----

    /// <summary>The dialogue's <c>opentrade</c> ("Got anything to trade?") opens the pack's trade
    /// window instead of vanilla's dialog. On the server vanilla's own handling runs (reach, one
    /// trading player at a time, <c>tradingPlayerUID</c>); its client side, which would open
    /// <c>GuiDialogTrader</c>, is replaced by <see cref="OpenWindow"/>.</summary>
    protected override int Dialog_DialogTriggers(EntityAgent triggeringEntity, string value, JsonObject data)
    {
        if (value != "opentrade") return base.Dialog_DialogTriggers(triggeringEntity, value, data);
        if (World.Side == EnumAppSide.Client)
        {
            if (!Alive || triggeringEntity.Pos.SquareDistanceTo(Pos) > 7f) return 0;
            if (WatchedAttributes.HasAttribute("tradingPlayerUID"))
            {
                (Api as ICoreClientAPI)?.TriggerIngameError(this, "alreadyTrading", Lang.Get("trader-trading"));
                return 0;
            }
            ConversableBh?.Dialog?.TryClose();
            if (triggeringEntity is EntityPlayer ep && OpenWindow((ICoreClientAPI)Api)) interactingWithPlayer.Add(ep);
            return -1;
        }
        int result = base.Dialog_DialogTriggers(triggeringEntity, value, data);
        if (triggeringEntity is EntityPlayer { Player: IServerPlayer sp } && WatchedAttributes.GetString("tradingPlayerUID") == sp.PlayerUID)
            OnTradeOpened(sp);
        return result;
    }

    /// <summary>Server: the player is now the trader's trading player (the dialogue's
    /// <c>opentrade</c>, or a test standing in for it): what follows a window opening.</summary>
    public void OnTradeOpened(IServerPlayer player)
    {
        (Inventory as SeraphTraderInventory)?.TakeOwnership(player, World, Pos.XYZ);
        TradeOpened?.Invoke(player, this);
        TradeWindowSystem.Of(Api)?.SendState(player, this);
    }

    /// <summary>Server: makes the player the trader's trading player as the dialogue's
    /// <c>opentrade</c> does (for tests and tools that do not talk to it); false if someone else is.</summary>
    public bool BeginTrade(IServerPlayer player)
    {
        string? current = WatchedAttributes.GetString("tradingPlayerUID");
        if (current != null && current != player.PlayerUID) return false;
        if (!interactingWithPlayer.Contains(player.Entity)) interactingWithPlayer.Add(player.Entity);
        WatchedAttributes.SetString("tradingPlayerUID", player.PlayerUID);
        player.InventoryManager.OpenInventory(Inventory);
        OnTradeOpened(player);
        return true;
    }

    /// <summary>Client: opens the pack's trade window, as vanilla's TryOpenTradeDialog opens its
    /// dialog: the server opens the trader's inventory to the player (packet 1001, so the sell slot
    /// syncs), and only one trade window at a time.</summary>
    private bool OpenWindow(ICoreClientAPI capi)
    {
        if (dlg?.IsOpened() == true) return false;
        if (capi.Gui.OpenedGuis.Any(d => d is GuiDialogTrader or GuiDialogSeraphTrade && d.IsOpened()))
        {
            capi.TriggerIngameError(this, "onlyonedialog", Lang.Get("Can only trade with one trader at a time"));
            return false;
        }
        capi.Network.SendEntityPacket(EntityId, 1001, null);
        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        var window = new GuiDialogSeraphTrade(capi, this);
        window.OnClosed += OnWindowClosed;
        dlg = window;
        return window.TryOpen();
    }

    private void OnWindowClosed()
    {
        dlg = null;
        if (Api is not ICoreClientAPI capi) return;
        interactingWithPlayer.Remove(capi.World.Player.Entity);
        capi.Network.SendEntityPacket(EntityId, 1212, null);
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
